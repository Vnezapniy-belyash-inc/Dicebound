using System;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Hold T, choose a reaction, release. Only short-lived events are sent over the network.</summary>
public class FunEffects : MonoBehaviour
{
    private const string RequestMessage = "FunRequestV2", ShowMessage = "FunShowV2";
    private const ulong MapTarget = ulong.MaxValue;
    private static readonly string[] Labels = { "Помидор", "Яйцо", "Штамп" };
    private static readonly UTF8Encoding TextEncoding = new(false, true);
    public static FunEffects Instance { get; private set; }
    public static bool IsWheelOpen => Instance != null && Instance._open;
    public static bool IsStampInputOpen => Instance != null && Instance._editingStamp;
    public static bool IsInputOpen => IsWheelOpen || IsStampInputOpen;
    private NetworkManager _network;
    private CustomMessagingManager _messaging;
    private bool _open, _moved;
    private bool _editingStamp, _focusStampInput;
    private string _stampInput = "";
    private Vector2 _wheelCenter, _openingPointer;
    private Vector3 _targetPoint;
    private Vector3 _targetLocalPoint;
    private ulong _targetId;
    private int _selected = -1;
    private readonly Dictionary<ulong, float> _nextRequest = new();
    private readonly List<Reaction> _reactions = new();
    private Texture2D[] _wheelTextures;
    private int _mapEpoch = int.MinValue;
    private sealed class Reaction
    {
        public int Type;
        public ulong Target;
        public Vector3 Point, LocalPoint;
        public Color Color;
        public float Started;
        public string Text, WrappedText;
    }

    public static void EnsureInstance()
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.GetComponent<FunEffects>() == null) nm.gameObject.AddComponent<FunEffects>();
    }
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }
    private void Bind()
    {
        var nm = GetComponent<NetworkManager>();
        var messaging = nm != null && nm.IsListening ? nm.CustomMessagingManager : null;
        if (ReferenceEquals(_messaging, messaging)) return;
        if (_messaging != null)
        {
            _messaging.UnregisterNamedMessageHandler(RequestMessage);
            _messaging.UnregisterNamedMessageHandler(ShowMessage);
        }
        CancelInput(); _reactions.Clear(); _nextRequest.Clear();
        _network = nm; _messaging = messaging;
        if (_messaging == null) return;
        if (nm.IsServer) _messaging.RegisterNamedMessageHandler(RequestMessage, OnRequest);
        else _messaging.RegisterNamedMessageHandler(ShowMessage, OnShow);
    }

    private void Update()
    {
        Bind();
        int epoch = MapSync.Instance != null ? MapSync.Instance.LocalSceneVersion : -1;
        if (epoch != _mapEpoch) { _mapEpoch = epoch; _reactions.Clear(); CancelInput(); }
        _reactions.RemoveAll(r => Time.unscaledTime - r.Started > 6.8f);
        var keyboard = Keyboard.current; var mouse = Mouse.current;
        if (_network == null || !_network.IsConnectedClient || keyboard == null || mouse == null)
        { CancelInput(); return; }
        if (_editingStamp)
        {
            if (!Application.isFocused || keyboard.escapeKey.wasPressedThisFrame
                || HostSceneCurtain.IsBlockingLocalPlayer || HostSceneCurtain.IsLocalSynchronizing
                || MovementReviewUI.IsOpen || PublicReferenceLibrary.IsOpen || SceneArrivalPicker.IsOpen || DisplaySettingsUI.IsOpen)
                CancelInput();
            return;
        }
        if (_open)
        {
            if (!Application.isFocused || keyboard.escapeKey.wasPressedThisFrame || GameplayInputGate.IsTextInputFocused
                || HostSceneCurtain.IsBlockingLocalPlayer || HostSceneCurtain.IsLocalSynchronizing
                || MovementReviewUI.IsOpen || PublicReferenceLibrary.IsOpen || SceneArrivalPicker.IsOpen || DisplaySettingsUI.IsOpen)
            { CancelWheel(); return; }
            Vector2 pointer = ToGui(mouse.position.ReadValue());
            if ((pointer - _openingPointer).sqrMagnitude > 64) _moved = true;
            _selected = _moved ? SelectSector(pointer - _wheelCenter, 42 * LocalUiScale.Value, 160 * LocalUiScale.Value) : -1;
            if (!keyboard.tKey.isPressed)
            {
                int chosen = _selected;
                CancelWheel();
                if (chosen == 2)
                {
                    _editingStamp = true; _focusStampInput = true; _stampInput = "";
                    // Enter belongs to this dialog, not a previously selected game UI button.
                    UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
                }
                else if (chosen >= 0) SendReaction(chosen);
            }
            return;
        }
        if (keyboard.tKey.wasPressedThisFrame && GameplayInputGate.AllowsKeyboardHotkeys && GameplayInputGate.AllowsWorldPointerInput
            && !SceneEditor.IsEditing && !FogManager.IsManualEditing
            && !(MeasurementTool.Instance != null && MeasurementTool.Instance.IsLocalActive)
            && !(EffectPaintTool.Instance != null && EffectPaintTool.Instance.IsActive) && PickTarget(mouse.position.ReadValue()))
        {
            float radius = 150 * LocalUiScale.Value;
            _openingPointer = ToGui(mouse.position.ReadValue());
            _wheelCenter = new Vector2(Mathf.Clamp(_openingPointer.x, radius, Mathf.Max(radius, Screen.width - radius)),
                Mathf.Clamp(_openingPointer.y, radius, Mathf.Max(radius, Screen.height - radius)));
            _open = true; _moved = false; _selected = -1;
        }
    }
    public static int SelectSector(Vector2 delta, float innerRadius, float outerRadius)
    {
        if (delta.magnitude <= innerRadius || delta.magnitude > outerRadius) return -1;
        float sectorSize = 360f / Labels.Length;
        float degrees = Mathf.Repeat(Mathf.Atan2(delta.x, -delta.y) * Mathf.Rad2Deg + sectorSize / 2, 360);
        return Mathf.Min(Labels.Length - 1, Mathf.FloorToInt(degrees / sectorSize));
    }
    private void CancelWheel()
    {
        if (_open) GameplayInputGate.MarkToolExit();
        _open = false; _selected = -1;
    }
    private void CancelInput()
    {
        if (_editingStamp) GameplayInputGate.MarkToolExit();
        _editingStamp = false; _focusStampInput = false; _stampInput = "";
        CancelWheel();
    }
    private void OnApplicationFocus(bool focused) { if (!focused) CancelInput(); }
    private static Vector2 ToGui(Vector2 p) => new(p.x, Screen.height - p.y);

    private bool PickTarget(Vector2 screen)
    {
        var camera = Camera.main;
        if (camera == null) return false;
        var ray = camera.ScreenPointToRay(screen);
        if (Physics.Raycast(ray, out var hit, Mathf.Infinity, camera.cullingMask, QueryTriggerInteraction.Ignore))
        {
            var token = hit.collider.GetComponentInParent<TokenController>();
            if (token != null)
            {
                if (!token.IsSpawned || !token.IsVisibleToLocalPlayer) return false;
                _targetId = token.NetworkObjectId; _targetPoint = hit.point;
                _targetLocalPoint = token.transform.InverseTransformPoint(hit.point); return true;
            }
        }
        var grid = FindAnyObjectByType<GridManager>();
        if (grid == null || !new Plane(grid.transform.up, grid.transform.position).Raycast(ray, out float distance)) return false;
        _targetId = MapTarget; _targetPoint = ray.GetPoint(distance);
        var point = grid.WorldToGridCoordinates(_targetPoint);
        return point.x >= 0 && point.z >= 0 && point.x < grid.gridWidth && point.z < grid.gridHeight
            && (FogManager.Instance == null || !FogManager.Instance.ShowingPlayerView || FogManager.Instance.IsVisibleInDisplayedView(_targetPoint));
    }

    private void SendReaction(int type, string text = "")
    {
        if (_messaging == null) return;
        Vector3 point = _targetPoint;
        if (_targetId != MapTarget)
        {
            var token = FindToken(_targetId);
            if (token == null) return;
            point = token.transform.TransformPoint(_targetLocalPoint);
        }
        if (_network.IsServer) Accept(_network.LocalClientId, type, _targetId, point, text);
        else
        {
            using var writer = new FastBufferWriter(512, Allocator.Temp);
            writer.WriteValueSafe((byte)type); writer.WriteValueSafe(_mapEpoch);
            writer.WriteValueSafe(_targetId); writer.WriteValueSafe(point);
            WriteText(writer, text);
            _messaging.SendNamedMessage(RequestMessage, NetworkManager.ServerClientId, writer);
        }
    }
    private void OnRequest(ulong sender, FastBufferReader reader)
    {
        if (!_network.IsServer || !reader.TryBeginRead(1 + sizeof(int) + sizeof(ulong) + 3 * sizeof(float))) return;
        reader.ReadValueSafe(out byte type); reader.ReadValueSafe(out int epoch);
        reader.ReadValueSafe(out ulong target); reader.ReadValueSafe(out Vector3 point);
        if (epoch == _mapEpoch && TryReadText(ref reader, out string text)) Accept(sender, type, target, point, text);
    }
    private void Accept(ulong sender, int type, ulong target, Vector3 point, string text)
    {
        if (type < 0 || type >= Labels.Length || !_network.ConnectedClients.ContainsKey(sender)
            || !Finite(point) || HostSceneCurtain.Instance?.IsClientSynchronizing(sender) == true
            || sender != NetworkManager.ServerClientId && HostSceneCurtain.IsCurtainDown) return;
        text = type == 2 ? StampText.Normalize(text) : "";
        if (text == null) return;
        if (_nextRequest.TryGetValue(sender, out float next) && Time.unscaledTime < next) return;
        var token = FindToken(target);
        if (target != MapTarget)
        {
            if (token == null || !token.IsSpawned || FogManager.Instance != null && !FogManager.Instance.CanSeeToken(sender, token)
                || token.IsHidden && sender != NetworkManager.ServerClientId) return;
            float limit = Mathf.Max(1, token.transform.lossyScale.magnitude * 2);
            if (Vector3.Distance(token.transform.position, point) > limit) return;
            point = token.transform.InverseTransformPoint(point);
        }
        else
        {
            var grid = FindAnyObjectByType<GridManager>();
            if (grid == null) return;
            var p = grid.WorldToGridCoordinates(point);
            if (p.x < 0 || p.z < 0 || p.x >= grid.gridWidth || p.z >= grid.gridHeight) return;
            if (sender != NetworkManager.ServerClientId && FogManager.Instance != null && !FogManager.Instance.IsVisible(point)) return;
        }
        _nextRequest[sender] = Time.unscaledTime + .7f;
        Color color = PlayerRegistry.GetServerPlayerColor(sender);
        AddReaction(type, target, point, color, text);
        using var writer = new FastBufferWriter(512, Allocator.Temp);
        writer.WriteValueSafe((byte)type); writer.WriteValueSafe(_mapEpoch); writer.WriteValueSafe(target);
        writer.WriteValueSafe(point); writer.WriteValueSafe(new Vector3(color.r, color.g, color.b));
        WriteText(writer, text);
        foreach (ulong client in _network.ConnectedClientsIds)
        {
            if (client == NetworkManager.ServerClientId || HostSceneCurtain.Instance?.IsClientSynchronizing(client) == true) continue;
            if (token != null && (token.IsHidden || FogManager.Instance != null && !FogManager.Instance.CanSeeToken(client, token))) continue;
            _messaging.SendNamedMessage(ShowMessage, client, writer);
        }
    }
    private static bool Finite(Vector3 p) => !float.IsNaN(p.x) && !float.IsNaN(p.y) && !float.IsNaN(p.z)
        && !float.IsInfinity(p.x) && !float.IsInfinity(p.y) && !float.IsInfinity(p.z);
    private TokenController FindToken(ulong id) => _network != null && _network.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj)
        ? obj.GetComponent<TokenController>() : null;
    private void OnShow(ulong sender, FastBufferReader reader)
    {
        if (sender != NetworkManager.ServerClientId || !reader.TryBeginRead(1 + sizeof(int) + sizeof(ulong) + 6 * sizeof(float))) return;
        reader.ReadValueSafe(out byte type); reader.ReadValueSafe(out int epoch); reader.ReadValueSafe(out ulong target);
        reader.ReadValueSafe(out Vector3 point); reader.ReadValueSafe(out Vector3 rgb);
        if (type >= Labels.Length || epoch != _mapEpoch || !Finite(point) || !TryReadText(ref reader, out string text)) return;
        text = type == 2 ? StampText.Normalize(text) : "";
        if (text != null) AddReaction(type, target, point, new Color(rgb.x, rgb.y, rgb.z), text);
    }
    private static void WriteText(FastBufferWriter writer, string text)
    {
        byte[] bytes = TextEncoding.GetBytes(text);
        writer.WriteValueSafe((ushort)bytes.Length);
        writer.WriteBytesSafe(bytes);
    }
    private static bool TryReadText(ref FastBufferReader reader, out string text)
    {
        text = null;
        if (!reader.TryBeginRead(sizeof(ushort))) return false;
        reader.ReadValueSafe(out ushort length);
        if (length > StampText.MaxBytes || !reader.TryBeginRead(length)) return false;
        var bytes = new byte[length]; reader.ReadBytesSafe(ref bytes, length);
        try { text = TextEncoding.GetString(bytes); return true; }
        catch (DecoderFallbackException) { return false; }
    }
    private void AddReaction(int type, ulong target, Vector3 point, Color color, string text)
    {
        if (_reactions.Count >= 48) _reactions.RemoveAt(0);
        _reactions.Add(new Reaction { Type = type, Target = target, Point = point, LocalPoint = point, Color = color, Text = text, Started = Time.unscaledTime });
    }

    private void OnGUI()
    {
        if (GameplayInputGate.AllowsImGuiOverlays && !HostSceneCurtain.IsLocalSynchronizing)
        {
            GUI.depth = -80;
            foreach (var reaction in _reactions) DrawReaction(reaction);
        }
        if (_open) DrawWheel();
        if (_editingStamp) DrawStampInput();
    }
    private void ConfirmStampInput()
    {
        string text = StampText.Normalize(_stampInput);
        if (!_editingStamp || text == null) return;
        CancelInput();
        SendReaction(2, text);
    }
    private void DrawStampInput()
    {
        GUI.depth = -800;
        Color previousColor = GUI.color;
        GUI.color = new Color(0, 0, 0, .65f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
        float scale = Mathf.Max(.25f, Mathf.Min(LocalUiScale.Value, Screen.width / 620f, Screen.height / 260f));
        var rect = new Rect((Screen.width - 560 * scale) / 2, (Screen.height - 220 * scale) / 2, 560 * scale, 220 * scale);
        GUI.Box(rect, "", VttUiSkin.ImGuiPanel);
        var label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(16 * scale), richText = false, wordWrap = true };
        label.normal.textColor = Color.white;
        GUI.Label(new Rect(rect.x + 20 * scale, rect.y + 16 * scale, rect.width - 40 * scale, 28 * scale), "Текст штампа", label);
        Event e = Event.current;
        bool confirmKey = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
        bool cancelKey = e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape;
        // Handle Enter before the field consumes the event; it confirms rather than inserting a newline.
        if (confirmKey || cancelKey) e.Use();
        GUI.SetNextControlName("CustomStampText");
        var fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(18 * scale), richText = false };
        _stampInput = GUI.TextField(new Rect(rect.x + 20 * scale, rect.y + 54 * scale, rect.width - 40 * scale, 40 * scale), _stampInput, StampText.MaxLength, fieldStyle);
        if (_focusStampInput && e.type == EventType.Repaint) { GUI.FocusControl("CustomStampText"); _focusStampInput = false; }
        GUI.Label(new Rect(rect.x + 20 * scale, rect.y + 106 * scale, rect.width - 40 * scale, 48 * scale),
            $"{_stampInput.Length}/{StampText.MaxLength} · длинная надпись переносится на две строки", label);
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && StampText.Normalize(_stampInput) != null;
        bool confirm = GUI.Button(new Rect(rect.x + 20 * scale, rect.yMax - 54 * scale, 250 * scale, 36 * scale), "Подтвердить · Enter", VttUiSkin.ImGuiButton);
        GUI.enabled = enabled;
        bool cancel = GUI.Button(new Rect(rect.xMax - 270 * scale, rect.yMax - 54 * scale, 250 * scale, 36 * scale), "Отмена · Esc", VttUiSkin.ImGuiButton);
        if (cancel || cancelKey) { CancelInput(); GUI.FocusControl(null); }
        else if (confirm || confirmKey) { ConfirmStampInput(); if (!_editingStamp) GUI.FocusControl(null); }
        GUI.color = previousColor;
    }
    private void DrawReaction(Reaction r)
    {
        var camera = Camera.main;
        if (camera == null) return;
        var token = r.Target == MapTarget ? null : FindToken(r.Target);
        if (r.Target != MapTarget && (token == null || !token.IsVisibleToLocalPlayer)) return;
        Vector3 world = token != null ? token.transform.TransformPoint(r.LocalPoint) + token.DisplayPosition - token.transform.position : r.Point;
        if (token == null && FogManager.Instance != null && FogManager.Instance.ShowingPlayerView && !FogManager.Instance.IsVisibleInDisplayedView(world)) return;
        var projected = camera.WorldToScreenPoint(world);
        if (projected.z <= 0) return;
        Vector2 end = ToGui(projected);
        var grid = FindAnyObjectByType<GridManager>();
        float worldWidth = grid != null ? grid.cellSize : 1;
        float size = Mathf.Max(4, Vector3.Distance(projected, camera.WorldToScreenPoint(world + camera.transform.right * worldWidth))) * .75f;
        if (token != null && token.TryGetScreenRect(camera, out var tokenRect)) size = tokenRect.width * .7f;
        if (r.Type >= 2) size = Mathf.Max(30, size) * 1.5f;
        Vector2 stampSize = r.Type == 2 ? GetStampLayout(r, size, out _) : Vector2.zero;
        float age = Time.unscaledTime - r.Started;
        float flight = r.Type < 2 ? .8f : .65f;
        Color previousColor = GUI.color; Matrix4x4 previousMatrix = GUI.matrix;
        if (age < flight)
        {
            if (r.Type >= 2)
            {
                DrawFlyingStamp(r, end, size, stampSize, age / flight, false);
                return;
            }
            float t = Mathf.Clamp01(age / flight), eased = t * t;
            Vector2 start = new Vector2(end.x - Screen.width * .12f, -Screen.height * .18f);
            Vector2 center = Vector2.Lerp(start, end, eased);
            float width = Mathf.Lerp(Mathf.Min(Screen.width, Screen.height) * .28f, size, t);
            GUIUtility.RotateAroundPivot(age * 190, center);
            FunEffectSprites.DrawAsset(r.Type == 0 ? "tomato" : "egg", new Rect(center.x - width / 2, center.y - width / 2, width, width));
        }
        else
        {
            float alpha = Mathf.Clamp01((6.8f - age) / 1f);
            GUI.color = new Color(1, 1, 1, alpha);
            float squash = r.Type < 2 ? 1 + .25f * (1 - Mathf.Clamp01((age - flight) / .15f)) : 1;
            if (r.Type < 2) FunEffectSprites.DrawAsset(r.Type == 0 ? "tomato-splat" : "egg-splat",
                new Rect(end.x - size * squash / 2, end.y - size / 2, size * squash, size));
            else
            {
                float impact = Mathf.Clamp01((age - flight) / .12f);
                DrawStamp(r, end, size * Mathf.Lerp(1.16f, 1, impact), alpha * Mathf.Lerp(.3f, 1, impact));
                if (age - flight < .25f) DrawFlyingStamp(r, end, size, stampSize, (age - flight) / .25f, true);
            }
        }
        GUI.matrix = previousMatrix; GUI.color = previousColor;
    }
    private static void DrawFlyingStamp(Reaction r, Vector2 point, float size, Vector2 impression, float progress, bool recoil)
    {
        Matrix4x4 previousMatrix = GUI.matrix;
        Color previousColor = GUI.color;
        size = Mathf.Max(30, size);
        Vector2 landing = point;
        float eased = progress * progress * progress;
        Vector2 center = recoil ? landing + Vector2.up * size * progress * .6f
            : Vector2.Lerp(new Vector2(landing.x - Screen.width * .10f, -Screen.height * .2f), landing, eased);
        float width = recoil ? impression.x : Mathf.Lerp(Mathf.Max(Mathf.Min(Screen.width, Screen.height) * .35f, impression.x * 2), impression.x, eased);
        float plateHeight = impression.y * width / impression.x;
        float alpha = recoil ? 1 - progress : 1;
        GUIUtility.RotateAroundPivot(Mathf.Lerp(-24, -10, recoil ? 1 : eased), center);
        void Block(Rect rect, Color color)
        {
            GUI.color = new Color(color.r, color.g, color.b, alpha);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }
        // A broad stamp base, short wooden neck and handle visibly press down, then lift off the impression.
        Block(new Rect(center.x - width * .17f, center.y - width * .58f, width * .34f, width * .23f), new Color(.24f, .12f, .065f));
        Block(new Rect(center.x - width * .12f, center.y - width * .55f, width * .24f, width * .15f), new Color(.62f, .34f, .14f));
        Block(new Rect(center.x - width * .07f, center.y - width * .40f, width * .14f, width * .26f), new Color(.43f, .22f, .1f));
        Block(new Rect(center.x - width * .50f, center.y - plateHeight / 2, width, plateHeight), new Color(.12f, .12f, .14f));
        Block(new Rect(center.x - width * .47f, center.y - plateHeight * .42f, width * .94f, plateHeight * .76f), new Color(.55f, .32f, .15f));
        Block(new Rect(center.x - width * .46f, center.y + plateHeight * .34f, width * .92f, plateHeight * .16f), r.Color);
        GUI.matrix = previousMatrix; GUI.color = previousColor;
    }
    private static Vector2 GetStampLayout(Reaction r, float size, out GUIStyle style)
    {
        style = new GUIStyle(GUI.skin.label) {
            fontSize = 100, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
            richText = false, wordWrap = false, padding = new RectOffset(0, 0, 0, 0)
        };
        style.normal.textColor = Color.white;
        if (r.WrappedText == null)
        {
            var measureStyle = style;
            r.WrappedText = StampText.Wrap(r.Text, text => measureStyle.CalcSize(new GUIContent(text)).x, 1200);
        }
        style.fontSize = Mathf.Max(1, Mathf.RoundToInt(size * .32f));
        float padding = Mathf.Max(6, size * .15f);
        float maxWidth = Mathf.Max(40, Mathf.Min(size * 5, Screen.width * .8f));
        float natural = 0;
        foreach (string line in r.WrappedText.Split('\n')) natural = Mathf.Max(natural, style.CalcSize(new GUIContent(line)).x);
        if (natural + 2 * padding > maxWidth)
            style.fontSize = Mathf.Max(1, Mathf.FloorToInt(style.fontSize * Mathf.Max(1, maxWidth - 2 * padding) / natural));
        float width = 0;
        foreach (string line in r.WrappedText.Split('\n')) width = Mathf.Max(width, style.CalcSize(new GUIContent(line)).x);
        int lines = r.WrappedText.Contains("\n") ? 2 : 1;
        return new Vector2(Mathf.Max(Mathf.Min(size * 2, maxWidth), width + 2 * padding), style.lineHeight * lines + 2 * padding);
    }
    private static void DrawStamp(Reaction r, Vector2 point, float size, float alpha)
    {
        Vector2 dimensions = GetStampLayout(r, size, out var style);
        var rect = new Rect(point - dimensions / 2, dimensions);
        float border = Mathf.Max(4, size * .07f);
        GUI.color = new Color(r.Color.r, r.Color.g, r.Color.b, alpha);
        Matrix4x4 previous = GUI.matrix;
        GUIUtility.RotateAroundPivot(-10, point);
        var ink = Texture2D.whiteTexture;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, border), ink);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - border, rect.width, border), ink);
        GUI.DrawTexture(new Rect(rect.x, rect.y, border, rect.height), ink);
        GUI.DrawTexture(new Rect(rect.xMax - border, rect.y, border, rect.height), ink);
        // Extra ink weight complements the bold font without interpreting user text as markup.
        float spread = Mathf.Max(.5f, size * .0075f);
        GUI.Label(new Rect(rect.x - spread, rect.y, rect.width, rect.height), r.WrappedText, style);
        GUI.Label(new Rect(rect.x + spread, rect.y, rect.width, rect.height), r.WrappedText, style);
        GUI.Label(rect, r.WrappedText, style);
        GUI.matrix = previous;
    }
    private void DrawWheel()
    {
        GUI.depth = -300;
        if (_wheelTextures == null)
        {
            _wheelTextures = new Texture2D[Labels.Length + 1];
            for (int selection = -1; selection < Labels.Length; selection++)
            {
                var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                var pixels = new Color32[256 * 256];
                for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
                {
                    var p = new Vector2(x - 127.5f, 127.5f - y);
                    int sector = SelectSector(p, 36, 124);
                    pixels[y * 256 + x] = p.magnitude > 124 ? Color.clear : sector == selection && selection >= 0
                        ? new Color(.18f, .48f, .62f, .98f) : new Color(.055f, .075f, .1f, .96f);
                }
                texture.SetPixels32(pixels); texture.Apply(false, true); _wheelTextures[selection + 1] = texture;
            }
        }
        float radius = 150 * LocalUiScale.Value;
        GUI.DrawTexture(new Rect(_wheelCenter.x - radius, _wheelCenter.y - radius, radius * 2, radius * 2), _wheelTextures[_selected + 1]);
        var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(13 * LocalUiScale.Value), fontStyle = FontStyle.Bold };
        style.normal.textColor = Color.white;
        for (int i = 0; i < Labels.Length; i++)
        {
            float angle = i * (360f / Labels.Length) * Mathf.Deg2Rad;
            Vector2 center = _wheelCenter + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * radius * .65f;
            GUI.Label(new Rect(center.x - radius * .43f, center.y - 18, radius * .86f, 36), Labels[i], style);
        }
        GUI.Label(new Rect(_wheelCenter.x - radius * .3f, _wheelCenter.y - 20, radius * .6f, 40), "Отмена", style);
    }
    private void OnDestroy()
    {
        if (_messaging != null)
        { _messaging.UnregisterNamedMessageHandler(RequestMessage); _messaging.UnregisterNamedMessageHandler(ShowMessage); }
        if (_wheelTextures != null) foreach (var texture in _wheelTextures) if (texture != null) Destroy(texture);
        if (Instance == this) Instance = null;
    }
}
