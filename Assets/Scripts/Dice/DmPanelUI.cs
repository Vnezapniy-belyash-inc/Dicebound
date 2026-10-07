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
    private InputField _tokenNameInput;
    private InputField _sceneNameInput;
    private InputField[] _statBlockInputs;
    private RectTransform _statBlockList;
    private string _selectedStatBlockId;
    private string _statBlockSignature;
    private Text _statBlockHint;
    private GameObject _mapCatalogPanel;
    private InputField _mapAssetNameInput;
    private RectTransform _mapAssetsContent;
    private string _mapAssetsSignature;
    private string _selectedMapAssetId;
    private Text _mapCatalogNotice;
    private Toggle _createHiddenToggle;
    private Text _tokenNotice;
    private Text _tokenCount;
    private RectTransform _tokensContent;
    private string _tokenSignature;
    private readonly HashSet<string> _selectedInitiativeTokenIds = new();
    private Text _sceneNotice, _sceneMarkupText, _sceneDiameterText;
    private Text _fogEnabledText, _fogPauseText, _fogPreviewText, _fogSourceText, _fogHistoryText, _fogAutosaveText, _fogStatus;

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

    public void ShowTokenCreation()
    {
        if (!IsLocalHost || _panel == null) return;
        _panel.SetActive(true);
        ShowPage(3);
        _tokenNameInput?.ActivateInputField();
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

        _pages = new GameObject[7];
        _tabs = new Image[7];
        string[] titles = { "Карта", "Игроки", "Бой", "Токены", "Сцена", "Туман", "Статы" };
        for (int i = 0; i < _pages.Length; i++)
        {
            int pageIndex = i;
            var tab = Button(_panel.transform, titles[i], 54, 34, 8 + i * 56, -58,
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
        BuildTokensPage(_pages[3].transform);
        BuildScenePage(_pages[4].transform);
        BuildFogPage(_pages[5].transform);
        BuildStatBlockPage(_pages[6].transform);
        ShowPage(0);
    }

    private void BuildFogPage(Transform parent)
    {
        Text Control(string title, float y, Action action) => Button(parent, title, 350, 34, 0, y,
            () => { action(); Refresh(); }, VttUiSkin.Button).GetComponentInChildren<Text>();
        _fogEnabledText = Control("Туман включён", -4, () => FogManager.Instance?.ToggleEnabled());
        _fogPauseText = Control("Продолжить раскрытие", -46, () => FogManager.Instance?.TogglePause());
        _fogPreviewText = Control("Посмотреть глазами игроков", -88, () => FogManager.Instance?.TogglePreview());
        _fogSourceText = Control("Обзор всей группы", -130, () => FogManager.Instance?.CyclePreviewSource());
        Control("Сбросить исследование…", -170, () => DiceUI.Instance?.ConfirmAction("Сбросить исследование?",
            "Карта станет неисследованной, раскрытие будет приостановлено. Действие можно отменить.", () => FogManager.Instance?.ResetHistory()));
        Button(parent, "Раскрыть кистью", 170, 30, 0, -210, () => FogManager.Instance?.SetManual(1), VttUiSkin.Button);
        Button(parent, "Закрыть кистью", 170, 30, 180, -210, () => FogManager.Instance?.SetManual(2), VttUiSkin.Button);
        Control("Завершить ручную кисть", -246, () => FogManager.Instance?.StopManual());
        Control("Отменить последнее действие", -286, GameMasterUndo.Undo);
        _fogHistoryText = Control("Экспорт с историей", -326, () => { if (FogManager.Instance != null) FogManager.Instance.SaveWithHistory = !FogManager.Instance.SaveWithHistory; });
        _fogAutosaveText = Control("Автосохранение включено", -366, () => { if (FogManager.Instance != null) FogManager.Instance.Autosave = !FogManager.Instance.Autosave; });
        Control("Восстановить автосохранение…", -406, () => SceneFileStore.LoadAutosave());
        _fogStatus = Label(parent, "", 12, VttUiSkin.Muted, TextAnchor.UpperLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(350, 56), new Vector2(0, -449));
    }

    private void BuildStatBlockPage(Transform parent)
    {
        Label(parent, "Статблоки кампании", 16, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(350, 26), new Vector2(0, -2), true);
        Button(parent, "Новый", 64, 26, 286, -2, ClearStatBlockInputs, VttUiSkin.Button);
        var viewport = Box(parent, "StatBlockViewport", new Color(0, 0, 0, 0.01f), 0, false);
        Place(viewport, new Vector2(0, 1), new Vector2(0, 1), new Vector2(350, 66), new Vector2(0, -30));
        viewport.AddComponent<RectMask2D>();
        var rows = new GameObject("StatBlocks", typeof(RectTransform));
        rows.transform.SetParent(viewport.transform, false);
        _statBlockList = rows.GetComponent<RectTransform>();
        _statBlockList.anchorMin = new Vector2(0, 1);
        _statBlockList.anchorMax = new Vector2(1, 1);
        _statBlockList.pivot = new Vector2(0.5f, 1);
        var scroll = viewport.AddComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = _statBlockList;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 24;

        _statBlockInputs = new InputField[11];
        _statBlockInputs[0] = CreateInput(parent, "Название статблока", 0, -104, 350, 30);
        _statBlockInputs[1] = CreateInput(parent, "Тип существа", 0, -138, 170, 30);
        _statBlockInputs[2] = CreateInput(parent, "Размер", 180, -138, 80, 30);
        _statBlockInputs[3] = CreateInput(parent, "Мировоззрение", 268, -138, 82, 30);
        _statBlockInputs[4] = CreateInput(parent, "КД", 0, -172, 64, 30);
        _statBlockInputs[5] = CreateInput(parent, "ХП", 72, -172, 64, 30);
        _statBlockInputs[6] = CreateInput(parent, "Скорость", 144, -172, 206, 30);
        _statBlockInputs[7] = CreateInput(parent, "СИЛ,ЛОВ,ТЕЛ,ИНТ,МДР,ХАР", 0, -206, 260, 30);
        _statBlockInputs[8] = CreateInput(parent, "Опасность", 268, -206, 82, 30);
        _statBlockInputs[9] = CreateInput(parent, "Описание", 0, -240, 350, 86, true);
        _statBlockInputs[10] = CreateInput(parent, "Действия: название: описание", 0, -332, 350, 64, true);
        Button(parent, "Сохранить статблок", 220, 30, 0, -402, SaveStatBlock, VttUiSkin.Button);
        Button(parent, "Удалить выбранный", 122, 30, 228, -402, DeleteSelectedStatBlock,
            new Color(0.28f, 0.11f, 0.14f));
        _statBlockHint = Label(parent, "", 12, VttUiSkin.Muted, TextAnchor.UpperLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(350, 52), new Vector2(0, -438));
    }

    private InputField CreateInput(Transform parent, string placeholderText, float x, float y,
        float width, float height, bool multiline = false)
    {
        var box = Box(parent, "Input " + placeholderText, VttUiSkin.Button, 6, false);
        Place(box, new Vector2(0, 1), new Vector2(0, 1), new Vector2(width, height), new Vector2(x, y));
        var input = box.AddComponent<InputField>();
        input.targetGraphic = box.GetComponent<Image>();
        input.lineType = multiline ? InputField.LineType.MultiLineNewline : InputField.LineType.SingleLine;
        input.characterLimit = multiline ? 4000 : 256;
        input.textComponent = Label(box.transform, "", 12, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(width - 14, height - 8), new Vector2(7, 0));
        input.placeholder = Label(box.transform, placeholderText, 11, VttUiSkin.Muted, TextAnchor.MiddleLeft,
            new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(width - 14, height - 8), new Vector2(7, 0));
        return input;
    }

    private void RefreshStatBlocks(bool force = false)
    {
        if (_statBlockList == null) return;
        var blocks = SceneFileStore.GetStatBlocks();
        if (!string.IsNullOrEmpty(_selectedStatBlockId)
            && !Array.Exists(blocks, item => item.id == _selectedStatBlockId)) ClearStatBlockInputs();
        var signature = new System.Text.StringBuilder();
        foreach (var block in blocks) signature.Append(block.id).Append(':').Append(block.name).Append(';');
        string snapshot = signature.ToString();
        if (!force && _statBlockSignature == snapshot) return;
        _statBlockSignature = snapshot;
        foreach (Transform child in _statBlockList) Destroy(child.gameObject);
        _statBlockList.sizeDelta = new Vector2(0, Mathf.Max(66, blocks.Length * 32));
        for (int i = 0; i < blocks.Length; i++)
        {
            var block = blocks[i];
            Button(_statBlockList, block.name, 340, 28, 0, -i * 32,
                () => SelectStatBlock(block), VttUiSkin.Button);
        }
        if (string.IsNullOrEmpty(_selectedStatBlockId)) ClearStatBlockInputs();
    }

    private void SelectStatBlock(StatBlockDefinition block)
    {
        if (block == null) return;
        _selectedStatBlockId = block.id;
        _statBlockInputs[0].text = block.name;
        _statBlockInputs[1].text = block.creatureType;
        _statBlockInputs[2].text = block.size;
        _statBlockInputs[3].text = block.alignment;
        _statBlockInputs[4].text = block.armorClass.ToString(CultureInfo.InvariantCulture);
        _statBlockInputs[5].text = block.hitPoints.ToString(CultureInfo.InvariantCulture);
        _statBlockInputs[6].text = block.speed;
        _statBlockInputs[7].text = string.Format(CultureInfo.InvariantCulture, "{0},{1},{2},{3},{4},{5}",
            block.strength, block.dexterity, block.constitution, block.intelligence, block.wisdom, block.charisma);
        _statBlockInputs[8].text = block.challengeRating;
        _statBlockInputs[9].text = block.description;
        var actions = new System.Text.StringBuilder();
        foreach (var action in block.actions)
        {
            if (actions.Length > 0) actions.Append('\n');
            actions.Append(action.name).Append(": ").Append(action.description);
        }
        _statBlockInputs[10].text = actions.ToString();
        _statBlockHint.text = "Выбран: " + block.name;
    }

    private void ClearStatBlockInputs()
    {
        if (_statBlockInputs == null) return;
        _selectedStatBlockId = null;
        foreach (var input in _statBlockInputs) input.SetTextWithoutNotify(string.Empty);
        if (_statBlockHint != null) _statBlockHint.text = "Новая запись: заполните поля и сохраните.";
    }

    private void SaveStatBlock()
    {
        try
        {
            string abilityText = string.IsNullOrWhiteSpace(_statBlockInputs[7].text)
                ? "10,10,10,10,10,10" : _statBlockInputs[7].text;
            string[] abilities = abilityText.Split(',');
            if (abilities.Length != 6) throw new FormatException("Укажите шесть характеристик через запятую.");
            var stats = new int[6];
            for (int i = 0; i < stats.Length; i++)
                if (!int.TryParse(abilities[i].Trim(), out stats[i])) throw new FormatException("Проверьте шесть характеристик.");
            var actions = new List<StatBlockAction>();
            foreach (string line in _statBlockInputs[10].text.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                int separator = line.IndexOf(':');
                if (separator <= 0) throw new FormatException("Действия записываются по одному на строку: Название: описание.");
                actions.Add(new StatBlockAction { name = line.Substring(0, separator).Trim(), description = line.Substring(separator + 1).Trim() });
            }
            var block = new StatBlockDefinition
            {
                id = _selectedStatBlockId, name = _statBlockInputs[0].text,
                creatureType = _statBlockInputs[1].text,
                size = string.IsNullOrWhiteSpace(_statBlockInputs[2].text) ? "Средний" : _statBlockInputs[2].text,
                alignment = _statBlockInputs[3].text,
                armorClass = int.Parse(_statBlockInputs[4].text),
                hitPoints = int.Parse(_statBlockInputs[5].text), speed = _statBlockInputs[6].text,
                strength = stats[0], dexterity = stats[1], constitution = stats[2],
                intelligence = stats[3], wisdom = stats[4], charisma = stats[5],
                challengeRating = _statBlockInputs[8].text, description = _statBlockInputs[9].text,
                actions = actions.ToArray()
            };
            SceneFileStore.UpsertStatBlock(block);
            _selectedStatBlockId = block.id;
            _statBlockHint.text = "Сохранено. Не забудьте сохранить кампанию, чтобы записать каталог в файл.";
            RefreshStatBlocks(true);
        }
        catch (Exception ex) { _statBlockHint.text = ex.Message; }
    }

    private void DeleteSelectedStatBlock()
    {
        if (string.IsNullOrEmpty(_selectedStatBlockId)) return;
        string id = _selectedStatBlockId;
        DiceUI.Instance?.ConfirmAction("Удалить статблок?", "Если он назначен токену, сначала снимите назначение.", () =>
        {
            try
            {
                SceneFileStore.DeleteStatBlock(id);
                ClearStatBlockInputs();
                _statBlockHint.text = "Статблок удалён из сессии.";
                RefreshStatBlocks(true);
            }
            catch (Exception ex) { _statBlockHint.text = ex.Message; }
        });
    }

    public void ShowSceneEditor()
    {
        if (!IsLocalHost || _panel == null) return;
        _panel.SetActive(true); ShowPage(4);
    }

    private void BuildScenePage(Transform parent)
    {
        Label(parent, "Разметка и файл сцены", 16, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(350, 30), new Vector2(0, -4), true);
        Button(parent, "Кисть стен", 170, 34, 0, -44,
            () => SceneEditor.Instance?.Activate(SceneEditor.Tool.Wall), VttUiSkin.Button);
        Button(parent, "Стереть ребро", 170, 34, 180, -44,
            () => SceneEditor.Instance?.Activate(SceneEditor.Tool.EraseEdge), VttUiSkin.Button);
        Button(parent, "Поставить дверь", 170, 34, 0, -87,
            () => SceneEditor.Instance?.Activate(SceneEditor.Tool.Door), VttUiSkin.Button);
        Button(parent, "Открыть / закрыть", 170, 34, 180, -87,
            () => SceneEditor.Instance?.Activate(SceneEditor.Tool.ToggleDoor), VttUiSkin.Button);
        Button(parent, "Квадрат", 170, 34, 0, -130,
            () => SceneEditor.Instance?.Activate(SceneEditor.Tool.Square), VttUiSkin.Button);
        Button(parent, "Колонна", 170, 34, 180, -130,
            () => SceneEditor.Instance?.Activate(SceneEditor.Tool.Column), VttUiSkin.Button);
        Button(parent, "−", 36, 30, 0, -173, () => AdjustColumn(-0.1f), VttUiSkin.Button);
        _sceneDiameterText = Label(parent, "Диаметр: 0.5 клетки", 13, VttUiSkin.Text, TextAnchor.MiddleCenter,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(260, 30), new Vector2(44, -173));
        Button(parent, "+", 36, 30, 314, -173, () => AdjustColumn(0.1f), VttUiSkin.Button);
        Button(parent, "Удалить препятствие", 350, 32, 0, -212,
            () => SceneEditor.Instance?.Activate(SceneEditor.Tool.EraseObstacle), VttUiSkin.Button);
        Button(parent, "Отменить действие", 170, 32, 0, -253, () => SceneEditor.Instance?.Undo(), VttUiSkin.Button);
        Button(parent, "Завершить разметку", 170, 32, 180, -253, () => SceneEditor.Instance?.Deactivate(), VttUiSkin.Button);
        var markup = Button(parent, "Скрыть разметку", 350, 32, 0, -294,
            () => { SceneEditor.Instance?.ToggleMarkup(); Refresh(); }, VttUiSkin.Button);
        _sceneMarkupText = markup.GetComponentInChildren<Text>();
        Button(parent, "Сохранить JSON", 170, 36, 0, -335, SceneFileStore.SaveDialog, VttUiSkin.Button);
        Button(parent, "Загрузить JSON", 170, 36, 180, -335, SceneFileStore.LoadDialog, VttUiSkin.Button);
        Button(parent, "Сохранить сессию", 170, 34, 0, -376, SceneFileStore.SaveCampaignDialog, VttUiSkin.Button);
        Button(parent, "Загрузить сессию", 170, 34, 180, -376, SceneFileStore.LoadCampaignDialog, VttUiSkin.Button);
        Button(parent, "←", 40, 32, 0, -416, () => SceneFileStore.CycleScene(-1), VttUiSkin.Button);
        Button(parent, "Копировать", 126, 32, 44, -416, SceneFileStore.CreateSceneCopy, VttUiSkin.Button);
        Button(parent, "Пустая", 126, 32, 176, -416, SceneFileStore.CreateEmptyScene, VttUiSkin.Button);
        Button(parent, "→", 40, 32, 308, -416, () => SceneFileStore.CycleScene(1), VttUiSkin.Button);
        var sceneNameBox = Box(parent, "SceneName", VttUiSkin.Button, 7);
        Place(sceneNameBox, new Vector2(0, 1), new Vector2(0, 1), new Vector2(150, 30), new Vector2(0, -452));
        var sceneNameText = Label(sceneNameBox.transform, "", 13, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(130, 26), new Vector2(10, 0));
        _sceneNameInput = sceneNameBox.AddComponent<InputField>();
        _sceneNameInput.textComponent = sceneNameText;
        _sceneNameInput.lineType = InputField.LineType.SingleLine;
        _sceneNameInput.characterLimit = 64;
        Button(parent, "Имя ✓", 92, 30, 156, -452, RenameActiveScene, VttUiSkin.Button);
        Button(parent, "Удалить", 92, 30, 256, -452, () => DiceUI.Instance?.ConfirmAction(
            "Удалить активную сцену?", "Будет загружена следующая сцена. Последнюю сцену удалить нельзя.",
            SceneFileStore.DeleteActiveScene), VttUiSkin.Button);
        _sceneNotice = Label(parent, "", 12, VttUiSkin.Text, TextAnchor.UpperLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(350, 28), new Vector2(0, -486));
    }

    private void RenameActiveScene()
    {
        if (_sceneNameInput == null || string.IsNullOrWhiteSpace(_sceneNameInput.text)) return;
        SceneFileStore.RenameActiveScene(_sceneNameInput.text);
        _sceneNameInput.text = string.Empty;
    }

    private void AdjustColumn(float delta)
    {
        var editor = SceneEditor.Instance;
        if (editor == null) return;
        editor.ColumnDiameter = Mathf.Clamp(Mathf.Round((editor.ColumnDiameter + delta) * 10) / 10, 0.1f, 0.9f);
        Refresh();
    }

    private void BuildTokensPage(Transform parent)
    {
        Label(parent, "Создать токен", 16, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(340, 30),
            new Vector2(0, -4), true);

        var nameBox = Box(parent, "TokenName", VttUiSkin.Button, 7);
        Place(nameBox, new Vector2(0, 1), new Vector2(0, 1), new Vector2(350, 36), new Vector2(0, -40));
        var nameText = Label(nameBox.transform, "", 14, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(326, 30), new Vector2(12, 0));
        var placeholder = Label(nameBox.transform, "Имя, например: Гоблин", 14, VttUiSkin.Muted,
            TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(326, 30), new Vector2(12, 0));
        _tokenNameInput = nameBox.AddComponent<InputField>();
        _tokenNameInput.textComponent = nameText;
        _tokenNameInput.placeholder = placeholder;
        _tokenNameInput.characterLimit = 64;
        _tokenNameInput.lineType = InputField.LineType.SingleLine;

        var toggleRoot = new GameObject("CreateHidden", typeof(RectTransform), typeof(Toggle));
        toggleRoot.transform.SetParent(parent, false);
        Place(toggleRoot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(350, 30), new Vector2(0, -83));
        var toggleBox = Box(toggleRoot.transform, "Checkbox", VttUiSkin.Button, 4);
        Place(toggleBox, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(24, 24), new Vector2(0, 0));
        var checkmark = Box(toggleBox.transform, "Checked", VttUiSkin.Blue, 2, false);
        Place(checkmark, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(14, 14), Vector2.zero);
        checkmark.GetComponent<Image>().raycastTarget = false;
        _createHiddenToggle = toggleRoot.GetComponent<Toggle>();
        _createHiddenToggle.targetGraphic = toggleBox.GetComponent<Image>();
        _createHiddenToggle.graphic = checkmark.GetComponent<Image>();
        _createHiddenToggle.isOn = false;
        var toggleLabel = Label(toggleRoot.transform, "Создать скрытым от игроков", 13, VttUiSkin.Text,
            TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f),
            new Vector2(310, 28), new Vector2(34, 0));
        toggleLabel.raycastTarget = true;
        Button(parent, "Создать токен", 350, 34, 0, -121, CreateNamedToken, new Color(0.10f, 0.30f, 0.48f));
        _tokenNotice = Label(parent, "Повторяющиеся имена получат номер: Гоблин 2, Гоблин 3…", 12,
            VttUiSkin.Muted, TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(350, 34), new Vector2(0, -161));
        _tokenCount = Label(parent, "Все токены", 14, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(350, 24), new Vector2(0, -202), true);

        var viewport = Box(parent, "TokenViewport", new Color(0, 0, 0, 0.01f), 0, false);
        Place(viewport, new Vector2(0, 1), new Vector2(0, 1), new Vector2(350, 208), new Vector2(0, -231));
        viewport.AddComponent<RectMask2D>();
        var rows = new GameObject("Tokens", typeof(RectTransform));
        rows.transform.SetParent(viewport.transform, false);
        _tokensContent = rows.GetComponent<RectTransform>();
        _tokensContent.anchorMin = new Vector2(0, 1);
        _tokensContent.anchorMax = new Vector2(1, 1);
        _tokensContent.pivot = new Vector2(0.5f, 1);
        _tokensContent.anchoredPosition = Vector2.zero;
        var scroll = viewport.AddComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = _tokensContent;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28;
        Label(parent, "Массовое удаление", 12, VttUiSkin.Muted, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(350, 22), new Vector2(0, -458));
        Button(parent, "Добавить выбранные в бой", 350, 30, 0, -425, AddSelectedTokensToInitiative,
            new Color(0.10f, 0.30f, 0.48f));
        Button(parent, "Все…", 110, 30, 0, -474,
            () => ConfirmDeleteTokens(0), new Color(0.28f, 0.11f, 0.14f));
        Button(parent, "Мои…", 110, 30, 120, -474,
            () => ConfirmDeleteTokens(1), new Color(0.28f, 0.11f, 0.14f));
        Button(parent, "Игроков…", 110, 30, 240, -474,
            () => ConfirmDeleteTokens(2), new Color(0.28f, 0.11f, 0.14f));
    }

    private void CreateNamedToken()
    {
        if (!IsLocalHost) return;
        var token = TokenManager.Instance?.CreateTokenAsHost(_tokenNameInput.text, _createHiddenToggle.isOn);
        _tokenNotice.text = token == null ? "Не удалось создать токен." :
            $"Создан: {token.TokenName}" + (token.IsHidden ? " · скрыт" : "");
        RefreshTokens(true);
    }

    private void RefreshTokens(bool force = false)
    {
        if (!IsLocalHost || _tokensContent == null) return;
        var tokens = new List<TokenController>();
        foreach (var token in FindObjectsByType<TokenController>(FindObjectsInactive.Exclude))
            if (token.IsSpawned) tokens.Add(token);
        tokens.Sort((a, b) => a.NetworkObjectId.CompareTo(b.NetworkObjectId));
        var liveIds = new HashSet<string>();
        foreach (var token in tokens) liveIds.Add(token.SceneId);
        _selectedInitiativeTokenIds.RemoveWhere(id => !liveIds.Contains(id));
        var signature = new System.Text.StringBuilder();
        foreach (var token in tokens)
            signature.Append(token.NetworkObjectId).Append(':').Append(token.TokenName).Append(':')
                .Append(token.IsHidden).Append(':').Append(token.IsHero).Append(':').Append(token.VisionFeet).Append(':')
                .Append(token.ControllerClientId).Append(':').Append(token.EveryoneCanMove).Append(';');
        string snapshot = signature.ToString();
        _tokenCount.text = $"Все токены · {tokens.Count}";
        if (!force && _tokenSignature == snapshot) return;
        _tokenSignature = snapshot;
        foreach (Transform child in _tokensContent)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        _tokensContent.sizeDelta = new Vector2(0, Mathf.Max(208, tokens.Count * 96));
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var row = Box(_tokensContent, "Token " + token.NetworkObjectId, VttUiSkin.Raised, 7, false);
            Place(row, new Vector2(0, 1), new Vector2(0, 1), new Vector2(340, 90), new Vector2(0, -i * 96));
            Button(row.transform, _selectedInitiativeTokenIds.Contains(token.SceneId) ? "✓" : "○", 24, 24, 7, -3,
                () => {
                    if (_selectedInitiativeTokenIds.Contains(token.SceneId)) _selectedInitiativeTokenIds.Remove(token.SceneId);
                    else _selectedInitiativeTokenIds.Add(token.SceneId);
                    RefreshTokens(true);
                }, VttUiSkin.Button);
            Label(row.transform, token.TokenName, 13, VttUiSkin.Text, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(172, 24), new Vector2(36, -3), true);
            string status = (token.IsHidden ? "Скрыт" : "Виден") + (token.IsHero ? " · герой" : "") +
                $" · {token.VisionFeet} фт";
            Label(row.transform, status, 11, VttUiSkin.Muted, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(172, 22), new Vector2(36, -28));
            Button(row.transform, token.IsHidden ? "Показать" : "Скрыть", 64, 30, 214, -12,
                () => {
                    if (!IsLocalHost || token == null || !token.IsSpawned) return;
                    token.RequestSetHidden(!token.IsHidden);
                    RefreshTokens(true);
                }, VttUiSkin.Button);
            Button(row.transform, "Удалить", 54, 30, 282, -12,
                () => {
                    if (!IsLocalHost || token == null || !token.IsSpawned) return;
                    token.RequestDespawn();
                    RefreshTokens(true);
                }, new Color(0.28f, 0.11f, 0.14f));
            string owner = token.ControllerClientId == ulong.MaxValue ? string.IsNullOrEmpty(token.SavedOwnerNickname) ? "Не назначен" : "Ждёт " + token.SavedOwnerNickname : token.ControllerClientId == NetworkManager.ServerClientId
                ? "Мастер" : PlayerColors.GetNickname(token.ControllerClientId) ?? token.SavedOwnerNickname ?? "Отключён";
            Button(row.transform, "Кому: " + owner, 142, 28, 8, -56, () => {
                if (!IsLocalHost || token == null || !token.IsSpawned) return;
                var participants = new List<ulong>(NetworkManager.Singleton.ConnectedClientsIds); participants.Sort(); participants.Add(ulong.MaxValue);
                int index = participants.IndexOf(token.ControllerClientId); token.AssignController(participants[(index + 1) % participants.Count]); RefreshTokens(true);
            }, VttUiSkin.Button);
            Button(row.transform, token.EveryoneCanMove ? "Двигать: всем" : "Только хозяину", 126, 28, 154, -56, () => {
                if (!IsLocalHost || token == null || !token.IsSpawned) return;
                token.SetEveryoneCanMove(!token.EveryoneCanMove); RefreshTokens(true);
            }, VttUiSkin.Button);
            Button(row.transform, token.IsHero ? "Герой" : "NPC", 48, 28, 284, -56, () => {
                if (!IsLocalHost || token == null || !token.IsSpawned) return;
                token.SetHero(!token.IsHero); RefreshTokens(true);
            }, VttUiSkin.Button);
        }
    }

    private void ConfirmDeleteTokens(int scope)
    {
        if (!IsLocalHost) return;
        string[] subjects = { "все токены", "свои токены", "токены игроков" };
        DiceUI.Instance?.ConfirmAction($"Удалить {subjects[scope]}?",
            "Токены исчезнут у всех участников. Мастер сможет отменить последнее удаление.",
            () => DeleteTokens(scope));
    }

    private static void DeleteTokens(int scope)
    {
        if (!IsLocalHost) return;
        ulong hostId = NetworkManager.Singleton.LocalClientId;
        var selected = new List<TokenController>();
        foreach (var token in FindObjectsByType<TokenController>(FindObjectsInactive.Exclude))
        {
            if (token == null || !token.IsSpawned || !token.IsServer) continue;
            bool hostToken = token.ControllerClientId == hostId
                || token.ControllerClientId == ulong.MaxValue && string.IsNullOrEmpty(token.SavedOwnerNickname);
            if (scope == 1 && !hostToken || scope == 2 && hostToken) continue;
            selected.Add(token);
        }
        var grid = FindAnyObjectByType<GridManager>();
        if (grid != null)
        {
            var saved = new List<SceneToken>(); foreach (var token in selected) saved.Add(SceneFileStore.CaptureToken(token, grid));
            GameMasterUndo.Record("массовое удаление токенов", () => {
                foreach (var data in saved) { var restored = TokenManager.Instance?.RestoreSceneToken(data, grid);
                    if (restored != null && !string.IsNullOrEmpty(data.portrait)) restored.LoadImage(Convert.FromBase64String(data.portrait)); }
            });
        }
        foreach (var token in selected) token.NetworkObject.Despawn();
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
        Button(parent, "Каталог карт сессии…", 350, 34, 0, -494, () =>
        {
            _mapCatalogPanel.SetActive(true);
            RefreshMapCatalog(true);
        }, VttUiSkin.Button);
        BuildMapCatalogOverlay(parent);
    }

    private void AddSelectedTokensToInitiative()
    {
        if (!IsLocalHost) return;
        var selected = new List<TokenController>();
        foreach (var token in FindObjectsByType<TokenController>(FindObjectsInactive.Exclude))
            if (token != null && token.IsSpawned && _selectedInitiativeTokenIds.Contains(token.SceneId)) selected.Add(token);
        selected.Sort((a, b) => a.NetworkObjectId.CompareTo(b.NetworkObjectId));
        int added = InitiativeTracker.Instance?.AddTokensAndArmInitiative(selected) ?? 0;
        _selectedInitiativeTokenIds.Clear();
        _tokenNotice.text = added == 0 ? "Нет выбранных токенов, которых ещё нет в инициативе." : $"Добавлено в инициативу: {added}. Бросайте d20 по очереди.";
        RefreshTokens(true);
    }

    private void BuildMapCatalogOverlay(Transform parent)
    {
        _mapCatalogPanel = Box(parent, "MapCatalog", VttUiSkin.Panel, 10);
        Place(_mapCatalogPanel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(358, 490), new Vector2(0, -6));
        Label(_mapCatalogPanel.transform, "КАТАЛОГ КАРТ", 15, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(280, 28), new Vector2(12, -4), true);
        Button(_mapCatalogPanel.transform, "×", 30, 28, 320, -4,
            () => _mapCatalogPanel.SetActive(false), VttUiSkin.Button);
        var nameBox = Box(_mapCatalogPanel.transform, "MapAssetName", VttUiSkin.Button, 6, false);
        Place(nameBox, new Vector2(0, 1), new Vector2(0, 1), new Vector2(334, 32), new Vector2(12, -38));
        _mapAssetNameInput = nameBox.AddComponent<InputField>();
        _mapAssetNameInput.textComponent = Label(nameBox.transform, "", 13, VttUiSkin.Text,
            TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(316, 28), new Vector2(8, 0));
        _mapAssetNameInput.placeholder = Label(nameBox.transform, "Название новой карты", 12, VttUiSkin.Muted,
            TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(316, 28), new Vector2(8, 0));
        _mapAssetNameInput.characterLimit = 128;
        _mapAssetNameInput.lineType = InputField.LineType.SingleLine;
        _mapAssetNameInput.targetGraphic = nameBox.GetComponent<Image>();
        Button(_mapCatalogPanel.transform, "Импортировать файл", 162, 32, 12, -76,
            ImportMapAsset, new Color(0.10f, 0.30f, 0.48f));
        Button(_mapCatalogPanel.transform, "Добавить текущую", 162, 32, 184, -76,
            AddCurrentMapAsset, VttUiSkin.Button);
        var viewport = Box(_mapCatalogPanel.transform, "MapAssetsViewport", new Color(0, 0, 0, 0.01f), 0, false);
        Place(viewport, new Vector2(0, 1), new Vector2(0, 1), new Vector2(334, 264), new Vector2(12, -116));
        viewport.AddComponent<RectMask2D>();
        var rows = new GameObject("MapAssets", typeof(RectTransform));
        rows.transform.SetParent(viewport.transform, false);
        _mapAssetsContent = rows.GetComponent<RectTransform>();
        _mapAssetsContent.anchorMin = new Vector2(0, 1);
        _mapAssetsContent.anchorMax = new Vector2(1, 1);
        _mapAssetsContent.pivot = new Vector2(0.5f, 1);
        var scroll = viewport.AddComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = _mapAssetsContent;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.scrollSensitivity = 28;
        Button(_mapCatalogPanel.transform, "Применить к активной сцене", 220, 32, 12, -388,
            ApplySelectedMapAsset, VttUiSkin.Button);
        Button(_mapCatalogPanel.transform, "Удалить", 104, 32, 242, -388,
            DeleteSelectedMapAsset, new Color(0.28f, 0.11f, 0.14f));
        _mapCatalogNotice = Label(_mapCatalogPanel.transform, "", 12, VttUiSkin.Muted,
            TextAnchor.UpperLeft, new Vector2(0, 1), new Vector2(0, 1), new Vector2(334, 45), new Vector2(12, -428));
        _mapCatalogPanel.SetActive(false);
    }

    public void RefreshMapCatalog(bool force = false)
    {
        if (_mapAssetsContent == null) return;
        var maps = SceneFileStore.GetMapAssets();
        if (!string.IsNullOrEmpty(_selectedMapAssetId)
            && !Array.Exists(maps, item => item.id == _selectedMapAssetId)) _selectedMapAssetId = null;
        var signature = new System.Text.StringBuilder();
        foreach (var map in maps) signature.Append(map.id).Append(':').Append(map.name).Append(';');
        string snapshot = signature.ToString();
        if (!force && snapshot == _mapAssetsSignature) return;
        _mapAssetsSignature = snapshot;
        foreach (Transform child in _mapAssetsContent) Destroy(child.gameObject);
        _mapAssetsContent.sizeDelta = new Vector2(0, Mathf.Max(36, maps.Length * 38));
        for (int i = 0; i < maps.Length; i++)
        {
            var map = maps[i];
            Button(_mapAssetsContent, map.name, 322, 34, 0, -i * 38,
                () =>
                {
                    _selectedMapAssetId = map.id;
                    _mapCatalogNotice.text = "Выбрана: " + map.name;
                }, map.id == _selectedMapAssetId ? new Color(0.11f, 0.29f, 0.48f) : VttUiSkin.Button);
        }
        if (maps.Length == 0 && _mapCatalogNotice != null)
            _mapCatalogNotice.text = "Каталог пуст. Импортируйте файл или добавьте текущую карту.";
    }

    private void ImportMapAsset()
    {
        if (string.IsNullOrWhiteSpace(_mapAssetNameInput.text))
        {
            _mapCatalogNotice.text = "Сначала введите название карты.";
            return;
        }
        MapController.Instance?.LoadImageToLibrary(_mapAssetNameInput.text);
    }

    private void AddCurrentMapAsset()
    {
        try
        {
            SceneFileStore.AddCurrentMapAsset(_mapAssetNameInput.text);
            _mapCatalogNotice.text = "Текущая карта добавлена. Сохраните сессию, чтобы записать каталог.";
            _mapAssetNameInput.text = string.Empty;
            _selectedMapAssetId = null;
            RefreshMapCatalog(true);
        }
        catch (Exception ex) { _mapCatalogNotice.text = ex.Message; }
    }

    private void ApplySelectedMapAsset()
    {
        if (string.IsNullOrEmpty(_selectedMapAssetId))
        {
            _mapCatalogNotice.text = "Выберите карту в списке.";
            return;
        }
        try
        {
            SceneFileStore.SelectMapAsset(_selectedMapAssetId);
            _mapCatalogNotice.text = "Карта применена к сцене. Сохраните сессию, чтобы закрепить изменение.";
        }
        catch (Exception ex) { _mapCatalogNotice.text = ex.Message; }
    }

    private void DeleteSelectedMapAsset()
    {
        if (string.IsNullOrEmpty(_selectedMapAssetId)) return;
        string id = _selectedMapAssetId;
        DiceUI.Instance?.ConfirmAction("Удалить карту из каталога?", "Карту, используемую сценой, удалить нельзя.", () =>
        {
            try
            {
                SceneFileStore.DeleteMapAsset(id);
                _selectedMapAssetId = null;
                _mapCatalogNotice.text = "Карта удалена из сессии.";
                RefreshMapCatalog(true);
            }
            catch (Exception ex) { _mapCatalogNotice.text = ex.Message; }
        });
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
        Button(parent, "Открыть / закрыть трекер", 350, 42, 0, -113,
            () => InitiativeTracker.Instance?.ToggleVisible(), VttUiSkin.Button);
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
        if (_page == 3) RefreshTokens();
        if (_page == 4 && SceneEditor.Instance != null)
        {
            var editor = SceneEditor.Instance;
            _sceneNotice.text = SceneFileStore.ActiveSceneLabel + "\n" + editor.Notice;
            _sceneMarkupText.text = editor.ShowMarkup ? "Скрыть разметку" : "Показать разметку";
            _sceneDiameterText.text = $"Диаметр: {editor.ColumnDiameter:0.0} клетки";
        }
        if (_page == 5 && FogManager.Instance != null)
        {
            var fog = FogManager.Instance;
            _fogEnabledText.text = fog.Enabled ? "Выключить туман" : "Включить туман";
            _fogPauseText.text = fog.Paused ? "Продолжить раскрытие" : "Приостановить раскрытие";
            _fogPreviewText.text = fog.Preview ? "Вернуться к обзору мастера" : "Посмотреть глазами игроков";
            _fogSourceText.text = fog.PreviewSourceName;
            _fogHistoryText.text = fog.SaveWithHistory ? "Экспорт: с историей исследования" : "Экспорт: неисследованная карта";
            _fogAutosaveText.text = fog.Autosave ? "Автосохранение: включено (2 минуты)" : "Автосохранение: выключено";
            _fogStatus.text = fog.Status + "\nОтмена: " + GameMasterUndo.NextLabel + "\nИгрок открывает ближайшую видимую дверь клавишей E.";
        }
        if (_page == 6) RefreshStatBlocks();
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
        label.supportRichText = false;
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
