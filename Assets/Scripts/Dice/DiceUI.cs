using System.Collections.Generic;
using System.IO;
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

    [Header("Спавн")]
    public float spawnSpread = 0.8f;

    private Text _resultText;
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

    void Start()
    {
        _gridManager = FindAnyObjectByType<GridManager>();
        BuildUI();
        if (DiceManager.Instance != null)
            DiceManager.Instance.OnAnyResult += OnDieResult;
    }

    void Update()
    {
        Keyboard k = Keyboard.current;
        if (k == null) return;

        if (k.eKey.wasPressedThisFrame)
        {
            if (_debugPanelGO != null)
                _debugPanelGO.SetActive(!_debugPanelGO.activeSelf);
        }

        if (k.qKey.wasPressedThisFrame && _sidebarGO != null)
        {
            _sidebarGO.SetActive(!_sidebarGO.activeSelf);
        }

        if (k.xKey.wasPressedThisFrame && _bottomPanelGO != null)
        {
            _bottomPanelGO.SetActive(!_bottomPanelGO.activeSelf);
        }

        if (k.lKey.wasPressedThisFrame && _logPanelGO != null)
        {
            _logPanelGO.SetActive(!_logPanelGO.activeSelf);
        }
    }

    void OnDestroy()
    {
        if (DiceManager.Instance != null)
            DiceManager.Instance.OnAnyResult -= OnDieResult;
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

        // Панель
        GameObject panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(canvasGO.transform, false);
        _debugPanelGO = panelGO;
        Image bg = panelGO.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.08f, 0.1f, 0.85f);

        RectTransform prt = panelGO.GetComponent<RectTransform>();
        prt.anchorMin = new Vector2(0.5f, 1f);
        prt.anchorMax = new Vector2(0.5f, 1f);
        prt.pivot = new Vector2(0.5f, 1f);

        // Считаем размеры
        int dieCols = 7; // d4..d100
        float topMargin = 5f;
        float totalW = padding * 2 + dieCols * buttonWidth + (dieCols - 1) * gap;
        float rowH = buttonHeight + gap;
        float toggleH = 24f; // высота ряда с чекбоксом
        float resultH = 55f;
        float totalH = topMargin + padding * 3 + rowH * 2 + toggleH + resultH;

        prt.sizeDelta = new Vector2(totalW, totalH);
        prt.anchoredPosition = new Vector2(0f, -topMargin);

        // Кнопки дайсов (верхний ряд)
        var dieTypes = new[] { DieType.d4, DieType.d6, DieType.d8, DieType.d10, DieType.d12, DieType.d20, DieType.d100 };
        string[] labels = { "d4", "d6", "d8", "d10", "d12", "d20", "d100" };
        for (int i = 0; i < dieTypes.Length; i++)
        {
            float x = padding + i * (buttonWidth + gap);
            float y = -topMargin;
            var dt = dieTypes[i];
            MakeButton(panelGO.transform, labels[i], x, y, buttonWidth, buttonHeight, uiFont, () => SpawnDie(dt));
        }

        // Roll + Clear + Tex + ↺ ↻ (нижний ряд)
        float row2y = -padding - rowH;
        float bx = padding;
        MakeButton(panelGO.transform, "Roll", bx, row2y, buttonWidth, buttonHeight, uiFont, RollAll);
        bx += buttonWidth + gap;
        MakeButton(panelGO.transform, "Clear", bx, row2y, buttonWidth, buttonHeight, uiFont, ClearAll);
        bx += buttonWidth + gap;
        MakeButton(panelGO.transform, "Tex", bx, row2y, buttonWidth, buttonHeight, uiFont, OpenTexturePicker);
        bx += buttonWidth + gap;
        MakeButton(panelGO.transform, "↺", bx, row2y, buttonWidth * 0.6f, buttonHeight, uiFont, () => RotateBoard(false));
        bx += buttonWidth * 0.6f + gap;
        MakeButton(panelGO.transform, "↻", bx, row2y, buttonWidth * 0.6f, buttonHeight, uiFont, () => RotateBoard(true));

        // Чекбокс авторазмера (3-й ряд)
        float row3y = -padding * 2 - rowH * 2;
        MakeToggle(panelGO.transform, "Fit to tex", padding, row3y, uiFont,
            _gridManager != null && _gridManager.autoResizeToTexture,
            v => { if (_gridManager != null) _gridManager.autoResizeToTexture = v; }
        );

        // Текст результатов
        GameObject textGO = new GameObject("ResultText");
        textGO.transform.SetParent(panelGO.transform, false);
        _resultText = textGO.AddComponent<Text>();
        _resultText.font = uiFont;
        _resultText.fontSize = 16;
        _resultText.color = new Color(0.9f, 0.9f, 0.95f);
        _resultText.alignment = TextAnchor.UpperLeft;
        _resultText.horizontalOverflow = HorizontalWrapMode.Wrap;

        RectTransform trt = _resultText.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0, 1);
        trt.anchorMax = new Vector2(1, 1);
        trt.pivot = new Vector2(0, 1);
        trt.anchoredPosition = new Vector2(padding, row3y - toggleH - padding);
        trt.sizeDelta = new Vector2(-padding * 2, resultH);

        UpdateResultText();
        BuildLeftSidebar();
        BuildBottomPanel();
        BuildLogPanel();

        // Дебаг-панель скрыта по умолчанию
        if (_debugPanelGO != null) _debugPanelGO.SetActive(false);

        Debug.Log("DiceUI: panel built successfully");
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
            MakeIcon(sidebarGO.transform, "?", new Color(0.2f, 0.25f, 0.35f, 0.9f),
                iconX, y, iconSize, uiFont, () => { /* TODO: функционал */ }, anchorY: 1f);
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

        RectTransform trt = _logText.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(8f, 8f);  // отступы внутри панели
        trt.offsetMax = new Vector2(-8f, -8f);
    }

    void MakeIcon(Transform parent, string label, Color color, float x, float y, float size, Font font, UnityEngine.Events.UnityAction onClick, float anchorY = 0.5f)
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
        txt.fontSize = 13;
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
        if (DiceManager.Instance == null) return;
        float x = Random.Range(-spawnSpread, spawnSpread);
        float z = Random.Range(-spawnSpread, spawnSpread);
        DiceManager.Instance.SpawnDieAt(type, x, z);
    }

    void RollAll()
    {
        if (DiceManager.Instance == null) return;
        int active = DiceManager.Instance.ActiveDice.Count;
        if (active == 0)
        {
            _resultText.text = "Нет кубиков";
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
        if (DiceManager.Instance == null) return;
        DiceManager.Instance.ClearAll();
        _lastResults.Clear();
        _trackingRoll = false;
        _rollingCount = 0;
        _logEntries.Clear();
        UpdateResultText();
        UpdateLogText();
    }

    /// <summary>Начинает отслеживание ручного броска (drag-and-drop).</summary>
    public void StartManualRoll(int diceCount)
    {
        _lastResults.Clear();
        _trackingRoll = true;
        _rollingCount = diceCount;
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
#if UNITY_EDITOR
        string path = EditorUtility.OpenFilePanel("Выберите изображение", "", "png,jpg,jpeg,bmp,tga");
        if (string.IsNullOrEmpty(path)) return;
        ApplyTexture(path);
#else
        Debug.LogWarning("Texture picker работает только в Editor. В билде используйте свою реализацию.");
#endif
    }

    void ApplyTexture(string filePath)
    {
        if (_gridManager == null)
        {
            _gridManager = FindAnyObjectByType<GridManager>();
            if (_gridManager == null)
            {
                Debug.LogWarning("DiceUI: GridManager not found in scene");
                return;
            }
        }

        byte[] data = File.ReadAllBytes(filePath);
        Texture2D tex = new Texture2D(2, 2);
        if (!tex.LoadImage(data))
        {
            Debug.LogError($"DiceUI: failed to load image: {filePath}");
            Destroy(tex);
            return;
        }

        _gridManager.SetBoardTexture(tex);
    }

    void RotateBoard(bool clockwise)
    {
        if (_gridManager == null)
            _gridManager = FindAnyObjectByType<GridManager>();
        _gridManager?.RotateBoardTexture(clockwise);
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
            _logEntries.Add(logLine);
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
