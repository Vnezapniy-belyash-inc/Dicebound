using UnityEngine;
using System.Text;
using System.IO;

/// <summary>
/// Конвертер между CharacterData и JSON-форматом longstoryshort.app
/// </summary>
public static class LssJsonConverter
{
    // ═══════════════════ ЭКСПОРТ ═══════════════════

    public static string ExportToLss(CharacterData cd)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");

        // data field
        sb.Append("\"data\":");
        sb.Append(EscapeJson(ExportData(cd)));
        sb.AppendLine(",");

        // fixed fields
        sb.AppendLine("\"edition\":\"2024\",");
        sb.AppendLine("\"linkAccess\":\"none\",");
        sb.AppendLine("\"jsonType\":\"character\",");
        sb.AppendLine("\"version\":\"2\",");
        sb.AppendLine("\"tags\":[],");
        sb.AppendLine("\"rooms\":[],");

        // spells (minimal)
        sb.AppendLine("\"spells\":{\"mode\":\"cards\",\"prepared\":[],\"book\":[],\"edition\":\"2024\"},");

        // disabled blocks
        sb.AppendLine("\"disabledBlocks\":{\"info-left\":[],\"info-right\":[],\"subinfo-left\":[],\"subinfo-right\":[],\"notes-left\":[],\"notes-right\":[],\"_id\":\"000000000000000000000000\"},");

        sb.AppendLine("\"lastWriterSessionId\":\"0\"");
        sb.AppendLine("}");

        return sb.ToString();
    }

    static string ExportData(CharacterData cd)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");

        sb.AppendLine("\"jsonType\":\"character\",");
        sb.AppendLine("\"template\":\"default\",");

        // name
        sb.Append("\"name\":{\"value\":");
        sb.Append(EscapeJsonValue(cd.characterName));
        sb.AppendLine("},");

        // ═══ info ═══
        sb.AppendLine("\"info\":{");
        sb.Append($"\"charClass\":{{\"name\":\"charClass\",\"value\":{EscapeJsonValue(cd.className)}}},");
        sb.AppendLine("\"charSubclass\":{\"name\":\"charSubclass\",\"value\":\"\"},");
        sb.Append($"\"level\":{{\"name\":\"level\",\"value\":{cd.level}}},");
        sb.AppendLine("\"background\":{\"name\":\"background\",\"value\":\"\"},");
        sb.AppendLine("\"playerName\":{\"name\":\"playerName\",\"value\":\"\"},");
        sb.AppendLine("\"race\":{\"name\":\"race\",\"value\":\"\"},");
        sb.AppendLine("\"alignment\":{\"name\":\"alignment\",\"value\":\"\"},");
        sb.Append($"\"experience\":{{\"name\":\"experience\",\"value\":{cd.currentXP}}}");
        sb.AppendLine("},");

        // ═══ subInfo (default empty) ═══
        sb.AppendLine("\"subInfo\":{\"age\":{\"name\":\"age\",\"value\":\"\"},\"height\":{\"name\":\"height\",\"value\":\"\"},\"weight\":{\"name\":\"weight\",\"value\":\"\"},\"eyes\":{\"name\":\"eyes\",\"value\":\"\"},\"skin\":{\"name\":\"skin\",\"value\":\"\"},\"hair\":{\"name\":\"hair\",\"value\":\"\"}},");

        // ═══ spellsInfo ═══
        sb.AppendLine("\"spellsInfo\":{\"base\":{\"name\":\"base\",\"value\":\"\"},\"save\":{\"name\":\"save\",\"value\":\"\"},\"mod\":{\"name\":\"mod\",\"value\":\"\"},\"available\":{\"classes\":[\"sorcerer\"]}},");
        sb.AppendLine("\"spells\":{\"slots-1\":{\"value\":1}},");
        sb.AppendLine("\"spellsPact\":{},");
        sb.AppendLine("\"bonuses\":[],");

        // ═══ proficiency ═══
        sb.Append($"\"proficiency\":{cd.proficiencyBonus},");

        // ═══ stats ═══
        sb.AppendLine("\"stats\":{");
        sb.Append($"\"str\":{{\"name\":\"str\",\"score\":{cd.strength}}},");
        sb.Append($"\"dex\":{{\"name\":\"dex\",\"score\":{cd.dexterity}}},");
        sb.Append($"\"con\":{{\"name\":\"con\",\"score\":{cd.constitution}}},");
        sb.Append($"\"int\":{{\"name\":\"int\",\"score\":{cd.intelligence}}},");
        sb.Append($"\"wis\":{{\"name\":\"wis\",\"score\":{cd.wisdom}}},");
        sb.Append($"\"cha\":{{\"name\":\"cha\",\"score\":{cd.charisma}}}");
        sb.AppendLine("},");

        // ═══ saves ═══
        sb.AppendLine("\"saves\":{");
        sb.Append($"\"str\":{{\"name\":\"str\",\"isProf\":{Bool(cd.strSaveProficient)}}},");
        sb.Append($"\"dex\":{{\"name\":\"dex\",\"isProf\":{Bool(cd.dexSaveProficient)}}},");
        sb.Append($"\"con\":{{\"name\":\"con\",\"isProf\":{Bool(cd.conSaveProficient)}}},");
        sb.Append($"\"int\":{{\"name\":\"int\",\"isProf\":{Bool(cd.intSaveProficient)}}},");
        sb.Append($"\"wis\":{{\"name\":\"wis\",\"isProf\":{Bool(cd.wisSaveProficient)}}},");
        sb.Append($"\"cha\":{{\"name\":\"cha\",\"isProf\":{Bool(cd.chaSaveProficient)}}}");
        sb.AppendLine("},");

        // ═══ skills ═══
        sb.AppendLine("\"skills\":{");
        // LSS-порядок → индекс в cd.skills
        WriteLssSkill(sb, cd.skills[1],  "acrobatics",      "dex"); // 1=Акробатика
        WriteLssSkill(sb, cd.skills[4],  "investigation",    "int"); // 4=Анализ
        WriteLssSkill(sb, cd.skills[0],  "athletics",        "str"); // 0=Атлетика
        WriteLssSkill(sb, cd.skills[9],  "perception",       "wis"); // 9=Восприятие
        WriteLssSkill(sb, cd.skills[10], "survival",         "wis"); // 10=Выживание
        WriteLssSkill(sb, cd.skills[14], "performance",      "cha"); // 14=Выступление
        WriteLssSkill(sb, cd.skills[15], "intimidation",     "cha"); // 15=Запугивание
        WriteLssSkill(sb, cd.skills[5],  "history",          "int"); // 5=История
        WriteLssSkill(sb, cd.skills[2],  "sleight of hand",  "dex"); // 2=Ловкость рук
        WriteLssSkill(sb, cd.skills[6],  "arcana",           "int"); // 6=Магия
        WriteLssSkill(sb, cd.skills[11], "medicine",         "wis"); // 11=Медицина
        WriteLssSkill(sb, cd.skills[16], "deception",        "cha"); // 16=Обман
        WriteLssSkill(sb, cd.skills[7],  "nature",           "int"); // 7=Природа
        WriteLssSkill(sb, cd.skills[12], "insight",          "wis"); // 12=Проницательность
        WriteLssSkill(sb, cd.skills[8],  "religion",         "int"); // 8=Религия
        WriteLssSkill(sb, cd.skills[3],  "stealth",          "dex"); // 3=Скрытность
        WriteLssSkill(sb, cd.skills[17], "persuasion",       "cha"); // 17=Убеждение
        WriteLssSkillLast(sb, cd.skills[13], "animal handling", "wis"); // 13=Уход за животными
        sb.AppendLine("},");

        // ═══ vitality ═══
        sb.AppendLine("\"vitality\":{");
        sb.AppendLine($"\"hp-dice-current\":{{\"value\":{cd.level}}},");
        sb.AppendLine("\"hp-dice-multi\":{},");
        sb.AppendLine("\"hp-max-con-bonus\":{\"value\":0},");
        sb.AppendLine("\"darkvision\":{\"value\":0},");
        sb.AppendLine("\"shield\":{\"value\":false},");
        sb.Append($"\"ac\":{{\"value\":\"{cd.armorClass}\"}},");
        sb.AppendLine("\"speed\":{\"value\":\"30\"},");
        sb.Append($"\"hp-max\":{{\"value\":{cd.maxHP}}},");
        sb.Append($"\"hp-current\":{{\"value\":{cd.currentHP}}},");
        sb.AppendLine("\"hp-temp\":{\"value\":0},");
        sb.AppendLine("\"isDying\":false,");
        sb.AppendLine("\"deathFails\":0,");
        sb.AppendLine("\"deathSuccesses\":0");
        sb.AppendLine("},");

        // ── text ═══
        sb.AppendLine("\"text\":{");
        sb.Append("\"attacks\":{"); WriteLssTextField(sb, cd.attacksAndSpells, "attacks-1891519"); sb.AppendLine("},");
        sb.Append("\"traits\":{"); WriteLssTextField(sb, cd.featuresAndTraits, "traits-8842227"); sb.AppendLine("},");
        sb.Append("\"features\":{"); WriteLssTextField(sb, cd.extraAbilities, "features-99307175"); sb.AppendLine("},");
        sb.Append("\"feats\":{"); WriteLssTextField(sb, cd.traits, "feats-76191194"); sb.AppendLine("},");
        sb.Append("\"equipment\":{"); WriteLssTextField(sb, cd.equipment, "equipment-33803582"); sb.Append("},");
        sb.AppendLine("\"items\":{\"isHidden\":false,\"value\":{\"id\":\"hover-toolbar-items-2888368\",\"data\":{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"\"}]}]}}},");
        for (int i = 1; i <= 6; i++)
        {
            string noteText = i switch {
                1 => cd.note1, 2 => cd.note2, 3 => cd.note3,
                4 => cd.note4, 5 => cd.note5, 6 => cd.note6, _ => ""
            };
            sb.Append($"\"notes-{i}\":{{"); WriteLssTextField(sb, noteText, $"notes-{i}-{10000000 + i * 1234567}"); sb.Append("},");
            sb.AppendLine();
        }
        sb.Append("\"prof\":{"); WriteLssTextField(sb, cd.otherProficiencies, "prof-91234567"); sb.Append("}");
        sb.AppendLine();
        sb.Append("},");

        // ═══ remaining ═══
        sb.AppendLine("\"attunementsList\":[{\"id\":\"attunement-1783081799811\",\"checked\":false,\"value\":\"\"}],");
        sb.AppendLine("\"weaponsList\":[{\"id\":\"weapon-1783081799811\",\"name\":{\"value\":\"\"},\"dmg\":{\"value\":\"\"}}],");
        sb.AppendLine("\"coins\":{},");
        sb.AppendLine("\"resources\":{},");
        sb.AppendLine("\"bonusesSkills\":{},");
        sb.AppendLine("\"bonusesStats\":{},");
        sb.AppendLine("\"conditions\":[],");
        sb.AppendLine("\"wizardStep\":\"initial\",");

        // ═══ createdAt ═══
        sb.AppendLine($"\"createdAt\":\"{System.DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}\"");

        sb.Append("}");
        return sb.ToString();
    }

    static void WriteLssSkill(StringBuilder sb, CharacterData.SkillEntry sk, string skillName, string baseStat)
    {
        sb.Append($"\"{skillName}\":{{\"baseStat\":\"{baseStat}\",\"name\":\"{skillName}\"");
        if (sk.ProfLevel >= 1)
            sb.Append($",\"isProf\":{sk.ProfLevel}");
        sb.Append("},");
        sb.AppendLine();
    }

    static void WriteLssSkillLast(StringBuilder sb, CharacterData.SkillEntry sk, string skillName, string baseStat)
    {
        sb.Append($"\"{skillName}\":{{\"baseStat\":\"{baseStat}\",\"name\":\"{skillName}\"");
        if (sk.ProfLevel >= 1)
            sb.Append($",\"isProf\":{sk.ProfLevel}");
        sb.Append("}");
        sb.AppendLine();
    }

    static void WriteLssTextField(StringBuilder sb, string text, string idSuffix)
    {
        sb.Append("\"value\":{\"id\":\"hover-toolbar-");
        sb.Append(idSuffix);
        sb.Append("\",\"data\":{\"type\":\"doc\",\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":");
        sb.Append(EscapeJsonValue(text));
        sb.Append("}]}]}}");
    }

    // ═══════════════════ ИМПОРТ ═══════════════════

    public static void ImportFromLss(string json, CharacterData cd)
    {
        if (cd == null || string.IsNullOrEmpty(json)) return;

        // Extract data string
        string dataJson = ExtractJsonStringField(json, "data");
        if (string.IsNullOrEmpty(dataJson)) return;

        // Parse fields from data
        var name = ExtractJsonStringFieldRaw(dataJson, "\"name\":{\"value\":");
        if (!string.IsNullOrEmpty(name) && name != "null")
            cd.characterName = name;

        var className = ExtractNestedField(dataJson, "charClass", "value");
        if (!string.IsNullOrEmpty(className)) cd.className = className;

        cd.level = ExtractIntField(dataJson, "\"level\":{\"name\":\"level\",\"value\":", 1, 20);
        cd.currentXP = ExtractIntField(dataJson, "\"experience\":{\"name\":\"experience\",\"value\":", 0, int.MaxValue);
        cd.proficiencyBonus = ExtractIntField(dataJson, "\"proficiency\":", 2, 6);

        // Stats
        cd.strength = ExtractStat(dataJson, "str");
        cd.dexterity = ExtractStat(dataJson, "dex");
        cd.constitution = ExtractStat(dataJson, "con");
        cd.intelligence = ExtractStat(dataJson, "int");
        cd.wisdom = ExtractStat(dataJson, "wis");
        cd.charisma = ExtractStat(dataJson, "cha");

        // Saves
        cd.strSaveProficient = ExtractSaveProf(dataJson, "str");
        cd.dexSaveProficient = ExtractSaveProf(dataJson, "dex");
        cd.conSaveProficient = ExtractSaveProf(dataJson, "con");
        cd.intSaveProficient = ExtractSaveProf(dataJson, "int");
        cd.wisSaveProficient = ExtractSaveProf(dataJson, "wis");
        cd.chaSaveProficient = ExtractSaveProf(dataJson, "cha");

        // Vitality
        cd.armorClass = ExtractIntFieldFlex(dataJson, "\"ac\":{\"value\":", 0, 30);
        cd.maxHP = ExtractIntFieldFlex(dataJson, "\"hp-max\":{\"value\":", 1, 999);
        cd.currentHP = ExtractIntFieldFlex(dataJson, "\"hp-current\":{\"value\":", 0, 999);

        // Text fields
        cd.attacksAndSpells = ExtractLssTextField(dataJson, "attacks");
        cd.featuresAndTraits = ExtractLssTextField(dataJson, "traits");
        cd.extraAbilities = ExtractLssTextField(dataJson, "features");
        cd.traits = ExtractLssTextField(dataJson, "feats");
        cd.equipment = ExtractLssTextField(dataJson, "equipment");

        // Treasure from "items" field
        string treasure = ExtractLssTextField(dataJson, "items");
        if (!string.IsNullOrEmpty(treasure)) cd.treasure = treasure;

        // Notes
        cd.note1 = ExtractLssTextField(dataJson, "notes-1");
        cd.note2 = ExtractLssTextField(dataJson, "notes-2");
        cd.note3 = ExtractLssTextField(dataJson, "notes-3");
        cd.note4 = ExtractLssTextField(dataJson, "notes-4");
        cd.note5 = ExtractLssTextField(dataJson, "notes-5");
        cd.note6 = ExtractLssTextField(dataJson, "notes-6");

        // Skills proficiency
        ImportSkillProficiencies(dataJson, cd);

        // Other proficiencies (from "prof" field)
        string profs = ExtractLssTextField(dataJson, "prof");
        if (!string.IsNullOrEmpty(profs)) cd.otherProficiencies = profs;
    }

    static void ImportSkillProficiencies(string json, CharacterData cd)
    {
        // LSS skill name → CharacterData skills index
        var map = new System.Collections.Generic.Dictionary<string, int> {
            {"athletics", 0}, {"acrobatics", 1}, {"sleight of hand", 2}, {"stealth", 3},
            {"investigation", 4}, {"history", 5}, {"arcana", 6}, {"nature", 7},
            {"religion", 8}, {"perception", 9}, {"survival", 10}, {"medicine", 11},
            {"insight", 12}, {"animal handling", 13}, {"performance", 14},
            {"intimidation", 15}, {"deception", 16}, {"persuasion", 17}
        };

        foreach (var kv in map)
        {
            // Find "skillName":{"...}
            string keyPattern = $"\"{kv.Key}\":";
            int idx = json.IndexOf(keyPattern);
            if (idx < 0) continue;

            // Find opening brace of this skill's object
            int objStart = json.IndexOf('{', idx);
            if (objStart < 0) continue;

            // Find matching closing brace — the skill object boundary
            int objEnd = FindMatchingBrace(json, objStart);
            if (objEnd < 0) continue;

            // Search "isProf":N strictly within this skill's object
            int profIdx = json.IndexOf("\"isProf\":", objStart, objEnd - objStart);
            if (profIdx < 0) continue;

            profIdx += 9; // skip "isProf":
            // skip whitespace (e.g. "isProf": 2)
            while (profIdx < objEnd && (json[profIdx] == ' ' || json[profIdx] == '\t')) profIdx++;
            int val = 0;
            while (profIdx < objEnd && json[profIdx] >= '0' && json[profIdx] <= '9')
            {
                val = val * 10 + (json[profIdx] - '0');
                profIdx++;
            }
            if (val >= 1 && kv.Value < cd.skills.Length)
            {
                cd.skills[kv.Value].ProfLevel = val; // 1=proficient, 2=expertise
            }
        }
    }

    /// <summary>
    /// Find the index of the closing brace that matches the opening brace at openIdx.
    /// Returns -1 if not found.
    /// </summary>
    static int FindMatchingBrace(string json, int openIdx)
    {
        if (openIdx < 0 || openIdx >= json.Length || json[openIdx] != '{') return -1;
        int depth = 1;
        int i = openIdx + 1;
        while (i < json.Length && depth > 0)
        {
            if (json[i] == '{') depth++;
            else if (json[i] == '}') depth--;
            i++;
        }
        return depth == 0 ? i - 1 : -1;
    }

    // ═══════════════════ Helpers ═══════════════════

    static string Bool(bool v) => v ? "true" : "false";
    static string EscapeJsonValue(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";
    static string EscapeJson(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";

    static int ExtractStat(string json, string statName)
    {
        return ExtractIntField(json, $"\"{statName}\":{{\"name\":\"{statName}\",\"score\":", 0, 30);
    }

    static bool ExtractSaveProf(string json, string saveName)
    {
        int idx = json.IndexOf($"\"{saveName}\":{{\"name\":\"{saveName}\",\"isProf\":");
        if (idx < 0) return false;
        idx = json.IndexOf("\"isProf\":", idx) + 9;
        while (idx < json.Length && (json[idx] == ' ' || json[idx] == '\t')) idx++;
        return idx < json.Length && json[idx] == 't';
    }

    static int ExtractIntField(string json, string prefix, int min, int max)
    {
        int idx = json.IndexOf(prefix);
        if (idx < 0) return min;
        idx += prefix.Length;

        // Skip optional quote
        if (idx < json.Length && json[idx] == '"') idx++;

        int val = 0;
        while (idx < json.Length && json[idx] >= '0' && json[idx] <= '9')
        {
            val = val * 10 + (json[idx] - '0');
            idx++;
        }
        return Mathf.Clamp(val, min, max);
    }

    /// Универсальный: "value":"12" или "value":12 или "value": 12
    static int ExtractIntFieldFlex(string json, string prefix, int min, int max)
    {
        int idx = json.IndexOf(prefix);
        if (idx < 0) return min;
        idx += prefix.Length;
        // Skip whitespace and optional quote
        while (idx < json.Length && (json[idx] == ' ' || json[idx] == '"')) idx++;
        int val = 0;
        while (idx < json.Length && json[idx] >= '0' && json[idx] <= '9')
        {
            val = val * 10 + (json[idx] - '0');
            idx++;
        }
        return Mathf.Clamp(val, min, max);
    }

    static string ExtractNestedField(string json, string fieldName, string subField)
    {
        int idx = json.IndexOf($"\"{fieldName}\":");
        if (idx < 0) return "";
        idx = json.IndexOf($"\"{subField}\":", idx);
        if (idx < 0) return "";
        return ExtractJsonStringValue(json, idx + subField.Length + 3);
    }

    static string ExtractJsonStringField(string json, string fieldName)
    {
        // Ищем "fieldName":
        string pattern = $"\"{fieldName}\":";
        int idx = json.IndexOf(pattern);
        if (idx < 0) return "";
        idx += pattern.Length; // skip "fieldName":
        return ExtractJsonStringValue(json, idx);
    }

    static string ExtractJsonStringFieldRaw(string json, string prefix)
    {
        int idx = json.IndexOf(prefix);
        if (idx < 0) return "";
        idx += prefix.Length;
        return ExtractJsonStringValue(json, idx);
    }

    static string ExtractJsonStringValue(string json, int start)
    {
        // Skip whitespace to find opening quote
        while (start < json.Length && (json[start] == ' ' || json[start] == '\t' || json[start] == '\n')) start++;
        if (start >= json.Length || json[start] != '"') return "";

        start++; // skip opening "
        var sb = new StringBuilder();
        while (start < json.Length)
        {
            char c = json[start];
            if (c == '\\' && start + 1 < json.Length)
            {
                char next = json[start + 1];
                if (next == '"') { sb.Append('"'); start += 2; continue; }
                if (next == 'n') { sb.Append('\n'); start += 2; continue; }
                if (next == '\\') { sb.Append('\\'); start += 2; continue; }
                sb.Append(c);
            }
            else if (c == '"')
            {
                break;
            }
            else
            {
                sb.Append(c);
            }
            start++;
        }
        return sb.ToString();
    }

    /// Извлекает ВЕСЬ текст из rich-text поля LSS (все параграфы, списки и т.д.)
    static string ExtractLssTextField(string json, string fieldName)
    {
        string pattern = $"\"{fieldName}\":";
        int idx = json.IndexOf(pattern);
        if (idx < 0) return "";
        idx += pattern.Length;

        // Find boundaries of this field's JSON object
        int objStart = json.IndexOf('{', idx);
        if (objStart < 0) return "";
        int objEnd = FindMatchingBrace(json, objStart);
        if (objEnd < 0) return "";

        var sb = new System.Text.StringBuilder();
        int searchFrom = objStart;
        bool first = true;

        while (true)
        {
            int typeIdx = json.IndexOf("\"type\":\"", searchFrom);
            if (typeIdx < 0 || typeIdx > objEnd) break;
            
            string typeVal = json.Substring(typeIdx + 8, Mathf.Min(8, json.Length - typeIdx - 8));
            bool isRich = typeVal.StartsWith("text") || typeVal.StartsWith("paragrap") 
                       || typeVal.StartsWith("bullet") || typeVal.StartsWith("listIte");
            
            if (!isRich) { searchFrom = typeIdx + 1; continue; }

            int textIdx = json.IndexOf("\"text\":\"", typeIdx);
            if (textIdx < 0 || textIdx > objEnd) { searchFrom = typeIdx + 1; continue; }
            
            textIdx += 7; // skip "text":"
            string text = ExtractJsonStringValue(json, textIdx);
            if (!string.IsNullOrEmpty(text))
            {
                if (!first) sb.Append("\n");
                sb.Append(text);
                first = false;
            }
            searchFrom = textIdx + text.Length + 2;
        }

        return sb.ToString();
    }
}
