using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// UI-панель для спавна дайсов, броска, очистки и отображения результатов.
/// Создаёт весь UI программно в Start() — достаточно повесить на пустой GameObject.
/// </summary>
public class DiceUI : MonoBehaviour
{
    /// <summary>Raised when a result is added to the journal, with the die type and face value.</summary>
    public static event Action<string, int, ulong> JournalResultRecorded;
    [Header("Размеры кнопок")]
    public float buttonWidth = 55f;
    public float buttonHeight = 32f;
    public float gap = 4f;
    public float padding = 8f;

    public static DiceUI Instance { get; private set; }

    [Header("Спавн")]
    public float spawnSpread = 0.8f;

    private InputField _scaleInput;
    public InputField ScaleInput => _scaleInput;
    private readonly List<DieResult> _lastResults = new();
    private bool _trackingRoll;
    private int _rollingCount;
    private Canvas _canvas;
    private GameObject _sidebarGO;
    private GameObject _bottomPanelGO; // нижний док
    private GameObject _debugPanelGO;  // старые служебные элементы карты
    private GameObject _mapTexButton;
    private GameObject _mapScaleLabel;
    private GameObject _logPanelGO;    // панель лога событий
    private GameObject _topBarGO;
    private Text _headerPlayers;
    private Text _headerRole;
    private Button _gmPanelButton;
    private float _nextHeaderRefresh;
    private Text _logText;
    private ScrollRect _logScroll;
    private readonly List<string> _logEntries = new(); // строки лога
    private GridManager _gridManager;
    private readonly Image[] _toolRows = new Image[8];
    private GameObject _toolContext;
    private Text _toolContextTitle;
    private Text _toolContextHint;
    private bool _toolContextUserVisible;
    private GameObject _toolEffectButton;
    private GameObject _toolExitButton;
    private GameObject _cameraModeButton;
    private CameraMovement _cameraMovement;
    private GameObject _confirmPanel;
    public bool IsConfirmationOpen => _confirmPanel != null && _confirmPanel.activeSelf;
    private Text _confirmTitle;
    private Text _confirmBody;
    private Button _confirmAccept;
    private string _toolFeedbackKey;
    private string _toolNoticeText;
    private float _toolNoticeUntil;
    private int _cachedAreaCellCount;
    private float _nextAreaCellCountRefresh;

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
        if (GetComponent<EffectPaintTool>() == null)
            gameObject.AddComponent<EffectPaintTool>();
        if (GetComponent<SceneEditor>() == null) gameObject.AddComponent<SceneEditor>();
        if (GetComponent<FogManager>() == null) gameObject.AddComponent<FogManager>();
        BuildUI();
        PlayerColors.Changed += OnPlayerColorsChanged;
        if (GetComponent<DmPanelUI>() == null)
            gameObject.AddComponent<DmPanelUI>();
        if (DiceManager.Instance != null)
            DiceManager.Instance.OnAnyResult += OnDieResult;
        InvokeRepeating(nameof(TrySubscribeToMeasureTool), 0.1f, 0.5f);
    }

    void TrySubscribeToMeasureTool()
    {
        var mt = MeasurementTool.Instance;
        if (mt == null || _subscribedToMT) return;
        mt.OnDragStart += () => { _textureMenuOpen = false; if (_textureMenu != null) _textureMenu.SetActive(false); };
        mt.OnDragEnd += () => {
            if (mt.IsActive && mt.CurrentMode != MeasurementTool.Mode.Ruler &&
                mt.GetCellsInArea().Count > 0)
                ShowTextureMenu();
        };
        _subscribedToMT = true;
        CancelInvoke(nameof(TrySubscribeToMeasureTool));
    }
    private bool _subscribedToMT;

    void Update()
    {
        if (_topBarGO == null || !_topBarGO.activeSelf) return;
        if (IsConfirmationOpen)
        {
            if (!GameplayInputGate.IsTextInputFocused &&
                Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                _confirmPanel.SetActive(false);
            return;
        }
        if (_textureMenuOpen &&
            (Keyboard.current?.escapeKey.wasPressedThisFrame == true &&
             GameplayInputGate.AllowsKeyboardHotkeys ||
             Mouse.current?.rightButton.wasPressedThisFrame == true))
        {
            _textureMenuOpen = false;
            _textureMenu?.SetActive(false);
        }
        if (Mouse.current?.rightButton.wasPressedThisFrame == true &&
            GameplayInputGate.IsPointerOverUI &&
            (MeasurementTool.Instance?.IsLocalActive == true ||
             EffectPaintTool.Instance?.IsActive == true))
            SelectDefaultTool();

        // Tab показывает игроков только пока клавиша удерживается вне текстового поля.
        if (_playerListPanel != null)
        {
            bool showPlayers = GameplayInputGate.AllowsKeyboardHotkeys &&
                Keyboard.current != null && Keyboard.current.tabKey.isPressed;
            if (_playerListPanel.activeSelf != showPlayers)
            {
                _playerListPanel.SetActive(showPlayers);
                if (showPlayers) RefreshPlayerList();
            }
        }

        UpdateHostMapControls();
        UpdateToolFeedback();
        if (Time.unscaledTime >= _nextHeaderRefresh)
        {
            UpdateSessionHeader();
            _nextHeaderRefresh = Time.unscaledTime + 1f;
        }
    }

    static bool IsLocalHost()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
    }

    void UpdateHostMapControls()
    {
        bool isHost = IsLocalHost();
        if (_mapTexButton != null) _mapTexButton.SetActive(isHost);
        if (_mapScaleLabel != null) _mapScaleLabel.SetActive(isHost);
        if (_scaleInput != null) _scaleInput.gameObject.SetActive(isHost);

        if (!isHost && _debugPanelGO != null && _debugPanelGO.activeSelf)
            _debugPanelGO.SetActive(false);
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
        PlayerColors.Changed -= OnPlayerColorsChanged;
        if (DiceManager.Instance != null)
            DiceManager.Instance.OnAnyResult -= OnDieResult;
    }

    private void OnPlayerColorsChanged()
    {
        if (_playerListPanel != null && _playerListPanel.activeSelf)
            RefreshPlayerList();
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
        VttUiSkin.Surface(bg, VttUiSkin.Panel, 10);

        float panelW = 240f;
        float panelH = 50f;
        RectTransform prt = panelGO.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(1f, 1f);
        prt.anchorMax = new Vector2(1f, 1f);
        prt.pivot = new Vector2(1f, 1f);
        prt.sizeDelta = new Vector2(panelW, panelH);
        prt.anchoredPosition = new Vector2(-14f, -68f);

        // Кнопка Tex
        float texW = 70f;
        MakeButton(panelGO.transform, "Карта", 8f, -8f, texW, buttonHeight, uiFont, OpenTexturePicker);
        _mapTexButton = panelGO.transform.Find("Btn_Карта")?.gameObject;

        // Поле Scale
        float scaleX = 8f + texW + gap;
        GameObject scaleGO = new GameObject("ScaleLabel");
        _mapScaleLabel = scaleGO;
        scaleGO.transform.SetParent(panelGO.transform, false);
        Text scaleLabel = scaleGO.AddComponent<Text>();
        scaleLabel.font = uiFont;
        scaleLabel.fontSize = 13;
        scaleLabel.color = VttUiSkin.Muted;
        scaleLabel.text = "Масштаб";
        scaleLabel.alignment = TextAnchor.MiddleLeft;
        RectTransform slrt = scaleGO.GetComponent<RectTransform>();
        slrt.anchorMin = new Vector2(0, 1);
        slrt.anchorMax = new Vector2(0, 1);
        slrt.pivot = new Vector2(0, 1);
        slrt.anchoredPosition = new Vector2(scaleX, -10f);
        slrt.sizeDelta = new Vector2(68f, 20f);

        float inputX = scaleX + 72f;
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
        VttUiSkin.Surface(inputGO.GetComponent<Image>(), VttUiSkin.Raised, 5);

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
        BuildTopBar(canvasGO.transform, uiFont);
        BuildLeftSidebar();
        BuildBottomPanel();
        BuildLogPanel();
        UpdateLogText();
        BuildLeaveButton(canvasGO.transform, uiFont);
        BuildPlayerListPanel(canvasGO.transform, uiFont);
        BuildConfirmationDialog(uiFont);

        // Все панели скрыты при старте — покажутся после host/join
        if (_debugPanelGO != null) _debugPanelGO.SetActive(false);
        if (_sidebarGO != null) _sidebarGO.SetActive(false);
        if (_bottomPanelGO != null) _bottomPanelGO.SetActive(false);
        if (_logPanelGO != null) _logPanelGO.SetActive(false);
        if (_topBarGO != null) _topBarGO.SetActive(false);
        if (_toolContext != null) _toolContext.SetActive(false);

        BuildTabs();
    }

    /// <summary>Показывает игровые панели (вызывается после host/join).</summary>
    public void ShowGamePanels()
    {
        _toolFeedbackKey = null;
        _toolContextUserVisible = false;
        if (_toolContext != null) _toolContext.SetActive(false);
        if (_topBarGO != null) _topBarGO.SetActive(true);
        if (_sidebarGO != null) _sidebarGO.SetActive(true);
        if (_bottomPanelGO != null) _bottomPanelGO.SetActive(true);
        if (_logPanelGO != null) { _logPanelGO.SetActive(true); if (_logTab != null) _logTab.SetActive(false); }
        if (_leaveButton != null) _leaveButton.SetActive(true);
        if (_debugPanelGO != null) _debugPanelGO.SetActive(false);
        UpdateHostMapControls();
        UpdateSessionHeader();
    }

    /// <summary>Скрывает все игровые панели (при выходе из лобби).</summary>
    public void HideGamePanels()
    {
        SelectDefaultTool();
        _toolContextUserVisible = false;
        if (_topBarGO != null) _topBarGO.SetActive(false);
        if (_sidebarGO != null) _sidebarGO.SetActive(false);
        if (_bottomPanelGO != null) _bottomPanelGO.SetActive(false);
        if (_debugPanelGO != null) _debugPanelGO.SetActive(false);
        if (_logPanelGO != null) _logPanelGO.SetActive(false);
        if (_logTab != null) _logTab.SetActive(false);
        if (_leaveButton != null) _leaveButton.SetActive(false);
        if (_leaveConfirmDialog != null) _leaveConfirmDialog.SetActive(false);
        if (_textureMenu != null) _textureMenu.SetActive(false);
        if (_toolContext != null) _toolContext.SetActive(false);
        if (_confirmPanel != null) _confirmPanel.SetActive(false);
        if (_playerListPanel != null) _playerListPanel.SetActive(false);
    }

    void BuildTopBar(Transform canvasTr, Font font)
    {
        _topBarGO = new GameObject("SessionTopBar", typeof(RectTransform), typeof(Image));
        _topBarGO.transform.SetParent(canvasTr, false);
        VttUiSkin.Surface(_topBarGO.GetComponent<Image>(),
            new Color(0.043f, 0.057f, 0.076f, 0.97f), 0);
        _topBarGO.GetComponent<Image>().raycastTarget = false;
        RectTransform bar = _topBarGO.GetComponent<RectTransform>();
        bar.anchorMin = new Vector2(0, 1);
        bar.anchorMax = new Vector2(1, 1);
        bar.pivot = new Vector2(0.5f, 1);
        bar.sizeDelta = new Vector2(0, 56);
        bar.anchoredPosition = Vector2.zero;

        _headerPlayers = MakeHeaderText("●  1 игрок", font, 14, VttUiSkin.Green,
            new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(150, 32), new Vector2(18, 0));

        MakeHeaderButton("Журнал", font, 110, -737,
            () => TogglePanel(_logPanelGO, _logTab), "icon-park-outline--log");
        MakeHeaderButton("Кубики", font, 100, -629,
            () => TogglePanel(_bottomPanelGO, null), "lucide--dices");
        MakeHeaderButton("Инструменты", font, 140, -481,
            () => { TogglePanel(_sidebarGO, null);
                if (_sidebarGO != null && !_sidebarGO.activeSelf)
                    SelectDefaultTool(); }, "fa-solid--tools");
        MakeHeaderButton("Инициатива", font, 145, -328,
            () => InitiativeTracker.Instance?.ToggleVisible(), "charm--swords");
        var characterButton = MakeHeaderButton("Персонаж", font, 130, -190,
            () => { }, "circle-user-round");
        characterButton.interactable = false;
        _gmPanelButton = MakeHeaderButton("ПАНЕЛЬ DM", font, 120, -56,
            () => DmPanelUI.Instance?.Toggle());
        _headerRole = _gmPanelButton.GetComponentInChildren<Text>();
        _headerRole.alignment = TextAnchor.MiddleCenter;
        _headerRole.color = VttUiSkin.Blue;
    }

    Text MakeHeaderText(string value, Font font, int size, Color color,
        Vector2 anchor, Vector2 pivot, Vector2 dimensions, Vector2 position)
    {
        var go = new GameObject(value, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(_topBarGO.transform, false);
        var text = go.GetComponent<Text>();
        text.font = font; text.fontSize = size; text.text = value;
        text.color = color; text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot;
        rt.sizeDelta = dimensions; rt.anchoredPosition = position;
        return text;
    }

    Button MakeHeaderButton(string label, Font font, float width, float right,
        UnityEngine.Events.UnityAction action, string iconKey = null)
    {
        var go = new GameObject("Header " + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(_topBarGO.transform, false);
        VttUiSkin.ButtonStyle(go.GetComponent<Image>(), VttUiSkin.Button, 7);
        go.GetComponent<Button>().onClick.AddListener(action);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1, 0.5f);
        rt.pivot = new Vector2(1, 0.5f);
        rt.sizeDelta = new Vector2(width, 34);
        rt.anchoredPosition = new Vector2(right, 0);
        var textGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textGO.transform.SetParent(go.transform, false);
        var text = textGO.GetComponent<Text>();
        text.font = font; text.text = label; text.fontSize = 14;
        text.color = VttUiSkin.Text; text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        var tr = textGO.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
        tr.offsetMin = iconKey != null ? new Vector2(30, 0) : Vector2.zero;
        tr.offsetMax = Vector2.zero;
        if (iconKey != null)
            AddButtonIcon(go.transform, iconKey, new Vector2(0, 0.5f),
                new Vector2(0, 0.5f), new Vector2(18, 18), new Vector2(9, 0), VttUiSkin.Text);
        return go.GetComponent<Button>();
    }

    void UpdateSessionHeader()
    {
        var nm = NetworkManager.Singleton;
        int count = nm != null && nm.IsListening ? nm.ConnectedClientsIds.Count : 1;
        if (_headerPlayers != null)
        {
            string noun = count % 10 == 1 && count % 100 != 11 ? "игрок" :
                count % 10 >= 2 && count % 10 <= 4 && (count % 100 < 12 || count % 100 > 14) ? "игрока" : "игроков";
            _headerPlayers.text = $"●  {count} {noun}";
        }
        if (_headerRole != null)
            _headerRole.text = nm != null && nm.IsHost ? "ПАНЕЛЬ DM" : "ИГРОК";
        if (_gmPanelButton != null)
            _gmPanelButton.interactable = IsLocalHost();
    }

    void BuildLeaveButton(Transform canvasTr, Font font)
    {
        // Кнопка выхода (красная, слева сверху)
        _leaveButton = new GameObject("LeaveButton");
        _leaveButton.transform.SetParent(canvasTr, false);
        _leaveButton.SetActive(false); // скрыта до входа в игру

        Image img = _leaveButton.AddComponent<Image>();
        VttUiSkin.ButtonStyle(img, new Color(0.27f, 0.09f, 0.11f), 7);

        Button btn = _leaveButton.AddComponent<Button>();
        btn.onClick.AddListener(ShowLeaveConfirm);

        RectTransform rt = _leaveButton.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(34f, 34f);
        rt.anchoredPosition = new Vector2(-10f, -11f);

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
        VttUiSkin.Surface(dImg, VttUiSkin.Panel, 12);

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
        {
            var question = _leaveConfirmDialog.transform.Find("Question").GetComponent<Text>();
            bool unsaved = SceneFileStore.HasUnsavedChanges();
            question.text = unsaved ? "Выйти из лобби?\nСессия не сохранена в JSON." : "Выйти из лобби?";
            question.fontSize = unsaved ? 16 : 20;
            _leaveConfirmDialog.SetActive(true);
        }
    }

    void DoLeave()
    {
        if (GameNetworkManager.Instance != null)
            _ = GameNetworkManager.Instance.ShutdownAndReset();
        else if (NetworkManager.Singleton != null)
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
        VttUiSkin.Surface(bg, VttUiSkin.Panel, 12);

        RectTransform rt = sidebarGO.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(196f, 620f);
        rt.anchoredPosition = new Vector2(12f, -72f);
        ToolLabel(sidebarGO.transform, "ИНСТРУМЕНТЫ", 17, VttUiSkin.Text, -11, true);
        ToolLabel(sidebarGO.transform, "Выберите действие ниже", 12,
            VttUiSkin.Muted, -42);
        _toolRows[0] = MakeToolButton(sidebarGO.transform, "Выбрать / двигать", "fa7-solid--arrows", -70,
            SelectDefaultTool);
        _toolRows[1] = MakeToolButton(sidebarGO.transform, "Создать токен", "circle-user-round", -116,
            () => { SelectDefaultTool(); TokenManager.Instance?.RequestSpawnToken(); });
        ToolLabel(sidebarGO.transform, "ИЗМЕРИТЬ И ОТМЕТИТЬ", 12, VttUiSkin.Muted, -169, true);
        _toolRows[2] = MakeToolButton(sidebarGO.transform, "Линейка", "ruler", -194,
            () => ActivateMeasurement(0));
        _toolRows[3] = MakeToolButton(sidebarGO.transform, "Область: круг", "circle-dashed", -240,
            () => ActivateMeasurement(1));
        _toolRows[4] = MakeToolButton(sidebarGO.transform, "Область: квадрат", "square-dashed", -286,
            () => ActivateMeasurement(2));
        _toolRows[5] = MakeToolButton(sidebarGO.transform, "Область: конус", "triangle-dashed", -332,
            () => ActivateMeasurement(3));
        ToolLabel(sidebarGO.transform, "ЭФФЕКТЫ НА КЛЕТКАХ", 12, VttUiSkin.Muted, -387, true);
        _toolRows[6] = MakeToolButton(sidebarGO.transform, "Закрасить клетки", "paintbrush", -412,
            ShowTextureMenu);
        _toolRows[7] = MakeToolButton(sidebarGO.transform, "Ластик: одна клетка", "eraser", -458,
            () => { _textureMenuOpen = false; _textureMenu?.SetActive(false);
                EffectPaintTool.Instance?.Activate(CellMarker.EraseToolIndex); });
        MakeToolButton(sidebarGO.transform, "Очистить мои…", "trash-2", -504,
            () => ConfirmAction("Очистить мои отметки?",
                "Будут удалены все клетки, которые отметили вы. Отменить удаление нельзя.",
                CellMarker.ClearAllMyMarkers));
        MakeToolButton(sidebarGO.transform, "Подсказка", "basil--lightbulb-outline", -562,
            () => { _toolContextUserVisible = !_toolContextUserVisible; UpdateToolFeedback(); });

        BuildToolContext();
        BuildTextureMenu();
    }

    Image MakeToolButton(Transform parent, string label, string iconKey, float y,
        UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject("Tool " + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        VttUiSkin.ButtonStyle(go.GetComponent<Image>(), VttUiSkin.Button, 8);
        go.GetComponent<Button>().onClick.AddListener(action);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(180, 40);
        rt.anchoredPosition = new Vector2(8, y);
        AddButtonIcon(go.transform, iconKey, new Vector2(0, 0.5f),
            new Vector2(0, 0.5f), new Vector2(22, 22), new Vector2(12, 0), VttUiSkin.Text);
        var text = new GameObject("Label", typeof(RectTransform), typeof(Text));
        text.transform.SetParent(go.transform, false);
        var txt = text.GetComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.text = label; txt.fontSize = 14; txt.color = VttUiSkin.Text;
        txt.alignment = TextAnchor.MiddleLeft; txt.raycastTarget = false;
        var trt = text.GetComponent<RectTransform>();
        trt.anchorMin = trt.anchorMax = new Vector2(0, 0.5f);
        trt.pivot = new Vector2(0, 0.5f); trt.sizeDelta = new Vector2(140, 30);
        trt.anchoredPosition = new Vector2(42, 0);
        return go.GetComponent<Image>();
    }

    Text ToolLabel(Transform parent, string value, int size, Color color, float y, bool bold = false)
    {
        var go = new GameObject("Label " + value, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var label = go.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = size;
        label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        label.color = color;
        label.alignment = TextAnchor.MiddleLeft;
        label.text = value;
        label.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(180, 26);
        rt.anchoredPosition = new Vector2(12, y);
        return label;
    }

    void BuildToolContext()
    {
        _toolContext = new GameObject("ToolContext", typeof(RectTransform), typeof(Image));
        _toolContext.transform.SetParent(_canvas.transform, false);
        VttUiSkin.Surface(_toolContext.GetComponent<Image>(), VttUiSkin.Panel, 10);
        var rt = _toolContext.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(268, 164);
        rt.anchoredPosition = new Vector2(216, -72);
        _toolContextTitle = ToolLabel(_toolContext.transform, "Обычный режим", 16,
            VttUiSkin.Text, -12, true);
        _toolContextTitle.GetComponent<RectTransform>().sizeDelta = new Vector2(172, 28);
        _toolContextHint = ToolLabel(_toolContext.transform, "", 13, VttUiSkin.Muted, -48);
        var hintRt = _toolContextHint.GetComponent<RectTransform>();
        hintRt.sizeDelta = new Vector2(244, 66);
        _toolContextHint.alignment = TextAnchor.UpperLeft;
        _toolContextHint.horizontalOverflow = HorizontalWrapMode.Wrap;
        _toolEffectButton = MakeContextButton(_toolContext.transform, "Выбрать эффект", 12, -124,
            244, 30, ShowTextureMenu);
        _toolExitButton = MakeContextButton(_toolContext.transform, "Выйти", 190, -11,
            66, 28, SelectDefaultTool);
        _cameraModeButton = MakeContextButton(_toolContext.transform, "Вид: сверху", 12, -124,
            244, 30, () => {
                if (_cameraMovement == null) _cameraMovement = Camera.main?.GetComponent<CameraMovement>();
                _cameraMovement?.ToggleMode();
                _toolFeedbackKey = null;
                UpdateToolFeedback();
            });
    }

    GameObject MakeContextButton(Transform parent, string title, float x, float y,
        float width, float height, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(title, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        VttUiSkin.ButtonStyle(go.GetComponent<Image>(), VttUiSkin.Button, 7);
        if (onClick != null) go.GetComponent<Button>().onClick.AddListener(onClick);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(width, height);
        rt.anchoredPosition = new Vector2(x, y);
        var label = ToolLabel(go.transform, title, 12, VttUiSkin.Text, -2, true);
        label.alignment = TextAnchor.MiddleCenter;
        var lr = label.GetComponent<RectTransform>();
        lr.anchoredPosition = new Vector2(2, -2);
        lr.sizeDelta = new Vector2(width - 4, height - 4);
        return go;
    }

    void BuildConfirmationDialog(Font font)
    {
        _confirmPanel = new GameObject("ConfirmAction", typeof(RectTransform), typeof(Image));
        _confirmPanel.transform.SetParent(_canvas.transform, false);
        var modalCanvas = _confirmPanel.AddComponent<Canvas>();
        modalCanvas.overrideSorting = true;
        modalCanvas.sortingOrder = 32000;
        _confirmPanel.AddComponent<GraphicRaycaster>();
        var overlay = _confirmPanel.GetComponent<Image>();
        overlay.color = new Color(0.01f, 0.02f, 0.04f, 0.65f);
        overlay.raycastTarget = true;
        var root = _confirmPanel.GetComponent<RectTransform>();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;

        var box = new GameObject("Dialog", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(_confirmPanel.transform, false);
        VttUiSkin.Surface(box.GetComponent<Image>(), VttUiSkin.Panel, 12);
        var rt = box.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(430, 208);
        rt.anchoredPosition = Vector2.zero;
        _confirmTitle = ToolLabel(box.transform, "Подтвердить действие", 18,
            VttUiSkin.Text, -18, true);
        _confirmTitle.GetComponent<RectTransform>().sizeDelta = new Vector2(400, 30);
        _confirmBody = ToolLabel(box.transform, "", 14, VttUiSkin.Muted, -58);
        _confirmBody.GetComponent<RectTransform>().sizeDelta = new Vector2(400, 76);
        _confirmBody.alignment = TextAnchor.UpperLeft;
        _confirmBody.horizontalOverflow = HorizontalWrapMode.Wrap;
        MakeContextButton(box.transform, "Отмена", 12, -154, 190, 38,
            () => _confirmPanel.SetActive(false));
        var accept = MakeContextButton(box.transform, "Удалить", 228, -154, 190, 38, null);
        accept.GetComponent<Image>().color = new Color(0.33f, 0.12f, 0.15f);
        _confirmAccept = accept.GetComponent<Button>();
        _confirmPanel.SetActive(false);
    }

    public void ConfirmAction(string title, string detail, System.Action action)
    {
        if (_confirmPanel == null || action == null) return;
        _confirmTitle.text = title;
        _confirmBody.text = detail;
        _confirmAccept.onClick.RemoveAllListeners();
        _confirmAccept.onClick.AddListener(() => {
            _confirmPanel.SetActive(false);
            action();
        });
        _confirmPanel.transform.SetAsLastSibling();
        _confirmPanel.SetActive(true);
    }

    void SelectDefaultTool()
    {
        SceneEditor.Instance?.Deactivate();
        FogManager.Instance?.StopManual();
        MeasurementTool.Instance?.Deactivate();
        EffectPaintTool.Instance?.Deactivate();
        _textureMenuOpen = false;
        _textureMenu?.SetActive(false);
        UpdateToolFeedback();
    }

    void ActivateMeasurement(int mode)
    {
        _cachedAreaCellCount = 0;
        _nextAreaCellCountRefresh = 0;
        _textureMenuOpen = false;
        _textureMenu?.SetActive(false);
        MeasurementTool.Instance?.Activate(mode);
        UpdateToolFeedback();
    }

    void UpdateToolFeedback()
    {
        if (_toolContext == null || _sidebarGO == null) return;
        if (_toolNoticeUntil > 0 && Time.unscaledTime >= _toolNoticeUntil)
        {
            _toolNoticeUntil = 0;
            _toolFeedbackKey = null;
        }
        _toolContext.SetActive(_sidebarGO.activeSelf && _toolContextUserVisible);
        var measure = MeasurementTool.Instance;
        var paint = EffectPaintTool.Instance;
        if (_cameraMovement == null) _cameraMovement = Camera.main?.GetComponent<CameraMovement>();
        bool freeCamera = _cameraMovement != null && _cameraMovement.IsFreeMode;
        bool measuring = measure != null && measure.IsLocalActive;
        bool painting = paint != null && paint.IsActive;
        int row = measuring ? 2 + (int)measure.CurrentMode :
            painting ? (paint.IsEraseMode ? 7 : 6) : 0;
        for (int i = 0; i < _toolRows.Length; i++)
            if (_toolRows[i] != null)
                _toolRows[i].color = i == row ? new Color(0.11f, 0.29f, 0.48f) : VttUiSkin.Button;

        int selectedCells = 0;
        if (measuring && measure.CurrentMode != MeasurementTool.Mode.Ruler)
        {
            if (Time.unscaledTime >= _nextAreaCellCountRefresh)
            {
                _cachedAreaCellCount = measure.GetCellsInArea().Count;
                _nextAreaCellCountRefresh = Time.unscaledTime + 0.12f;
            }
            selectedCells = _cachedAreaCellCount;
        }
        string key = measuring ? "m" + (int)measure.CurrentMode + ":" + selectedCells :
            painting ? "p" + paint.SelectedTextureIndex : "default" + freeCamera;
        if (!measuring && !painting && _toolFeedbackKey != null &&
            !_toolFeedbackKey.StartsWith("default"))
        {
            _textureMenuOpen = false;
            _textureMenu?.SetActive(false);
        }
        if (key == _toolFeedbackKey) return;
        _toolFeedbackKey = key;
        _toolEffectButton.SetActive(measuring && measure.CurrentMode != MeasurementTool.Mode.Ruler ||
            painting && !paint.IsEraseMode);
        _toolExitButton.SetActive(measuring || painting);
        _cameraModeButton.SetActive(!measuring && !painting);
        if (!measuring && !painting)
            _cameraModeButton.GetComponentInChildren<Text>().text = freeCamera
                ? "Вид: свободный · F1" : "Вид: сверху · F1";
        if (measuring && measure.CurrentMode != MeasurementTool.Mode.Ruler)
            _toolExitButton.GetComponentInChildren<Text>().text = "Отмена";
        else if (measuring || painting)
            _toolExitButton.GetComponentInChildren<Text>().text = "Выйти";
        if (measuring)
        {
            if (measure.CurrentMode == MeasurementTool.Mode.Ruler)
            {
                _toolContextTitle.text = "Линейка · активна";
                _toolContextHint.text = "Зажмите ЛКМ на карте и протяните до цели. Esc или ПКМ — выйти.";
            }
            else
            {
                int count = selectedCells;
                _toolContextTitle.text = "Область: " + (measure.CurrentMode switch
                {
                    MeasurementTool.Mode.Circle => "круг · активна",
                    MeasurementTool.Mode.Square => "квадрат · активна",
                    _ => "конус · активна"
                });
                _toolContextHint.text = count > 0
                    ? $"Выделено клеток: {count}. Выберите эффект и примените область. Esc или ПКМ — отменить."
                    : "Зажмите ЛКМ и протяните на карте. Затем выберите эффект для выделенных клеток.";
            }
        }
        else if (painting)
        {
            _toolContextTitle.text = paint.IsEraseMode ? "Ластик · активен" : "Закраска · активна";
            _toolContextHint.text = paint.IsEraseMode
                ? "Нажмите ЛКМ на клетку, чтобы стереть одну отметку. Esc или ПКМ — выйти."
                : $"Эффект: {CellMarker.TextureNames[paint.SelectedTextureIndex]}. ЛКМ по клеткам — нанести. Esc или ПКМ — выйти.";
        }
        else
        {
            _toolContextTitle.text = "Обычный режим";
            _toolContextHint.text = freeCamera
                ? "Свободная камера: ПКМ + мышь — обзор, WASD — движение. Кнопка ниже или F1 — вернуться."
                : IsLocalHost()
                    ? "ЛКМ — двигать токен или кубик. ПКМ по токену — действия. Средняя кнопка — двигать карту."
                    : "ЛКМ — двигать токен или кубик. ПКМ по токену — действия. Средняя кнопка — сдвинуть вид.";
        }
        bool notice = _toolNoticeUntil > Time.unscaledTime;
        if (notice) _toolContextHint.text = _toolNoticeText;
        _toolContextHint.color = notice ? VttUiSkin.Red : VttUiSkin.Muted;
    }

    public void ShowToolNotice(string message)
    {
        _toolNoticeText = message;
        _toolNoticeUntil = Time.unscaledTime + 3.5f;
        _toolFeedbackKey = null;
        UpdateToolFeedback();
    }

    void AddButtonIcon(Transform parent, string key, Vector2 anchor, Vector2 pivot,
        Vector2 size, Vector2 position, Color color)
    {
        var sprite = VttUiSkin.Icon(key);
        if (sprite == null)
        {
            var fallback = new GameObject("IconFallback", typeof(RectTransform), typeof(Text));
            fallback.transform.SetParent(parent, false);
            var glyph = fallback.GetComponent<Text>();
            glyph.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            glyph.text = "↖";
            glyph.fontSize = 24;
            glyph.color = color;
            glyph.alignment = TextAnchor.MiddleCenter;
            glyph.raycastTarget = false;
            var fallbackRt = fallback.GetComponent<RectTransform>();
            fallbackRt.anchorMin = fallbackRt.anchorMax = anchor;
            fallbackRt.pivot = pivot;
            fallbackRt.sizeDelta = size;
            fallbackRt.anchoredPosition = position;
            return;
        }
        var go = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite; image.color = color; image.preserveAspect = true;
        image.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = pivot;
        rt.sizeDelta = size; rt.anchoredPosition = position;
    }

    private GameObject _textureMenu;
    private Text _effectMenuHint;
    private bool _textureMenuOpen;
    private readonly List<Image> _effectMenuRows = new();

    void ShowTextureMenu()
    {
        _textureMenuOpen = true;
        if (_textureMenu != null) _textureMenu.SetActive(_textureMenuOpen);
        var mt = MeasurementTool.Instance;
        int count = mt != null && mt.IsActive && mt.CurrentMode != MeasurementTool.Mode.Ruler
            ? mt.GetCellsInArea().Count : 0;
        if (_effectMenuHint != null)
            _effectMenuHint.text = count > 0
                ? $"Выберите эффект → применить к {count} клеткам области."
                : "1. Выберите эффект. 2. Нажимайте ЛКМ по клеткам.";
        if (_textureMenuOpen) RefreshEffectMenuSelection();
    }

    // ═══ Список игроков (Tab) ═══

    void BuildPlayerListPanel(Transform canvasTr, Font font)
    {
        _playerListPanel = new GameObject("PlayerListPanel", typeof(RectTransform));
        _playerListPanel.transform.SetParent(canvasTr, false);
        _playerListPanel.SetActive(false);

        var bg = _playerListPanel.AddComponent<Image>();
        VttUiSkin.Surface(bg, VttUiSkin.Panel, 11);

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

        string joinCode = RelayManager.CurrentJoinCode;
        if (!string.IsNullOrEmpty(joinCode))
        {
            var codeGO = new GameObject("LobbyCode", typeof(RectTransform));
            codeGO.transform.SetParent(_playerListContent.transform, false);

            var codeRt = codeGO.GetComponent<RectTransform>();
            codeRt.anchorMin = codeRt.anchorMax = new Vector2(0f, 1f);
            codeRt.pivot = new Vector2(0f, 1f);
            codeRt.sizeDelta = new Vector2(260f, 22f);
            codeRt.anchoredPosition = new Vector2(0f, y);

            var codeTxt = codeGO.AddComponent<Text>();
            codeTxt.text = $"Код лобби: {joinCode}";
            codeTxt.font = font;
            codeTxt.fontSize = 13;
            codeTxt.fontStyle = FontStyle.Bold;
            codeTxt.color = new Color(0.75f, 0.85f, 1f);
            codeTxt.alignment = TextAnchor.MiddleLeft;

            y -= 28f;
        }

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

        // Обновить высоту панели под количество игроков (+ строка кода)
        float headerExtra = string.IsNullOrEmpty(joinCode) ? 0f : 28f;
        float h = Mathf.Max(80f, Mathf.Abs(y) + 50f + headerExtra);
        _playerListPanel.GetComponent<RectTransform>().sizeDelta = new Vector2(280f, h);
    }

    void BuildTextureMenu()
    {
        _textureMenu = new GameObject("TextureMenu");
        _textureMenu.transform.SetParent(_canvas.transform, false);
        _textureMenu.SetActive(false);

        Image bg = _textureMenu.AddComponent<Image>();
        VttUiSkin.Surface(bg, VttUiSkin.Panel, 10);
        RectTransform rt = _textureMenu.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(286f, 372f);
        rt.anchoredPosition = new Vector2(216f, -244f);
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var heading = new GameObject("Title", typeof(RectTransform), typeof(Text));
        heading.transform.SetParent(_textureMenu.transform, false);
        var headingText = heading.GetComponent<Text>();
        headingText.font = font; headingText.fontSize = 15;
        headingText.fontStyle = FontStyle.Bold; headingText.color = VttUiSkin.Text;
        headingText.text = "ЭФФЕКТЫ КАРТЫ";
        headingText.raycastTarget = false;
        var headingRt = heading.GetComponent<RectTransform>();
        headingRt.anchorMin = headingRt.anchorMax = headingRt.pivot = new Vector2(0, 1);
        headingRt.sizeDelta = new Vector2(250, 25);
        headingRt.anchoredPosition = new Vector2(14, -12);

        string[] names = { "Огонь / опасность", "Вода / магия", "Природа / яд",
            "Сложная местность", "Тьма", "Стена" };
        int totalButtons = CellMarker.TextureNames.Length;
        for (int i = 0; i < totalButtons; i++)
        {
            int idx = i;
            GameObject btnGO = new GameObject("Effect " + names[i],
                typeof(RectTransform), typeof(Image), typeof(Button));
            btnGO.transform.SetParent(_textureMenu.transform, false);
            Image bImg = btnGO.GetComponent<Image>();
            VttUiSkin.ButtonStyle(bImg, VttUiSkin.Button, 7);
            _effectMenuRows.Add(bImg);
            Button btn = btnGO.GetComponent<Button>();
            btn.onClick.AddListener(() => OnTextureMenuPick(idx));
            RectTransform brt = btnGO.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(0f, 1f);
            brt.pivot = new Vector2(0f, 1f);
            brt.sizeDelta = new Vector2(258f, 39f);
            brt.anchoredPosition = new Vector2(14f, -48f - i * 44f);

            {
                var swatch = new GameObject("Swatch", typeof(RectTransform), typeof(Image));
                swatch.transform.SetParent(btnGO.transform, false);
                var swatchImage = swatch.GetComponent<Image>();
                Color swatchColor = CellMarker.TextureColors[i]; swatchColor.a = 1f;
                VttUiSkin.Surface(swatchImage, swatchColor, 5, false);
                swatchImage.raycastTarget = false;
                var swatchRt = swatch.GetComponent<RectTransform>();
                swatchRt.anchorMin = swatchRt.anchorMax = swatchRt.pivot = new Vector2(0, 0.5f);
                swatchRt.sizeDelta = new Vector2(22, 22);
                swatchRt.anchoredPosition = new Vector2(12, 0);
            }

            var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(btnGO.transform, false);
            var labelText = label.GetComponent<Text>();
            labelText.font = font; labelText.fontSize = 14; labelText.text = names[i];
            labelText.color = VttUiSkin.Text; labelText.alignment = TextAnchor.MiddleLeft;
            labelText.raycastTarget = false;
            var labelRt = label.GetComponent<RectTransform>();
            labelRt.anchorMin = labelRt.anchorMax = labelRt.pivot = new Vector2(0, 0.5f);
            labelRt.sizeDelta = new Vector2(210, 30);
            labelRt.anchoredPosition = new Vector2(46, 0);
        }

        var hint = new GameObject("Hint", typeof(RectTransform), typeof(Text));
        hint.transform.SetParent(_textureMenu.transform, false);
        var hintText = hint.GetComponent<Text>();
        _effectMenuHint = hintText;
        hintText.font = font; hintText.fontSize = 12; hintText.color = VttUiSkin.Muted;
        hintText.text = "Выберите эффект → нажимайте ЛКМ по клеткам. Ластик находится слева.";
        hintText.alignment = TextAnchor.MiddleLeft; hintText.raycastTarget = false;
        var hintRt = hint.GetComponent<RectTransform>();
        hintRt.anchorMin = hintRt.anchorMax = hintRt.pivot = new Vector2(0, 1);
        hintRt.sizeDelta = new Vector2(262, 34);
        hintRt.anchoredPosition = new Vector2(14, -326);
    }

    void RefreshEffectMenuSelection()
    {
        var tool = EffectPaintTool.Instance;
        for (int i = 0; i < _effectMenuRows.Count; i++)
        {
            int index = i;
            _effectMenuRows[i].color = tool != null && tool.IsActive &&
                tool.SelectedTextureIndex == index
                ? new Color(0.11f, 0.29f, 0.48f) : VttUiSkin.Button;
        }
    }

    void OnTextureMenuPick(int textureIndex)
    {
        var mt = MeasurementTool.Instance;
        if (mt != null && mt.IsActive && mt.CurrentMode != MeasurementTool.Mode.Ruler)
        {
            if (mt.GetCellsInArea().Count == 0)
            {
                _textureMenuOpen = false;
                _textureMenu?.SetActive(false);
                ShowToolNotice("Сначала протяните область на карте, затем выберите эффект.");
                return;
            }
            mt.ApplyArea(textureIndex);
            EffectPaintTool.Instance?.Deactivate();
        }
        else
            EffectPaintTool.Instance?.Activate(textureIndex);

        _textureMenuOpen = false;
        if (_textureMenu != null) _textureMenu.SetActive(false);
        UpdateToolFeedback();
    }

    static void AddEraseCross(Transform parent, float size)
    {
        Color crossColor = new Color(0.85f, 0.15f, 0.15f, 0.95f);
        float thickness = 4f;
        float len = size * 0.55f;

        for (int d = 0; d < 2; d++)
        {
            var lineGO = new GameObject(d == 0 ? "Cross1" : "Cross2");
            lineGO.transform.SetParent(parent, false);
            var img = lineGO.AddComponent<Image>();
            img.color = crossColor;
            img.raycastTarget = false;

            var lrt = lineGO.GetComponent<RectTransform>();
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.sizeDelta = new Vector2(len, thickness);
            lrt.localRotation = Quaternion.Euler(0f, 0f, d == 0 ? 45f : -45f);
        }
    }

    /// <summary>
    /// Рабочий док кубиков. Каждая кнопка вызывает прежний SpawnDie/ClearAll.
    /// </summary>
    void BuildBottomPanel()
    {
        if (_canvas == null) return;

        GameObject panelGO = new GameObject("BottomPanel");
        panelGO.transform.SetParent(_canvas.transform, false);
        _bottomPanelGO = panelGO;

        Image bg = panelGO.AddComponent<Image>();
        VttUiSkin.Surface(bg, VttUiSkin.Panel, 14);

        RectTransform rt = panelGO.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(650f, 86f);
        rt.anchoredPosition = new Vector2(0f, 16f);

        // Иконки дайсов
        Font uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var dieTypes = new[] { DieType.d4, DieType.d6, DieType.d8, DieType.d10, DieType.d12, DieType.d20, DieType.d100 };
        string[] labels = { "d4", "d6", "d8", "d10", "d12", "d20", "d100" };
        string[] iconNames = { "mdi--dice-d4-outline", "mdi--dice-d6-outline",
            "mdi--dice-d8-outline", "mdi--dice-d10-outline", "mdi--dice-d12-outline",
            "mdi--dice-d20-outline", "fa6-solid--percent" };
        Color[] colors = {
            new Color(0.9f, 0.3f, 0.3f),  // d4   — красный
            new Color(0.3f, 0.7f, 0.9f),  // d6   — голубой
            new Color(0.3f, 0.9f, 0.4f),  // d8   — зелёный
            new Color(0.9f, 0.6f, 0.2f),  // d10  — оранжевый
            new Color(0.7f, 0.3f, 0.9f),  // d12  — фиолетовый
            new Color(0.9f, 0.8f, 0.2f),  // d20  — золотой
            new Color(0.6f, 0.6f, 0.7f),  // d100 — серый
        };

        for (int i = 0; i < dieTypes.Length; i++)
        {
            var dt = dieTypes[i];
            MakeDieButton(panelGO.transform, labels[i], iconNames[i], colors[i],
                14f + i * 80f, uiFont, () => SpawnDie(dt));
        }

        MakeDieButton(panelGO.transform, "Убрать…", "trash-2", VttUiSkin.Red,
            574f, uiFont, ConfirmDiceClear);

    }

    void MakeDieButton(Transform parent, string label, string iconKey, Color color, float x,
        Font font, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject("Dice " + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        VttUiSkin.ButtonStyle(go.GetComponent<Image>(), VttUiSkin.Button, 8);
        go.GetComponent<Button>().onClick.AddListener(action);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 0.5f);
        rt.sizeDelta = new Vector2(62, 66);
        rt.anchoredPosition = new Vector2(x, 0);

        if (!string.IsNullOrEmpty(iconKey) && VttUiSkin.Icon(iconKey) != null)
            AddButtonIcon(go.transform, iconKey, new Vector2(0.5f, 1),
                new Vector2(0.5f, 1), new Vector2(38, 38), new Vector2(0, -4), color);
        else
        {
            var number = new GameObject("Number", typeof(RectTransform), typeof(Text));
            number.transform.SetParent(go.transform, false);
            var text = number.GetComponent<Text>();
            text.font = font; text.fontSize = 26; text.fontStyle = FontStyle.Bold;
            text.text = label.Length > 1 && label[0] == 'd' ? label.Substring(1) : label;
            text.color = color; text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            var nr = number.GetComponent<RectTransform>();
            nr.anchorMin = nr.anchorMax = new Vector2(0.5f, 1);
            nr.pivot = new Vector2(0.5f, 1);
            nr.sizeDelta = new Vector2(46, 40);
            nr.anchoredPosition = new Vector2(0, -4);
        }

        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGO.transform.SetParent(go.transform, false);
        var caption = labelGO.GetComponent<Text>();
        caption.font = font; caption.fontSize = label == "Очистить" ? 10 : 12;
        caption.text = label; caption.color = VttUiSkin.Muted;
        caption.alignment = TextAnchor.MiddleCenter; caption.raycastTarget = false;
        var lr = labelGO.GetComponent<RectTransform>();
        lr.anchorMin = new Vector2(0, 0); lr.anchorMax = new Vector2(1, 0);
        lr.pivot = new Vector2(0.5f, 0); lr.sizeDelta = new Vector2(0, 19);
        lr.anchoredPosition = new Vector2(0, 3);
    }

    /// <summary>
    /// Журнал реальных результатов бросков, не демонстрационные записи.
    /// </summary>
    void BuildLogPanel()
    {
        if (_canvas == null) return;

        GameObject panelGO = new GameObject("LogPanel");
        panelGO.transform.SetParent(_canvas.transform, false);
        _logPanelGO = panelGO;

        Image bg = panelGO.AddComponent<Image>();
        VttUiSkin.Surface(bg, VttUiSkin.Panel, 12);

        RectTransform rt = panelGO.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(360f, 360f);
        rt.anchoredPosition = new Vector2(-14f, 16f);

        var headingGO = new GameObject("JournalHeading", typeof(RectTransform), typeof(Text));
        headingGO.transform.SetParent(panelGO.transform, false);
        var heading = headingGO.GetComponent<Text>();
        heading.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        heading.text = "ЖУРНАЛ БРОСКОВ"; heading.fontSize = 15;
        heading.fontStyle = FontStyle.Bold; heading.color = VttUiSkin.Text;
        heading.alignment = TextAnchor.MiddleLeft; heading.raycastTarget = false;
        var headingRt = headingGO.GetComponent<RectTransform>();
        headingRt.anchorMin = headingRt.anchorMax = headingRt.pivot = new Vector2(0, 1);
        headingRt.sizeDelta = new Vector2(270, 38);
        headingRt.anchoredPosition = new Vector2(15, -9);

        // Кнопка сворачивания ► (лямбда ссылается на поле _logTab, а не на параметр)
        {
            Font uiFont2 = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject btnGO = new GameObject("CollapseBtn");
            btnGO.transform.SetParent(panelGO.transform, false);
            btnGO.transform.SetAsLastSibling();

            Image img = btnGO.AddComponent<Image>();
            VttUiSkin.ButtonStyle(img, VttUiSkin.Button, 6);
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

        // Прокручиваемый журнал с высотой по содержимому.
        Font uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var viewport = new GameObject("LogViewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(panelGO.transform, false);
        var viewportRt = viewport.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(16f, 14f);
        viewportRt.offsetMax = new Vector2(-16f, -56f);
        viewport.GetComponent<Image>().color = new Color(0, 0, 0, 0.01f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;
        GameObject textGO = new GameObject("LogText");
        textGO.transform.SetParent(viewport.transform, false);
        _logText = textGO.AddComponent<Text>();
        _logText.font = uiFont;
        _logText.fontSize = 15;
        _logText.lineSpacing = 1f;
        _logText.color = VttUiSkin.Muted;
        _logText.alignment = TextAnchor.UpperLeft;
        _logText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _logText.verticalOverflow = VerticalWrapMode.Overflow;
        _logText.raycastTarget = false; // чтобы не перехватывал клики кнопки сворачивания

        RectTransform trt = _logText.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0, 1);
        trt.anchorMax = new Vector2(1, 1);
        trt.pivot = new Vector2(0.5f, 1);
        trt.anchoredPosition = Vector2.zero;
        trt.sizeDelta = Vector2.zero;
        var fitter = textGO.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        _logScroll = panelGO.AddComponent<ScrollRect>();
        _logScroll.viewport = viewportRt;
        _logScroll.content = trt;
        _logScroll.horizontal = false;
        _logScroll.vertical = true;
        _logScroll.movementType = ScrollRect.MovementType.Clamped;
        _logScroll.scrollSensitivity = 24f;
    }

    // ═══ Табы для возврата скрытых панелей ═══

    void BuildTabs()
    {
        if (_canvas == null) return;

        // Return handle beside the bottom-right roll journal.
        _logTab = MakeTab(_canvas.transform, "◀",
            new Vector2(1f, 0f), new Vector2(1f, 0.5f), new Vector2(28f, 64f),
            () => { if (_logPanelGO != null) { _logPanelGO.SetActive(true); _logTab.SetActive(false); } });
        _logTab.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 196f);

        _logTab.SetActive(false);
    }

    GameObject MakeTab(Transform parent, string label, Vector2 anchor, Vector2 pivot, Vector2 sizeDelta, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject($"Tab_{label}");
        go.transform.SetParent(parent, false);

        Image img = go.AddComponent<Image>();
        VttUiSkin.ButtonStyle(img, VttUiSkin.Panel, 7);

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

    void MakeButton(Transform parent, string label, float x, float y, float w, float h, Font font, UnityEngine.Events.UnityAction onClick)
    {
        GameObject go = new GameObject($"Btn_{label}");
        go.transform.SetParent(parent, false);

        Image img = go.AddComponent<Image>();
        VttUiSkin.ButtonStyle(img, VttUiSkin.Button, 6);

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
            ShowToolNotice("Нет кубиков.");
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
        var nm = NetworkManager.Singleton;
        bool isHost = nm != null && nm.IsHost;
        ulong localClientId = nm != null ? nm.LocalClientId : 0;
        bool hostHasOwnDice = isHost && NetworkPermissions.HostHasSpawnedDice(localClientId);

        // Удаляем сетевые кубики (права: IsSpawner; хост без своих — все)
        var netDice = FindObjectsByType<NetworkDice>(FindObjectsInactive.Exclude);
        foreach (var nd in netDice)
        {
            if (!NetworkPermissions.ShouldDeleteDiceInClearAll(nd, localClientId, isHost, hostHasOwnDice))
                continue;

            if (!nd.TryGetComponent<NetworkObject>(out var no) || !no.IsSpawned)
                continue;

            if (nm != null && nm.IsServer)
                no.Despawn();
            else
                SendDespawnRequest(no.NetworkObjectId);
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

    void ConfirmDiceClear()
    {
        var nm = NetworkManager.Singleton;
        bool hostClearsAll = nm != null && nm.IsHost &&
            !NetworkPermissions.HostHasSpawnedDice(nm.LocalClientId);
        ConfirmAction(hostClearsAll ? "Убрать все кубики?" : "Убрать мои кубики?",
            hostClearsAll
                ? "На столе нет ваших кубиков. Как DM вы удалите кубики всех игроков. Отменить удаление нельзя."
                : "Ваши кубики будут удалены со стола. Журнал бросков сохранится. Отменить удаление нельзя.",
            ClearAll);
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

        string who = !string.IsNullOrEmpty(ownerNickname)
            ? ownerNickname
            : $"P{throwerId}";
        string logLine = $"{System.DateTime.Now:HH:mm:ss}  [{who}] {DieTypeName(type)}={result}";
        _logEntries.Insert(0, logLine);
        if (_logEntries.Count > 500) _logEntries.RemoveAt(_logEntries.Count - 1);
        UpdateLogText();
        JournalResultRecorded?.Invoke(DieTypeName(type), result, throwerId);
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
        _logText.text = _logEntries.Count == 0
            ? "Бросков пока нет. Результаты появятся здесь."
            : string.Join("\n", _logEntries);
        if (_logScroll != null) _logScroll.verticalNormalizedPosition = 1f;
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
        if (!_trackingRoll || _rollingCount > 0 || _lastResults.Count == 0) return;
        _trackingRoll = false;
        int total = 0;
        foreach (var result in _lastResults) total += result.value;
        string logLine = $"{System.DateTime.Now:HH:mm:ss}  [{_lastResults.Count}] {string.Join(" + ", _lastResults.ConvertAll(r => $"{DieTypeName(r.type)}={r.value}"))} = {total}";
        _logEntries.Insert(0, logLine);
        if (_logEntries.Count > 500) _logEntries.RemoveAt(_logEntries.Count - 1);
        UpdateLogText();
        foreach (var result in _lastResults)
            JournalResultRecorded?.Invoke(DieTypeName(result.type), result.value,
                NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0);
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
