using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// UI-панель для спавна дайсов, броска, очистки и отображения результатов.
/// Создаёт весь UI программно в Start() — достаточно повесить на пустой GameObject.
/// </summary>
public class DiceUI : MonoBehaviour
{
    [Header("Размеры кнопок")]
    public float buttonWidth = 55f;
    public float buttonHeight = 32f;
    public float gap = 4f;
    public float padding = 8f;

    public static DiceUI Instance { get; private set; }

    [Header("Спавн")]
    public float spawnSpread = 0.8f;

    private Text _resultText;
    private InputField _scaleInput;
    public InputField ScaleInput => _scaleInput;
    private readonly List<DieResult> _lastResults = new();
    private bool _trackingRoll;
    private int _rollingCount;
    private Canvas _canvas;
    private GameObject _sidebarGO;
    private GameObject _bottomPanelGO; // панель снизу-слева (X — скрыть/показать)
    private GameObject _debugPanelGO;  // панель генерации дайсов (E — скрыть/показать)
    private GameObject _logPanelGO;    // панель лога событий (L — скрыть/показать)
    private Text _logText;
    private readonly List<string> _logEntries = new(); // строки лога
    private GridManager _gridManager;

    // Кнопка выхода
    private GameObject _leaveButton;
    private GameObject _leaveConfirmDialog;

    // Список игроков (Tab)
    private GameObject _playerListPanel;

    // Таб для возврата свёрнутого лога
    private GameObject _logTab;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _gridManager = FindAnyObjectByType<GridManager>();
        BuildUI();
        if (DiceManager.Instance != null)
            DiceManager.Instance.OnAnyResult += OnDieResult;
        InvokeRepeating(nameof(TrySubscribeToMeasureTool), 0.1f, 0.5f);
    }

    void TrySubscribeToMeasureTool()
    {
        var mt = MeasurementTool.Instance;
        if (mt == null || _subscribedToMT) return;
        mt.OnDragStart += () => { if (_textureMenu != null) _textureMenu.SetActive(false); };
        mt.OnDragEnd += () => { if (mt.IsActive && mt.CurrentMode != MeasurementTool.Mode.Ruler) { _textureMenuOpen = true; _textureMenu?.SetActive(true); } };
        _subscribedToMT = true;
        CancelInvoke(nameof(TrySubscribeToMeasureTool));
    }
    private bool _subscribedToMT;

    void Update()
    {
        Keyboard k = Keyboard.current;
        if (k == null) return;

        // В главном меню хоткеи не работают
        if (_sidebarGO == null || !_sidebarGO.activeSelf) return;

        // Не работают когда фокус на текстовом поле
        if (IsInputFocused()) return;

        // Tab — показать/скрыть список игроков
        if (_playerListPanel != null)
        {
            bool tabHeld = k.tabKey.isPressed;
            if (_playerListPanel.activeSelf != tabHeld)
            {
                _playerListPanel.SetActive(tabHeld);
                if (tabHeld) RefreshPlayerList();
            }
        }

        if (k.eKey.wasPressedThisFrame)
            TogglePanel(_debugPanelGO, null);

        if (k.qKey.wasPressedThisFrame)
            TogglePanel(_sidebarGO, null);

        if (k.xKey.wasPressedThisFrame)
            TogglePanel(_bottomPanelGO, null);

        if (k.lKey.wasPressedThisFrame)
            TogglePanel(_logPanelGO, _logTab);
    }

    void TogglePanel(GameObject panel, GameObject tab)
    {
        if (panel == null) return;
        bool show = !panel.activeSelf;
        panel.SetActive(show);
        if (tab != null) tab.SetActive(!show);
    }

    void OnDestroy()
    {
        if (DiceManager.Instance != null)
            DiceManager.Instance.OnAnyResult -= OnDieResult;
    }

    static bool IsInputFocused()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null) return false;
        var go = es.currentSelectedGameObject;
        return go != null && go.GetComponent<InputField>() != null;
    }

    // ══════════════════════════════════════════════
    //  UI
    // ══════════════════════════════════════════════

    void BuildUI()
    {
        try
        {
            BuildUIInternal();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"DiceUI build failed: {e.Message}\n{e.StackTrace}");
        }
    }

    void BuildUIInternal()
    {
        // EventSystem (нужен для кнопок, совместимый с Input System)
        var es = FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (es == null)
        {
            GameObject esGO = new GameObject("EventSystem");
            esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
        else
        {
            // Заменяем StandaloneInputModule на InputSystemUIInputModule если нужно
            var oldModule = es.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            if (oldModule != null)
            {
                Destroy(oldModule);
                es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
        }

        // Шрифт: встроенный LegacyRuntime (всегда доступен в Unity)
        Font uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Canvas
        GameObject canvasGO = new GameObject("DiceCanvas");
        canvasGO.transform.SetParent(transform);
        Canvas canvas = canvasGO.AddComponent<Canvas>();
        _canvas = canvas;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGO.AddComponent<GraphicRaycaster>();

        // ═══ Дебаг-панель (E): только Tex и Scale ═══
        GameObject panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        _debugPanelGO = panelGO;
        Image bg = panelGO.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.1f, 0.85f);

        float panelW = 220f;
        float panelH = 50f;
        RectTransform prt = panelGO.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 0.5f);
        prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(panelW, panelH);
        prt.anchoredPosition = Vector2.zero;

        // Кнопка Tex
        float texW = 50f;
        MakeButton(panelGO.transform, "Tex", 8f, -8f, texW, buttonHeight, uiFont, OpenTexturePicker);

        // Поле Scale
        float scaleX = 8f + texW + gap;
        GameObject scaleGO = new GameObject("ScaleLabel");
        scaleGO.transform.SetParent(panelGO.transform, false);
        Text scaleLabel = scaleGO.AddComponent<Text>();
        scaleLabel.font = uiFont;
        scaleLabel.fontSize = 14;
        scaleLabel.color = new Color(0.7f, 0.75f, 0.85f);
        scaleLabel.text = "Scale:";
        scaleLabel.alignment = TextAnchor.MiddleLeft;
        RectTransform slrt = scaleGO.GetComponent<RectTransform>();
        slrt.anchorMin = new Vector2(0, 1);
        slrt.anchorMax = new Vector2(0, 1);
        slrt.pivot = new Vector2(0, 1);
        slrt.anchoredPosition = new Vector2(scaleX, -10f);
        slrt.sizeDelta = new Vector2(42f, 20f);

        float inputX = scaleX + 46f;
        GameObject inputGO = new GameObject("ScaleInput", typeof(RectTransform), typeof(Image), typeof(InputField));
        inputGO.transform.SetParent(panelGO.transform, false);
        InputField scaleIF = inputGO.GetComponent<InputField>();
        // Создаём текстовый компонент для InputField
        GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGO.transform.SetParent(inputGO.transform, false);
        Text inputText = textGO.GetComponent<Text>();
        inputText.font = uiFont;
        inputText.fontSize = 14;
        inputText.color = Color.white;
        inputText.alignment = TextAnchor.MiddleLeft;
        inputText.supportRichText = false;
        RectTransform textRt = textGO.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.sizeDelta = new Vector2(-8, 0);
        textRt.anchoredPosition = new Vector2(4, 0);
        scaleIF.textComponent = inputText;
        scaleIF.text = "1.0";
        RectTransform irect = inputGO.GetComponent<RectTransform>();
        irect.anchorMin = new Vector2(0, 1);
        irect.anchorMax = new Vector2(0, 1);
        irect.pivot = new Vector2(0, 1);
        irect.anchoredPosition = new Vector2(inputX, -10f);
        irect.sizeDelta = new Vector2(50f, 24f);
        inputGO.GetComponent<Image>().color = new Color(0.15f, 0.18f, 0.25f);

        // Placeholder
        GameObject phGO = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
        phGO.transform.SetParent(inputGO.transform, false);
        Text ph = phGO.GetComponent<Text>();
        ph.text = "1.0";
        ph.font = uiFont;
        ph.fontSize = 14;
        ph.color = new Color(0.4f, 0.4f, 0.5f);
        ph.alignment = TextAnchor.MiddleLeft;
        RectTransform phrt = phGO.GetComponent<RectTransform>();
        phrt.anchorMin = Vector2.zero;
        phrt.anchorMax = Vector2.one;
        phrt.sizeDelta = Vector2.zero;
        scaleIF.placeholder = ph;
        _scaleInput = scaleIF;
        BuildLeftSidebar();
        BuildBottomPanel();
        BuildLogPanel();
        BuildLeaveButton(canvasGO.transform, uiFont);
        BuildPlayerListPanel(canvasGO.transform, uiFont);

        // Все панели скрыты при старте — покажутся после host/join
        if (_debugPanelGO != null) _debugPanelGO.SetActive(false);
        if (_sidebarGO != null) _sidebarGO.SetActive(false);
        if (_bottomPanelGO != null) _bottomPanelGO.SetActive(false);
        if (_logPanelGO != null) _logPanelGO.SetActive(false);

        BuildTabs();
    }

    /// <summary>Показывает игровые панели (вызывается после host/join).</summary>
    public void ShowGamePanels()
    {
        if (_sidebarGO != null) _sidebarGO.SetActive(true);
        if (_bottomPanelGO != null) _bottomPanelGO.SetActive(true);
        if (_logPanelGO != null) { _logPanelGO.SetActive(true); if (_logTab != null) _logTab.SetActive(false); }
        if (_leaveButton != null) _leaveButton.SetActive(true);
    }

    /// <summary>Скрывает все игровые панели (при выходе из лобби).</summary>
    public void HideGamePanels()
    {
        if (_sidebarGO != null) _sidebarGO.SetActive(false);
        if (_bottomPanelGO != null) _bottomPanelGO.SetActive(false);
        if (_debugPanelGO != null) _debugPanelGO.SetActive(false);
        if (_logPanelGO != null) _logPanelGO.SetActive(false);
        if (_leaveButton != null) _leaveButton.SetActive(false);
        if (_leaveConfirmDialog != null) _leaveConfirmDialog.SetActive(false);
        if (_textureMenu != null) _textureMenu.SetActive(false);
    }

    void BuildLeaveButton(Transform canvasTr, Font font)
    {
        // Кнопка выхода (красная, слева сверху)
        _leaveButton = new GameObject("LeaveButton");
        _leaveButton.transform.SetParent(canvasTr, false);
        _leaveButton.SetActive(false); // скрыта до входа в игру

        Image img = _leaveButton.AddComponent<Image>();
        img.color = new Color(0.85f, 0.1f, 0.1f, 0.85f);

        Button btn = _leaveButton.AddComponent<Button>();
        btn.onClick.AddListener(ShowLeaveConfirm);

        RectTransform rt = _leaveButton.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(28f, 28f);
        rt.anchoredPosition = new Vector2(12f, -12f);

        // Крестик
        GameObject xGO = new GameObject("X");
        xGO.transform.SetParent(_leaveButton.transform, false);
        Text xTxt = xGO.AddComponent<Text>();
        xTxt.text = "✕"; xTxt.font = font; xTxt.fontSize = 18;
        xTxt.fontStyle = FontStyle.Bold; xTxt.color = Color.white;
        xTxt.alignment = TextAnchor.MiddleCenter;
        RectTransform xrt = xTxt.GetComponent<RectTransform>();
        xrt.anchorMin = Vector2.zero; xrt.anchorMax = Vector2.one;
        xrt.sizeDelta = Vector2.zero;

        // Диалог подтверждения
        _leaveConfirmDialog = new GameObject("LeaveConfirm");
        _leaveConfirmDialog.transform.SetParent(canvasTr, false);
        _leaveConfirmDialog.SetActive(false);

        Image dImg = _leaveConfirmDialog.AddComponent<Image>();
        dImg.color = new Color(0.08f, 0.08f, 0.12f, 0.95f);

        RectTransform drt = _leaveConfirmDialog.GetComponent<RectTransform>();
        drt.anchorMin = drt.anchorMax = drt.pivot = new Vector2(0.5f, 0.5f);
        drt.sizeDelta = new Vector2(300f, 120f);
        drt.anchoredPosition = Vector2.zero;

        // Текст
        GameObject qGO = new GameObject("Question");
        qGO.transform.SetParent(_leaveConfirmDialog.transform, false);
        Text qTxt = qGO.AddComponent<Text>();
        qTxt.text = "Выйти из лобби?";
        qTxt.font = font; qTxt.fontSize = 20; qTxt.fontStyle = FontStyle.Bold;
        qTxt.color = Color.white; qTxt.alignment = TextAnchor.MiddleCenter;
        RectTransform qrt = qTxt.GetComponent<RectTransform>();
        qrt.anchorMin = new Vector2(0, 0.5f); qrt.anchorMax = new Vector2(1, 1f);
        qrt.sizeDelta = Vector2.zero; qrt.anchoredPosition = Vector2.zero;

        // Кнопка Да
        MakeButton(_leaveConfirmDialog.transform, "Да", 40f, -80f, 90f, 34f, font, DoLeave);

        // Кнопка Нет
        MakeButton(_leaveConfirmDialog.transform, "Нет", 170f, -80f, 90f, 34f, font,
            () => _leaveConfirmDialog.SetActive(false));
    }

    void ShowLeaveConfirm()
    {
        if (_leaveConfirmDialog != null)
            _leaveConfirmDialog.SetActive(true);
    }

    void DoLeave()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();
    }

    /// <summary>
    /// Создаёт боковую панель-плейсхолдер (тёмно-синий полупрозрачный прямоугольник).
    /// 
    /// ═══ КАК ЗАМЕНИТЬ ДИЗАЙН В БУДУЩЕМ ═══
    /// Вариант А: заменить Image на свой спрайт:
    ///   1. Импортируй изображение в Assets/
    ///   2. Выстави ему Texture Type = Sprite (2D and UI)
    ///   3. В этом методе после AddComponent&lt;Image&gt;() добавь:
    ///      bg.sprite = Resources.Load&lt;Sprite&gt;("имя_файла_без_расширения");
    ///      bg.type = Image.Type.Sliced; // если это 9-slice
    /// 
    /// Вариант Б: полная замена на префаб:
    ///   1. Создай префаб с любым дизайном (панель + дочерние элементы)
    ///   2. Замени всё тело метода на:
    ///      _sidebarGO = Instantiate(Resources.Load&lt;GameObject&gt;("MySidebar"), _canvas.transform);
    /// 
    /// Вариант В: вынести в отдельный MonoBehaviour:
    ///   1. Создай скрипт SidebarPanel : MonoBehaviour с методом Build(Transform parent)
    ///   2. Здесь вызови: gameObject.AddComponent&lt;SidebarPanel&gt;().Build(_canvas.transform);
    /// </summary>
    void BuildLeftSidebar()
    {
        if (_canvas == null) return;

        GameObject sidebarGO = new GameObject("LeftSidebar");
        sidebarGO.transform.SetParent(_canvas.transform, false);
        _sidebarGO = sidebarGO;

        Image bg = sidebarGO.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.1f, 0.25f, 0.8f); // тёмно-синий, непрозрачность 80%

        RectTransform rt = sidebarGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0.5f);   // левый край, центр по вертикали
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);       // точка привязки — левый центр
        rt.sizeDelta = new Vector2(60f, 700f);   // ширина × высота
        rt.anchoredPosition = new Vector2(12f, 0f); // отступ 12px от левого края

        // Иконки-плейсхолдеры (функционал не определён)
        // Замена: изменить массивы labels/colors/actions — MakeIcon тот же что в BottomPanel
        Font uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        int iconCount = 10;
        float iconSize = 46f;
        float iconGap = 20f;
        float topMargin = 5f;
        float iconX = (60f - iconSize) / 2f; // по центру горизонтали

        for (int i = 0; i < iconCount; i++)
        {
            float y = -topMargin - i * (iconSize + iconGap);
            if (i == 0)
            {
                // Создать токен
                MakeIcon(sidebarGO.transform, "TOK\nEN", new Color(0.2f, 0.25f, 0.35f, 0.9f),
                    iconX, y, iconSize, uiFont, () => TokenManager.Instance?.RequestSpawnToken(), anchorY: 1f, fontSize: 16);
            }
            else if (i == 1)
            {
                // Линейка
                MakeIcon(sidebarGO.transform, "DI\nST", new Color(0.2f, 0.25f, 0.35f, 0.9f),
                    iconX, y, iconSize, uiFont, () => MeasurementTool.Instance?.Activate(0), anchorY: 1f, fontSize: 16);
            }
            else if (i == 2)
            {
                // Радиус (круг)
                MakeIcon(sidebarGO.transform, "SPH\nERE", new Color(0.2f, 0.25f, 0.35f, 0.9f),
                    iconX, y, iconSize, uiFont, () => MeasurementTool.Instance?.Activate(1), anchorY: 1f, fontSize: 16);
            }
            else if (i == 3)
            {
                // Квадрат
                MakeIcon(sidebarGO.transform, "SQA\nRE", new Color(0.2f, 0.25f, 0.35f, 0.9f),
                    iconX, y, iconSize, uiFont, () => MeasurementTool.Instance?.Activate(2), anchorY: 1f, fontSize: 16);
            }
            else if (i == 4)
            {
                // Конус
                MakeIcon(sidebarGO.transform, "CON\nUS", new Color(0.2f, 0.25f, 0.35f, 0.9f),
                    iconX, y, iconSize, uiFont, () => MeasurementTool.Instance?.Activate(3), anchorY: 1f, fontSize: 16);
            }
            else if (i == 5)
            {
                // Применить область
                MakeIcon(sidebarGO.transform, "EFF\nECT", new Color(0.2f, 0.25f, 0.35f, 0.9f),
                    iconX, y, iconSize, uiFont, () => ToggleTextureMenu(), anchorY: 1f, fontSize: 16);
            }
            else if (i == 6)
            {
                // Удалить все эффекты
                MakeIcon(sidebarGO.transform, "DEL\nEFF", new Color(0.2f, 0.25f, 0.35f, 0.9f),
                    iconX, y, iconSize, uiFont, () => CellMarker.ClearAllMyMarkers(), anchorY: 1f, fontSize: 16);
            }
            else
            {
                MakeIcon(sidebarGO.transform, "?", new Color(0.2f, 0.25f, 0.35f, 0.9f),
                    iconX, y, iconSize, uiFont, () => { /* TODO */ }, anchorY: 1f);
            }
        }

        BuildTextureMenu(sidebarGO.transform);
    }

    private GameObject _textureMenu;
    private bool _textureMenuOpen;

    void ToggleTextureMenu()
    {
        if (MeasurementTool.Instance == null || !MeasurementTool.Instance.IsActive) return;
        _textureMenuOpen = !_textureMenuOpen;
        if (_textureMenu != null) _textureMenu.SetActive(_textureMenuOpen);
    }

    // ═══ Список игроков (Tab) ═══

    void BuildPlayerListPanel(Transform canvasTr, Font font)
    {
        _playerListPanel = new GameObject("PlayerListPanel", typeof(RectTransform));
        _playerListPanel.transform.SetParent(canvasTr, false);
        _playerListPanel.SetActive(false);

        var bg = _playerListPanel.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.07f, 0.11f, 0.92f);

        var rt = _playerListPanel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(280f, 200f);
        rt.anchoredPosition = Vector2.zero;

        // Заголовок
        var titleGO = new GameObject("Title", typeof(RectTransform));
        titleGO.transform.SetParent(_playerListPanel.transform, false);
        var titleTxt = titleGO.AddComponent<Text>();
        titleTxt.text = "ИГРОКИ";
        titleTxt.font = font; titleTxt.fontSize = 14; titleTxt.fontStyle = FontStyle.Bold;
        titleTxt.color = new Color(0.5f, 0.55f, 0.65f);
        titleTxt.alignment = TextAnchor.MiddleCenter;
        var trt = titleGO.GetComponent<RectTransform>();
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 1f);
        trt.pivot = new Vector2(0.5f, 1f);
        trt.sizeDelta = new Vector2(260f, 24f);
        trt.anchoredPosition = new Vector2(0f, -8f);

        // Контейнер для списка
        _playerListContent = new GameObject("Content", typeof(RectTransform));
        _playerListContent.transform.SetParent(_playerListPanel.transform, false);
        var crt = _playerListContent.GetComponent<RectTransform>();
        crt.anchorMin = Vector2.zero; crt.anchorMax = Vector2.one;
        crt.offsetMin = new Vector2(10f, 10f);
        crt.offsetMax = new Vector2(-10f, -36f);
    }

    private GameObject _playerListContent;

    void RefreshPlayerList()
    {
        if (_playerListContent == null) return;
        foreach (Transform t in _playerListContent.transform) Destroy(t.gameObject);

        var nm = NetworkManager.Singleton;
        if (nm == null || nm.ConnectedClients == null) return;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        float y = 0f;

        foreach (var kv in nm.ConnectedClients)
        {
            var entryGO = new GameObject($"Player_{kv.Key}", typeof(RectTransform));
            entryGO.transform.SetParent(_playerListContent.transform, false);

            var ert = entryGO.GetComponent<RectTransform>();
            ert.anchorMin = ert.anchorMax = new Vector2(0f, 1f);
            ert.pivot = new Vector2(0f, 1f);
            ert.sizeDelta = new Vector2(260f, 22f);
            ert.anchoredPosition = new Vector2(0f, y);

            // Цветной квадратик
            var sq = new GameObject("Color", typeof(RectTransform));
            sq.transform.SetParent(entryGO.transform, false);
            var sqImg = sq.AddComponent<Image>();
            Color pc = PlayerColors.GetColor(kv.Key);
            sqImg.color = pc;
            var srt = sq.GetComponent<RectTransform>();
            srt.anchorMin = srt.anchorMax = new Vector2(0f, 0.5f);
            srt.pivot = new Vector2(0f, 0.5f);
            srt.sizeDelta = new Vector2(16f, 16f);
            srt.anchoredPosition = new Vector2(4f, 0f);

            // Ник
            var nickGO = new GameObject("Nick", typeof(RectTransform));
            nickGO.transform.SetParent(entryGO.transform, false);
            var nickTxt = nickGO.AddComponent<Text>();
            string nick = PlayerColors.GetNickname(kv.Key) ?? $"Player_{kv.Key}";
            if (kv.Key == nm.LocalClientId) nick += " (Вы)";
            nickTxt.text = nick;
            nickTxt.font = font; nickTxt.fontSize = 14;
            nickTxt.color = Color.white;
            nickTxt.alignment = TextAnchor.MiddleLeft;
            var nrt = nickGO.GetComponent<RectTransform>();
            nrt.anchorMin = nrt.anchorMax = new Vector2(0f, 0.5f);
            nrt.pivot = new Vector2(0f, 0.5f);
            nrt.sizeDelta = new Vector2(230f, 20f);
            nrt.anchoredPosition = new Vector2(24f, 0f);

            y -= 26f;
        }

        // Обновить высоту панели под количество игроков
        float h = Mathf.Max(80f, Mathf.Abs(y) + 50f);
        _playerListPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(280f, h);
    }

    void BuildTextureMenu(Transform sidebarTransform)
    {
        _textureMenu = new GameObject("TextureMenu");
        _textureMenu.transform.SetParent(_canvas.transform, false);
        _textureMenu.SetActive(false);

        // Фон
        Image bg = _textureMenu.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.08f, 0.15f, 0.92f);

        int cols = 3;
        int rows = 2;
        float sqSize = 48f;
        float gap = 6f;
        float pad = 8f;
        float menuW = pad * 2 + cols * sqSize + (cols - 1) * gap;
        float menuH = pad * 2 + rows * sqSize + (rows - 1) * gap;

        RectTransform rt = _textureMenu.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
        rt.sizeDelta = new Vector2(menuW, menuH);
        rt.anchoredPosition = new Vector2(80f, 0f);

        for (int i = 0; i < CellMarker.TextureNames.Length; i++)
        {
            int idx = i;
            int col = i % cols;
            int row = i / cols;
            float x = pad + col * (sqSize + gap);
            float y = -pad - row * (sqSize + gap);

            GameObject btnGO = new GameObject($"TexBtn_{i}");
            btnGO.transform.SetParent(_textureMenu.transform, false);

            Image bImg = btnGO.AddComponent<Image>();
            bImg.color = CellMarker.TextureColors[i];

            Button btn = btnGO.AddComponent<Button>();
            btn.onClick.AddListener(() => {
                MeasurementTool.Instance?.ApplyArea(idx);
                _textureMenuOpen = false;
                _textureMenu.SetActive(false);
            });

            RectTransform brt = btnGO.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f);
            brt.pivot = new Vector2(0f, 1f);
            brt.sizeDelta = new Vector2(sqSize, sqSize);
            brt.anchoredPosition = new Vector2(x, y);
        }
    }

    /// <summary>
    /// Панель-плейсхолдер в левом нижнем углу (560×60, тёмно-синий полупрозрачный).
    /// Дизайн заменяется так же как LeftSidebar (см. BuildLeftSidebar).
    /// </summary>
    void BuildBottomPanel()
    {
        if (_canvas == null) return;

        GameObject panelGO = new GameObject("BottomPanel");
        panelGO.transform.SetParent(_canvas.transform, false);
        _bottomPanelGO = panelGO;

        Image bg = panelGO.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.1f, 0.25f, 0.8f); // тёмно-синий, 80%

        RectTransform rt = panelGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);   // левый нижний угол
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);       // точка привязки — левый низ
        rt.sizeDelta = new Vector2(560f, 60f); // ширина × высота
        rt.anchoredPosition = new Vector2(12f, 12f); // отступ 12px слева и снизу

        // Иконки дайсов
        Font uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var dieTypes = new[] { DieType.d4, DieType.d6, DieType.d8, DieType.d10, DieType.d100, DieType.d12, DieType.d20 };
        string[] labels = { "d4", "d6", "d8", "d10", "d%", "d12", "d20" };
        Color[] colors = {
            new Color(0.9f, 0.3f, 0.3f),  // d4   — красный
            new Color(0.3f, 0.7f, 0.9f),  // d6   — голубой
            new Color(0.3f, 0.9f, 0.4f),  // d8   — зелёный
            new Color(0.9f, 0.6f, 0.2f),  // d10  — оранжевый
            new Color(0.6f, 0.6f, 0.7f),  // d%   — серый
            new Color(0.7f, 0.3f, 0.9f),  // d12  — фиолетовый
            new Color(0.9f, 0.8f, 0.2f),  // d20  — золотой
        };

        float iconSize = 50f;
        float iconGap = 20f;
        float startX = 10f; // отступ от левого края панели
        float iconY = 0f; // по центру вертикали (60-50=10, по 5px сверху/снизу)

        for (int i = 0; i < dieTypes.Length; i++)
        {
            float x = startX + i * (iconSize + iconGap);
            var dt = dieTypes[i];
            MakeIcon(panelGO.transform, labels[i], colors[i], x, iconY, iconSize, uiFont, () => SpawnDie(dt));
        }

        // Кнопка удаления всех дайсов
        float clearX = startX + dieTypes.Length * (iconSize + iconGap);
        MakeIcon(panelGO.transform, "✕", new Color(0.8f, 0.2f, 0.2f), clearX, iconY, iconSize, uiFont, ClearAll);
    }

    /// <summary>
    /// Панель лога событий в правом нижнем углу (420×330, тёмно-синий полупрозрачный).
    /// </summary>
    void BuildLogPanel()
    {
        if (_canvas == null) return;

        GameObject panelGO = new GameObject("LogPanel");
        panelGO.transform.SetParent(_canvas.transform, false);
        _logPanelGO = panelGO;

        Image bg = panelGO.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.1f, 0.25f, 0.8f); // тёмно-синий, 80%

        RectTransform rt = panelGO.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);   // правый нижний угол
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);       // точка привязки — правый низ
        rt.sizeDelta = new Vector2(420f, 330f); // ширина × высота
        rt.anchoredPosition = new Vector2(-12f, 12f); // отступ 12px справа и снизу

        // Кнопка сворачивания ► (лямбда ссылается на поле _logTab, а не на параметр)
        {
            Font uiFont2 = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject btnGO = new GameObject("CollapseBtn");
            btnGO.transform.SetParent(panelGO.transform, false);
            btnGO.transform.SetAsLastSibling();

            Image img = btnGO.AddComponent<Image>();
            img.color = new Color(0.08f, 0.12f, 0.22f, 0.7f);
            img.raycastTarget = true;

            Button btn = btnGO.AddComponent<Button>();
            btn.onClick.AddListener(() => TogglePanel(panelGO, _logTab)); // поле, не параметр!

            RectTransform brt = btnGO.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(1f, 1f);
            brt.sizeDelta = new Vector2(28f, 28f);
            brt.anchoredPosition = new Vector2(-6f, -6f);

            GameObject lbl = new GameObject("Arrow");
            lbl.transform.SetParent(btnGO.transform, false);
            Text txt = lbl.AddComponent<Text>();
            txt.text = "►"; txt.font = uiFont2; txt.fontSize = 16;
            txt.fontStyle = FontStyle.Bold;
            txt.color = new Color(0.7f, 0.75f, 0.85f);
            txt.alignment = TextAnchor.MiddleCenter;
            RectTransform lrt = txt.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
            lrt.sizeDelta = Vector2.zero;
        }

        // Текст лога
        Font uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        GameObject textGO = new GameObject("LogText");
        textGO.transform.SetParent(panelGO.transform, false);
        _logText = textGO.AddComponent<Text>();
        _logText.font = uiFont;
        _logText.fontSize = 13;
        _logText.color = new Color(0.85f, 0.88f, 0.95f);
        _logText.alignment = TextAnchor.UpperLeft;
        _logText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _logText.verticalOverflow = VerticalWrapMode.Truncate;
        _logText.raycastTarget = false; // чтобы не перехватывал клики кнопки сворачивания

        RectTransform trt = _logText.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(8f, 8f);  // отступы внутри панели
        trt.offsetMax = new Vector2(-8f, -8f);
    }

    // ═══ Табы для возврата скрытых панелей ═══

    void BuildTabs()
    {
        if (_canvas == null) return;

        // Log tab (правый край, на высоте середины панели логов: 12 + 330/2 = 177)
        _logTab = MakeTab(_canvas.transform, "◀",
            new Vector2(1f, 0f), new Vector2(1f, 0.5f), new Vector2(24f, 60f),
            () => { if (_logPanelGO != null) { _logPanelGO.SetActive(true); _logTab.SetActive(false); } });
        _logTab.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 177f);

        _logTab.SetActive(false);
    }

    GameObject MakeTab(Transform parent, string label, Vector2 anchor, Vector2 pivot, Vector2 sizeDelta, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject($"Tab_{label}");
        go.transform.SetParent(parent, false);

        Image img = go.AddComponent<Image>();
        img.color = new Color(0.05f, 0.1f, 0.25f, 0.8f); // 80% непрозрачности

        Button btn = go.AddComponent<Button>();
        btn.onClick.AddListener(onClick);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = Vector2.zero;

        // Текст
        Font uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        GameObject labelGO = new GameObject("Label");
        labelGO.transform.SetParent(go.transform, false);
        Text txt = labelGO.AddComponent<Text>();
        txt.text = label;
        txt.font = uiFont;
        txt.fontSize = 14;
        txt.color = new Color(0.7f, 0.75f, 0.85f);
        txt.alignment = TextAnchor.MiddleCenter;

        RectTransform lrt = txt.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.sizeDelta = Vector2.zero;

        return go;
    }

    void MakeIcon(Transform parent, string label, Color color, float x, float y, float size, Font font, UnityEngine.Events.UnityAction onClick, float anchorY = 0.5f, int fontSize = 13)
    {
        GameObject go = new GameObject($"Icon_{label}");
        go.transform.SetParent(parent, false);

        Image img = go.AddComponent<Image>();
        img.color = color;

        Button btn = go.AddComponent<Button>();
        btn.onClick.AddListener(onClick);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, anchorY);
        rt.anchorMax = new Vector2(0f, anchorY);
        rt.pivot = new Vector2(0f, anchorY);
        rt.sizeDelta = new Vector2(size, size);
        rt.anchoredPosition = new Vector2(x, y);

        // Текст
        GameObject labelGO = new GameObject("Label");
        labelGO.transform.SetParent(go.transform, false);
        Text txt = labelGO.AddComponent<Text>();
        txt.text = label;
        txt.font = font;
        txt.fontSize = fontSize;
        txt.fontStyle = FontStyle.Bold;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleCenter;

        RectTransform lrt = txt.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.sizeDelta = Vector2.zero;
    }

    void MakeButton(Transform parent, string label, float x, float y, float w, float h, Font font, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject($"Btn_{label}");
        go.transform.SetParent(parent, false);

        Image img = go.AddComponent<Image>();
        img.color = new Color(0.25f, 0.3f, 0.4f);

        Button btn = go.AddComponent<Button>();
        btn.onClick.AddListener(onClick);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, y);

        // Текст на кнопке
        GameObject labelGO = new GameObject("Label");
        labelGO.transform.SetParent(go.transform, false);
        Text txt = labelGO.AddComponent<Text>();
        txt.text = label;
        txt.font = font;
        txt.fontSize = 16;
        txt.color = Color.white;
        txt.alignment = TextAnchor.MiddleCenter;

        RectTransform lrt = txt.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.sizeDelta = Vector2.zero;
    }

    void MakeToggle(Transform parent, string label, float x, float y, Font font, bool initialValue, UnityEngine.Events.UnityAction<bool> onChanged)
    {
        float toggleSize = 18f;
        float labelW = 70f;

        GameObject go = new GameObject($"Tgl_{label}");
        go.transform.SetParent(parent, false);

        // Background
        Image bg = go.AddComponent<Image>();
        bg.color = new Color(0.2f, 0.25f, 0.35f);

        Toggle toggle = go.AddComponent<Toggle>();
        toggle.isOn = initialValue;
        toggle.onValueChanged.AddListener(onChanged);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(toggleSize, toggleSize);
        rt.anchoredPosition = new Vector2(x, y);

        // Checkmark
        GameObject checkGO = new GameObject("Checkmark");
        checkGO.transform.SetParent(go.transform, false);
        Image checkImg = checkGO.AddComponent<Image>();
        checkImg.color = new Color(0.4f, 0.7f, 1f);
        RectTransform crt = checkGO.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0.15f, 0.15f);
        crt.anchorMax = new Vector2(0.85f, 0.85f);
        crt.sizeDelta = Vector2.zero;
        toggle.graphic = checkImg;

        // Label
        GameObject labelGO = new GameObject("Label");
        labelGO.transform.SetParent(parent, false);
        Text txt = labelGO.AddComponent<Text>();
        txt.text = label;
        txt.font = font;
        txt.fontSize = 14;
        txt.color = new Color(0.8f, 0.85f, 0.9f);
        txt.alignment = TextAnchor.MiddleLeft;
        RectTransform lrt2 = txt.GetComponent<RectTransform>();
        lrt2.anchorMin = new Vector2(0, 1);
        lrt2.anchorMax = new Vector2(0, 1);
        lrt2.pivot = new Vector2(0, 1);
        lrt2.sizeDelta = new Vector2(labelW, toggleSize);
        lrt2.anchoredPosition = new Vector2(x + toggleSize + 6f, y);
    }

    // ══════════════════════════════════════════════
    //  Действия
    // ══════════════════════════════════════════════

    void SpawnDie(DieType type)
    {
        // Центр карты (или 0,0,0 если карты нет)
        Vector3 mapCenter = MapController.Instance != null
            ? MapController.Instance.transform.position
            : Vector3.zero;

        // Сетевой режим — кубики через NetworkDiceManager
        if (GameNetworkManager.Instance != null && GameNetworkManager.Instance.IsConnected)
        {
            Vector3 pos = mapCenter + new Vector3(
                Random.Range(-spawnSpread, spawnSpread),
                0,
                Random.Range(-spawnSpread, spawnSpread)
            );
            NetworkDiceManager.Instance?.RequestSpawnDie(type, pos);
            return;
        }

        // Локальный режим — старый DiceManager
        if (DiceManager.Instance == null) return;
        float x = mapCenter.x + Random.Range(-spawnSpread, spawnSpread);
        float z = mapCenter.z + Random.Range(-spawnSpread, spawnSpread);
        DiceManager.Instance.SpawnDieAt(type, x, z);
    }

    void RollAll()
    {
        if (DiceManager.Instance == null) return;
        int active = DiceManager.Instance.ActiveDice.Count;
        if (active == 0)
        {
            if (_resultText != null) _resultText.text = "Нет кубиков";
            return;
        }
        _lastResults.Clear();
        _trackingRoll = true;
        _rollingCount = active;
        DiceManager.Instance.RollAll();
        UpdateResultText();
    }

    void ClearAll()
    {
        // Удаляем сетевые кубики
        var netDice = FindObjectsByType<NetworkDice>(FindObjectsInactive.Exclude);
        foreach (var nd in netDice)
        {
            if (nd.IsOwner && nd.TryGetComponent<NetworkObject>(out var no) && no.IsSpawned)
            {
                if (NetworkManager.Singleton.IsServer)
                    no.Despawn();
                else
                    SendDespawnRequest(no.NetworkObjectId);
            }
        }

        // Удаляем локальные кубики
        if (DiceManager.Instance != null)
            DiceManager.Instance.ClearAll();

        _lastResults.Clear();
        _trackingRoll = false;
        _rollingCount = 0;
        // Логи НЕ чистим!
        UpdateResultText();
        UpdateLogText();
    }

    private static void SendDespawnRequest(ulong netId)
    {
        var writer = new FastBufferWriter(sizeof(ulong), Unity.Collections.Allocator.Temp);
        writer.WriteValueSafe(netId);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            "DespawnDice", NetworkManager.ServerClientId, writer);
        writer.Dispose();
    }

    /// <summary>Начинает отслеживание ручного броска (drag-and-drop).</summary>
    public void StartManualRoll(int diceCount)
    {
        _lastResults.Clear();
        _trackingRoll = true;
        _rollingCount = diceCount;
    }

    /// <summary>
    /// Показывает результат удалённого броска (приходит по сети от NetworkDice).
    /// </summary>
    public void ShowResult(string dieTypeName, int result, ulong throwerId, string ownerNickname = null)
    {
        DieType type = ParseDieType(dieTypeName);
        _lastResults.Add(new DieResult { type = type, value = result });
        UpdateResultText();

        string who = !string.IsNullOrEmpty(ownerNickname)
            ? ownerNickname
            : $"P{throwerId}";
        string logLine = $"[{who}] {DieTypeName(type)}={result}";
        _logEntries.Insert(0, logLine);
        UpdateLogText();
        Debug.Log($"[Dice] Remote: {who} rolled {dieTypeName}: {result}");
    }

    static DieType ParseDieType(string name)
    {
        return name switch
        {
            "d4" => DieType.d4,
            "d6" => DieType.d6,
            "d8" => DieType.d8,
            "d10" => DieType.d10,
            "d100" => DieType.d100,
            "d12" => DieType.d12,
            "d20" => DieType.d20,
            _ => DieType.d20,
        };
    }

    void UpdateLogText()
    {
        if (_logText == null) return;
        _logText.text = string.Join("\n", _logEntries);
    }

    // ══════════════════════════════════════════════
    //  Текстура геймборда
    // ══════════════════════════════════════════════

    void OpenTexturePicker()
    {
        // Если есть MapController — делегируем ему
        if (MapController.Instance != null)
        {
            MapController.Instance.LoadImage();
            return;
        }

        // Fallback: старая логика (для оффлайн-режима)
#if UNITY_EDITOR
        string path = EditorUtility.OpenFilePanel("Выберите изображение", "", "png,jpg,jpeg,bmp,tga");
        if (string.IsNullOrEmpty(path)) return;
        ApplyTexture(path);
#else
        Debug.LogWarning("Texture picker работает только в Editor.");
#endif
    }

    void ApplyTexture(string filePath)
    {
        // Маршрутизируем через MapController
        if (MapController.Instance != null)
        {
            byte[] data = File.ReadAllBytes(filePath);
            MapController.Instance.ApplyImage(data);
            return;
        }

        // Fallback: напрямую в GridManager (для оффлайн-режима)
        if (_gridManager == null) return;
        byte[] data2 = File.ReadAllBytes(filePath);
        Texture2D tex = new Texture2D(2, 2);
        if (!tex.LoadImage(data2))
        {
            Debug.LogError($"DiceUI: failed to load image: {filePath}");
            Destroy(tex);
            return;
        }
        // GridManager больше не имеет SetBoardTexture — карта на MapPlane
        Debug.LogWarning("DiceUI: GridManager.SetBoardTexture removed — use MapController");
        Destroy(tex);
    }

    void RotateBoard(bool clockwise)
    {
        // Поворот теперь через MapController (R)
        // Ничего не делаем — карта управляется через MapController
    }

    // ══════════════════════════════════════════════
    //  Результаты
    // ══════════════════════════════════════════════

    void OnDieResult(Dice die)
    {
        if (!_trackingRoll) return;
        _lastResults.Add(new DieResult { type = die.DieType, value = die.Result });
        _rollingCount--;
        UpdateResultText();
    }

    void UpdateResultText()
    {
        if (_resultText == null) return;

        if (_lastResults.Count == 0)
        {
            if (_trackingRoll)
                _resultText.text = $"Бросок... ({_rollingCount} кубиков)";
            else
                _resultText.text = "Готово";
            return;
        }

        int total = 0;
        var sb = new System.Text.StringBuilder();

        // Группировка по типу: "3d6: 4,5,2"
        var groups = new Dictionary<DieType, List<int>>();
        foreach (var r in _lastResults)
        {
            if (!groups.ContainsKey(r.type)) groups[r.type] = new List<int>();
            groups[r.type].Add(r.value);
            total += r.value;
        }

        foreach (var kv in groups)
        {
            int count = kv.Value.Count;
            string typeName = DieTypeName(kv.Key);
            if (count == 1)
                sb.AppendLine($"{typeName}: {kv.Value[0]}");
            else
                sb.AppendLine($"{count}{typeName}: {string.Join(", ", kv.Value)}");
        }

        // Если ещё не все кубики остановились — показываем прогресс
        if (_rollingCount > 0)
            sb.AppendLine($"→ {_lastResults.Count}/{_lastResults.Count + _rollingCount}...");
        else
        {
            sb.AppendLine($"→ Total: {total}");
            _trackingRoll = false;
        }
        _resultText.text = sb.ToString();

        // Лог: добавляем строку когда бросок завершён
        if (_rollingCount == 0 && _lastResults.Count > 0)
        {
            string logLine = $"[{_lastResults.Count}] {string.Join(" + ", _lastResults.ConvertAll(r => $"{DieTypeName(r.type)}={r.value}"))} = {total}";
            _logEntries.Insert(0, logLine);
            UpdateLogText();
        }
    }

    static string DieTypeName(DieType t) => t switch
    {
        DieType.d4 => "d4",
        DieType.d6 => "d6",
        DieType.d8 => "d8",
        DieType.d10 => "d10",
        DieType.d12 => "d12",
        DieType.d20 => "d20",
        DieType.d100 => "d100",
        _ => "d?",
    };

    struct DieResult
    {
        public DieType type;
        public int value;
    }
}
