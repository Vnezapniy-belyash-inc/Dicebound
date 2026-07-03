using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Чарник D&D в стиле референса: блоки статов, навыки с кружками, пассивные чувства.
/// Клавиша C.
/// </summary>
public class CharacterSheetUI : MonoBehaviour
{
    public CharacterData characterData;

    float PW = 740f;  // ширина панели
    float CW = 350f;  // ширина колонки
    float HDR = 36f;  // высота заголовка стата
    float SUB = 26f;  // высота подстроки проверка/спас
    float SKH = 26f;  // высота строки навыка
    float GAP = 6f;   // отступ между блоками
    float PD = 14f;   // общий паддинг
    float SBH = 90f;  // высота статус-бара

    Canvas _cv;
    GameObject _pn;
    CharacterData _cd;

    // Статы
    Text[] _scoreTxt = new Text[6], _checkTxt = new Text[6], _saveTxt = new Text[6];
    Image[] _saveTgl = new Image[6];

    // Навыки: текст-индикатор + бонус
    Text[] _skIndTxt = new Text[18], _skBonTxt = new Text[18];

    // Низ
    Text _profTxt, _pasPerTxt, _pasInsTxt, _pasInvTxt;

    // Статус-бар
    Text _nameTxt, _classTxt, _acTxt, _statusProfTxt, _hpTxt, _levelTxt;
    InputField _xpInput;
    Image _xpFill;
    RectTransform _hpRightMinusRt, _hpRightPlusRt, _hpTextRt;

    // Цвета статов (для акцентов)
    static readonly Color[] SC = {
        new(0.9f,0.35f,0.35f), new(0.35f,0.75f,0.9f), new(0.9f,0.7f,0.3f),
        new(0.4f,0.5f,0.95f),  new(0.5f,0.85f,0.5f),  new(0.85f,0.55f,0.9f),
    };

    static readonly string[] SN = { "СИЛА","ЛОВКОСТЬ","ТЕЛОСЛОЖЕНИЕ","ИНТЕЛЛЕКТ","МУДРОСТЬ","ХАРИЗМА" };

    static readonly int[][] SS = {
        new[]{0}, new[]{1,2,3}, new int[]{}, new[]{4,5,6,7,8}, new[]{9,10,11,12,13}, new[]{14,15,16,17},
    };

    void Start()
    {
        _cd = characterData ? characterData : FindAnyObjectByType<CharacterData>();
        BuildUI();
        if (_pn) _pn.SetActive(false);
    }

    void Update()
    {
        var k = Keyboard.current;
        if (k == null) return;
        if (k.cKey.wasPressedThisFrame && _pn)
        {
            _pn.SetActive(!_pn.activeSelf);
            if (_pn.activeSelf) RefreshDisplay();
        }
    }

    // ══════════════════════════════  Сборка  ══════════════════════════════

    void BuildUI() { try { B(); } catch (System.Exception e) { Debug.LogError($"Sheet: {e.Message}"); } }

    void B()
    {
        EvSys();
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (!_cd) _cd = FindAnyObjectByType<CharacterData>();
        if (!_cd) return;

        // Canvas
        var cgo = GO("CharCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(transform, false);
        _cv = cgo.GetComponent<Canvas>();
        _cv.renderMode = RenderMode.ScreenSpaceOverlay; _cv.sortingOrder = 15;
        var sc = cgo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;

        // Высота (правая колонка: DEX + WIS + пассивные + владения)
        float OTHER_H = 100f;
        float lh = BlockH(0) + BlockH(2) + BlockH(3) + BlockH(5) + 3*GAP;
        float rh = BlockH(1) + BlockH(4) + GAP + GAP + 88f + GAP + OTHER_H;
        float blocks = Mathf.Max(lh, rh);
        float th = SBH + PD + 30f + PD + blocks + PD;

        // Панель
        _pn = GO("Panel", typeof(RectTransform), typeof(Image));
        _pn.transform.SetParent(cgo.transform, false);
        _pn.GetComponent<Image>().color = new Color(0.05f, 0.07f, 0.11f, 0.96f); // тёмно-синий
        RS(_pn, AC, AC, PC, new(PW, th), new(60, -10));

        float cy = -PD;

        // ── Статус-бар ──
        cy = BuildStatusBar(cy, f);
        cy -= PD;

        // Заголовок
        var t1 = Txt(_pn.transform, "ХАРАКТЕРИСТИКИ", f, 20, FontStyle.Bold, new Color(0.92f,0.88f,0.65f));
        R(t1, AL, AL, PL, new(0,30f), new(PD,cy));
        cy -= 30f+PD;

        // Колонки
        float syl = cy;
        syl = Col(syl, PD, new[]{0,2,3,5}, f);

        // Правая колонка: ЛОВКОСТЬ, МУДРОСТЬ, ПАССИВНЫЕ ЧУВСТВА
        float syr = cy;
        float rx = PD+CW+PD;
        syr = Col(syr, rx, new[]{1}, f);
        syr = Col(syr, rx, new[]{4}, f);
        syr = BuildPassive(rx, syr, f);
        syr -= GAP;
        BuildOtherProf(rx, syr, OTHER_H, f);
    }

    float BuildStatusBar(float y, Font f)
    {
        float w = PW - PD*2;
        float r1 = 34f, r2 = 20f, r3 = 24f;

        // Фон
        var bg = GO("StatusBar", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(_pn.transform, false);
        bg.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.14f, 0.85f);
        R(bg, AL, AL, PL, new(w, SBH), new(PD, y));

        float cy = -4f;

        // ═══ РЯД 1: Имя, КБ, Мастерство, HP ═══
        var nameIF = MkInput(bg.transform, _cd.characterName, f, 18, FontStyle.Bold,
            Color.white, new(280,r1), AL, AL, PL, new(280,r1), new(0,cy),
            v => { if (_cd) _cd.characterName = v; });
        _nameTxt = nameIF.textComponent;

        float acX = 310f;
        // Кнопка − КБ
        MkBtn(bg.transform, "-", f, new Color(0.45f,0.18f,0.18f), acX, cy, 18, r1,
            () => { if(_cd)_cd.armorClass=Mathf.Clamp(_cd.armorClass-1,0,30); RefreshDisplay(); });
        // Число КБ
        var acT = Txt(bg.transform, _cd.armorClass.ToString(), f, 20, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        R(acT, AL, AL, PL, new(34, r1), new(acX+20, cy));
        _acTxt = acT.GetComponent<Text>();
        // Кнопка + КБ
        MkBtn(bg.transform, "+", f, new Color(0.18f,0.4f,0.22f), acX+56, cy, 18, r1,
            () => { if(_cd)_cd.armorClass=Mathf.Clamp(_cd.armorClass+1,0,30); RefreshDisplay(); });

        float prX = 420f;
        // Кнопка − Мастерство
        MkBtn(bg.transform, "-", f, new Color(0.55f,0.2f,0.15f), prX-2, cy, 16, r1,
            () => { if(_cd)_cd.proficiencyBonus=Mathf.Clamp(_cd.proficiencyBonus-1,2,6); RefreshDisplay(); });
        // Число
        var prT = Txt(bg.transform, $"+{_cd.proficiencyBonus}", f, 22, FontStyle.Bold,
            new Color(0.85f,0.8f,0.5f), TextAnchor.MiddleCenter);
        R(prT, AL, AL, PL, new(36, r1), new(prX+16, cy));
        _statusProfTxt = prT.GetComponent<Text>();
        // Кнопка + Мастерство
        MkBtn(bg.transform, "+", f, new Color(0.18f,0.4f,0.22f), prX+54, cy, 16, r1,
            () => { if(_cd)_cd.proficiencyBonus=Mathf.Clamp(_cd.proficiencyBonus+1,2,6); RefreshDisplay(); });

        float hpX = 530f;
        // Слева: текущее HP [− сверху, + снизу]
        MkBtn(bg.transform, "-", f, new Color(0.6f,0.22f,0.22f), hpX, cy-17, 18, 17,
            () => { if(_cd)_cd.currentHP=Mathf.Clamp(_cd.currentHP-1,0,_cd.maxHP); RefreshDisplay(); });
        MkBtn(bg.transform, "+", f, new Color(0.25f,0.55f,0.28f), hpX, cy, 18, 17,
            () => { if(_cd)_cd.currentHP=Mathf.Clamp(_cd.currentHP+1,0,_cd.maxHP); RefreshDisplay(); });
        // Текст HP (ширина с запасом на 3-значные числа)
        var hpT = Txt(bg.transform, $"{_cd.currentHP}/{_cd.maxHP}", f, 20, FontStyle.Bold,
            new Color(0.95f,0.5f,0.5f), TextAnchor.MiddleCenter);
        R(hpT, AL, AL, PL, new(80, r1), new(hpX+20, cy));
        _hpTxt = hpT.GetComponent<Text>();
        _hpTextRt = hpT.GetComponent<RectTransform>();
        // Справа: макс HP [− сверху, + снизу]
        var hpRM = MkBtn(bg.transform, "-", f, new Color(0.5f,0.2f,0.2f), hpX+500, cy-17, 18, 17,
            () => { if(_cd)_cd.maxHP=Mathf.Max(1,_cd.maxHP-1); if(_cd.currentHP>_cd.maxHP)_cd.currentHP=_cd.maxHP; RefreshDisplay(); });
        _hpRightMinusRt = hpRM.GetComponent<RectTransform>();
        var hpRP = MkBtn(bg.transform, "+", f, new Color(0.2f,0.45f,0.25f), hpX+500, cy, 18, 17,
            () => { if(_cd)_cd.maxHP=_cd.maxHP+1; RefreshDisplay(); });
        _hpRightPlusRt = hpRP.GetComponent<RectTransform>();

        cy -= r1 + 2f;

        // ═══ РЯД 2: Класс, подписи ═══
        var classIF = MkInput(bg.transform, _cd.className, f, 14, FontStyle.Italic,
            new Color(0.65f,0.7f,0.8f), new(200,r2), AL, AL, PL, new(200,r2), new(0,cy),
            v => { if (_cd) _cd.className = v; });
        _classTxt = classIF.textComponent;

        var acLbl = Txt(bg.transform, "КБ", f, 11, FontStyle.Normal, new Color(0.5f,0.55f,0.65f), TextAnchor.MiddleCenter);
        R(acLbl, AL, AL, PL, new(54, r2), new(acX+10, cy));
        var prLbl = Txt(bg.transform, "МАСТ", f, 11, FontStyle.Normal, new Color(0.5f,0.55f,0.65f), TextAnchor.MiddleCenter);
        R(prLbl, AL, AL, PL, new(50, r2), new(prX, cy));
        var hpLbl = Txt(bg.transform, "♥ ХП", f, 11, FontStyle.Normal, new Color(0.5f,0.55f,0.65f), TextAnchor.MiddleCenter);
        R(hpLbl, AL, AL, PL, new(60, r2), new(hpX+18, cy));

        cy -= r2 + 2f;

        // ═══ РЯД 3: Уровень + XP ═══
        var lvBg = GO("LV", typeof(RectTransform), typeof(Image));
        lvBg.transform.SetParent(bg.transform, false);
        lvBg.GetComponent<Image>().color = new Color(0.25f, 0.2f, 0.5f);
        R(lvBg, AL, AL, PL, new(110, r3), new(0, cy));
        var lvT = Txt(lvBg.transform, $"{_cd.level} УРОВЕНЬ", f, 13, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        R(lvT, AC, AC, PC, new(110, r3), Vector2.zero);
        _levelTxt = lvT.GetComponent<Text>();
        MkBtn(bg.transform, "-", f, new Color(0.4f,0.15f,0.25f), 114, cy, 16, r3,
            () => { if(_cd)_cd.level=Mathf.Clamp(_cd.level-1,1,20); RefreshDisplay(); });
        MkBtn(bg.transform, "+", f, new Color(0.18f,0.4f,0.22f), 132, cy, 16, r3,
            () => {
                if (!_cd) return;
                _cd.level = Mathf.Clamp(_cd.level+1, 1, 20);
                if (_cd.currentXP >= _cd.maxXP) _cd.currentXP -= _cd.maxXP;
                else _cd.currentXP = 0;
                RefreshDisplay();
            });

        // XP-шкала
        float xpX = 160f, xpW = 180f;
        var xpBg = GO("XPBg", typeof(RectTransform), typeof(Image));
        xpBg.transform.SetParent(bg.transform, false);
        xpBg.GetComponent<Image>().color = new Color(0.08f, 0.1f, 0.18f);
        R(xpBg, AL, AL, PL, new(xpW, r3-6), new(xpX, cy));

        float frac = _cd.maxXP > 0 ? Mathf.Clamp01((float)_cd.currentXP / _cd.maxXP) : 0f;
        var xpFill = GO("XPFill", typeof(RectTransform), typeof(Image));
        xpFill.transform.SetParent(xpBg.transform, false);
        xpFill.GetComponent<Image>().color = new Color(0.45f, 0.3f, 0.8f);
        R(xpFill, AL, AL, PL, new(xpW * frac, r3-6), Vector2.zero);
        _xpFill = xpFill.GetComponent<Image>();

        // XP текущее — InputField только число
        var xpIF = MkInput(bg.transform, _cd.currentXP.ToString(), f, 12, FontStyle.Normal,
            new Color(0.7f,0.7f,0.8f), new(44,r3), AL, AL, PL, new(44, r3), new(xpX+xpW+4, cy),
            v => {
                if (!_cd) return;
                if (int.TryParse(v.Trim(), out int xp))
                    _cd.currentXP = Mathf.Max(0, xp);
                RefreshDisplay();
            });
        _xpInput = xpIF;

        // XP макс — статика
        var xpMaxT = Txt(bg.transform, $"/{_cd.maxXP}", f, 12, FontStyle.Normal,
            new Color(0.5f,0.55f,0.65f), TextAnchor.MiddleLeft);
        R(xpMaxT, AL, AL, PL, new(50, r3), new(xpX+xpW+48, cy));

        // Следующий уровень
        var nextT = Txt(bg.transform, $"{_cd.level+1}", f, 13, FontStyle.Bold,
            new Color(0.5f,0.55f,0.7f), TextAnchor.MiddleCenter);
        R(nextT, AL, AL, PL, new(30, r3), new(xpX+xpW+100, cy));

        return y - SBH;
    }

    float Col(float cy, float cx, int[] stats, Font f)
    {
        foreach (int si in stats) { BuildBlock(cx, cy, si, f); cy -= BlockH(si)+GAP; }
        return cy;
    }

    float BlockH(int si) => HDR + SUB + SS[si].Length * SKH;

    void BuildBlock(float cx, float y, int si, Font f)
    {
        float w = CW;
        var cd = _cd;

        // ── Заголовок ──
        var hg = GO("H", typeof(RectTransform), typeof(Image));
        hg.transform.SetParent(_pn.transform, false);
        hg.GetComponent<Image>().color = new Color(0.07f, 0.09f, 0.16f, 0.9f);
        R(hg, AL, AL, PL, new(w, HDR), new(cx, y));

        var nm = Txt(hg.transform, SN[si], f, 17, FontStyle.Bold, Color.white);
        R(nm, AML, AML, PML, new(200, HDR), new(10, 0));

        // Бокс со счётом
        var sb = GO("SB", typeof(RectTransform), typeof(Image));
        sb.transform.SetParent(hg.transform, false);
        sb.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.22f);
        R(sb, AML, AML, PML, new(44, 24), new(w-100, 0));

        int score = StatScore(si);
        var st = Txt(sb.transform, score.ToString(), f, 16, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        R(st, AC, AC, PC, new(44, 24), Vector2.zero);
        _scoreTxt[si] = st.GetComponent<Text>();

        // ± кнопки
        Btn(hg.transform, "-", f, new Color(0.55f,0.2f,0.2f), w-54, 16, 18, () => ChStat(si,-1));
        Btn(hg.transform, "+", f, new Color(0.2f,0.5f,0.25f), w-22, 16, 18, () => ChStat(si,+1));

        // ── Подстрока ──
        float sy = y - HDR;
        var sg = GO("S", typeof(RectTransform), typeof(Image));
        sg.transform.SetParent(_pn.transform, false);
        sg.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.14f, 0.7f);
        R(sg, AL, AL, PL, new(w, SUB), new(cx, sy));

        // ПРОВЕРКА
        var cl = Txt(sg.transform, "ПРОВЕРКА", f, 11, FontStyle.Normal, new Color(0.5f,0.55f,0.65f));
        R(cl, AML, AML, PML, new(65, SUB), new(10, 0));
        // Бокс значения
        var cb = GO("CB", typeof(RectTransform), typeof(Image));
        cb.transform.SetParent(sg.transform, false);
        cb.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.22f);
        R(cb, AML, AML, PML, new(34, 20), new(76, 0));
        int mod = StatMod(si);
        var cv = Txt(cb.transform, ModS(mod), f, 14, FontStyle.Bold, ModC(mod), TextAnchor.MiddleCenter);
        R(cv, AC, AC, PC, new(34, 20), Vector2.zero);
        _checkTxt[si] = cv.GetComponent<Text>();

        // СПАСБРОСОК
        var sl = Txt(sg.transform, "СПАСБРОСОК", f, 11, FontStyle.Normal, new Color(0.5f,0.55f,0.65f));
        R(sl, AML, AML, PML, new(90, SUB), new(w/2-4, 0));
        var svb = GO("SVB", typeof(RectTransform), typeof(Image));
        svb.transform.SetParent(sg.transform, false);
        svb.GetComponent<Image>().color = new Color(0.12f, 0.14f, 0.22f);
        R(svb, AML, AML, PML, new(34, 20), new(w/2+86, 0));
        int save = SaveVal(si);
        var sv = Txt(svb.transform, ModS(save), f, 14, FontStyle.Bold, ModC(save), TextAnchor.MiddleCenter);
        R(sv, AC, AC, PC, new(34, 20), Vector2.zero);
        _saveTxt[si] = sv.GetComponent<Text>();

        // Тоггл владения спасом (белый контур)
        _saveTgl[si] = TglOutline(sg.transform, w-28, 16, SaveProf(si), v => SetSaveProf(si, v));

        // ── Навыки ──
        for (int i = 0; i < SS[si].Length; i++)
        {
            int ki = SS[si][i];
            float ry = sy - SUB - i*SKH;

            var kg = GO("K", typeof(RectTransform), typeof(Image));
            kg.transform.SetParent(_pn.transform, false);
            kg.GetComponent<Image>().color = new Color(0.06f, 0.09f, 0.18f, 0.7f); // тёмно-синий
            R(kg, AL, AL, PL, new(w, SKH), new(cx, ry));

            // Кружок-индикатор владения
            int lv = cd.skills[ki].ProfLevel;
            string ic = lv == 2 ? "★" : (lv == 1 ? "●" : "○");
            Color icc = lv == 2 ? new Color(0.9f,0.75f,0.2f) : (lv==1 ? new Color(0.35f,0.65f,1f) : new Color(0.3f,0.35f,0.45f));
            var it = Txt(kg.transform, ic, f, 16, FontStyle.Normal, icc, TextAnchor.MiddleCenter);
            R(it, AML, AML, PML, new(22, SKH), new(8, 0));
            _skIndTxt[ki] = it.GetComponent<Text>();

            // Невидимая кнопка поверх кружка
            var ib = GO("IB", typeof(RectTransform), typeof(Image), typeof(Button));
            ib.transform.SetParent(kg.transform, false);
            ib.GetComponent<Image>().color = new Color(0,0,0,0);
            int cap = ki; ib.GetComponent<Button>().onClick.AddListener(() => CycleProf(cap));
            R(ib, AML, AML, PML, new(26, SKH), new(6, 0));

            // Название навыка
            var nt = Txt(kg.transform, cd.skills[ki].name, f, 13, FontStyle.Normal, new Color(0.85f,0.88f,0.95f));
            R(nt, AML, AML, PML, new(w-80, SKH), new(36, 0));

            // Бонус
            int bn = cd.GetSkillBonus(cd.skills[ki]);
            var bt = Txt(kg.transform, ModS(bn), f, 14, FontStyle.Bold, ModC(bn), TextAnchor.MiddleCenter);
            R(bt, AML, AML, PML, new(32, SKH), new(w-40, 0));
            _skBonTxt[ki] = bt.GetComponent<Text>();
        }
    }

    float BuildPassive(float cx, float y, Font f)
    {
        float w = CW;
        float h = 20f;

        // Заголовок
        var hdr = Txt(_pn.transform, "ПАССИВНЫЕ ЧУВСТВА", f, 14, FontStyle.Bold, new Color(0.55f,0.6f,0.7f));
        R(hdr, AL, AL, PL, new(w, 22f), new(cx, y));
        y -= 22f;

        string[] lbs = { "МУДРОСТЬ (ВОСПРИЯТИЕ)", "МУДРОСТЬ (ПРОНИЦАТЕЛЬНОСТЬ)", "ИНТЕЛЛЕКТ (АНАЛИЗ)" };
        for (int i = 0; i < 3; i++)
        {
            float ry = y - i*22f;
            var bg = GO("P", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(_pn.transform, false);
            bg.GetComponent<Image>().color = new Color(0.07f, 0.09f, 0.16f, 0.6f);
            R(bg, AL, AL, PL, new(w, h), new(cx, ry));

            var lb = Txt(bg.transform, lbs[i], f, 11, FontStyle.Normal, new Color(0.6f,0.65f,0.73f));
            R(lb, AML, AML, PML, new(w-50, h), new(8, 0));

            var vb = GO("V", typeof(RectTransform), typeof(Image));
            vb.transform.SetParent(bg.transform, false);
            vb.GetComponent<Image>().color = new Color(0.11f, 0.13f, 0.21f);
            R(vb, AML, AML, PML, new(34, 16), new(w-42, 0));
            var vl = Txt(vb.transform, "10", f, 13, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
            R(vl, AC, AC, PC, new(34, 16), Vector2.zero);

            if (i==0) _pasPerTxt = vl.GetComponent<Text>();
            if (i==1) _pasInsTxt = vl.GetComponent<Text>();
            if (i==2) _pasInvTxt = vl.GetComponent<Text>();
        }
        return y - 3*22f; // низ последней строки
    }

    void BuildOtherProf(float cx, float y, float maxH, Font f)
    {
        float w = CW;
        float hdrH = 22f;
        float fieldH = maxH - hdrH - 4f;

        // Заголовок
        var hdr = Txt(_pn.transform, "ПРОЧИЕ ВЛАДЕНИЯ И ЯЗЫКИ", f, 14, FontStyle.Bold, new Color(0.55f,0.6f,0.7f));
        R(hdr, AL, AL, PL, new(w, hdrH), new(cx, y));
        y -= hdrH;

        // Фон поля ввода
        var bg = GO("OtherProf", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(_pn.transform, false);
        bg.GetComponent<Image>().color = new Color(0.07f, 0.09f, 0.16f, 0.8f);
        R(bg, AL, AL, PL, new(w, fieldH), new(cx, y));

        // Текст (отображаемый)
        var textGO = GO("Text", typeof(RectTransform), typeof(Text));
        textGO.transform.SetParent(bg.transform, false);
        var textComp = textGO.GetComponent<Text>();
        textComp.font = f;
        textComp.fontSize = 12;
        textComp.color = new Color(0.85f, 0.88f, 0.95f);
        textComp.alignment = TextAnchor.UpperLeft;
        textComp.supportRichText = false;
        R(textGO, new(0,1), new(1,1), new(0,1), new(-8, fieldH-8), new(4,-4));

        // Placeholder
        var phGO = GO("Placeholder", typeof(RectTransform), typeof(Text));
        phGO.transform.SetParent(bg.transform, false);
        var phComp = phGO.GetComponent<Text>();
        phComp.text = "Владения, инструменты, языки...";
        phComp.font = f;
        phComp.fontSize = 12;
        phComp.fontStyle = FontStyle.Italic;
        phComp.color = new Color(0.35f, 0.4f, 0.5f);
        phComp.alignment = TextAnchor.UpperLeft;
        phComp.raycastTarget = false;
        R(phGO, new(0,1), new(1,1), new(0,1), new(-8, fieldH-8), new(4,-4));

        // InputField
        var ifComp = bg.AddComponent<InputField>();
        ifComp.textComponent = textComp;
        ifComp.placeholder = phComp;
        ifComp.lineType = InputField.LineType.MultiLineNewline;
        ifComp.text = _cd ? _cd.otherProficiencies : "";
        ifComp.onValueChanged.AddListener(v => { if (_cd) _cd.otherProficiencies = v; });
    }

    // ══════════════════════════════  Данные  ══════════════════════════════

    int StatScore(int si) => si switch {0=>_cd.strength,1=>_cd.dexterity,2=>_cd.constitution,3=>_cd.intelligence,4=>_cd.wisdom,5=>_cd.charisma,_=>0};
    int StatMod(int si) => si switch {0=>_cd.StrMod,1=>_cd.DexMod,2=>_cd.ConMod,3=>_cd.IntMod,4=>_cd.WisMod,5=>_cd.ChaMod,_=>0};
    int SaveVal(int si) => si switch {0=>_cd.StrSave,1=>_cd.DexSave,2=>_cd.ConSave,3=>_cd.IntSave,4=>_cd.WisSave,5=>_cd.ChaSave,_=>0};
    bool SaveProf(int si) => si switch {0=>_cd.strSaveProficient,1=>_cd.dexSaveProficient,2=>_cd.conSaveProficient,3=>_cd.intSaveProficient,4=>_cd.wisSaveProficient,5=>_cd.chaSaveProficient,_=>false};

    void SetScore(int si, int v) { switch(si) {case 0:_cd.strength=v;break;case 1:_cd.dexterity=v;break;case 2:_cd.constitution=v;break;case 3:_cd.intelligence=v;break;case 4:_cd.wisdom=v;break;case 5:_cd.charisma=v;break;} }
    void SetSaveProf(int si, bool v) { switch(si) {case 0:_cd.strSaveProficient=v;break;case 1:_cd.dexSaveProficient=v;break;case 2:_cd.conSaveProficient=v;break;case 3:_cd.intSaveProficient=v;break;case 4:_cd.wisSaveProficient=v;break;case 5:_cd.chaSaveProficient=v;break;} RefreshDisplay(); }

    void ChStat(int si, int d) { SetScore(si, Mathf.Clamp(StatScore(si)+d,1,30)); RefreshDisplay(); }
    void CycleProf(int ki) { if (_cd && ki < _cd.skills.Length) { var s = _cd.skills[ki]; s.ProfLevel = (s.ProfLevel+1)%3; RefreshDisplay(); } }

    public void RefreshDisplay()
    {
        if (!_cd) { _cd = FindAnyObjectByType<CharacterData>(); if (!_cd) return; }

        for (int si=0; si<6; si++)
        {
            if (_scoreTxt[si]) _scoreTxt[si].text = StatScore(si).ToString();
            int m = StatMod(si);
            if (_checkTxt[si]) { _checkTxt[si].text = ModS(m); _checkTxt[si].color = ModC(m); }
            int sv = SaveVal(si);
            if (_saveTxt[si]) { _saveTxt[si].text = ModS(sv); _saveTxt[si].color = ModC(sv); }
            if (_saveTgl[si]) _saveTgl[si].color = SaveProf(si) ? new Color(0.35f,0.7f,1f) : new Color(0.18f,0.2f,0.28f);
        }

        for (int ki=0; ki<_cd.skills.Length && ki<18; ki++)
        {
            int lv = _cd.skills[ki].ProfLevel;
            if (_skIndTxt[ki])
            {
                _skIndTxt[ki].text = lv==2?"★":(lv==1?"●":"○");
                _skIndTxt[ki].color = lv==2?new Color(0.9f,0.75f,0.2f):(lv==1?new Color(0.35f,0.65f,1f):new Color(0.3f,0.35f,0.45f));
            }
            int bn = _cd.GetSkillBonus(_cd.skills[ki]);
            if (_skBonTxt[ki]) { _skBonTxt[ki].text = ModS(bn); _skBonTxt[ki].color = ModC(bn); }
        }

        if (_profTxt) _profTxt.text = $"Бонус мастерства: +{_cd.proficiencyBonus}";
        if (_pasPerTxt) _pasPerTxt.text = _cd.passiveWisdomPerception.ToString();
        if (_pasInsTxt) _pasInsTxt.text = _cd.passiveWisdomInsight.ToString();
        if (_pasInvTxt) _pasInvTxt.text = _cd.passiveIntAnalysis.ToString();

        // Статус-бар
        if (_nameTxt) _nameTxt.text = _cd.characterName;
        if (_classTxt) _classTxt.text = _cd.className;
        if (_acTxt) _acTxt.text = _cd.armorClass.ToString();
        if (_statusProfTxt) _statusProfTxt.text = $"+{_cd.proficiencyBonus}";
        if (_hpTxt) { _hpTxt.text = $"{_cd.currentHP}/{_cd.maxHP}"; RepositionHpButtons(); }
        if (_levelTxt) _levelTxt.text = $"{_cd.level} УРОВЕНЬ";
        if (_xpInput) { _xpInput.text = _cd.currentXP.ToString(); _xpInput.textComponent.text = _cd.currentXP.ToString(); }
        if (_xpFill)
        {
            float frac = _cd.maxXP > 0 ? Mathf.Clamp01((float)_cd.currentXP / _cd.maxXP) : 0f;
            _xpFill.rectTransform.sizeDelta = new Vector2(200f * frac, _xpFill.rectTransform.sizeDelta.y);
        }
    }

    void RepositionHpButtons()
    {
        if (!_hpTextRt || !_hpRightMinusRt || !_hpRightPlusRt || !_hpTxt) return;
        float pw = _hpTxt.preferredWidth;
        float rightX = _hpTextRt.anchoredPosition.x + pw + 30f;
        float baseY = _hpTextRt.anchoredPosition.y;
        _hpRightMinusRt.anchoredPosition = new Vector2(rightX, baseY - 17f);
        _hpRightPlusRt.anchoredPosition = new Vector2(rightX, baseY);
    }

    // ══════════════════════════════  Хелперы  ══════════════════════════════

    static string ModS(int m) => m>=0?$"+{m}":$"{m}";
    static Color ModC(int m) => m>=0?new Color(0.5f,0.9f,0.5f):new Color(0.9f,0.4f,0.4f);
    static Vector2 A(float x,float y)=>new(x,y);
    static Vector2 P(float x,float y)=>new(x,y);

    static readonly Vector2 AL=A(0,1),AR=A(1,1),AC=A(0.5f,0.5f),AML=A(0,0.5f);
    static readonly Vector2 PL=P(0,1),PR=P(1,1),PC=P(0.5f,0.5f),PML=P(0,0.5f);

    void R(GameObject go, Vector2 am, Vector2 aM, Vector2 pv, Vector2 sz, Vector2 ps)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin=am; rt.anchorMax=aM; rt.pivot=pv; rt.sizeDelta=sz; rt.anchoredPosition=ps;
    }

    void RS(GameObject go, Vector2 am, Vector2 aM, Vector2 pv, Vector2 sz, Vector2 ps) => R(go,am,aM,pv,sz,ps);

    GameObject GO(string n, params System.Type[] ts) { var g = new GameObject(n, ts); return g; }

    GameObject Txt(Transform p, string t, Font f, float sz, FontStyle st, Color c, TextAnchor a = TextAnchor.MiddleLeft)
    {
        var g = GO("T", typeof(RectTransform), typeof(Text));
        g.transform.SetParent(p, false);
        var tx = g.GetComponent<Text>();
        tx.text=t; tx.font=f; tx.fontSize=(int)sz; tx.fontStyle=st; tx.color=c; tx.alignment=a; tx.raycastTarget=false;
        return g;
    }

    void Btn(Transform p, string l, Font f, Color bg, float x, float w, float h, UnityEngine.Events.UnityAction cb)
    {
        var g = GO("B", typeof(RectTransform), typeof(Image), typeof(Button));
        g.transform.SetParent(p, false);
        g.GetComponent<Image>().color = bg;
        g.GetComponent<Button>().onClick.AddListener(cb);
        R(g, AML, AML, PML, new(w,h), new(x,0));
        var lb = Txt(g.transform, l, f, 11, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        R(lb, AC, AC, PC, new(w,h), Vector2.zero);
    }

    Image TglOutline(Transform p, float x, float sz, bool on, UnityEngine.Events.UnityAction<bool> cb)
    {
        var g = GO("Tgl", typeof(RectTransform), typeof(Image), typeof(Button));
        g.transform.SetParent(p, false);
        var img = g.GetComponent<Image>();
        bool st = on;
        img.color = st ? new Color(0.35f,0.7f,1f) : new Color(0.18f,0.2f,0.28f);
        g.GetComponent<Button>().onClick.AddListener(()=>{st=!st;img.color=st?new Color(0.35f,0.7f,1f):new Color(0.18f,0.2f,0.28f);cb?.Invoke(st);});
        R(g, AML, AML, PML, new(sz,sz), new(x,0));
        // Белая галочка
        var ck = GO("C", typeof(RectTransform), typeof(Image));
        ck.transform.SetParent(g.transform, false);
        ck.GetComponent<Image>().color = Color.white; ck.GetComponent<Image>().raycastTarget = false;
        R(ck, AC, AC, PC, new(sz*0.3f, sz*0.5f), Vector2.zero);
        return img;
    }

    // Кнопка с якорем AL/PL (для статус-бара)
    GameObject MkBtn(Transform p, string l, Font f, Color bg, float x, float y, float w, float h, UnityEngine.Events.UnityAction cb)
    {
        var g = GO("B", typeof(RectTransform), typeof(Image), typeof(Button));
        g.transform.SetParent(p, false);
        g.GetComponent<Image>().color = bg;
        g.GetComponent<Button>().onClick.AddListener(cb);
        R(g, AL, AL, PL, new(w,h), new(x,y));
        var lb = Txt(g.transform, l, f, 11, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);
        R(lb, AC, AC, PC, new(w,h), Vector2.zero);
        return g;
    }

    // InputField
    InputField MkInput(Transform p, string text, Font f, float sz, FontStyle st, Color c,
        Vector2 sz2, Vector2 aMin, Vector2 aMax, Vector2 pv, Vector2 size, Vector2 pos,
        UnityEngine.Events.UnityAction<string> cb)
    {
        var g = GO("Inp", typeof(RectTransform), typeof(Image), typeof(InputField));
        g.transform.SetParent(p, false);
        g.GetComponent<Image>().color = new Color(0.08f,0.1f,0.18f,0.9f);
        R(g, aMin, aMax, pv, size, pos);

        // Text child — stretch fill
        var tGO = GO("T", typeof(RectTransform), typeof(Text));
        tGO.transform.SetParent(g.transform, false);
        var tc = tGO.GetComponent<Text>();
        tc.text = text; tc.font = f; tc.fontSize = (int)sz; tc.fontStyle = st;
        tc.color = c; tc.alignment = TextAnchor.MiddleLeft; tc.supportRichText = false;
        var trt = tGO.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(4, 2); trt.offsetMax = new Vector2(-4, -2);

        // Placeholder — stretch fill
        var phGO = GO("PH", typeof(RectTransform), typeof(Text));
        phGO.transform.SetParent(g.transform, false);
        var phc = phGO.GetComponent<Text>();
        phc.text = "..."; phc.font = f; phc.fontSize = (int)sz; phc.fontStyle = FontStyle.Italic;
        phc.color = new Color(0.35f,0.4f,0.5f); phc.alignment = TextAnchor.MiddleLeft; phc.raycastTarget = false;
        var prt = phGO.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(4, 2); prt.offsetMax = new Vector2(-4, -2);

        var ifc = g.GetComponent<InputField>();
        ifc.textComponent = tc; ifc.placeholder = phc;
        ifc.lineType = InputField.LineType.SingleLine;
        ifc.text = text;
        ifc.onValueChanged.AddListener(cb);
        return ifc;
    }

    void EvSys()
    {
        var es = FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (!es)
        {
            var g = new GameObject("EventSystem");
            g.AddComponent<UnityEngine.EventSystems.EventSystem>();
            g.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            g.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
        else
        {
            if (!es.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>())
                es.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (!es.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>())
                es.gameObject.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
    }
}
