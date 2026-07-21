using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Initiative Tracker. Права: IsHost — добавление, ход, очистка.
/// Клиенты только просматривают (I — показать/скрыть).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class InitiativeTracker : NetworkBehaviour
{
    private NetworkVariable<FixedString4096Bytes> _netData = new(
        new FixedString4096Bytes(""),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private List<Entry> _entries = new();
    private int _currentIndex;
    private Canvas _canvas;
    private GameObject _panel;
    private Font _font;
    private GameObject _btnAdd;
    private GameObject _btnNext;
    private GameObject _btnClear;
    private bool _hostControlsVisible;

    private struct Entry
    {
        public string name;
        public int initiative;
        public string colorHex;
    }

    public static InitiativeTracker Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
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
        _netData.OnValueChanged += (old, val) => { ParseData(val.ToString()); RebuildCards(); };
        if (_netData.Value.ToString().Length > 0)
        {
            ParseData(_netData.Value.ToString());
            RebuildCards();
        }

        UpdateHostControlsVisibility();
    }

    private void Update()
    {
        bool connected = GameNetworkManager.Instance != null && GameNetworkManager.Instance.IsConnected;
        if (_panel != null && _panel.activeSelf != connected)
            _panel.SetActive(connected);

        if (GameplayInputGate.AllowsKeyboardHotkeys)
        {
            var k = Keyboard.current;
            if (k != null && k.iKey.wasPressedThisFrame && _panel != null && connected)
                _panel.SetActive(!_panel.activeSelf);
        }

        if (connected)
            UpdateHostControlsVisibility();
    }

    private void UpdateHostControlsVisibility()
    {
        bool show = IsHost;
        if (_btnAdd != null) _btnAdd.SetActive(show);
        if (_btnNext != null) _btnNext.SetActive(show);
        if (_btnClear != null) _btnClear.SetActive(show);

        if (!show && _addPanel != null && _addPanel.activeSelf)
            _addPanel.SetActive(false);

        if (show == _hostControlsVisible) return;
        _hostControlsVisible = show;
        RebuildCards();
    }

    // ═══ Данные (JSON: idx|n,i,c;n,i,c;...) ═══

    private void ParseData(string data)
    {
        _entries.Clear();
        _currentIndex = 0;
        if (string.IsNullOrEmpty(data)) return;

        var parts = data.Split('|');
        if (parts.Length > 0 && int.TryParse(parts[0], out int idx))
            _currentIndex = idx;

        for (int p = 1; p < parts.Length; p++)
        {
            var fields = parts[p].Split(',');
            if (fields.Length >= 3)
            {
                _entries.Add(new Entry
                {
                    name = fields[0],
                    initiative = int.TryParse(fields[1], out int i) ? i : 0,
                    colorHex = fields[2]
                });
            }
        }
    }

    private string SerializeData()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(_currentIndex);
        foreach (var e in _entries)
        {
            sb.Append('|');
            sb.Append(e.name);
            sb.Append(',');
            sb.Append(e.initiative);
            sb.Append(',');
            sb.Append(e.colorHex);
        }
        return sb.ToString();
    }

    // ═══ Действия (хост) ═══

    public void AddEntry(string name, int initiative, string colorHex = "#FFFFFF")
    {
        if (!IsHost) return;
        _entries.Add(new Entry { name = name, initiative = initiative, colorHex = colorHex });
        _entries.Sort((a, b) => b.initiative.CompareTo(a.initiative));
        _currentIndex = 0;
        Sync();
    }

    public void RemoveEntry(int index)
    {
        if (!IsHost || index < 0 || index >= _entries.Count) return;
        _entries.RemoveAt(index);
        if (_currentIndex >= _entries.Count) _currentIndex = 0;
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
        _entries.Clear(); _currentIndex = 0;
        Sync();
    }

    private void Sync() => _netData.Value = new FixedString4096Bytes(SerializeData());

    // ═══ UI ═══

    void BuildUI()
    {
        var cgo = GO("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(transform, false);
        _canvas = cgo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.sortingOrder = 20;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;

        float PW = 1060f, PD = 8f;
        float panelX = 83f, panelY = -8f;
        _panelX = panelX; _panelY = panelY; _panelW = PW; _panelPD = PD;

        _panel = GO("Panel", typeof(RectTransform), typeof(Image));
        _panel.transform.SetParent(cgo.transform, false);
        _panel.GetComponent<Image>().color = new Color(0.05f, 0.07f, 0.11f, 0.94f);
        _panel.SetActive(false);
        // Начальная высота — обновится в RebuildCards
        _panelRt = _panel.GetComponent<RectTransform>();
        _panelRt.anchorMin = _panelRt.anchorMax = _panelRt.pivot = PL;
        _panelRt.sizeDelta = new Vector2(PW, 50f);
        _panelRt.anchoredPosition = new Vector2(panelX, panelY);

        Txt(_panel.transform, "INITIATIVE", _font, 11, FontStyle.Bold,
            new Color(0.4f, 0.45f, 0.55f), TextAnchor.MiddleLeft);
        R(FindChild(_panel.transform, "T"), AL, AL, PL, new(100, 16), new(PD, -2));

        // Карточки (под кнопками)
        _cardsParent = GO("Cards", typeof(RectTransform));
        _cardsParent.transform.SetParent(_panel.transform, false);
        _cardsRt = _cardsParent.GetComponent<RectTransform>();
        _cardsRt.anchorMin = _cardsRt.anchorMax = new Vector2(0, 1);
        _cardsRt.pivot = new Vector2(0, 1);

        // Кнопки (хост)
        float bx = PW - PD - 24;
        _btnAdd = MkBtn(_panel.transform, "+", _font, new Color(0.12f, 0.25f, 0.4f), bx - 56, -PD, 24, 24, ShowAddPanel);
        _btnNext = MkBtn(_panel.transform, ">", _font, new Color(0.12f, 0.25f, 0.4f), bx - 28, -PD, 24, 24, NextTurn);
        _btnClear = MkBtn(_panel.transform, "X", _font, new Color(0.35f, 0.12f, 0.12f), bx, -PD, 24, 24, ClearAll);

        BuildAddPanel(cgo.transform);
        UpdateHostControlsVisibility();
    }

    private float _panelX, _panelY, _panelW, _panelPD;
    private RectTransform _panelRt, _cardsRt;
    private GameObject _cardsParent;

    void RebuildCards()
    {
        foreach (Transform t in _cardsParent.transform) Destroy(t.gameObject);

        float cardW = 160f, rowH = 36f, gap = 6f;
        float btnRowH = IsHost ? 28f : 0f;
        float availW = _panelW - _panelPD * 2;
        int perRow = Mathf.Max(1, (int)((availW + gap) / (cardW + gap)));

        int rows = _entries.Count == 0 ? 0 : (_entries.Count - 1) / perRow + 1;
        float panelH = _panelPD + btnRowH + _panelPD + rows * (rowH + gap) + _panelPD;
        _panelRt.sizeDelta = new Vector2(_panelW, panelH);

        // Позиция карточек: под кнопками
        float cardsTopY = -(_panelPD + btnRowH + _panelPD);
        _cardsRt.anchoredPosition = new Vector2(_panelPD, cardsTopY);
        _cardsRt.sizeDelta = new Vector2(availW, rows * (rowH + gap));

        for (int i = 0; i < _entries.Count; i++)
        {
            int row = i / perRow;
            int column = i % perRow;
            int colsInRow = Mathf.Min(perRow, _entries.Count - row * perRow);
            float rowStartX = (availW - (colsInRow * cardW + (colsInRow - 1) * gap)) / 2f;

            var e = _entries[i];
            var card = GO("Card", typeof(RectTransform), typeof(Image));
            card.transform.SetParent(_cardsParent.transform, false);

            bool active = i == _currentIndex;
            card.GetComponent<Image>().color = active
                ? new Color(0.2f, 0.3f, 0.5f, 0.85f)
                : new Color(0.15f, 0.2f, 0.35f, 0.6f);
            R(card, AL, AL, PL, new(cardW, rowH),
                new(rowStartX + column * (cardW + gap), -row * (rowH + gap)));

            string dn = e.name.Length > 15 ? e.name.Substring(0, 14) + ".." : e.name;
            Txt(card.transform, dn, _font, active ? 13 : 12,
                active ? FontStyle.Bold : FontStyle.Normal,
                active ? Color.white : new Color(0.7f, 0.75f, 0.85f), TextAnchor.MiddleCenter);
            // Имя слева (130px), инициатива справа (30px)
            R(FindChild(card.transform, "T"), AC, AC, PC, new(cardW - 30, rowH), new(-15, 0));

            Txt(card.transform, e.initiative.ToString(), _font, active ? 14 : 12,
                FontStyle.Bold, active ? Color.white : new Color(0.5f, 0.55f, 0.65f), TextAnchor.MiddleCenter);
            R(FindChild(card.transform, "T", 1), AC, AC, PC, new(30, rowH), new(cardW / 2 - 15, 0));

            if (IsHost)
            {
                int idx = i;
                var xBtn = MkBtn(card.transform, "x", _font, new Color(0.5f, 0.15f, 0.15f),
                    cardW - 20, -8, 16, 16, () => RemoveEntry(idx));
                xBtn.SetActive(false); // скрыта, показывается при наведении
                // HoverReveal: показывает кнопку при наведении, скрывает инициативу
                var hover = card.AddComponent<HoverReveal>();
                hover.target = xBtn;
                hover.hideOnHover = FindChild(card.transform, "T", 1); // текст инициативы
            }
        }

        // Обновить позицию панели добавления
        if (_addPanel != null)
        {
            var art = _addPanel.GetComponent<RectTransform>();
            art.anchoredPosition = new Vector2(_panelX + _panelW - 380, _panelY + _panelPD - panelH - 8);
        }
    }

    void BuildAddPanel(Transform parent)
    {
        _addPanel = GO("AddPanel", typeof(RectTransform), typeof(Image));
        _addPanel.transform.SetParent(parent, false);
        _addPanel.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.15f, 0.96f);
        _addPanel.SetActive(false);
        R(_addPanel, AL, AL, PL, new(380, 140), new(_panelX + _panelW - 380, _panelY - 66f));

        Txt(_addPanel.transform, "Добавить в инициативу", _font, 13, FontStyle.Bold,
            new Color(0.7f, 0.75f, 0.85f));
        R(FindChild(_addPanel.transform, "T"), AL, AL, PL, new(360, 18), new(10, -10));

        Txt(_addPanel.transform, "Имя:", _font, 12, FontStyle.Normal, new Color(0.5f, 0.55f, 0.65f));
        R(FindChild(_addPanel.transform, "T", 1), AL, AL, PL, new(36, 18), new(10, -36));

        _nameInput = MkInput(_addPanel.transform, "", _font, 13, FontStyle.Normal, Color.white,
            new(220, 26), AL, AL, PL, new(220, 26), new(50, -36), v => { });

        Txt(_addPanel.transform, "Init:", _font, 12, FontStyle.Normal, new Color(0.5f, 0.55f, 0.65f));
        R(FindChild(_addPanel.transform, "T", 2), AL, AL, PL, new(36, 18), new(10, -70));

        _initInput = MkInput(_addPanel.transform, "0", _font, 13, FontStyle.Normal, Color.white,
            new(60, 26), AL, AL, PL, new(60, 26), new(50, -70), v => { });

        MkBtn(_addPanel.transform, "Add", _font, new Color(0.15f, 0.35f, 0.2f),
            10, -108, 100, 26, () =>
        {
            if (!IsHost || string.IsNullOrEmpty(_nameInput.text)) return;
            int.TryParse(_initInput.text, out int init);
            AddEntry(_nameInput.text, init, "#FFFFFF");
            _addPanel.SetActive(false);
            _nameInput.text = "";
            _initInput.text = "0";
        });

        MkBtn(_addPanel.transform, "Cancel", _font, new Color(0.2f, 0.2f, 0.25f),
            120, -108, 80, 26, () => _addPanel.SetActive(false));
    }

    private GameObject _addPanel;
    private InputField _nameInput, _initInput;

    void ShowAddPanel()
    {
        if (!IsHost || _addPanel == null) return;
        _addPanel.SetActive(true);
    }

    // ═══ Хелперы ═══

    static Vector2 P(float x, float y) => new(x, y);
    static readonly Vector2 AL = P(0, 1), AC = P(0.5f, 0.5f), PL = P(0, 1), PC = P(0.5f, 0.5f);

    void R(GameObject go, Vector2 am, Vector2 aM, Vector2 pv, Vector2 sz, Vector2 ps)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = am; rt.anchorMax = aM; rt.pivot = pv; rt.sizeDelta = sz; rt.anchoredPosition = ps;
    }

    GameObject GO(string n, params Type[] ts) => new GameObject(n, ts);

    GameObject Txt(Transform p, string t, Font f, float sz, FontStyle st, Color c, TextAnchor a = TextAnchor.MiddleLeft)
    {
        var g = GO("T", typeof(RectTransform), typeof(Text));
        g.transform.SetParent(p, false);
        var tx = g.GetComponent<Text>();
        tx.text = t; tx.font = f; tx.fontSize = (int)sz; tx.fontStyle = st; tx.color = c; tx.alignment = a;
        tx.raycastTarget = false;
        return g;
    }

    GameObject MkBtn(Transform p, string l, Font f, Color bg, float x, float y, float w, float h, Action cb)
    {
        var g = GO("B", typeof(RectTransform), typeof(Image), typeof(Button));
        g.transform.SetParent(p, false);
        g.GetComponent<Image>().color = bg;
        g.GetComponent<Button>().onClick.AddListener(() => cb());
        R(g, AL, AL, PL, new(w, h), new(x, y));
        var lb = Txt(g.transform, l, f, 12, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        R(lb, AC, AC, PC, new(w, h), Vector2.zero);
        return g;
    }

    InputField MkInput(Transform p, string text, Font f, float sz, FontStyle st, Color c,
        Vector2 sz2, Vector2 aMin, Vector2 aMax, Vector2 pv, Vector2 size, Vector2 pos,
        UnityEngine.Events.UnityAction<string> cb)
    {
        var g = GO("Inp", typeof(RectTransform), typeof(Image), typeof(InputField));
        g.transform.SetParent(p, false);
        g.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.18f, 0.9f);
        R(g, aMin, aMax, pv, size, pos);
        var tGO = GO("Txt", typeof(RectTransform), typeof(Text));
        tGO.transform.SetParent(g.transform, false);
        var tc = tGO.GetComponent<Text>();
        tc.text = text; tc.font = f; tc.fontSize = (int)sz; tc.fontStyle = st;
        tc.color = c; tc.alignment = TextAnchor.MiddleLeft; tc.supportRichText = false;
        var trt = tGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(4, 2); trt.offsetMax = new Vector2(-4, -2);
        var phGO = GO("PH", typeof(RectTransform), typeof(Text));
        phGO.transform.SetParent(g.transform, false);
        var phc = phGO.GetComponent<Text>();
        phc.text = "..."; phc.font = f; phc.fontSize = (int)sz; phc.fontStyle = FontStyle.Italic;
        phc.color = new Color(0.35f, 0.4f, 0.5f); phc.alignment = TextAnchor.MiddleLeft; phc.raycastTarget = false;
        var prt = phGO.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(4, 2); prt.offsetMax = new Vector2(-4, -2);
        var ifc = g.GetComponent<InputField>();
        ifc.textComponent = tc; ifc.placeholder = phc;
        ifc.lineType = InputField.LineType.SingleLine; ifc.text = text;
        ifc.onValueChanged.AddListener(cb);
        return ifc;
    }

    GameObject FindChild(Transform parent, string startsWith, int skip = 0)
    {
        int count = 0;
        foreach (Transform t in parent)
        {
            if (t.name.StartsWith(startsWith))
            {
                if (count == skip) return t.gameObject;
                count++;
            }
        }
        return null;
    }

    private new void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
    }
}

/// <summary>Показывает target при наведении, скрывает hideOnHover.</summary>
public class HoverReveal : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public GameObject target;
    public GameObject hideOnHover;

    private void Start() { if (target != null) target.SetActive(false); }

    public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData d)
    {
        if (target != null) target.SetActive(true);
        if (hideOnHover != null) hideOnHover.SetActive(false);
    }

    public void OnPointerExit(UnityEngine.EventSystems.PointerEventData d)
    {
        if (target != null) target.SetActive(false);
        if (hideOnHover != null) hideOnHover.SetActive(true);
    }
}
