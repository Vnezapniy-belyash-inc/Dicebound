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
    private const float WheelInnerRatio = .31f;
    private const float StampMaxWidth = 560f;
    private const float StampMaxHeight = 240f;
    private const float StampHorizontalPadding = 44f;
    private const float StampVerticalPadding = 24f;
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
    private GUIStyle _wheelLabel, _stampTitle, _stampHint, _stampField, _stampPrimary, _stampSecondary;
    private float WheelRadius => Mathf.Min(150 * LocalUiScale.Value, Mathf.Min(Screen.width, Screen.height) * .48f);
    private int _mapEpoch = int.MinValue;
    private sealed class Reaction
    {
        public int Type;
        public ulong Target;
        public Vector3 Point, LocalPoint;
        public Color Color;
        public float Started;
        public string Text;
        public GUIContent StampContent;
        public GUIStyle StampStyle;
        public Vector2 StampLayoutSize;
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
            _selected = _moved ? SelectSector(pointer - _wheelCenter, WheelRadius * WheelInnerRatio) : -1;
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
            float radius = WheelRadius;
            _openingPointer = ToGui(mouse.position.ReadValue());
            _wheelCenter = new Vector2(Mathf.Clamp(_openingPointer.x, radius, Mathf.Max(radius, Screen.width - radius)),
                Mathf.Clamp(_openingPointer.y, radius, Mathf.Max(radius, Screen.height - radius)));
            _open = true; _moved = false; _selected = -1;
        }
    }
    public static int SelectSector(Vector2 delta, float innerRadius)
    {
        if (delta.sqrMagnitude <= innerRadius * innerRadius) return -1;
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
        GUI.color = Color.white;
        Color dim = AtlasUi.Forest;
        dim.a = .65f;
        FillGuiRect(new Rect(0, 0, Screen.width, Screen.height), dim);
        float scale = Mathf.Min(LocalUiScale.Value, Screen.width / 500f, Screen.height / 235f);
        var rect = new Rect((Screen.width - 460 * scale) / 2, (Screen.height - 195 * scale) / 2, 460 * scale, 195 * scale);
        Rect Place(float x, float y, float width, float height) =>
            new Rect(rect.x + x * scale, rect.y + y * scale, width * scale, height * scale);

        FillGuiRect(Place(0, 5, 460, 195), AtlasUi.Forest);
        FillGuiRect(rect, AtlasUi.Copper);
        FillGuiRect(Place(1, 1, 458, 193), AtlasUi.Paper);
        FillGuiRect(Place(1, 1, 458, 49), AtlasUi.Forest);

        _stampTitle ??= MakeGuiLabel(AtlasUi.Light, TextAnchor.MiddleLeft, AtlasUi.Title);
        _stampHint ??= MakeGuiLabel(AtlasUi.Muted, TextAnchor.MiddleRight, AtlasUi.Body);
        _stampTitle.fontSize = Mathf.Max(1, Mathf.RoundToInt(24 * scale));
        _stampHint.fontSize = Mathf.Max(1, Mathf.RoundToInt(13 * scale));
        GUI.Label(Place(20, 1, 420, 49), "Текст штампа", _stampTitle);
        Event e = Event.current;
        bool confirmKey = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
        bool cancelKey = e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape;
        // Handle Enter before the field consumes the event; it confirms rather than inserting a newline.
        if (confirmKey || cancelKey) e.Use();
        if (_stampField == null)
            _stampField = MakeGuiLabel(AtlasUi.Ink, TextAnchor.MiddleLeft, AtlasUi.Body);
        _stampField.fontSize = Mathf.Max(1, Mathf.RoundToInt(18 * scale));
        int inset = Mathf.RoundToInt(12 * scale);
        if (_stampField.padding.left != inset)
            _stampField.padding = new RectOffset(inset, inset, 0, 0);
        FillGuiRect(Place(20, 65, 420, 42), AtlasUi.Copper);
        FillGuiRect(Place(21, 66, 418, 40), Color.Lerp(AtlasUi.Paper, Color.white, .6f));
        GUI.SetNextControlName("CustomStampText");
        Color cursorColor = GUI.skin.settings.cursorColor;
        GUI.skin.settings.cursorColor = AtlasUi.Ink;
        _stampInput = GUI.TextField(Place(21, 66, 418, 40), _stampInput, StampText.MaxLength, _stampField);
        GUI.skin.settings.cursorColor = cursorColor;
        if (_focusStampInput && e.type == EventType.Repaint) { GUI.FocusControl("CustomStampText"); _focusStampInput = false; }
        GUI.Label(Place(20, 109, 420, 25), $"{_stampInput.Length}/{StampText.MaxLength}", _stampHint);

        _stampPrimary ??= MakeGuiLabel(AtlasUi.Light, TextAnchor.MiddleCenter, AtlasUi.Body);
        _stampSecondary ??= MakeGuiLabel(AtlasUi.Ink, TextAnchor.MiddleCenter, AtlasUi.Body);
        _stampPrimary.fontSize = _stampSecondary.fontSize = Mathf.Max(1, Mathf.RoundToInt(15 * scale));
        bool enabled = GUI.enabled;
        GUI.enabled = enabled && StampText.Normalize(_stampInput) != null;
        bool confirm = DrawStampButton(Place(234, 143, 206, 36), "Поставить · Enter", AtlasUi.Forest, _stampPrimary);
        GUI.enabled = enabled;
        bool cancel = DrawStampButton(Place(20, 143, 202, 36), "Отмена · Esc", AtlasUi.Raised, _stampSecondary);
        if (cancel || cancelKey) { CancelInput(); GUI.FocusControl(null); }
        else if (confirm || confirmKey) { ConfirmStampInput(); if (!_editingStamp) GUI.FocusControl(null); }
        GUI.color = previousColor;
    }

    private static GUIStyle MakeGuiLabel(Color color, TextAnchor alignment, Font font)
    {
        var style = new GUIStyle { alignment = alignment, font = font, clipping = TextClipping.Clip };
        style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = color;
        return style;
    }

    private static void FillGuiRect(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static bool DrawStampButton(Rect rect, string text, Color color, GUIStyle style)
    {
        if (!GUI.enabled) color = Color.Lerp(color, AtlasUi.Muted, .72f);
        else if (rect.Contains(Event.current.mousePosition)) color = Color.Lerp(color, AtlasUi.Copper, .28f);
        FillGuiRect(rect, color);
        return GUI.Button(rect, text, style);
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
        float stampScale = 1;
        Vector2 stampSize = Vector2.zero;
        if (r.Type >= 2)
        {
            size = Mathf.Max(30, size) * 1.5f;
            if (r.StampStyle == null) PrepareStampText(r);
            stampScale = Mathf.Min(size / 200f,
                Mathf.Min(Screen.width * .74f / r.StampLayoutSize.x, Screen.height * .74f / r.StampLayoutSize.y));
            stampSize = r.StampLayoutSize * stampScale;
        }
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
                DrawStamp(r, end, stampScale * Mathf.Lerp(1.16f, 1, impact), alpha * Mathf.Lerp(.3f, 1, impact));
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
    private static void PrepareStampText(Reaction reaction)
    {
        var style = MakeGuiLabel(Color.white, TextAnchor.MiddleCenter, AtlasUi.Body);
        style.fontStyle = FontStyle.Bold;
        var measure = new GUIContent();
        float Measure(string text)
        {
            measure.text = text;
            return style.CalcSize(measure).x;
        }

        const float textWidth = StampMaxWidth - 2 * StampHorizontalPadding;
        const float textHeight = StampMaxHeight - 2 * StampVerticalPadding;
        string wrapped;
        int lines;
        style.fontSize = 88;
        while (true)
        {
            wrapped = StampText.Wrap(reaction.Text, Measure, textWidth, out lines);
            if (lines * style.lineHeight <= textHeight || style.fontSize <= 4) break;
            style.fontSize -= 4;
        }

        float widestLine = 0;
        foreach (string line in wrapped.Split('\n'))
            widestLine = Mathf.Max(widestLine, Measure(line));
        float height = lines * style.lineHeight + 2 * StampVerticalPadding;
        float width = Mathf.Max(330f, widestLine + 2 * StampHorizontalPadding, height * 2);
        reaction.StampLayoutSize = new Vector2(width, Mathf.Max(height, width / 3.6f));
        reaction.StampStyle = style;
        reaction.StampContent = new GUIContent(wrapped);
    }
    private static void DrawStamp(Reaction r, Vector2 point, float scale, float alpha)
    {
        var rect = new Rect(point - r.StampLayoutSize / 2, r.StampLayoutSize);
        const float border = 13;
        GUI.color = new Color(r.Color.r, r.Color.g, r.Color.b, alpha);
        Matrix4x4 previous = GUI.matrix;
        GUIUtility.ScaleAroundPivot(Vector2.one * scale, point);
        GUIUtility.RotateAroundPivot(-10, point);
        var ink = Texture2D.whiteTexture;
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, border), ink);
        GUI.DrawTexture(new Rect(rect.x, rect.yMax - border, rect.width, border), ink);
        GUI.DrawTexture(new Rect(rect.x, rect.y, border, rect.height), ink);
        GUI.DrawTexture(new Rect(rect.xMax - border, rect.y, border, rect.height), ink);
        var textRect = new Rect(rect.x + StampHorizontalPadding, rect.y + StampVerticalPadding,
            rect.width - 2 * StampHorizontalPadding, rect.height - 2 * StampVerticalPadding);
        GUI.Label(textRect, r.StampContent, r.StampStyle);
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
                var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[256 * 256];
                for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
                {
                    var p = new Vector2(x - 127.5f, 127.5f - y);
                    const float inner = 128 * WheelInnerRatio;
                    float distance = p.magnitude;
                    if (distance >= 127) continue;
                    int sector = SelectSector(p, inner);
                    Color color;
                    if (distance >= 125) color = AtlasUi.Forest;
                    else if (distance >= 121 || Mathf.Abs(distance - inner) < 1.25f) color = AtlasUi.Copper;
                    else if (sector < 0) color = selection < 0 ? AtlasUi.Copper : AtlasUi.Forest;
                    else if (sector != SelectSector(p + Vector2.right * 1.5f, inner)
                        || sector != SelectSector(p + Vector2.up * 1.5f, inner)) color = AtlasUi.Copper;
                    else color = sector == selection ? AtlasUi.Forest : AtlasUi.Paper;
                    pixels[y * 256 + x] = color;
                }
                texture.SetPixels32(pixels); texture.Apply(false, true); _wheelTextures[selection + 1] = texture;
            }
        }
        float radius = WheelRadius;
        Color previous = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(_wheelCenter.x - radius, _wheelCenter.y - radius, radius * 2, radius * 2), _wheelTextures[_selected + 1]);
        _wheelLabel ??= MakeGuiLabel(AtlasUi.Ink, TextAnchor.MiddleCenter, AtlasUi.Body);
        _wheelLabel.fontSize = Mathf.Max(1, Mathf.RoundToInt(radius * .105f));
        _wheelLabel.fontStyle = FontStyle.Bold;
        for (int i = 0; i < Labels.Length; i++)
        {
            float angle = i * (360f / Labels.Length) * Mathf.Deg2Rad;
            Vector2 center = _wheelCenter + new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * radius * .65f;
            _wheelLabel.normal.textColor = i == _selected ? AtlasUi.Light : AtlasUi.Ink;
            GUI.Label(new Rect(center.x - radius * .43f, center.y - radius * .12f, radius * .86f, radius * .24f), Labels[i], _wheelLabel);
        }
        _wheelLabel.normal.textColor = _selected < 0 ? Color.white : AtlasUi.Light;
        GUI.Label(new Rect(_wheelCenter.x - radius * .3f, _wheelCenter.y - radius * .12f, radius * .6f, radius * .24f), "Отмена", _wheelLabel);
        GUI.color = previous;
    }
    private void OnDestroy()
    {
        if (_messaging != null)
        { _messaging.UnregisterNamedMessageHandler(RequestMessage); _messaging.UnregisterNamedMessageHandler(ShowMessage); }
        if (_wheelTextures != null) foreach (var texture in _wheelTextures) if (texture != null) Destroy(texture);
        if (Instance == this) Instance = null;
    }
}
