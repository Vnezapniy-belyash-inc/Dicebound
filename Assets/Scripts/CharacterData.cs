using UnityEngine;

/// <summary>
/// Данные персонажа D&D 5e: характеристики, спас-броски, навыки, пассивные чувства, владения.
/// </summary>
public class CharacterData : MonoBehaviour
{
    [Header("Персонаж")]
    public string characterName = "Безымянный персонаж";
    public string className = "Волшебник";

    [Header("Уровень и опыт")]
    [Range(1, 20)] public int level = 1;
    public int currentXP = 0;

    static readonly int[] XpThresholds = {
        0, 300, 900, 2700, 6500, 14000, 23000, 34000, 48000, 64000,
        85000, 100000, 120000, 140000, 165000, 195000, 225000, 265000, 305000, 355000
    };

    public int maxXP => level < 20 ? XpThresholds[level] : 0;

    /// XP needed to reach current level (0 for level 1)
    public int xpForCurrentLevel => level > 1 ? XpThresholds[level - 1] : 0;

    /// Progress 0..1 within current level
    public float xpProgress => maxXP > 0 ? Mathf.Clamp01((float)(currentXP - xpForCurrentLevel) / (maxXP - xpForCurrentLevel)) : 0f;

    [Header("Боевые показатели")]
    [Range(0, 30)] public int armorClass = 12;
    public int currentHP = 10;
    public int maxHP = 10;

    [Header("Базовые характеристики")]
    [Range(1, 30)] public int strength = 10;
    [Range(1, 30)] public int dexterity = 10;
    [Range(1, 30)] public int constitution = 10;
    [Range(1, 30)] public int intelligence = 10;
    [Range(1, 30)] public int wisdom = 10;
    [Range(1, 30)] public int charisma = 10;

    [Header("Бонус мастерства")]
    [Range(2, 6)] public int proficiencyBonus = 2;

    [Header("Владение спас-бросками")]
    public bool strSaveProficient = false;
    public bool dexSaveProficient = false;
    public bool conSaveProficient = false;
    public bool intSaveProficient = false;
    public bool wisSaveProficient = false;
    public bool chaSaveProficient = false;

    [Header("Навыки")]
    public SkillEntry[] skills = new SkillEntry[]
    {
        // СИЛА
        new() { name = "Атлетика",            stat = Stat.STR },
        // ЛОВКОСТЬ
        new() { name = "Акробатика",          stat = Stat.DEX },
        new() { name = "Ловкость рук",        stat = Stat.DEX },
        new() { name = "Скрытность",          stat = Stat.DEX },
        // ИНТЕЛЛЕКТ
        new() { name = "Анализ",              stat = Stat.INT },
        new() { name = "История",             stat = Stat.INT },
        new() { name = "Магия",               stat = Stat.INT },
        new() { name = "Природа",             stat = Stat.INT },
        new() { name = "Религия",             stat = Stat.INT },
        // МУДРОСТЬ
        new() { name = "Восприятие",          stat = Stat.WIS },
        new() { name = "Выживание",           stat = Stat.WIS },
        new() { name = "Медицина",            stat = Stat.WIS },
        new() { name = "Проницательность",    stat = Stat.WIS },
        new() { name = "Уход за животными",   stat = Stat.WIS },
        // ХАРИЗМА
        new() { name = "Выступление",         stat = Stat.CHA },
        new() { name = "Запугивание",         stat = Stat.CHA },
        new() { name = "Обман",               stat = Stat.CHA },
        new() { name = "Убеждение",           stat = Stat.CHA },
    };

    [Header("Пассивные чувства")]
    public int passiveWisdomPerception  => 10 + WisMod + (IsSkillProfOrExp("Восприятие") ? proficiencyBonus : 0);
    public int passiveWisdomInsight     => 10 + WisMod + (IsSkillProfOrExp("Проницательность") ? proficiencyBonus : 0);
    public int passiveIntAnalysis       => 10 + IntMod + (IsSkillProfOrExp("Анализ") ? proficiencyBonus : 0);

    [Header("Прочие владения и языки")]
    [TextArea(2, 5)]
    public string otherProficiencies = "";

    [Header("Атаки и способности")]
    [TextArea(3, 10)]
    public string attacksAndSpells = "";
    [TextArea(3, 10)]
    public string featuresAndTraits = "";

    [Header("Доп. способности и черты")]
    [TextArea(3, 10)]
    public string extraAbilities = "";
    [TextArea(3, 10)]
    public string traits = "";

    [Header("Снаряжение")]
    [TextArea(3, 10)]
    public string equipment = "";
    [TextArea(3, 10)]
    public string treasure = "";

    [Header("Заметки")]
    [TextArea(2, 5)]
    public string note1 = "";
    [TextArea(2, 5)]
    public string note2 = "";
    [TextArea(2, 5)]
    public string note3 = "";
    [TextArea(2, 5)]
    public string note4 = "";
    [TextArea(2, 5)]
    public string note5 = "";
    [TextArea(2, 5)]
    public string note6 = "";

    bool IsSkillProfOrExp(string name)
    {
        foreach (var s in skills)
            if (s.name == name && (s.proficient || s.expertise)) return true;
        return false;
    }

    // ═══════ Типы ═══════

    public enum Stat { STR, DEX, CON, INT, WIS, CHA }

    [System.Serializable]
    public class SkillEntry
    {
        public string name;
        public Stat stat;
        public bool proficient;
        public bool expertise;
        // 0 = none, 1 = proficient, 2 = expertise
        public int ProfLevel
        {
            get => expertise ? 2 : (proficient ? 1 : 0);
            set { proficient = value >= 1; expertise = value >= 2; }
        }
    }

    // ═══════ Вычисляемые свойства ═══════

    public int StrMod => Modifier(strength);
    public int DexMod => Modifier(dexterity);
    public int ConMod => Modifier(constitution);
    public int IntMod => Modifier(intelligence);
    public int WisMod => Modifier(wisdom);
    public int ChaMod => Modifier(charisma);

    public int StrSave => StrMod + (strSaveProficient ? proficiencyBonus : 0);
    public int DexSave => DexMod + (dexSaveProficient ? proficiencyBonus : 0);
    public int ConSave => ConMod + (conSaveProficient ? proficiencyBonus : 0);
    public int IntSave => IntMod + (intSaveProficient ? proficiencyBonus : 0);
    public int WisSave => WisMod + (wisSaveProficient ? proficiencyBonus : 0);
    public int ChaSave => ChaMod + (chaSaveProficient ? proficiencyBonus : 0);

    public int GetStatMod(Stat s) => s switch
    {
        Stat.STR => StrMod, Stat.DEX => DexMod, Stat.CON => ConMod,
        Stat.INT => IntMod, Stat.WIS => WisMod, Stat.CHA => ChaMod,
        _ => 0,
    };

    public int GetSkillBonus(SkillEntry sk)
    {
        int m = GetStatMod(sk.stat);
        if (sk.expertise) return m + proficiencyBonus * 2;
        if (sk.proficient) return m + proficiencyBonus;
        return m;
    }

    public static int Modifier(int score) => Mathf.FloorToInt((score - 10f) / 2f);
    public static string ModString(int mod) => mod >= 0 ? $"+{mod}" : $"{mod}";
}
