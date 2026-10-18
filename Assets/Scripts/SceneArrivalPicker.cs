using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Host-only grid preview for choosing an arrival without switching the live scene.</summary>
public sealed class SceneArrivalPicker : MonoBehaviour, IPointerClickHandler
{
    public static bool IsOpen { get; private set; }
    private SceneDefinition _scene;
    private string _transitionId;
    private RectTransform _preview;
    private RawImage _image;
    private Text _selection;
    private Texture2D _texture;
    private GameObject _root;
    private Vector2Int _cell;
    private InputField _xInput, _yInput;
    private string _sourceSceneId;

    public static void Open(SceneTransition transition, CampaignScene target)
    {
        if (IsOpen || transition == null || target?.scene == null || GameNetworkManager.Instance?.IsHost != true) return;
        var root = new GameObject("ArrivalPicker", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var shade = Rect(root.transform, "Shade", Vector2.zero, Vector2.zero);
        shade.anchorMin = Vector2.zero; shade.anchorMax = Vector2.one;
        shade.offsetMin = shade.offsetMax = Vector2.zero;
        shade.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.65f);
        var panel = Rect(root.transform, "Panel", new Vector2(590, 690), Vector2.zero);
        VttUiSkin.Surface(panel.gameObject.AddComponent<Image>(), VttUiSkin.Panel, 10);
        Label(panel, "Точка прибытия · " + target.title, new Vector2(540, 28), new Vector2(0, 314));
        Label(panel, "Нажмите на клетку или введите координаты с нуля.\nСерый — препятствия; жёлтый — токены; линии — стены и двери.", new Vector2(550, 44), new Vector2(0, 270));
        var preview = Rect(panel, "Grid", new Vector2(500, 420), new Vector2(0, 15));
        var picker = preview.gameObject.AddComponent<SceneArrivalPicker>();
        picker._root = root;
        picker._scene = target.scene;
        picker._transitionId = transition.id;
        picker._sourceSceneId = SceneFileStore.ActiveSceneId;
        picker._cell = transition.hasArrivalPoint ? new Vector2Int(transition.arrivalX, transition.arrivalY)
            : new Vector2Int(target.scene.gridWidth / 2, target.scene.gridHeight / 2);
        picker._preview = preview;
        picker._image = preview.gameObject.AddComponent<RawImage>();
        picker._selection = Label(panel, "", new Vector2(550, 32), new Vector2(0, -263));
        picker._xInput = picker.CoordinateInput(panel, "X", new Vector2(-95, -224));
        picker._yInput = picker.CoordinateInput(panel, "Y", new Vector2(95, -224));
        Button(panel, "Сохранить точку", new Vector2(-183, -312), () =>
        {
            SceneFileStore.SetTransitionArrival(picker._transitionId, true, picker._cell.x, picker._cell.y);
            picker.Close();
        });
        Button(panel, "Сбросить", new Vector2(0, -312), () =>
        {
            SceneFileStore.SetTransitionArrival(picker._transitionId, false, 0, 0);
            picker.Close();
        });
        Button(panel, "Отмена · Esc", new Vector2(183, -312), picker.Close);
        IsOpen = true;
        picker.DrawPreview();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_preview, eventData.position, eventData.pressEventCamera, out var point)) return;
        var rect = _preview.rect;
        _cell = new Vector2Int(Mathf.Clamp(Mathf.FloorToInt((point.x - rect.xMin) / rect.width * _scene.gridWidth), 0, _scene.gridWidth - 1),
            Mathf.Clamp(Mathf.FloorToInt((point.y - rect.yMin) / rect.height * _scene.gridHeight), 0, _scene.gridHeight - 1));
        DrawPreview();
    }

    private void DrawPreview()
    {
        const int samples = 4;
        int width = _scene.gridWidth * samples, height = _scene.gridHeight * samples;
        if (_texture == null)
            _texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var pixels = new Color[width * height];
        void PaintCell(int x, int y, Color color)
        {
            if (!Inside(x, y)) return;
            for (int py = 0; py < samples; py++) for (int px = 0; px < samples; px++)
                pixels[(y * samples + py) * width + x * samples + px] = color;
        }
        for (int y = 0; y < _scene.gridHeight; y++)
            for (int x = 0; x < _scene.gridWidth; x++)
                PaintCell(x, y, (x + y) % 2 == 0 ? new Color(0.15f, 0.21f, 0.27f) : new Color(0.19f, 0.25f, 0.32f));
        foreach (var obstacle in _scene.geometry.obstacles)
            if (obstacle != null) PaintCell(obstacle.x, obstacle.y, Color.gray);
        foreach (var token in _scene.tokens)
        {
            if (token == null) continue;
            int x = Mathf.FloorToInt(token.position.x), y = Mathf.FloorToInt(token.position.z);
            PaintCell(x, y, new Color(0.95f, 0.7f, 0.2f));
        }
        PaintCell(_cell.x, _cell.y, VttUiSkin.Green);
        foreach (var edge in _scene.geometry.edges)
        {
            if (edge == null) continue;
            for (int i = 0; i < samples; i++)
            {
                if (edge.door && edge.open && i > 0 && i < samples - 1) continue;
                int x = edge.x * samples + (edge.vertical ? 0 : i), y = edge.y * samples + (edge.vertical ? i : 0);
                x = Mathf.Clamp(x, 0, width - 1); y = Mathf.Clamp(y, 0, height - 1);
                pixels[y * width + x] = edge.door ? edge.open ? VttUiSkin.Green : new Color(1, .55f, .1f) : new Color(.2f, .75f, 1);
            }
        }
        _texture.SetPixels(pixels); _texture.Apply(); _image.texture = _texture;
        bool occupied = Array.Exists(_scene.geometry.obstacles, o => o != null && o.x == _cell.x && o.y == _cell.y)
            || Array.Exists(_scene.tokens, t => t != null && Mathf.FloorToInt(t.position.x) == _cell.x && Mathf.FloorToInt(t.position.z) == _cell.y);
        _selection.text = $"Прибытие: {_cell.x}, {_cell.y} · сетка {_scene.gridWidth} × {_scene.gridHeight}" +
            (occupied ? "\nТочка занята: группа прибудет на ближайшие свободные клетки." : "");
        _xInput?.SetTextWithoutNotify(_cell.x.ToString()); _yInput?.SetTextWithoutNotify(_cell.y.ToString());
    }

    private void Update()
    {
        if (GameNetworkManager.Instance?.IsHost != true || SceneFileStore.ActiveSceneId != _sourceSceneId
            || UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true) Close();
    }
    private InputField CoordinateInput(Transform parent, string axis, Vector2 position)
    {
        Label(parent, axis, new Vector2(20, 28), position + Vector2.left * 90);
        var rect = Rect(parent, axis, new Vector2(150, 28), position);
        VttUiSkin.Surface(rect.gameObject.AddComponent<Image>(), VttUiSkin.Button, 6);
        var input = rect.gameObject.AddComponent<InputField>();
        input.textComponent = Label(rect, "", new Vector2(130, 24), Vector2.zero);
        input.contentType = InputField.ContentType.IntegerNumber; input.characterLimit = 3;
        input.placeholder = Label(rect, axis, new Vector2(130, 24), Vector2.zero);
        input.onEndEdit.AddListener(_ =>
        {
            if (!IsOpen) return;
            if (_xInput != null && _yInput != null && int.TryParse(_xInput.text, out int x) && int.TryParse(_yInput.text, out int y) && Inside(x, y))
            { _cell = new Vector2Int(x, y); DrawPreview(); }
            else { DrawPreview(); DiceUI.Instance?.ShowToolNotice("Координаты должны находиться в пределах сетки; отсчёт начинается с нуля."); }
        });
        return input;
    }

    private bool Inside(int x, int y) => x >= 0 && y >= 0 && x < _scene.gridWidth && y < _scene.gridHeight;
    private void Close() { IsOpen = false; Destroy(_root); }
    private void OnDestroy() { IsOpen = false; if (_texture != null) Destroy(_texture); }

    private static RectTransform Rect(Transform parent, string name, Vector2 size, Vector2 position)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size; rect.anchoredPosition = position; return rect;
    }

    private static Text Label(Transform parent, string text, Vector2 size, Vector2 position)
    {
        var label = Rect(parent, "Label", size, position).gameObject.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = 14;
        label.color = VttUiSkin.Text; label.text = text; label.alignment = TextAnchor.MiddleCenter;
        label.supportRichText = false;
        label.raycastTarget = false; return label;
    }

    private static void Button(Transform parent, string text, Vector2 position, Action callback)
    {
        var rect = Rect(parent, text, new Vector2(170, 36), position);
        var image = rect.gameObject.AddComponent<Image>(); VttUiSkin.Surface(image, VttUiSkin.Button, 6);
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
        button.onClick.AddListener(() => callback()); Label(rect, text, new Vector2(160, 32), Vector2.zero);
    }
}
