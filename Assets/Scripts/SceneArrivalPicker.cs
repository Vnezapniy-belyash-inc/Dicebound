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
        var panel = Rect(root.transform, "Panel", new Vector2(590, 650), Vector2.zero);
        VttUiSkin.Surface(panel.gameObject.AddComponent<Image>(), VttUiSkin.Panel, 10);
        Label(panel, "Точка прибытия · " + target.title, new Vector2(540, 28), new Vector2(0, 294));
        Label(panel, "Нажмите на клетку. Серые клетки — препятствия, жёлтые — токены.", new Vector2(550, 36), new Vector2(0, 255));
        var preview = Rect(panel, "Grid", new Vector2(500, 450), new Vector2(0, 4));
        var picker = preview.gameObject.AddComponent<SceneArrivalPicker>();
        picker._root = root;
        picker._scene = target.scene;
        picker._transitionId = transition.id;
        picker._cell = transition.hasArrivalPoint ? new Vector2Int(transition.arrivalX, transition.arrivalY)
            : new Vector2Int(target.scene.gridWidth / 2, target.scene.gridHeight / 2);
        picker._preview = preview;
        picker._image = preview.gameObject.AddComponent<RawImage>();
        picker._selection = Label(panel, "", new Vector2(540, 28), new Vector2(0, -243));
        Button(panel, "Сохранить точку", new Vector2(-183, -292), () =>
        {
            SceneFileStore.SetTransitionArrival(picker._transitionId, true, picker._cell.x, picker._cell.y);
            picker.Close();
        });
        Button(panel, "Сбросить", new Vector2(0, -292), () =>
        {
            SceneFileStore.SetTransitionArrival(picker._transitionId, false, 0, 0);
            picker.Close();
        });
        Button(panel, "Отмена", new Vector2(183, -292), picker.Close);
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
        if (_texture == null)
            _texture = new Texture2D(_scene.gridWidth, _scene.gridHeight, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        var pixels = new Color[_scene.gridWidth * _scene.gridHeight];
        for (int y = 0; y < _scene.gridHeight; y++)
            for (int x = 0; x < _scene.gridWidth; x++)
                pixels[y * _scene.gridWidth + x] = (x + y) % 2 == 0 ? new Color(0.15f, 0.21f, 0.27f) : new Color(0.19f, 0.25f, 0.32f);
        foreach (var obstacle in _scene.geometry.obstacles)
            if (obstacle != null && Inside(obstacle.x, obstacle.y)) pixels[obstacle.y * _scene.gridWidth + obstacle.x] = Color.gray;
        foreach (var token in _scene.tokens)
        {
            if (token == null) continue;
            int x = Mathf.FloorToInt(token.position.x), y = Mathf.FloorToInt(token.position.z);
            if (Inside(x, y)) pixels[y * _scene.gridWidth + x] = new Color(0.95f, 0.7f, 0.2f);
        }
        if (Inside(_cell.x, _cell.y)) pixels[_cell.y * _scene.gridWidth + _cell.x] = VttUiSkin.Green;
        _texture.SetPixels(pixels); _texture.Apply(); _image.texture = _texture;
        _selection.text = $"Прибытие: {_cell.x}, {_cell.y} · сетка {_scene.gridWidth} × {_scene.gridHeight}";
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
