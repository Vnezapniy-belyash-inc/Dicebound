using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Чарник D&D: статы блоками, навыки, пассивные чувства, владения. Клавиша C.
/// </summary>
public class CharacterSheetUI : MonoBehaviour
{
    public CharacterData characterData;

    [Header("Размеры")]
    public float panelW = 720f;
    public float colW = 340f;
    public float blockHdrH = 34f;
    public float subRowH = 24f;
    public float skillH = 28f;
    public float blockGap = 6f;
    public float pad = 14f;

    Canvas _canvas;
    GameObject _panel;
    CharacterData _cd;

    // Статы
    Text[] _scoreTxt = new Text[6], _checkTxt = new Text[6], _saveTxt = new Text[6];
    Image[] _saveTgl = new Image[6];

    // Навыки
    Text[] _skillProfTxt = new Text[18], _skillBonusTxt = new Text[18];

    // Низ
    Text _profTxt, _pasPerTxt, _pasInsTxt, _pasInvTxt;
    InputField _otherInput;

    // Цвета статов
    static readonly Color[] StatClr = {
        new(0.9f, 0.35f, 0.35f), new(0.35f, 0.75f, 0.9f), new(0.9f, 0.7f, 0.3f),
        new(0.4f, 0.5f, 0.95f),  new(0.5f, 0.85f, 0.5f),  new(0.85f, 0.55f, 0.9f),
    };

    static readonly string[] StatNames = { "СИЛА", "ЛОВКОСТЬ", "ТЕЛОСЛОЖЕНИЕ", "ИНТЕЛЛЕКТ", "МУДРОСТЬ", "ХАРИЗМА" };

    // Индексы навыков для каждого стата
    static readonly int[][] StatSkills = {
        new[]{0},             // STR: Атлетика
        new[]{1,2,3},         // DEX: Акробатика, Ловкость рук, Скрытность
        new int[]{},          // CON: нет
        new[]{4,5,6,7,8},     // INT: Анализ, История, Магия, Природа, Религия
        new[]{9,10,11,12,13}, // WIS: Восприятие, Выживание, Медицина, Проницательность, Уход за животными
        new[]{14,15,16,17},   // CHA: Выступление, Запугивание, Обман, Убеждение
    };

    void Start()
    {
        _cd = characterData != null ? characterData : FindAnyObjectByType<CharacterData>();
        BuildUI();
        if (_panel != null) _panel.SetActive(false);
    }

    void Update()
    {
        var k = Keyboard.current;
        if (k == null) return;
        if (k.cKey.wasPressedThisFrame && _panel != null)
        {
            _panel.SetActive(!_panel.activeSelf);
            if (_panel.activeSelf) RefreshDisplay();
        }
    }

    // ═══════════════════════════════════════
    //  Сборка
    // ═══════════════════════════════════════

    void BuildUI()
    {
        try { BuildUIInternal(); }
        catch (System.Exception e) { Debug.LogError($"CharSheet fail: {e.Message}"); }
    }

    void BuildUIInternal()
    {
        EnsureEventSystem();
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _cd = _cd != null ? _cd : FindAnyObjectByType<CharacterData>();
        if (_cd == null) { Debug.LogError("CharacterData not found"); return; }

        // Canvas
        var cgo = NewGO("CharCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(transform, false);
        _canvas = cgo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.sortingOrder = 15;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;

        // Высота панели
        float leftH = BlockH(0) + BlockH(2) + BlockH(3) + BlockH(5) + 3 * blockGap;
        float rightH = BlockH(1) + BlockH(4) + blockGap;
        float blocksH = Mathf.Max(leftH, rightH);
        float bottomH = 130f;
        float totalH = pad + 30f + pad + blocksH + pad + bottomH + pad;

        // Панель
        _panel = NewGO("Panel", typeof(RectTransform), typeof(Image));
        _panel.transform.SetParent(cgo.transform, false);
        _panel.GetComponent<Image>().color = new Color(0.04f, 0.05f, 0.10f, 0.94f);
        SetRect(_panel, A(0.5f, 0.5f), A(0.5f, 0.5f), A(0.5f, 0.5f), new(panelW, totalH), new(80, -10));

        float cy = -pad;

        // Заголовок + Бонус мастерства
        var t = MkText(_panel.transform, "ХАРАКТЕРИСТИКИ", f, 20, FontStyle.Bold, new Color(0.95f, 0.9f, 0.7f));
        SetR(t, ATL, ATL, P(0, 1), new(0, 30f), new(pad, cy));
        var pt = MkText(_panel.transform, "Бонус мастерства: +2", f, 16, FontStyle.Normal, new Color(0.7f, 0.75f, 0.85f));
        SetR(pt, ATR, ATR, P(1, 1), new(220, 30f), new(-pad, cy));
        _profTxt = pt.GetComponent<Text>();
        cy -= 30f + pad;

        // Левая колонка: СИЛА(0), ТЕЛ(2), ИНТ(3), ХАР(5)
        float cxL = pad;
        cy = BuildStatBlocks(cxL, cy, new[] { 0, 2, 3, 5 }, f);

        // Правая колонка: ЛОВ(1), МДР(4) — стартуем с того же cy
        float cxR = pad + colW + pad;
        float rightCY = -pad - 30f - pad; // тот же старт что у левой
        BuildStatBlocks(cxR, rightCY, new[] { 1, 4 }, f);

        // Низ
        float bottomY = cy - pad;
        BuildBottom(_panel.transform, f, bottomY);
    }

    float BuildStatBlocks(float cx, float startY, int[] stats, Font f)
    {
        float cy = startY;
        foreach (int si in stats)
        {
            float h = BlockH(si);
            BuildStatBlock(cx, cy, si, f);
            cy -= h + blockGap;
        }
        return cy;
    }

    float BlockH(int si) => blockHdrH + subRowH + StatSkills[si].Length * skillH;

    void BuildStatBlock(float cx, float y, int si, Font f)
    {
        float w = colW;
        Color clr = StatClr[si];
        var cd = _cd;

        // ── Заголовок ──
        var hdrGO = NewGO($"Hdr_{si}", typeof(RectTransform), typeof(Image));
        hdrGO.transform.SetParent(_panel.transform, false);
        hdrGO.GetComponent<Image>().color = new Color(0.08f, 0.10f, 0.16f, 0.8f);
        SetR(hdrGO, ATL, ATL, P(0, 1), new(w, blockHdrH), new(cx, y));

        var nm = MkText(hdrGO.transform, StatNames[si], f, 17, FontStyle.Bold, clr);
        SetR(nm, AML, AML, P(0, 0.5f), new(180, blockHdrH), new(8, 0));

        // Счёт с +/−
        int score = GetStatScore(si);
        var sc = MkText(hdrGO.transform, score.ToString(), f, 17, FontStyle.Bold, Color.white);
        SetR(sc, AML, AML, P(0, 0.5f), new(28, blockHdrH), new(w - 68, 0));
        _scoreTxt[si] = sc.GetComponent<Text>();

        // −
        MkBtn(hdrGO.transform, "−", f, new Color(0.6f, 0.2f, 0.2f), w - 40, 16, 18, () => ChangeStat(si, -1));
        // +
        MkBtn(hdrGO.transform, "+", f, new Color(0.2f, 0.55f, 0.25f), w - 20, 16, 18, () => ChangeStat(si, +1));

        // ── Подстрока: ПРОВЕРКА / СПАСБРОСОК ──
        float sy = y - blockHdrH;
        var subGO = NewGO($"Sub_{si}", typeof(RectTransform), typeof(Image));
        subGO.transform.SetParent(_panel.transform, false);
        subGO.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.14f, 0.5f);
        SetR(subGO, ATL, ATL, P(0, 1), new(w, subRowH), new(cx, sy));

        // ПРОВЕРКА
        var cl = MkText(subGO.transform, "ПРОВЕРКА", f, 12, FontStyle.Normal, new Color(0.55f, 0.6f, 0.7f));
        SetR(cl, AML, AML, P(0, 0.5f), new(70, subRowH), new(8, 0));
        int mod = GetStatMod(si);
        var cv = MkText(subGO.transform, ModStr(mod), f, 13, FontStyle.Bold, ModClr(mod));
        SetR(cv, AML, AML, P(0, 0.5f), new(30, subRowH), new(74, 0));
        _checkTxt[si] = cv.GetComponent<Text>();

        // СПАСБРОСОК
        var sl = MkText(subGO.transform, "СПАСБРОСОК", f, 12, FontStyle.Normal, new Color(0.55f, 0.6f, 0.7f));
        SetR(sl, AML, AML, P(0, 0.5f), new(80, subRowH), new(w / 2 + 0, 0));
        int save = GetSaveVal(si);
        var sv = MkText(subGO.transform, ModStr(save), f, 13, FontStyle.Bold, ModClr(save));
        SetR(sv, AML, AML, P(0, 0.5f), new(30, subRowH), new(w / 2 + 76, 0));
        _saveTxt[si] = sv.GetComponent<Text>();

        // Тоггл владения спас-броском
        bool profSave = GetSaveProf(si);
        _saveTgl[si] = MkTgl(subGO.transform, w - 28, 16, profSave, v => SetSaveProf(si, v));

        // ── Навыки ──
        var skillIndices = StatSkills[si];
        for (int i = 0; i < skillIndices.Length; i++)
        {
            int ski = skillIndices[i];
            float ry = sy - subRowH - i * skillH;

            var skGO = NewGO($"Sk_{ski}", typeof(RectTransform), typeof(Image));
            skGO.transform.SetParent(_panel.transform, false);
            skGO.GetComponent<Image>().color = new Color(0.07f, 0.09f, 0.18f, 0.6f);
            SetR(skGO, ATL, ATL, P(0, 1), new(w, skillH), new(cx, ry));

            // Индикатор владения (○/●/★)
            int lvl = cd.skills[ski].ProfLevel;
            string ind = lvl == 2 ? "★" : (lvl == 1 ? "●" : "○");
            var it = MkText(skGO.transform, ind, f, 14, FontStyle.Normal,
                lvl == 2 ? new Color(0.9f, 0.75f, 0.2f) : (lvl == 1 ? new Color(0.4f, 0.7f, 1f) : new Color(0.4f, 0.4f, 0.5f)));
            SetR(it, AML, AML, P(0, 0.5f), new(20, skillH), new(6, 0));
            _skillProfTxt[ski] = it.GetComponent<Text>();

            // Кликабельная область для индикатора
            var ib = NewGO($"IndBtn_{ski}", typeof(RectTransform), typeof(Image), typeof(Button));
            ib.transform.SetParent(skGO.transform, false);
            ib.GetComponent<Image>().color = new Color(0, 0, 0, 0);
            ib.GetComponent<Button>().onClick.AddListener(() => CycleProf(ski));
            SetR(ib, AML, AML, P(0, 0.5f), new(24, skillH), new(4, 0));

            // Название навыка
            var skn = cd.skills[ski].name;
            var nt = MkText(skGO.transform, skn, f, 14, FontStyle.Normal, new Color(0.85f, 0.88f, 0.95f));
            SetR(nt, AML, AML, P(0, 0.5f), new(w - 80, skillH), new(30, 0));

            // Бонус
            int bonus = cd.GetSkillBonus(cd.skills[ski]);
            var bt = MkText(skGO.transform, ModStr(bonus), f, 14, FontStyle.Bold, ModClr(bonus));
            SetR(bt, AML, AML, P(0, 0.5f), new(30, skillH), new(w - 36, 0));
            _skillBonusTxt[ski] = bt.GetComponent<Text>();
        }
    }

    void BuildBottom(Transform parent, Font f, float y)
    {
        float w = panelW - pad * 2;
        float x = pad;

        // ПАССИВНЫЕ ЧУВСТВА
        var h1 = MkText(parent, "ПАССИВНЫЕ ЧУВСТВА", f, 14, FontStyle.Bold, new Color(0.6f, 0.65f, 0.75f));
        SetR(h1, ATL, ATL, P(0, 1), new(w, 22f), new(x, y));
        y -= 22f;

        string[] pasLabels = { "МУДРОСТЬ (ВОСПРИЯТИЕ)", "МУДРОСТЬ (ПРОНИЦАТЕЛЬНОСТЬ)", "ИНТЕЛЛЕКТ (АНАЛИЗ)" };
        for (int i = 0; i < 3; i++)
        {
            float ry = y - i * 22f;
            var go = NewGO($"Pas_{i}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = new Color(0.08f, 0.10f, 0.16f, 0.5f);
            SetR(go, ATL, ATL, P(0, 1), new(w, 20f), new(x, ry));

            var lb = MkText(go.transform, pasLabels[i], f, 12, FontStyle.Normal, new Color(0.65f, 0.7f, 0.78f));
            SetR(lb, AML, AML, P(0, 0.5f), new(w - 40, 20f), new(6, 0));

            var vl = MkText(go.transform, "10", f, 13, FontStyle.Bold, Color.white);
            SetR(vl, AML, AML, P(1, 0.5f), new(30, 20f), new(-6, 0));

            if (i == 0) _pasPerTxt = vl.GetComponent<Text>();
            if (i == 1) _pasInsTxt = vl.GetComponent<Text>();
            if (i == 2) _pasInvTxt = vl.GetComponent<Text>();
        }
        y -= 3 * 22f + pad;

        // ПРОЧИЕ ВЛАДЕНИЯ И ЯЗЫКИ
        var h2 = MkText(parent, "ПРОЧИЕ ВЛАДЕНИЯ И ЯЗЫКИ", f, 14, FontStyle.Bold, new Color(0.6f, 0.65f, 0.75f));
        SetR(h2, ATL, ATL, P(0, 1), new(w, 22f), new(x, y));
        y -= 22f;

        var ifGO = NewGO("OtherInput", typeof(RectTransform), typeof(Image), typeof(InputField));
        ifGO.transform.SetParent(parent, false);
        ifGO.GetComponent<Image>().color = new Color(0.08f, 0.10f, 0.16f, 0.6f);
        SetR(ifGO, ATL, ATL, P(0, 1), new(w, 42f), new(x, y));

        var ifText = NewGO("Text", typeof(RectTransform), typeof(Text));
        ifText.transform.SetParent(ifGO.transform, false);
        var txc = ifText.GetComponent<Text>();
        txc.font = f; txc.fontSize = 13; txc.color = new Color(0.8f, 0.82f, 0.9f);
        txc.alignment = TextAnchor.UpperLeft; txc.supportRichText = false;
        SetR(ifText, A(0, 1), A(1, 0), P(0, 1), new(-8, -8), new(4, -4));

        var ifPlaceholder = NewGO("Ph", typeof(RectTransform), typeof(Text));
        ifPlaceholder.transform.SetParent(ifGO.transform, false);
        var ph = ifPlaceholder.GetComponent<Text>();
        ph.text = "Введите владения и языки..."; ph.font = f; ph.fontSize = 13;
        ph.color = new Color(0.4f, 0.42f, 0.5f); ph.alignment = TextAnchor.UpperLeft; ph.fontStyle = FontStyle.Italic;
        SetR(ifPlaceholder, A(0, 1), A(1, 0), P(0, 1), new(-8, -8), new(4, -4));
        ph.raycastTarget = false;

        var input = ifGO.GetComponent<InputField>();
        input.textComponent = txc;
        input.placeholder = ph;
        input.lineType = InputField.LineType.MultiLineNewline;
        if (!string.IsNullOrEmpty(_cd.otherProficiencies)) input.text = _cd.otherProficiencies;
        input.onEndEdit.AddListener(v => { if (_cd != null) _cd.otherProficiencies = v; });
        _otherInput = input;
    }

    // ═══════════════════════════════════════
    //  Логика
    // ═══════════════════════════════════════

    int GetStatScore(int si) => si switch
    { 0 => _cd.strength, 1 => _cd.dexterity, 2 => _cd.constitution, 3 => _cd.intelligence, 4 => _cd.wisdom, 5 => _cd.charisma, _ => 0 };

    int GetStatMod(int si) => si switch
    { 0 => _cd.StrMod, 1 => _cd.DexMod, 2 => _cd.ConMod, 3 => _cd.IntMod, 4 => _cd.WisMod, 5 => _cd.ChaMod, _ => 0 };

    int GetSaveVal(int si) => si switch
    { 0 => _cd.StrSave, 1 => _cd.DexSave, 2 => _cd.ConSave, 3 => _cd.IntSave, 4 => _cd.WisSave, 5 => _cd.ChaSave, _ => 0 };

    void SetStatScore(int si, int v) { switch (si) {
        case 0: _cd.strength = v; break; case 1: _cd.dexterity = v; break;
        case 2: _cd.constitution = v; break; case 3: _cd.intelligence = v; break;
        case 4: _cd.wisdom = v; break; case 5: _cd.charisma = v; break;
    }}

    bool GetSaveProf(int si) => si switch
    { 0 => _cd.strSaveProficient, 1 => _cd.dexSaveProficient, 2 => _cd.conSaveProficient, 3 => _cd.intSaveProficient, 4 => _cd.wisSaveProficient, 5 => _cd.chaSaveProficient, _ => false };

    void SetSaveProf(int si, bool v) { switch (si) {
        case 0: _cd.strSaveProficient = v; break; case 1: _cd.dexSaveProficient = v; break;
        case 2: _cd.conSaveProficient = v; break; case 3: _cd.intSaveProficient = v; break;
        case 4: _cd.wisSaveProficient = v; break; case 5: _cd.chaSaveProficient = v; break;
    } RefreshDisplay(); }

    void ChangeStat(int si, int d)
    {
        int v = Mathf.Clamp(GetStatScore(si) + d, 1, 30);
        SetStatScore(si, v);
        RefreshDisplay();
    }

    void CycleProf(int ski)
    {
        if (_cd == null || ski >= _cd.skills.Length) return;
        var s = _cd.skills[ski];
        s.ProfLevel = (s.ProfLevel + 1) % 3; // 0→1→2→0
        RefreshDisplay();
    }

    public void RefreshDisplay()
    {
        if (_cd == null) { _cd = FindAnyObjectByType<CharacterData>(); if (_cd == null) return; }

        // Статы
        for (int si = 0; si < 6; si++)
        {
            if (_scoreTxt[si] != null) _scoreTxt[si].text = GetStatScore(si).ToString();
            int mod = GetStatMod(si);
            if (_checkTxt[si] != null) { _checkTxt[si].text = ModStr(mod); _checkTxt[si].color = ModClr(mod); }
            int save = GetSaveVal(si);
            if (_saveTxt[si] != null) { _saveTxt[si].text = ModStr(save); _saveTxt[si].color = ModClr(save); }
            if (_saveTgl[si] != null) _saveTgl[si].color = GetSaveProf(si) ? new Color(0.35f, 0.7f, 1f) : new Color(0.25f, 0.28f, 0.35f);
        }

        // Навыки
        for (int ski = 0; ski < _cd.skills.Length && ski < 18; ski++)
        {
            int lvl = _cd.skills[ski].ProfLevel;
            if (_skillProfTxt[ski] != null)
            {
                _skillProfTxt[ski].text = lvl == 2 ? "★" : (lvl == 1 ? "●" : "○");
                _skillProfTxt[ski].color = lvl == 2 ? new Color(0.9f, 0.75f, 0.2f) : (lvl == 1 ? new Color(0.4f, 0.7f, 1f) : new Color(0.4f, 0.4f, 0.5f));
            }
            int bonus = _cd.GetSkillBonus(_cd.skills[ski]);
            if (_skillBonusTxt[ski] != null) { _skillBonusTxt[ski].text = ModStr(bonus); _skillBonusTxt[ski].color = ModClr(bonus); }
        }

        // Бонус мастерства
        if (_profTxt != null) _profTxt.text = $"Бонус мастерства: +{_cd.proficiencyBonus}";

        // Пассивные чувства
        if (_pasPerTxt != null) _pasPerTxt.text = _cd.passiveWisdomPerception.ToString();
        if (_pasInsTxt != null) _pasInsTxt.text = _cd.passiveWisdomInsight.ToString();
        if (_pasInvTxt != null) _pasInvTxt.text = _cd.passiveIntAnalysis.ToString();
    }

    // ═══════════════════════════════════════
    //  Хелперы
    // ═══════════════════════════════════════

    static string ModStr(int m) => m >= 0 ? $"+{m}" : $"{m}";
    static Color ModClr(int m) => m >= 0 ? new Color(0.5f, 0.9f, 0.5f) : new Color(0.9f, 0.4f, 0.4f);
    static Vector2 A(float x, float y) => new(x, y);
    static Vector2 P(float x, float y) => new(x, y);

    static readonly Vector2 ATL = A(0, 1), ATR = A(1, 1), AML = A(0, 0.5f), AMR = A(1, 0.5f);

    void SetR(GameObject go, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size, Vector2 pos)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = pivot;
        rt.sizeDelta = size; rt.anchoredPosition = pos;
    }

    void SetRect(GameObject go, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size, Vector2 pos)
        => SetR(go, aMin, aMax, pivot, size, pos);

    GameObject NewGO(string name, params System.Type[] comps)
    {
        var go = new GameObject(name, comps);
        return go;
    }

    GameObject MkText(Transform parent, string txt, Font f, float sz, FontStyle st, Color c, TextAnchor align = TextAnchor.MiddleLeft)
    {
        var go = NewGO("T", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.text = txt; t.font = f; t.fontSize = (int)sz; t.fontStyle = st;
        t.color = c; t.alignment = align; t.raycastTarget = false;
        return go;
    }

    void MkBtn(Transform parent, string label, Font f, Color bg, float x, float w, float h, UnityEngine.Events.UnityAction cb)
    {
        var go = NewGO("B", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = bg;
        go.GetComponent<Button>().onClick.AddListener(cb);
        SetR(go, AML, AML, P(0, 0.5f), new(w, h), new(x, 0));
        var lb = MkText(go.transform, label, f, 11, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        SetR(lb, A(0.5f, 0.5f), A(0.5f, 0.5f), P(0.5f, 0.5f), new(w, h), Vector2.zero);
    }

    Image MkTgl(Transform parent, float x, float sz, bool on, UnityEngine.Events.UnityAction<bool> cb)
    {
        var go = NewGO("Tgl", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        bool state = on;
        img.color = state ? new Color(0.35f, 0.7f, 1f) : new Color(0.25f, 0.28f, 0.35f);
        go.GetComponent<Button>().onClick.AddListener(() => { state = !state; img.color = state ? new Color(0.35f, 0.7f, 1f) : new Color(0.25f, 0.28f, 0.35f); cb?.Invoke(state); });
        SetR(go, AML, AML, P(0, 0.5f), new(sz, sz), new(x, 0));
        var chk = NewGO("C", typeof(RectTransform), typeof(Image));
        chk.transform.SetParent(go.transform, false);
        chk.GetComponent<Image>().color = Color.white; chk.GetComponent<Image>().raycastTarget = false;
        SetR(chk, A(0.5f, 0.5f), A(0.5f, 0.5f), P(0.5f, 0.5f), new(sz * 0.35f, sz * 0.55f), Vector2.zero);
        return img;
    }

    void EnsureEventSystem()
    {
        if (FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }
    }
}
