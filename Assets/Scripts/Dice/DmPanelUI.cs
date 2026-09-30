using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Host-only controls built on the existing map and initiative APIs.</summary>
public class DmPanelUI : MonoBehaviour
{
    public static DmPanelUI Instance { get; private set; }

    private Canvas _canvas;
    private Font _font;
    private GameObject _panel;
    private GameObject[] _pages;
    private Image[] _tabs;
    private int _page;
    private RectTransform _playersContent;
    private Text _playerHint;
    private Text _mapScaleText;
    private InputField _mapScaleInput;
    private Text _curtainButtonText;
    private Text _initiativeHint;
    private string _playerSignature;
    private float _nextRefresh;

    private static bool IsLocalHost =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildUI();
    }

    private void Update()
    {
        if (_panel == null || !_panel.activeSelf) return;
        if (!IsLocalHost) { _panel.SetActive(false); return; }
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 1f;
        Refresh();
    }

    public void Toggle()
    {
        if (!IsLocalHost || _panel == null) return;
        _panel.SetActive(!_panel.activeSelf);
        if (_panel.activeSelf) Refresh();
    }

    private void BuildUI()
    {
        var root = new GameObject("DmCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        _canvas = root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 30;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _panel = Box(root.transform, "Dungeon Master", VttUiSkin.Panel, 12);
        Place(_panel, new Vector2(1, 1), new Vector2(1, 1),
            new Vector2(410, 620), new Vector2(-14, -68));
        _panel.SetActive(false);
        Label(_panel.transform, "ПАНЕЛЬ DM", 17, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(310, 34),
            new Vector2(18, -12), true);
        Button(_panel.transform, "×", 32, 30, 362, -14,
            () => _panel.SetActive(false), VttUiSkin.Button);

        _pages = new GameObject[3];
        _tabs = new Image[3];
        string[] titles = { "Карта", "Игроки", "Инициатива" };
        for (int i = 0; i < 3; i++)
        {
            int pageIndex = i;
            var tab = Button(_panel.transform, titles[i], 120, 34, 18 + i * 127, -58,
                () => ShowPage(pageIndex), VttUiSkin.Button);
            _tabs[i] = tab.GetComponent<Image>();
            _pages[i] = new GameObject(titles[i] + "Page", typeof(RectTransform));
            _pages[i].transform.SetParent(_panel.transform, false);
            Place(_pages[i], new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(374, 506), new Vector2(18, -104));
        }
        BuildMapPage(_pages[0].transform);
        BuildPlayersPage(_pages[1].transform);
        BuildInitiativePage(_pages[2].transform);
        ShowPage(0);
    }

    private void ShowPage(int index)
    {
        _page = index;
        for (int i = 0; i < _pages.Length; i++)
        {
            _pages[i].SetActive(i == index);
            _tabs[i].color = i == index ? new Color(0.11f, 0.29f, 0.48f) : VttUiSkin.Button;
        }
        Refresh();
    }

    private void BuildMapPage(Transform parent)
    {
        Label(parent, "Карта сессии", 16, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(340, 30),
            new Vector2(0, -4), true);
        Label(parent, "Загрузите изображение. Оно появится у всех игроков.", 13,
            VttUiSkin.Muted, TextAnchor.UpperLeft, new Vector2(0, 1),
            new Vector2(0, 1), new Vector2(360, 42), new Vector2(0, -38));
        Button(parent, "Загрузить изображение", 350, 42, 0, -90,
            () => MapController.Instance?.LoadImage(), new Color(0.10f, 0.30f, 0.48f));

        var scaleBox = Box(parent, "ScaleControls", VttUiSkin.Raised, 9);
        Place(scaleBox, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(350, 102), new Vector2(0, -151));
        Label(scaleBox.transform, "Масштаб карты", 14, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(280, 25),
            new Vector2(14, -9), true);
        Button(scaleBox.transform, "−", 54, 42, 14, -47,
            () => AdjustScale(-0.25f), VttUiSkin.Button);
        var scaleInputBox = Box(scaleBox.transform, "ExactScale", VttUiSkin.Button, 7);
        Place(scaleInputBox, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(160, 42), new Vector2(84, -47));
        _mapScaleText = Label(scaleInputBox.transform, "1.00", 17, VttUiSkin.Text,
            TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(148, 36), Vector2.zero, true);
        _mapScaleInput = scaleInputBox.AddComponent<InputField>();
        _mapScaleInput.textComponent = _mapScaleText;
        _mapScaleInput.contentType = InputField.ContentType.DecimalNumber;
        _mapScaleInput.text = "1.00";
        _mapScaleInput.onEndEdit.AddListener(ApplyExactScale);
        Button(scaleBox.transform, "+", 54, 42, 282, -47,
            () => AdjustScale(0.25f), VttUiSkin.Button);

        Button(parent, "Повернуть на 90°", 170, 40, 0, -275,
            () => MapController.Instance?.Rotate90(), VttUiSkin.Button);
        Button(parent, "Сбросить положение", 170, 40, 180, -275,
            () => MapController.Instance?.ResetMapPosition(), VttUiSkin.Button);
        var curtainButton = Button(parent, "Скрыть карту от игроков", 350, 40, 0, -326,
            () => { HostSceneCurtain.Instance?.ToggleCurtainOnHost(); Refresh(); }, VttUiSkin.Button);
        _curtainButtonText = curtainButton.GetComponentInChildren<Text>();
        Label(parent, "Управление на карте: средняя кнопка мыши — перемещение, R — поворот.",
            13, VttUiSkin.Muted, TextAnchor.UpperLeft, new Vector2(0, 1),
            new Vector2(0, 1), new Vector2(350, 64), new Vector2(0, -384));
        Button(parent, "Очистить все отметки игроков…", 350, 36, 0, -452,
            () => DiceUI.Instance?.ConfirmAction("Очистить все отметки?",
                "Будут удалены отметки всех игроков на карте. Отменить удаление нельзя.",
                CellMarker.ClearAllMarkersAsHost), new Color(0.28f, 0.11f, 0.14f));
    }

    private void AdjustScale(float amount)
    {
        var map = MapController.Instance;
        if (map == null) return;
        map.SetScale(Mathf.Round((map.CurrentScale + amount) * 100f) / 100f);
        Refresh();
    }

    private void ApplyExactScale(string value)
    {
        var map = MapController.Instance;
        if (map == null) return;
        string normalized = value.Trim().Replace(',', '.');
        if (float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
            map.SetScale(scale);
        Refresh();
    }

    private void BuildPlayersPage(Transform parent)
    {
        Label(parent, "Игроки в сессии", 16, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(340, 30),
            new Vector2(0, -4), true);
        _playerHint = Label(parent, "", 13, VttUiSkin.Muted, TextAnchor.UpperLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(350, 56),
            new Vector2(0, -38));
        Button(parent, "Копировать код приглашения", 350, 36, 0, -101,
            () => {
                string code = RelayManager.CurrentJoinCode;
                if (!string.IsNullOrEmpty(code)) GUIUtility.systemCopyBuffer = code;
            }, VttUiSkin.Button);

        var viewport = Box(parent, "PlayerViewport", new Color(0, 0, 0, 0.01f), 0, false);
        Place(viewport, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(350, 308), new Vector2(0, -153));
        viewport.GetComponent<Image>().raycastTarget = false;
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        var rows = new GameObject("Players", typeof(RectTransform));
        rows.transform.SetParent(viewport.transform, false);
        _playersContent = rows.GetComponent<RectTransform>();
        _playersContent.anchorMin = new Vector2(0, 1);
        _playersContent.anchorMax = new Vector2(1, 1);
        _playersContent.pivot = new Vector2(0.5f, 1);
        _playersContent.anchoredPosition = Vector2.zero;
        var scroll = viewport.AddComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = _playersContent;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28;
        Label(parent, "Кнопка рядом с именем добавляет игрока в бой или убирает его.",
            12, VttUiSkin.Muted, TextAnchor.UpperLeft, new Vector2(0, 1),
            new Vector2(0, 1), new Vector2(350, 34), new Vector2(0, -468));
    }

    private void RefreshPlayers(bool force = false)
    {
        if (_playersContent == null) return;
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsHost) return;
        var ids = new List<ulong>(nm.ConnectedClientsIds);
        ids.Sort();
        string code = RelayManager.CurrentJoinCode;
        _playerHint.text = $"Подключено: {ids.Count} · Код: {(string.IsNullOrEmpty(code) ? "—" : code)}";
        var signature = new System.Text.StringBuilder();
        foreach (ulong id in ids)
            signature.Append(id).Append(':').Append(PlayerColors.GetNickname(id)).Append(':')
                .Append(InitiativeTracker.Instance != null && InitiativeTracker.Instance.ContainsPlayer(id)).Append(';');
        string snapshot = signature.ToString();
        if (!force && snapshot == _playerSignature) return;
        _playerSignature = snapshot;
        foreach (Transform child in _playersContent) Destroy(child.gameObject);
        _playersContent.sizeDelta = new Vector2(0, Mathf.Max(308, ids.Count * 50));
        for (int i = 0; i < ids.Count; i++)
        {
            ulong playerId = ids[i];
            var row = Box(_playersContent, "Player " + playerId, VttUiSkin.Raised, 8, false);
            Place(row, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(340, 44), new Vector2(0, -i * 50));
            var color = PlayerColors.GetColor(playerId);
            var mark = Box(row.transform, "Color", color, 5, false);
            Place(mark, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(18, 18), new Vector2(12, 0));
            mark.GetComponent<Image>().raycastTarget = false;
            string name = PlayerColors.GetNickname(playerId) ?? $"Игрок {playerId}";
            if (playerId == nm.LocalClientId) name += " (DM)";
            Label(row.transform, name, 13, VttUiSkin.Text, TextAnchor.MiddleLeft,
                new Vector2(0, 0.5f), new Vector2(0, 0.5f),
                new Vector2(185, 32), new Vector2(39, 0));
            var tracker = InitiativeTracker.Instance;
            bool inFight = tracker != null && tracker.ContainsPlayer(playerId);
            Button(row.transform, inFight ? "Убрать" : "+ В бой", 99, 30, 232, -7,
                () => {
                    var current = InitiativeTracker.Instance;
                    if (current == null) return;
                    if (current.ContainsPlayer(playerId)) current.RemovePlayer(playerId);
                    else current.AddPlayer(playerId);
                    RefreshPlayers(true);
                }, inFight ? new Color(0.27f, 0.11f, 0.14f) :
                    new Color(0.10f, 0.30f, 0.48f));
        }
    }

    private void BuildInitiativePage(Transform parent)
    {
        Label(parent, "Управление боем", 16, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(340, 30),
            new Vector2(0, -4), true);
        _initiativeHint = Label(parent, "", 13, VttUiSkin.Muted, TextAnchor.UpperLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(350, 60),
            new Vector2(0, -41));
        Button(parent, "Показать трекер", 350, 42, 0, -113,
            () => InitiativeTracker.Instance?.SetVisible(true), VttUiSkin.Button);
        Button(parent, "+ Добавить персонажа / врага", 350, 42, 0, -165,
            () => {
                _panel.SetActive(false);
                InitiativeTracker.Instance?.ShowAddPanel();
            }, new Color(0.10f, 0.30f, 0.48f));
        Button(parent, "Следующий ход  →", 350, 42, 0, -217,
            () => InitiativeTracker.Instance?.NextTurn(), VttUiSkin.Button);
        Button(parent, "Очистить инициативу", 350, 42, 0, -269,
            () => InitiativeTracker.Instance?.RequestClearAll(),
            new Color(0.28f, 0.11f, 0.14f));
        Label(parent, "Игроков можно добавить одной кнопкой на вкладке «Игроки». В трекере можно изменить значение инициативы у каждого участника.",
            13, VttUiSkin.Muted, TextAnchor.UpperLeft, new Vector2(0, 1),
            new Vector2(0, 1), new Vector2(350, 95), new Vector2(0, -334));
    }

    private void Refresh()
    {
        if (_page == 0 && _mapScaleText != null)
        {
            var map = MapController.Instance;
            if (_mapScaleInput != null && !_mapScaleInput.isFocused)
                _mapScaleInput.SetTextWithoutNotify(map == null ? "—" :
                    map.CurrentScale.ToString("F2", CultureInfo.InvariantCulture));
            if (_curtainButtonText != null)
                _curtainButtonText.text = HostSceneCurtain.IsCurtainDown
                    ? "Показать карту игрокам" : "Скрыть карту от игроков";
        }
        if (_page == 1) RefreshPlayers();
        if (_page == 2 && _initiativeHint != null)
            _initiativeHint.text = "Ход виден всем игрокам. DM меняет порядок и состав участников.";
    }

    private GameObject Box(Transform parent, string name, Color color, int radius, bool outline = true)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        VttUiSkin.Surface(go.GetComponent<Image>(), color, radius, outline);
        return go;
    }

    private static void Place(GameObject go, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 position)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = position;
    }

    private Text Label(Transform parent, string value, int size, Color color, TextAnchor align,
        Vector2 anchor, Vector2 pivot, Vector2 dimensions, Vector2 position, bool bold = false)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Place(go, anchor, pivot, dimensions, position);
        var label = go.GetComponent<Text>();
        label.font = _font;
        label.fontSize = size;
        label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        label.color = color;
        label.alignment = align;
        label.text = value;
        label.raycastTarget = false;
        return label;
    }

    private GameObject Button(Transform parent, string title, float width, float height,
        float x, float y, Action callback, Color background)
    {
        var go = new GameObject("Button " + title, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Place(go, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(width, height), new Vector2(x, y));
        VttUiSkin.ButtonStyle(go.GetComponent<Image>(), background, 7);
        go.GetComponent<Button>().onClick.AddListener(() => callback());
        Label(go.transform, title, 13, VttUiSkin.Text, TextAnchor.MiddleCenter,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(width - 4, height - 4), Vector2.zero, true);
        return go;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
