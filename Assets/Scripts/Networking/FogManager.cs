using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class FogManager : MonoBehaviour
{
    public static FogManager Instance { get; private set; }
    public bool Enabled { get; private set; } = true;
    public bool Paused => SceneEditor.Instance == null || SceneEditor.Instance.Model.RevealPaused;
    public bool Preview { get; private set; }
    public string PreviewSourceId { get; private set; }
    public bool PreviewTestSource { get; private set; }
    public string PreviewSourceName => PreviewTestSource ? "Тестовый обзор (30 фт)" : PreviewSourceId == null ? "Обзор всей группы"
        : TokenController.FindSceneToken(PreviewSourceId)?.TokenName ?? "Источник удалён";
    private bool[] _previewVisible;
    private Vector2 _testPosition;
    private Vector2 _previousTest = new(float.PositiveInfinity, float.PositiveInfinity);
    private bool _previewDirty = true;
    public int ManualMode { get; private set; }
    public static bool IsManualEditing => Instance != null && Instance.ManualMode != 0;
    public bool SaveWithHistory { get; set; } = true;
    public bool Autosave { get; set; } = true;
    public string Status { get; private set; } = "Раскрытие приостановлено";
    private GridManager _grid;
    private NetworkManager _network;
    private bool[] _visible = Array.Empty<bool>(), _explored = Array.Empty<bool>(), _manualVisible = Array.Empty<bool>(), _manualHidden = Array.Empty<bool>();
    private int _width, _height, _revision, _received = -1;
    private int _pendingRevisionAck = -1;
    private bool _dirty = true, _textureDirty = true, _hasState;
    private Texture2D _mask;
    private GameObject _cover;
    private Material _coverMaterial;
    private float _nextRequest, _nextAutosave;
    private bool _manualStroke;
    private Vector2Int _lastManual = new(-1, -1);
    private string _manualBefore;
    private const string StateMessage = "FogStateV2", RequestMessage = "FogStateRequestV2", AckMessage = "FogStateAckV2";
    private byte[] _encodedState;
    private bool _publishPending;
    private float _nextPublish;
    private readonly Dictionary<ulong, int> _clientAcks = new();
    public bool IsMaster => _network != null && _network.IsHost && _network.IsListening;
    public bool HasClientCurrentRevision(ulong client) => _network != null && _network.IsServer
        && _network.ConnectedClients.ContainsKey(client) && _revision > 0
        && _clientAcks.TryGetValue(client, out int revision) && revision == _revision;
    public void ResendStateToClient(ulong client)
    {
        if (_network != null && _network.IsServer && _network.ConnectedClients.ContainsKey(client)) Send(client);
    }
    public bool AllClientsHaveCurrentRevision
    {
        get
        {
            if (_network == null || !_network.IsServer || _revision <= 0) return false;
            foreach (ulong client in _network.ConnectedClientsIds)
                if (client != NetworkManager.ServerClientId
                    && (!_clientAcks.TryGetValue(client, out int ack) || ack < _revision)) return false;
            return true;
        }
    }
    public bool ShowingPlayerView => _network != null && _network.IsListening && (!IsMaster || Preview);
    private void Awake() { Instance = this; }
    public void MarkDirty() { _dirty = true; _previewDirty = true; }
    public void PrepareForSceneTransfer() { if (IsMaster && _dirty) Recalculate(); }
    private void Register(NetworkManager manager)
    {
        _network = manager; _received = -1; _revision = 0; _hasState = manager.IsServer;
        _encodedState = null; _publishPending = false; _nextPublish = 0;
        Enabled = true; Preview = false; PreviewSourceId = null; PreviewTestSource = false; _dirty = true;
        manager.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, Receive);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(RequestMessage, ReceiveRequest);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(AckMessage, ReceiveAck);
        manager.OnClientConnectedCallback += Connected;
        _nextAutosave = Time.unscaledTime + 120;
    }
    private void Unregister()
    {
        _network.OnClientConnectedCallback -= Connected;
        if (_network.CustomMessagingManager != null)
        {
            _network.CustomMessagingManager.UnregisterNamedMessageHandler(StateMessage);
            _network.CustomMessagingManager.UnregisterNamedMessageHandler(RequestMessage);
            _network.CustomMessagingManager.UnregisterNamedMessageHandler(AckMessage);
        }
        _network = null; Preview = false; ManualMode = 0; _hasState = false;
        _clientAcks.Clear();
        _pendingRevisionAck = -1;
        _width = _height = 0; _explored = _visible = _manualVisible = _manualHidden = Array.Empty<bool>();
        _textureDirty = true; GameMasterUndo.Clear();
        _encodedState = null; _publishPending = false;
    }
    private void Update()
    {
        var manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening && _network != manager) Register(manager);
        if (_network != null && !_network.IsListening) Unregister();
        if (_grid == null) _grid = FindAnyObjectByType<GridManager>();
        if (_grid == null || _network == null) return;
        if ((_width != _grid.Width || _height != _grid.Height) && (IsMaster || !_hasState)) Resize(_grid.Width, _grid.Height);
        if (!_network.IsServer && !_hasState && Time.unscaledTime >= _nextRequest)
        {
            _nextRequest = Time.unscaledTime + 2;
            using var writer = new FastBufferWriter(1, Allocator.Temp);
            _network.CustomMessagingManager.SendNamedMessage(RequestMessage, NetworkManager.ServerClientId, writer);
        }
        if (IsMaster && _dirty) Recalculate();
        if (IsMaster && _publishPending && Time.unscaledTime >= _nextPublish)
        {
            _publishPending = false; _nextPublish = Time.unscaledTime + 0.2f;
            foreach (ulong client in _network.ConnectedClientsIds) if (client != NetworkManager.ServerClientId) Send(client);
        }
        if (IsMaster && Autosave && Time.unscaledTime >= _nextAutosave)
        {
            _nextAutosave = Time.unscaledTime + 120;
            bool dragging = false;
            foreach (var token in FindObjectsByType<TokenController>(FindObjectsInactive.Exclude)) dragging |= token.IsLocalDragActiveAny();
            if (!dragging && !SceneEditor.IsEditing && !IsManualEditing && MapController.Instance?.HasImage == true)
            {
                try
                {
                    SceneFileStore.SaveAutosave();
                    Status = "Автосохранение: " + DateTime.Now.ToString("HH:mm");
                }
                catch (Exception ex) { Status = "Автосохранение: " + ex.Message; }
            }
        }
        HandleManual();
        if (GameplayInputGate.AllowsWorldPointerInput && GameplayInputGate.AllowsKeyboardHotkeys && !SceneEditor.IsEditing && !IsManualEditing
            && Keyboard.current?.eKey.wasPressedThisFrame == true)
        {
            string door = NearestDoor(_network.LocalClientId);
            if (door != null) SceneEditor.Instance?.RequestDoorToggle(door);
        }
    }
    private void Resize(int width, int height)
    {
        _width = width; _height = height;
        int count = width * height * FogVisibility.SamplesPerCell * FogVisibility.SamplesPerCell;
        _visible = new bool[count]; _explored = new bool[count]; _manualVisible = new bool[count]; _manualHidden = new bool[count];
        _dirty = _textureDirty = _previewDirty = true;
    }
    private void Recalculate()
    {
        _dirty = false;
        var sources = new List<VisionSource>();
        if (Enabled && !Paused)
            foreach (var token in FindObjectsByType<TokenController>(FindObjectsInactive.Exclude))
            {
                if (!token.IsSpawned || token.IsHidden || token.VisionFeet <= 0) continue;
                Vector3 p = _grid.WorldToGridCoordinates(token.CommittedPosition);
                sources.Add(new VisionSource(new Vector2(p.x, p.z), token.VisionFeet / 5f));
            }
        _visible = new FogVisibility(SceneEditor.Instance?.Model.Snapshot() ?? new SceneGeometry()).Calculate(_width, _height, sources);
        ApplyManualOverrides(_visible, _manualVisible, _manualHidden, Paused);
        for (int i = 0; i < _visible.Length; i++)
        {
            // Disabling fog is a display mode, never a bulk discovery operation.
            if (Enabled && !Paused && _visible[i]) _explored[i] = true;
        }
        _textureDirty = true; Publish();
        Status = !Enabled ? "Туман отключён; история сохранена" : Paused ? "Раскрытие приостановлено" : "Общий обзор группы";
    }
    private int Index(Vector3 world)
    {
        if (_grid == null || _width <= 0) return -1;
        Vector3 p = _grid.WorldToGridCoordinates(world);
        int x = Mathf.FloorToInt(p.x * FogVisibility.SamplesPerCell), y = Mathf.FloorToInt(p.z * FogVisibility.SamplesPerCell);
        int width = _width * FogVisibility.SamplesPerCell;
        return x < 0 || y < 0 || x >= width || y >= _height * FogVisibility.SamplesPerCell ? -1 : y * width + x;
    }
    public bool IsVisible(Vector3 world)
    {
        if (!Enabled) return true;
        int index = Index(world);
        return index >= 0 && index < _visible.Length && _visible[index];
    }
    private bool PreviewVisible(Vector3 world)
    {
        int index = Index(world);
        return index >= 0 && index < (_previewVisible?.Length ?? 0) && _previewVisible[index];
    }
    private static void ApplyManualOverrides(bool[] visible, bool[] revealed, bool[] hidden, bool paused)
    {
        for (int i = 0; i < visible.Length; i++)
        {
            if (!paused) visible[i] |= revealed[i];
            visible[i] &= !hidden[i];
        }
    }
    public bool IsVisibleInDisplayedView(Vector3 world) => !Enabled ||
        (Preview && (PreviewSourceId != null || PreviewTestSource) ? PreviewVisible(world) : IsVisible(world));
    public bool CanSeeToken(ulong client, TokenController token, bool preview = false)
    {
        if (NetworkPermissions.IsHostClient(client) && !preview) return true;
        if (token.IsHidden) return false;
        if (!Enabled || !preview && token.ControllerClientId == client || token.IsHero) return true;
        if (preview && (PreviewSourceId != null || PreviewTestSource)) return PreviewVisible(token.CommittedPosition) && PreviewVisible(token.DisplayPosition);
        return IsVisible(token.CommittedPosition) && IsVisible(token.DisplayPosition);
    }
    public void ToggleEnabled()
    {
        if (!IsMaster) return;
        var before = Capture(true); GameMasterUndo.Record("режим тумана", () => Restore(before, false));
        Enabled = !Enabled; MarkDirty();
    }
    public void TogglePause()
    {
        if (!IsMaster || SceneEditor.Instance == null) return;
        var before = Capture(true); bool paused = Paused;
        GameMasterUndo.Record("пауза раскрытия", () => { SceneEditor.Instance.Model.RevealPaused = paused; Restore(before, false); SceneEditor.Instance.Publish(); });
        SceneEditor.Instance.Model.RevealPaused = !paused; SceneEditor.Instance.Publish(); MarkDirty();
    }
    public void TogglePreview()
    {
        if (!IsMaster) return;
        if (!Preview)
        {
            SceneEditor.Instance?.Deactivate(); StopManual(); MeasurementTool.Instance?.Deactivate(); EffectPaintTool.Instance?.Deactivate();
        }
        Preview = !Preview; _textureDirty = _previewDirty = true;
    }
    public void CyclePreviewSource()
    {
        if (!IsMaster) return;
        var tokens = new List<TokenController>();
        foreach (var token in FindObjectsByType<TokenController>(FindObjectsInactive.Exclude))
            if (token.IsSpawned && !token.IsHidden && token.VisionFeet > 0) tokens.Add(token);
        tokens.Sort((a, b) => a.NetworkObjectId.CompareTo(b.NetworkObjectId));
        int previous = tokens.FindIndex(token => token.SceneId == PreviewSourceId);
        if (PreviewTestSource) { PreviewTestSource = false; PreviewSourceId = null; }
        else if (PreviewSourceId == null && tokens.Count > 0) PreviewSourceId = tokens[0].SceneId;
        else if (previous >= 0 && previous + 1 < tokens.Count) PreviewSourceId = tokens[previous + 1].SceneId;
        else { PreviewSourceId = null; PreviewTestSource = true; _testPosition = new Vector2(_width * 0.5f, _height * 0.5f); }
        if (!Preview) TogglePreview();
        _textureDirty = _previewDirty = true;
    }
    public void SetManual(int mode)
    {
        if (!IsMaster) return;
        FinishManual();
        SceneEditor.Instance?.Deactivate(); MeasurementTool.Instance?.Deactivate(); EffectPaintTool.Instance?.Deactivate();
        ManualMode = mode; _lastManual = new Vector2Int(-1, -1);
    }
    public void StopManual() { FinishManual(); if (ManualMode != 0) GameplayInputGate.MarkToolExit(); ManualMode = 0; }
    private void HandleManual()
    {
        if (!IsMaster || ManualMode == 0 || Mouse.current == null) return;
        var mouse = Mouse.current;
        if (GameplayInputGate.AllowsKeyboardHotkeys && Keyboard.current?.escapeKey.wasPressedThisFrame == true
            || mouse.rightButton.wasPressedThisFrame && GameplayInputGate.AllowsWorldPointerInput) { StopManual(); return; }
        if (mouse.leftButton.wasReleasedThisFrame || !GameplayInputGate.AllowsWorldPointerInput) FinishManual();
        if (!GameplayInputGate.AllowsWorldPointerInput) return;
        if (mouse.leftButton.wasPressedThisFrame) { _manualBefore = JsonUtility.ToJson(Capture(true)); _manualStroke = true; _lastManual = new Vector2Int(-1, -1); }
        if (!_manualStroke || !mouse.leftButton.isPressed || Camera.main == null) return;
        if (!new Plane(Vector3.up, Vector3.zero).Raycast(Camera.main.ScreenPointToRay(mouse.position.ReadValue()), out float d)) return;
        var cell = _grid.GetGridPosition(Camera.main.ScreenPointToRay(mouse.position.ReadValue()).GetPoint(d));
        if (cell.x < 0 || cell == _lastManual) return;
        int stride = _width * FogVisibility.SamplesPerCell;
        Vector2Int previous = _lastManual.x < 0 ? cell : _lastManual;
        int steps = Mathf.Max(Mathf.Abs(cell.x - previous.x), Mathf.Abs(cell.y - previous.y));
        for (int step = 0; step <= steps; step++)
        {
            float fraction = steps == 0 ? 0 : (float)step / steps;
            var painted = new Vector2Int(Mathf.RoundToInt(Mathf.Lerp(previous.x, cell.x, fraction)), Mathf.RoundToInt(Mathf.Lerp(previous.y, cell.y, fraction)));
            for (int y = painted.y * 4; y < painted.y * 4 + 4; y++) for (int x = painted.x * 4; x < painted.x * 4 + 4; x++)
            {
                int i = y * stride + x; _manualVisible[i] = ManualMode == 1; _manualHidden[i] = ManualMode == 2;
                _explored[i] = ManualMode == 1;
            }
        }
        _lastManual = cell;
        MarkDirty();
    }
    private void FinishManual()
    {
        if (!_manualStroke) return;
        _manualStroke = false;
        string before = _manualBefore;
        GameMasterUndo.Record("ручное раскрытие карты", () => Restore(JsonUtility.FromJson<SavedFog>(before), false));
    }
    public void ResetHistory()
    {
        if (!IsMaster) return;
        var before = Capture(true); bool previousPause = Paused;
        GameMasterUndo.Record("сброс исследования", () => { Restore(before, false); SceneEditor.Instance.Model.RevealPaused = previousPause; SceneEditor.Instance.Publish(); });
        Array.Clear(_explored, 0, _explored.Length); Array.Clear(_manualVisible, 0, _manualVisible.Length); Array.Clear(_manualHidden, 0, _manualHidden.Length);
        SceneEditor.Instance.Model.RevealPaused = true; SceneEditor.Instance.Publish(); MarkDirty();
    }
    public SavedFog Capture(bool history)
    {
        return new SavedFog { enabled = history ? Enabled : true, width = _width, height = _height,
            explored = history ? Convert.ToBase64String(FogVisibility.Pack(_explored)) : "",
            revealed = history ? Convert.ToBase64String(FogVisibility.Pack(_manualVisible)) : "",
            hidden = history ? Convert.ToBase64String(FogVisibility.Pack(_manualHidden)) : "" };
    }
    public void Restore(SavedFog saved, bool pause = true)
    {
        if (_grid == null) _grid = FindAnyObjectByType<GridManager>();
        if (_grid == null) return;
        Resize(_grid.Width, _grid.Height); Enabled = saved?.enabled ?? true;
        if (saved != null && saved.width == _width && saved.height == _height)
        {
            _explored = SavedFog.Decode(saved.explored, _visible.Length);
            _manualVisible = SavedFog.Decode(saved.revealed, _visible.Length);
            _manualHidden = SavedFog.Decode(saved.hidden, _visible.Length);
        }
        if (pause && SceneEditor.Instance != null) SceneEditor.Instance.Model.RevealPaused = true;
        MarkDirty();
    }
    private void Connected(ulong client)
    {
        if (!_network.IsServer || client == NetworkManager.ServerClientId) return;
        _clientAcks.Remove(client);
        Send(client);
    }
    private void ReceiveRequest(ulong sender, FastBufferReader reader)
    { if (_network.IsServer && _network.ConnectedClients.ContainsKey(sender)) Send(sender); }
    private void ReceiveAck(ulong sender, FastBufferReader reader)
    {
        if (_network == null || !_network.IsServer || !_network.ConnectedClients.ContainsKey(sender)) return;
        if (!reader.TryBeginRead(sizeof(int))) return;
        reader.ReadValueSafe(out int revision);
        if (revision > 0 && revision <= _revision) _clientAcks[sender] = revision;
    }
    private void Publish()
    {
        _revision++;
        _encodedState = null; _publishPending = true;
    }
    private void Send(ulong client)
    {
        if (_width <= 0 || !_network.IsServer) return;
        byte[] state = _encodedState ??= FogStateCodec.Encode(_visible, _explored);
        using var writer = new FastBufferWriter(state.Length + 40, Allocator.Temp);
        writer.WriteValueSafe(_revision); writer.WriteValueSafe(_width); writer.WriteValueSafe(_height); writer.WriteValueSafe(Enabled);
        writer.WriteValueSafe(state.Length); writer.WriteBytesSafe(state);
        _network.CustomMessagingManager.SendNamedMessage(StateMessage, client, writer, NetworkDelivery.ReliableFragmentedSequenced);
    }
    private void Receive(ulong sender, FastBufferReader reader)
    {
        if (_network.IsServer || sender != NetworkManager.ServerClientId) return;
        try
        {
            reader.ReadValueSafe(out int revision); reader.ReadValueSafe(out int width); reader.ReadValueSafe(out int height);
            reader.ReadValueSafe(out bool enabled); reader.ReadValueSafe(out int length);
            if (revision <= _received)
            {
                if (revision == _received && revision > 0 && _hasState) _pendingRevisionAck = revision;
                return;
            }
            if (width <= 0 || width > 256 || height <= 0 || height > 256
                || length <= 0 || length > 512 * 1024 || !reader.TryBeginRead(length)) return;
            var payload = new byte[length]; reader.ReadBytesSafe(ref payload, length);
            FogStateCodec.Decode(payload, width * height * 16, out var visible, out var explored);
            if (_width != width || _height != height) Resize(width, height);
            _visible = visible; _explored = explored;
            Enabled = enabled; _received = revision; _hasState = true; _textureDirty = true;
            _pendingRevisionAck = revision;
        }
        catch (Exception ex) { Debug.LogWarning("[Fog] " + ex.Message); }
    }
    private void LateUpdate()
    {
        if (_grid == null) return;
        bool display = ShowingPlayerView && Enabled;
        if (IsMaster && Preview && (PreviewSourceId != null || PreviewTestSource))
        {
            if (PreviewTestSource && Mouse.current != null && Camera.main != null && GameplayInputGate.AllowsWorldPointerInput)
            {
                Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
                if (new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float d))
                {
                    Vector3 p = _grid.WorldToGridCoordinates(ray.GetPoint(d));
                    _testPosition = new Vector2(Mathf.Round(p.x * 4) / 4, Mathf.Round(p.z * 4) / 4);
                    if (_testPosition != _previousTest) { _previewDirty = true; _previousTest = _testPosition; }
                }
            }
            if (_previewDirty)
            {
                var sources = new List<VisionSource>();
                if (PreviewTestSource) sources.Add(new VisionSource(_testPosition, 6));
                else
                {
                    var token = TokenController.FindSceneToken(PreviewSourceId);
                    if (token != null && !token.IsHidden) { Vector3 p = _grid.WorldToGridCoordinates(token.CommittedPosition); sources.Add(new VisionSource(new Vector2(p.x, p.z), token.VisionFeet / 5f)); }
                }
                _previewVisible = new FogVisibility(SceneEditor.Instance?.Model.Snapshot() ?? new SceneGeometry()).Calculate(_width, _height, sources);
                ApplyManualOverrides(_previewVisible, _manualVisible, _manualHidden, Paused);
                _previewDirty = false; _textureDirty = true;
            }
        }
        if (_textureDirty && _width > 0)
        {
            int w = _width * 4, h = _height * 4;
            if (_mask == null || _mask.width != w || _mask.height != h)
            {
                if (_mask != null) Destroy(_mask);
                _mask = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            var colors = new Color32[_visible.Length];
            bool[] current = Preview && (PreviewSourceId != null || PreviewTestSource) && _previewVisible != null ? _previewVisible : _visible;
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(i < current.Length && current[i] ? (byte)255 : (byte)0, _explored[i] ? (byte)255 : (byte)0, 0, 255);
            _mask.SetPixels32(colors); _mask.Apply(false); _textureDirty = false;
        }
        Texture2D displayMask = _width == _grid.Width && _height == _grid.Height ? _mask : null;
        bool appliedToMap = MapController.Instance?.ApplyFog(displayMask, display, _grid) == true;
        if (_cover == null)
        {
            _cover = GameObject.CreatePrimitive(PrimitiveType.Quad); _cover.name = "Unexplored map cover"; _cover.transform.SetParent(transform, false);
            _cover.GetComponent<Collider>().enabled = false; Destroy(_cover.GetComponent<Collider>());
            _coverMaterial = new Material(Resources.Load<Shader>("DiceboundFogCover"));
            _cover.GetComponent<Renderer>().sharedMaterial = _coverMaterial;
            _cover.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        _cover.SetActive(display);
        _cover.transform.SetPositionAndRotation(_grid.GridOrigin + Vector3.up * 0.07f, _grid.GridRotation * Quaternion.Euler(90, 0, 0));
        _cover.transform.localScale = new Vector3(_grid.Width * _grid.CellSize, _grid.Height * _grid.CellSize, 1);
        _coverMaterial.SetTexture("_FogMask", displayMask);
        foreach (var token in FindObjectsByType<TokenController>(FindObjectsInactive.Exclude)) if (token.IsSpawned) token.RefreshFogAppearance();
        if (_pendingRevisionAck > 0 && appliedToMap && displayMask != null && !_textureDirty
            && _network != null && _network.IsListening && !_network.IsServer)
        {
            using var ack = new FastBufferWriter(sizeof(int), Allocator.Temp);
            ack.WriteValueSafe(_pendingRevisionAck);
            _network.CustomMessagingManager.SendNamedMessage(AckMessage, NetworkManager.ServerClientId, ack);
            _pendingRevisionAck = -1;
        }
    }
    public void ApplyMarkupFog(Material material, bool playerView)
    {
        material.SetFloat("_FogEnabled", playerView && Enabled ? 1 : 0);
        if (_grid == null) return;
        material.SetTexture("_FogMask", _width == _grid.Width && _height == _grid.Height ? _mask : null);
        material.SetMatrix("_GridWorldToLocal", Matrix4x4.TRS(_grid.GridOrigin, _grid.GridRotation, Vector3.one).inverse);
        material.SetVector("_GridSize", new Vector4(_grid.Width * _grid.CellSize, _grid.Height * _grid.CellSize, 0, 0));
    }
    public string NearestDoor(ulong client)
    {
        if (_grid == null || SceneEditor.Instance == null || Paused && Enabled) return null;
        string best = null; float distance = 2.25f;
        foreach (var edge in SceneEditor.Instance.Model.Snapshot().edges)
        {
            if (!edge.door) continue;
            Vector3 midpoint = _grid.GridCoordinatesToWorld(new Vector3(edge.x + (edge.vertical ? 0 : 0.5f), 0, edge.y + (edge.vertical ? 0.5f : 0)));
            Vector3 probeA = _grid.GridCoordinatesToWorld(new Vector3(edge.x + (edge.vertical ? -0.125f : 0.5f), 0, edge.y + (edge.vertical ? 0.5f : -0.125f)));
            Vector3 probeB = _grid.GridCoordinatesToWorld(new Vector3(edge.x + (edge.vertical ? 0.125f : 0.5f), 0, edge.y + (edge.vertical ? 0.5f : 0.125f)));
            if (!IsVisible(probeA) && !IsVisible(probeB)) continue;
            foreach (var token in FindObjectsByType<TokenController>(FindObjectsInactive.Exclude))
            {
                if (!token.IsSpawned || token.IsHidden || token.ControllerClientId != client) continue;
                Vector3 a = _grid.WorldToGridCoordinates(token.CommittedPosition), b = _grid.WorldToGridCoordinates(midpoint);
                float d = new Vector2(a.x - b.x, a.z - b.z).sqrMagnitude;
                if (d <= distance) { distance = d; best = edge.id; }
            }
        }
        return best;
    }
    private void OnGUI()
    {
        if (_network == null || !_network.IsListening || !GameplayInputGate.AllowsImGuiOverlays) return;
        if (Preview) GUI.Label(new Rect(12, Screen.height - 42, 350, 28), "Предпросмотр: " + PreviewSourceName, VttUiSkin.ImGuiPanel);
        else if (!IsMaster && NearestDoor(_network.LocalClientId) != null)
            GUI.Label(new Rect(Screen.width * 0.5f - 140, Screen.height - 100, 280, 28), "E — открыть / закрыть дверь", VttUiSkin.ImGuiPanel);
    }
    private void OnDestroy()
    {
        if (_network != null) Unregister();
        if (_mask != null) Destroy(_mask);
        if (_cover != null) Destroy(_cover);
        if (_coverMaterial != null) Destroy(_coverMaterial);
        if (Instance == this) Instance = null;
    }
}
