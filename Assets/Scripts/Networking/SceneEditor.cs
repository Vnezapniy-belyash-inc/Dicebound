using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>GM geometry editor. Full revisioned snapshots use NGO's fragmented reliable delivery.</summary>
public sealed class SceneEditor : MonoBehaviour
{
    public enum Tool { None, Wall, EraseEdge, Door, ToggleDoor, Square, Column, EraseObstacle }
    public static SceneEditor Instance { get; private set; }
    public static bool IsEditing => Instance != null && Instance.ActiveTool != Tool.None;
    public Tool ActiveTool { get; private set; }
    public bool ShowMarkup { get; private set; } = true;
    public bool PlayerShowMarkup { get; private set; } = true;
    public float ColumnDiameter { get; set; } = 0.5f;
    public SceneGeometryModel Model { get; } = new();
    public string Notice { get; private set; } = "Выберите инструмент и рисуйте на карте. ПКМ / Esc — закончить.";
    private const string StateMessage = "SceneGeometryV1", RequestMessage = "SceneGeometryRequestV1", AckMessage = "SceneGeometryAckV1";
    private const string DoorMessage = "SceneDoorInteractV1";
    private readonly Dictionary<ulong, float> _doorRequests = new();
    private readonly Dictionary<ulong, int> _clientAcks = new();
    private const int MaxStateBytes = 768 * 1024;
    private NetworkManager _network;
    private GridManager _grid;
    private GameObject _visualRoot;
    private readonly Dictionary<string, GameObject> _visualObjects = new();
    private Material _wallMaterial, _doorMaterial, _openMaterial, _obstacleMaterial;
    private bool _stroke, _dirty, _changed;
    private Vector2Int _lastNode;
    private string _beforeStroke;
    private SavedFog _beforeFog;
    private int _revision, _receivedRevision = -1;
    private int _pendingRevisionAck = -1;
    private Vector3 _lastOrigin;
    private Quaternion _lastRotation;
    private float _lastCellSize;
    private float _nextRequest;
    private bool _hasState;
    private int _loadGeneration;
    public bool IsMaster => _network != null && _network.IsListening && _network.IsHost;
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

    private void Awake() { Instance = this; }
    private void Update()
    {
        var manager = NetworkManager.Singleton;
        if (manager != null && manager.IsListening && _network != manager) Register(manager);
        if (_network != null && !_network.IsListening) Unregister();
        if (_grid == null) _grid = FindAnyObjectByType<GridManager>();
        if (_network != null && !_network.IsServer && !_hasState && Time.unscaledTime >= _nextRequest)
        {
            _nextRequest = Time.unscaledTime + 2;
            using var writer = new FastBufferWriter(1, Allocator.Temp);
            _network.CustomMessagingManager.SendNamedMessage(RequestMessage, NetworkManager.ServerClientId, writer);
        }
        if (ActiveTool == Tool.None || !IsMaster || _grid == null) return;
        var mouse = Mouse.current;
        if (mouse == null) return;
        if (GameplayInputGate.AllowsKeyboardHotkeys && Keyboard.current?.escapeKey.wasPressedThisFrame == true
            || GameplayInputGate.AllowsWorldPointerInput && mouse.rightButton.wasPressedThisFrame)
        { Deactivate(); return; }
        if (mouse.leftButton.wasReleasedThisFrame) FinishStroke();
        if (!GameplayInputGate.AllowsWorldPointerInput) { FinishStroke(); return; }
        if (mouse.leftButton.wasPressedThisFrame)
        {
            _beforeStroke = JsonUtility.ToJson(Model.Snapshot());
            _beforeFog = FogManager.Instance?.Capture(true);
            _stroke = true; _changed = false; _lastNode = new Vector2Int(-1, -1);
        }
        if (_stroke && mouse.leftButton.isPressed) Paint(mouse.position.ReadValue());
    }
    private void LateUpdate()
    {
        if (_grid == null) return;
        if (_dirty || _lastOrigin != _grid.GridOrigin || _lastRotation != _grid.GridRotation || _lastCellSize != _grid.CellSize)
            RebuildVisuals();
        if (_pendingRevisionAck > 0 && _network != null && _network.IsListening)
        {
            using var ack = new FastBufferWriter(sizeof(int), Allocator.Temp);
            ack.WriteValueSafe(_pendingRevisionAck);
            _network.CustomMessagingManager.SendNamedMessage(AckMessage, NetworkManager.ServerClientId, ack);
            _pendingRevisionAck = -1;
        }
        bool playerView = !IsMaster || FogManager.Instance?.Preview == true;
        if (_visualRoot != null) _visualRoot.SetActive(_network != null && _network.IsListening && (playerView ? PlayerShowMarkup : ShowMarkup));
        foreach (var material in new[] { _wallMaterial, _doorMaterial, _openMaterial, _obstacleMaterial })
            if (material != null) FogManager.Instance?.ApplyMarkupFog(material, playerView);
    }
    private void Register(NetworkManager manager)
    {
        _network = manager; _revision = 0; _receivedRevision = -1; _hasState = manager.IsServer;
        manager.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, ReceiveState);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(RequestMessage, ReceiveRequest);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(AckMessage, ReceiveAck);
        manager.CustomMessagingManager.RegisterNamedMessageHandler(DoorMessage, ReceiveDoor);
        manager.OnClientConnectedCallback += Connected;
        if (!manager.IsServer) Model.Replace(new SceneGeometry());
        _dirty = true;
    }
    private void Unregister()
    {
        Deactivate();
        if (_network.CustomMessagingManager != null)
        {
            _network.CustomMessagingManager.UnregisterNamedMessageHandler(StateMessage);
            _network.CustomMessagingManager.UnregisterNamedMessageHandler(RequestMessage);
            _network.CustomMessagingManager.UnregisterNamedMessageHandler(AckMessage);
            _network.CustomMessagingManager.UnregisterNamedMessageHandler(DoorMessage);
        }
        _network.OnClientConnectedCallback -= Connected;
        _clientAcks.Clear();
        _pendingRevisionAck = -1;
        _network = null; _hasState = false;
        _doorRequests.Clear();
        Model.Replace(new SceneGeometry()); _dirty = true;
    }
    private void Connected(ulong client)
    {
        if (!_network.IsServer || client == NetworkManager.ServerClientId) return;
        _clientAcks.Remove(client);
        SendState(client);
    }
    private void ReceiveRequest(ulong client, FastBufferReader reader)
    { if (_network.IsServer && _network.ConnectedClients.ContainsKey(client)) SendState(client); }
    private void ReceiveAck(ulong client, FastBufferReader reader)
    {
        if (_network == null || !_network.IsServer || !_network.ConnectedClients.ContainsKey(client)) return;
        if (!reader.TryBeginRead(sizeof(int))) return;
        reader.ReadValueSafe(out int revision);
        if (revision > 0 && revision <= _revision) _clientAcks[client] = revision;
    }
    private void ReceiveState(ulong sender, FastBufferReader reader)
    {
        if (_network.IsServer || sender != NetworkManager.ServerClientId || _grid == null) return;
        try
        {
            reader.ReadValueSafe(out int revision); reader.ReadValueSafe(out int width); reader.ReadValueSafe(out int height);
            reader.ReadValueSafe(out float size); reader.ReadValueSafe(out Vector3 origin); reader.ReadValueSafe(out Quaternion rotation);
            reader.ReadValueSafe(out int length);
            if (revision <= _receivedRevision)
            {
                if (revision == _receivedRevision && revision > 0 && _hasState) _pendingRevisionAck = revision;
                return;
            }
            if (length <= 0 || length > MaxStateBytes || !reader.TryBeginRead(length)) return;
            var bytes = new byte[length]; reader.ReadBytesSafe(ref bytes, length);
            var geometry = JsonUtility.FromJson<SceneGeometry>(Encoding.UTF8.GetString(bytes));
            if (width <= 0 || width > 256 || height <= 0 || height > 256 || !SceneValidation.Finite(size) || size < 0.1f || size > 10) return;
            SceneValidation.Geometry(geometry, width, height);
            if (_grid.Width != width || _grid.Height != height || _grid.CellSize != size
                || _grid.GridOrigin != origin || _grid.GridRotation != rotation)
                _grid.RestoreGrid(width, height, size, origin, rotation);
            TokenController.RebuildCellOccupancy();
            Model.Replace(geometry); _receivedRevision = revision; _hasState = true; _dirty = true;
            _pendingRevisionAck = revision;
        }
        catch (Exception ex) { Debug.LogWarning("[Scene] Invalid geometry snapshot: " + ex.Message); }
    }
    private void SendState(ulong client)
    {
        if (_grid == null || _network == null || !_network.IsServer) return;
        byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(Model.Snapshot()));
        if (bytes.Length > MaxStateBytes) { Notice = "Разметка слишком велика для передачи."; return; }
        using var writer = new FastBufferWriter(bytes.Length + 96, Allocator.Temp);
        writer.WriteValueSafe(_revision); writer.WriteValueSafe(_grid.Width); writer.WriteValueSafe(_grid.Height);
        writer.WriteValueSafe(_grid.CellSize); writer.WriteValueSafe(_grid.GridOrigin); writer.WriteValueSafe(_grid.GridRotation);
        writer.WriteValueSafe(bytes.Length); writer.WriteBytesSafe(bytes);
        _network.CustomMessagingManager.SendNamedMessage(StateMessage, client, writer, NetworkDelivery.ReliableFragmentedSequenced);
    }
    public void Publish()
    {
        _dirty = true;
        FogManager.Instance?.MarkDirty();
        if (!IsMaster) return;
        _revision++;
        foreach (ulong client in _network.ConnectedClientsIds)
            if (client != NetworkManager.ServerClientId) SendState(client);
    }
    public void Activate(Tool tool)
    {
        if (NetworkManager.Singleton?.IsHost != true) return;
        FogManager.Instance?.StopManual();
        if (FogManager.Instance?.Preview == true) FogManager.Instance.TogglePreview();
        FinishStroke(); MeasurementTool.Instance?.Deactivate(); EffectPaintTool.Instance?.Deactivate();
        ActiveTool = tool; ShowMarkup = true;
        string[] names = { "выбор", "кисть стен", "ластик рёбер", "дверь", "открытие / закрытие дверей",
            "квадратное препятствие", "круглая колонна", "ластик препятствий" };
        Notice = "Разметка: " + names[(int)tool] + ". ПКМ / Esc — закончить.";
    }
    public void Deactivate() { FinishStroke(); if (ActiveTool != Tool.None) GameplayInputGate.MarkToolExit(); ActiveTool = Tool.None; }
    public void ToggleMarkup() { ShowMarkup = !ShowMarkup; if (!ShowMarkup) Deactivate(); }
    public void Undo()
    {
        if (!IsMaster) return;
        FinishStroke();
        GameMasterUndo.Undo();
    }
    public void Replace(SceneGeometry geometry)
    { Deactivate(); Model.Replace(geometry); Model.RevealPaused = true; Publish(); }
    public void RevealAfterMapTransfer()
    {
        LateJoinSync.EnsureInstance();
        LateJoinSync.Instance?.SynchronizeCurrentWorldForAll();
        StartCoroutine(WaitForMapTransfer(++_loadGeneration));
    }
    private System.Collections.IEnumerator WaitForMapTransfer(int generation)
    {
        float deadline = Time.unscaledTime + (MapSync.Instance?.TransferWaitSeconds ?? 40);
        while (generation == _loadGeneration && IsMaster && Time.unscaledTime < deadline
            && (MapSync.Instance == null || !MapSync.Instance.AllClientsHaveCurrentMap
                || LateJoinSync.Instance == null || !LateJoinSync.Instance.AllSceneWorldClientsReady
                || !AllClientsHaveCurrentRevision || FogManager.Instance == null
                || !FogManager.Instance.AllClientsHaveCurrentRevision
                || !TokenImageSync.AllClientsHaveCurrentPortraits
                || InitiativeTracker.Instance != null && !InitiativeTracker.Instance.AllClientsHaveCurrentState))
            yield return null;
        if (generation != _loadGeneration || !IsMaster) yield break;
        if (MapSync.Instance?.AllClientsHaveCurrentMap == true
            && LateJoinSync.Instance?.AllSceneWorldClientsReady == true
            && AllClientsHaveCurrentRevision && FogManager.Instance?.AllClientsHaveCurrentRevision == true
            && TokenImageSync.AllClientsHaveCurrentPortraits
            && (InitiativeTracker.Instance == null || InitiativeTracker.Instance.AllClientsHaveCurrentState))
        {
            if (HostSceneCurtain.IsCurtainDown) HostSceneCurtain.Instance?.ToggleCurtainOnHost();
        }
        else DiceUI.Instance?.ShowToolNotice("Не все игроки получили карту, объекты, портреты, разметку и туман сцены. Занавес оставлен закрытым; можно повторить передачу или показать карту вручную.");
    }
    private void FinishStroke()
    {
        if (!_stroke) return;
        _stroke = false;
        if (!_changed) return;
        Publish();
        string previous = _beforeStroke;
        var memory = _beforeFog;
        GameMasterUndo.Record("разметка карты", () => { Model.Replace(JsonUtility.FromJson<SceneGeometry>(previous)); if (memory != null) FogManager.Instance?.Restore(memory, false); Publish(); });
    }

    public void RequestDoorToggle(string id)
    {
        if (_network == null || !_network.IsListening) return;
        if (IsMaster) { ToggleDoor(id); return; }
        using var writer = new FastBufferWriter(100, Allocator.Temp);
        writer.WriteValueSafe(new FixedString64Bytes(id));
        _network.CustomMessagingManager.SendNamedMessage(DoorMessage, NetworkManager.ServerClientId, writer);
    }
    private void ReceiveDoor(ulong sender, FastBufferReader reader)
    {
        if (!IsMaster || !_network.ConnectedClients.ContainsKey(sender)) return;
        if (_doorRequests.TryGetValue(sender, out float previous) && Time.unscaledTime - previous < 0.3f) return;
        _doorRequests[sender] = Time.unscaledTime;
        try
        {
            reader.ReadValueSafe(out FixedString64Bytes id);
            if (FogManager.Instance?.NearestDoor(sender) == id.ToString()) ToggleDoor(id.ToString());
        }
        catch (Exception ex) { Debug.LogWarning("[Door] " + ex.Message); }
    }
    private void ToggleDoor(string id)
    {
        foreach (var edge in Model.Snapshot().edges)
        {
            if (edge.id != id || !edge.door) continue;
            string before = JsonUtility.ToJson(Model.Snapshot()); var memory = FogManager.Instance?.Capture(true);
            GameMasterUndo.Record("открытие двери", () => { Model.Replace(JsonUtility.FromJson<SceneGeometry>(before)); if (memory != null) FogManager.Instance?.Restore(memory, false); Publish(); });
            Model.SetEdge(edge.x, edge.y, edge.vertical, true, false, true); Publish(); return;
        }
    }
    private void Paint(Vector2 screen)
    {
        var camera = Camera.main;
        if (camera == null) return;
        Ray ray = camera.ScreenPointToRay(screen);
        if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance)) return;
        Vector3 world = ray.GetPoint(distance);
        if (!_grid.IsPointOnMap(world)) { _lastNode = new Vector2Int(-1, -1); return; }
        Vector3 point = _grid.WorldToGridCoordinates(world);
        if (point.x < 0 || point.z < 0 || point.x > _grid.Width || point.z > _grid.Height) return;
        bool changed = false;
        if (ActiveTool >= Tool.Square)
        {
            var cell = _grid.GetGridPosition(world);
            if (cell.x < 0) return;
            changed = Model.SetObstacle(cell.x, cell.y, ActiveTool == Tool.Column, ColumnDiameter, ActiveTool == Tool.EraseObstacle);
        }
        else if (ActiveTool == Tool.Door || ActiveTool == Tool.ToggleDoor)
        {
            // Door toggles once per click, rather than flickering every frame while held.
            if (Mouse.current.leftButton.wasPressedThisFrame)
                changed = PaintNearestEdge(point);
        }
        else
        {
            var node = new Vector2Int(Mathf.Clamp(Mathf.RoundToInt(point.x), 0, _grid.Width),
                Mathf.Clamp(Mathf.RoundToInt(point.z), 0, _grid.Height));
            if (_lastNode.x < 0) changed = PaintNearestEdge(point);
            else
            {
                var cursor = _lastNode;
                while (cursor != node)
                {
                    var next = cursor;
                    if (Mathf.Abs(node.x - cursor.x) >= Mathf.Abs(node.y - cursor.y) && cursor.x != node.x) next.x += Math.Sign(node.x - cursor.x);
                    else next.y += Math.Sign(node.y - cursor.y);
                    changed |= ApplyEdge(Mathf.Min(cursor.x, next.x), Mathf.Min(cursor.y, next.y), cursor.x == next.x);
                    cursor = next;
                }
            }
            _lastNode = node;
        }
        _changed |= changed; _dirty |= changed;
    }
    private bool PaintNearestEdge(Vector3 point)
    {
        bool vertical = Mathf.Abs(point.x - Mathf.Round(point.x)) < Mathf.Abs(point.z - Mathf.Round(point.z));
        int x = Mathf.Clamp(vertical ? Mathf.RoundToInt(point.x) : Mathf.FloorToInt(point.x), 0, vertical ? _grid.Width : _grid.Width - 1);
        int y = Mathf.Clamp(vertical ? Mathf.FloorToInt(point.z) : Mathf.RoundToInt(point.z), 0, vertical ? _grid.Height - 1 : _grid.Height);
        return ApplyEdge(x, y, vertical);
    }
    private bool ApplyEdge(int x, int y, bool vertical) => Model.SetEdge(x, y, vertical,
        ActiveTool == Tool.Door, ActiveTool == Tool.EraseEdge, ActiveTool == Tool.ToggleDoor);

    private Material Material(ref Material material, Color color)
    {
        if (material != null) return material;
        material = new Material(Resources.Load<Shader>("DiceboundMarkup"));
        material.color = color; return material;
    }
    private void RebuildVisuals()
    {
        _dirty = false; _lastOrigin = _grid.GridOrigin; _lastRotation = _grid.GridRotation; _lastCellSize = _grid.CellSize;
        if (_visualRoot == null)
        {
            _visualRoot = new GameObject("Scene markup (GM)");
            _visualRoot.transform.SetParent(transform, false);
        }
        var seen = new HashSet<string>();
        foreach (var edge in Model.Snapshot().edges)
        {
            seen.Add(edge.id);
            var go = GetVisual(edge.id, "Edge", null);
            var line = go.GetComponent<LineRenderer>();
            if (line == null) line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true; line.positionCount = 2;
            line.startWidth = line.endWidth = _grid.CellSize * (edge.door ? 0.09f : 0.05f);
            line.sharedMaterial = edge.door ? edge.open ? Material(ref _openMaterial, new Color(0.2f, 0.8f, 0.4f))
                : Material(ref _doorMaterial, new Color(1, 0.65f, 0.15f)) : Material(ref _wallMaterial, new Color(0.1f, 0.75f, 1));
            Vector3 a = _grid.GridCoordinatesToWorld(new Vector3(edge.x, 0.06f, edge.y));
            Vector3 b = _grid.GridCoordinatesToWorld(new Vector3(edge.x + (edge.vertical ? 0 : 1), 0.06f, edge.y + (edge.vertical ? 1 : 0)));
            if (edge.open) { Vector3 full = b - a; a += full * 0.35f; b -= full * 0.35f; }
            line.SetPosition(0, a); line.SetPosition(1, b); line.numCapVertices = 3;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        foreach (var obstacle in Model.Snapshot().obstacles)
        {
            seen.Add(obstacle.id);
            var go = GetVisual(obstacle.id, obstacle.round ? "Column" : "Square obstacle",
                obstacle.round ? PrimitiveType.Cylinder : PrimitiveType.Cube);
            go.transform.position = _grid.GetCellCenter(obstacle.x, obstacle.y, 0.045f);
            go.transform.rotation = _grid.GridRotation;
            float size = _grid.CellSize * (obstacle.round ? obstacle.diameter : 1);
            go.transform.localScale = new Vector3(size, obstacle.round ? 0.025f : 0.05f, size);
            go.GetComponent<Renderer>().sharedMaterial = Material(ref _obstacleMaterial, new Color(0.55f, 0.25f, 0.75f));
        }
        var gone = new List<string>();
        foreach (var visual in _visualObjects)
            if (!seen.Contains(visual.Key)) { visual.Value.SetActive(false); Destroy(visual.Value); gone.Add(visual.Key); }
        foreach (string id in gone) _visualObjects.Remove(id);
    }
    private GameObject GetVisual(string id, string name, PrimitiveType? primitive)
    {
        if (_visualObjects.TryGetValue(id, out var existing) && existing != null)
        {
            if (existing.name == name) return existing;
            existing.SetActive(false); Destroy(existing);
        }
        var go = primitive.HasValue ? GameObject.CreatePrimitive(primitive.Value) : new GameObject(name);
        go.name = name; go.transform.SetParent(_visualRoot.transform, false);
        var collider = go.GetComponent<Collider>();
        if (collider != null) { collider.enabled = false; Destroy(collider); }
        _visualObjects[id] = go;
        return go;
    }
    private void OnDestroy()
    {
        if (_network != null) Unregister();
        if (_visualRoot != null) Destroy(_visualRoot);
        foreach (var material in new[] { _wallMaterial, _doorMaterial, _openMaterial, _obstacleMaterial })
            if (material != null) Destroy(material);
        if (Instance == this) Instance = null;
    }
    private void OnGUI()
    {
        if (_network == null || !_network.IsListening || IsMaster || !GameplayInputGate.AllowsImGuiOverlays) return;
        if (GUI.Button(new Rect(12, Screen.height - 72, 180, 28), PlayerShowMarkup ? "Скрыть стены и двери" : "Показать стены и двери"))
            PlayerShowMarkup = !PlayerShowMarkup;
    }
}
