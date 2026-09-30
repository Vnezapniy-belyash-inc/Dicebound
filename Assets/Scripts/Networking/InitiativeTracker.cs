using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared initiative order. Only the host changes entries and turns.</summary>
[RequireComponent(typeof(NetworkObject))]
public class InitiativeTracker : NetworkBehaviour
{
    private readonly NetworkVariable<FixedString4096Bytes> _netData = new(
        new FixedString4096Bytes(""), NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private struct Entry
    {
        public int id;
        public string name;
        public int initiative;
        public string colorHex;
        public ulong playerId;
    }

    private readonly List<Entry> _entries = new();
    private int _currentIndex;
    private int _nextEntryId;
    private bool _userVisible;
    private bool _hostControlsVisible;
    private Canvas _canvas;
    private Font _font;
    private GameObject _panel, _addPanel, _hostButtons, _emptyMessage;
    private GameObject _viewport;
    private RectTransform _content;
    private InputField _nameInput, _initInput;
    private Text _roundText;
    private Text _addErrorText;
    private const int CardsPerRow = 5;
    private const int CardWidth = 164;
    private const int CardHeight = 48;
    private const int CardStepX = 170;
    private const int CardStepY = 54;

    public static InitiativeTracker Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildUI();
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
    }

    private void OnServerStarted()
    {
        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && !netObj.IsSpawned) netObj.Spawn();
    }

    public override void OnNetworkSpawn()
    {
        _netData.OnValueChanged += OnDataChanged;
        ParseData(_netData.Value.ToString());
        RebuildRows();
        UpdateHostControls();
    }

    public override void OnNetworkDespawn()
    {
        _netData.OnValueChanged -= OnDataChanged;
    }

    private void OnDataChanged(FixedString4096Bytes oldValue, FixedString4096Bytes newValue)
    {
        ParseData(newValue.ToString());
        RebuildRows();
    }

    private void Update()
    {
        bool connected = GameNetworkManager.Instance != null && GameNetworkManager.Instance.IsConnected;
        bool shouldShow = connected && (_entries.Count > 0 || _userVisible);
        if (_panel != null && _panel.activeSelf != shouldShow)
            _panel.SetActive(shouldShow);
        if (!connected && _addPanel != null) _addPanel.SetActive(false);

        if (connected) UpdateHostControls();
    }

    public void SetVisible(bool visible)
    {
        _userVisible = visible;
        if (_panel != null) _panel.SetActive(visible || _entries.Count > 0);
        if (!visible && _addPanel != null) _addPanel.SetActive(false);
    }

    public void ToggleVisible() => SetVisible(!_userVisible);

    private void UpdateHostControls()
    {
        bool host = IsHost;
        if (_hostButtons != null) _hostButtons.SetActive(host);
        if (!host && _addPanel != null) _addPanel.SetActive(false);
        if (host == _hostControlsVisible) return;
        _hostControlsVisible = host;
        RebuildRows();
    }

    private void ParseData(string data)
    {
        _entries.Clear();
        _currentIndex = 0;
        _nextEntryId = 0;
        if (string.IsNullOrEmpty(data)) return;
        var parts = data.Split('|');
        if (int.TryParse(parts[0], out int current)) _currentIndex = current;
        for (int i = 1; i < parts.Length; i++)
        {
            var fields = parts[i].Split(',');
            if (fields.Length < 3) continue;
            int id = fields.Length >= 5 && int.TryParse(fields[4], out int parsedId)
                ? parsedId : ++_nextEntryId;
            _nextEntryId = Mathf.Max(_nextEntryId, id);
            _entries.Add(new Entry {
                id = id,
                name = fields[0],
                initiative = int.TryParse(fields[1], out int score) ? score : 0,
                colorHex = fields[2],
                playerId = fields.Length >= 4 && ulong.TryParse(fields[3], out ulong playerId)
                    ? playerId : ulong.MaxValue
            });
        }
        if (_currentIndex >= _entries.Count) _currentIndex = 0;
    }

    private string SerializeData()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(_currentIndex);
        foreach (var entry in _entries)
            sb.Append('|').Append(entry.name).Append(',').Append(entry.initiative)
                .Append(',').Append(entry.colorHex).Append(',').Append(entry.playerId)
                .Append(',').Append(entry.id);
        return sb.ToString();
    }

    private void Sync()
    {
        if (!IsHost) return;
        string data = SerializeData();
        if (System.Text.Encoding.UTF8.GetByteCount(data)
            > FixedString4096Bytes.UTF8MaxLengthInBytes)
        {
            Debug.LogError("[Initiative] State exceeds network string capacity");
            DiceUI.Instance?.ShowToolNotice("Список инициативы слишком большой.");
            return;
        }
        _netData.Value = new FixedString4096Bytes(data);
        RebuildRows();
    }

    private int ActiveId => _entries.Count > 0 && _currentIndex < _entries.Count
        ? _entries[_currentIndex].id : -1;

    private void SortKeepingTurn(int activeId)
    {
        _entries.Sort((a, b) => {
            int byScore = b.initiative.CompareTo(a.initiative);
            return byScore != 0 ? byScore : a.id.CompareTo(b.id);
        });
        _currentIndex = Mathf.Max(0, _entries.FindIndex(e => e.id == activeId));
    }

    public void AddEntry(string name, int initiative, string colorHex = "#FFFFFF")
    {
        AddEntryInternal(name, initiative, colorHex, ulong.MaxValue);
    }

    private void AddEntryInternal(string name, int initiative, string colorHex, ulong playerId)
    {
        if (!IsHost || _entries.Count >= 30 || string.IsNullOrWhiteSpace(name)) return;
        initiative = Mathf.Clamp(initiative, -999, 999);
        string cleanName = name.Trim().Replace('|', ' ').Replace(',', ' ');
        if (cleanName.Length > 28) cleanName = cleanName.Substring(0, 28);
        string nextEntry = $"|{cleanName},{initiative},{colorHex},{playerId},{_nextEntryId + 1}";
        // Leave room for every score to grow from one digit to -999 later.
        if (System.Text.Encoding.UTF8.GetByteCount(SerializeData() + nextEntry)
            + 3 * (_entries.Count + 1) + 2
            > FixedString4096Bytes.UTF8MaxLengthInBytes)
        {
            DiceUI.Instance?.ShowToolNotice("Список инициативы слишком большой.");
            return;
        }
        int activeId = ActiveId;
        _entries.Add(new Entry { id = ++_nextEntryId, name = cleanName,
            initiative = initiative, colorHex = colorHex, playerId = playerId });
        SortKeepingTurn(activeId);
        Sync();
    }

    public bool ContainsPlayer(ulong playerId) =>
        _entries.Exists(e => e.playerId == playerId);

    public void AddPlayer(ulong playerId)
    {
        if (!IsHost || ContainsPlayer(playerId)) return;
        string name = PlayerColors.GetNickname(playerId) ?? $"Игрок {playerId}";
        string color = "#" + ColorUtility.ToHtmlStringRGB(PlayerColors.GetColor(playerId));
        AddEntryInternal(name, 0, color, playerId);
    }

    public void RemovePlayer(ulong playerId)
    {
        if (!IsHost) return;
        int index = _entries.FindIndex(e => e.playerId == playerId);
        RemoveEntry(index);
    }

    public void RemoveEntry(int index)
    {
        if (!IsHost || index < 0 || index >= _entries.Count) return;
        int activeId = ActiveId;
        _entries.RemoveAt(index);
        _currentIndex = Mathf.Max(0, _entries.FindIndex(e => e.id == activeId));
        Sync();
    }

    private void RemoveEntryById(int id) => RemoveEntry(_entries.FindIndex(e => e.id == id));

    private void SetInitiativeById(int id, string value)
    {
        if (!IsHost || !int.TryParse(value, out int score)) { RebuildRows(); return; }
        score = Mathf.Clamp(score, -999, 999);
        int index = _entries.FindIndex(e => e.id == id);
        if (index < 0) return;
        if (_entries[index].initiative == score) return;
        int activeId = ActiveId;
        var entry = _entries[index];
        entry.initiative = score;
        _entries[index] = entry;
        SortKeepingTurn(activeId);
        Sync();
    }

    public void NextTurn()
    {
        if (!IsHost || _entries.Count == 0) return;
        _currentIndex = (_currentIndex + 1) % _entries.Count;
        Sync();
    }

    public void ClearAll()
    {
        if (!IsHost) return;
        _entries.Clear();
        _currentIndex = 0;
        Sync();
    }

    public void RequestClearAll()
    {
        if (!IsHost || _entries.Count == 0) return;
        DiceUI.Instance?.ConfirmAction("Очистить инициативу?",
            "Все участники будут удалены из трекера. Отменить удаление нельзя.", ClearAll);
    }

    private void BuildUI()
    {
        var root = new GameObject("InitiativeCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        _canvas = root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 20;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _panel = Box(root.transform, "InitiativePanel", VttUiSkin.Panel, 10);
        Place(_panel, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
            new Vector2(880, 102), new Vector2(0, -72));
        _panel.SetActive(false);
        Label(_panel.transform, "ИНИЦИАТИВА", 17, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(154, 32), new Vector2(18, -10), true);
        _roundText = Label(_panel.transform, "ОЧЕРЁДНОСТЬ ХОДОВ", 12, VttUiSkin.Muted,
            TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(290, 24), new Vector2(180, -14));

        _hostButtons = new GameObject("HostActions", typeof(RectTransform));
        _hostButtons.transform.SetParent(_panel.transform, false);
        Place(_hostButtons, new Vector2(1, 1), new Vector2(1, 1), new Vector2(350, 36), new Vector2(-48, -10));
        Button(_hostButtons.transform, "+ Добавить", 112, 32, 0, 0, ShowAddPanel, VttUiSkin.Button);
        Button(_hostButtons.transform, "Следующий ход  →", 146, 32, 118, 0, NextTurn,
            new Color(0.10f, 0.28f, 0.47f));
        Button(_hostButtons.transform, "Очистить", 78, 32, 270, 0, RequestClearAll,
            new Color(0.28f, 0.11f, 0.14f));

        var viewport = Box(_panel.transform, "Viewport", new Color(0, 0, 0, 0.01f), 0, false);
        _viewport = viewport;
        var viewportRt = viewport.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(12, 10);
        viewportRt.offsetMax = new Vector2(-12, -48);
        viewport.GetComponent<Image>().raycastTarget = false;
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var contentGo = new GameObject("Rows", typeof(RectTransform));
        contentGo.transform.SetParent(viewport.transform, false);
        _content = contentGo.GetComponent<RectTransform>();
        _content.anchorMin = new Vector2(0, 1);
        _content.anchorMax = new Vector2(0, 1);
        _content.pivot = new Vector2(0, 1);
        _content.anchoredPosition = Vector2.zero;
        _content.sizeDelta = new Vector2(0, 70);

        var scroll = viewport.AddComponent<ScrollRect>();
        scroll.viewport = viewportRt;
        scroll.content = _content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28;

        _emptyMessage = new GameObject("EmptyMessage", typeof(RectTransform));
        _emptyMessage.transform.SetParent(viewport.transform, false);
        Place(_emptyMessage, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(980, 60), Vector2.zero);
        Label(_emptyMessage.transform, "Пока нет участников. DM может добавить игрока или персонажа.",
            15, VttUiSkin.Muted, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(980, 54), Vector2.zero);

        BuildAddPanel(root.transform);
        RebuildRows();
        UpdateHostControls();
    }

    private void RebuildRows()
    {
        if (_content == null) return;
        foreach (Transform child in _content) Destroy(child.gameObject);
        if (_emptyMessage != null) _emptyMessage.SetActive(_entries.Count == 0);
        if (_viewport != null) _viewport.SetActive(_entries.Count > 0);
        int rows = Mathf.CeilToInt(_entries.Count / (float)CardsPerRow);
        _content.sizeDelta = new Vector2(850, rows * CardStepY);
        if (_panel != null)
            _panel.GetComponent<RectTransform>().sizeDelta =
                new Vector2(880, _entries.Count == 0 ? 52 : Mathf.Min(48 + rows * CardStepY + 12, 48 + 4 * CardStepY + 12));
        if (_roundText != null)
            _roundText.text = _entries.Count == 0 ? "НЕТ УЧАСТНИКОВ" :
                $"{_entries.Count} участников · ход {_currentIndex + 1}";

        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            bool active = i == _currentIndex;
            var row = Box(_content, "Entry " + entry.id,
                active ? new Color(0.13f, 0.30f, 0.49f, 0.99f) : VttUiSkin.Raised, 8, active);
            Place(row, new Vector2(0, 1), new Vector2(0, 1), new Vector2(CardWidth, CardHeight),
                new Vector2(i % CardsPerRow * CardStepX, -(i / CardsPerRow) * CardStepY));
            Label(row.transform, active ? "▶" : (i + 1).ToString(), 14,
                active ? VttUiSkin.Blue : VttUiSkin.Muted, TextAnchor.MiddleCenter,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, 24),
                new Vector2(4, -4), active);
            string displayName = entry.name.Length > 14
                ? entry.name.Substring(0, 13) + "…" : entry.name;
            Label(row.transform, displayName, 13, VttUiSkin.Text, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(122, 25),
                new Vector2(32, -3), active);
            if (IsHost)
            {
                var input = Input(row.transform, entry.initiative.ToString(), 48, 22, 32, -25);
                input.contentType = InputField.ContentType.IntegerNumber;
                int id = entry.id;
                input.onEndEdit.AddListener(value => SetInitiativeById(id, value));
                Button(row.transform, "×", 24, 22, 135, -25,
                    () => RemoveEntryById(id), new Color(0.28f, 0.11f, 0.14f));
            }
            else
                Label(row.transform, entry.initiative.ToString(), 14, VttUiSkin.Text,
                    TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(90, 22), new Vector2(32, -25), true);
        }
    }

    private void BuildAddPanel(Transform parent)
    {
        _addPanel = Box(parent, "AddParticipant", VttUiSkin.Panel, 12);
        Place(_addPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(414, 174), Vector2.zero);
        _addPanel.SetActive(false);
        Label(_addPanel.transform, "Добавить участника", 18, VttUiSkin.Text,
            TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(380, 28), new Vector2(18, -14), true);
        Label(_addPanel.transform, "Имя", 13, VttUiSkin.Muted, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(120, 22), new Vector2(18, -54));
        _nameInput = Input(_addPanel.transform, "", 246, 32, 150, -51, "Например, гоблин");
        Label(_addPanel.transform, "Инициатива", 13, VttUiSkin.Muted, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(125, 22), new Vector2(18, -96));
        _initInput = Input(_addPanel.transform, "0", 74, 32, 150, -93);
        _initInput.contentType = InputField.ContentType.IntegerNumber;
        _addErrorText = Label(_addPanel.transform, "", 12,
            VttUiSkin.Muted, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(365, 20), new Vector2(18, -129));
        Button(_addPanel.transform, "Отмена", 92, 34, 18, -137,
            () => _addPanel.SetActive(false), VttUiSkin.Button);
        Button(_addPanel.transform, "Добавить", 112, 34, 284, -137,
            SubmitAdd, new Color(0.10f, 0.30f, 0.48f));
    }

    public void ShowAddPanel()
    {
        if (!IsHost || _addPanel == null) return;
        _addErrorText.text = "";
        SetVisible(true);
        _addPanel.SetActive(true);
    }

    private void SubmitAdd()
    {
        if (!IsHost) return;
        if (string.IsNullOrWhiteSpace(_nameInput.text))
        {
            _addErrorText.text = "Введите имя участника.";
            return;
        }
        if (!int.TryParse(_initInput.text, out int score))
        {
            _addErrorText.text = "Укажите инициативу числом.";
            return;
        }
        AddEntry(_nameInput.text, Mathf.Clamp(score, -999, 999));
        _nameInput.text = "";
        _initInput.text = "0";
        _addPanel.SetActive(false);
    }

    private GameObject Box(Transform parent, string name, Color color, int radius, bool outline = true)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        VttUiSkin.Surface(go.GetComponent<Image>(), color, radius, outline);
        return go;
    }

    private static void Place(GameObject go, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 pos)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    private Text Label(Transform parent, string value, int size, Color color, TextAnchor align,
        Vector2 anchor, Vector2 pivot, Vector2 dimensions, Vector2 position, bool bold = false)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Place(go, anchor, pivot, dimensions, position);
        var text = go.GetComponent<Text>();
        text.font = _font;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        text.color = color;
        text.alignment = align;
        text.raycastTarget = false;
        return text;
    }

    private GameObject Button(Transform parent, string title, float width, float height,
        float x, float y, Action click, Color color)
    {
        var go = new GameObject("Button " + title, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Place(go, new Vector2(0, 1), new Vector2(0, 1), new Vector2(width, height), new Vector2(x, y));
        VttUiSkin.ButtonStyle(go.GetComponent<Image>(), color, 7);
        go.GetComponent<Button>().onClick.AddListener(() => click());
        if (!string.IsNullOrEmpty(title))
            Label(go.transform, title, 13, VttUiSkin.Text, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(width - 4, height - 4), Vector2.zero, true);
        return go;
    }

    private InputField Input(Transform parent, string value, float width, float height,
        float x, float y, string hint = "")
    {
        var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
        go.transform.SetParent(parent, false);
        Place(go, new Vector2(0, 1), new Vector2(0, 1), new Vector2(width, height), new Vector2(x, y));
        VttUiSkin.Surface(go.GetComponent<Image>(), VttUiSkin.Raised, 6);
        var text = Label(go.transform, value, 14, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(width - 16, height - 4), Vector2.zero);
        text.raycastTarget = false;
        var placeholder = Label(go.transform, hint, 13, VttUiSkin.Muted,
            TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(width - 16, height - 4), Vector2.zero);
        var field = go.GetComponent<InputField>();
        field.textComponent = text;
        field.placeholder = placeholder;
        field.text = value;
        return field;
    }

    private new void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        if (Instance == this) Instance = null;
    }
}
