using System;
using System.Collections.Generic;
using System.Text;

/// <summary>Lossless one-action-per-line format for the existing compact editor.</summary>
public static class StatBlockActionText
{
    public static string Format(StatBlockAction[] actions)
    {
        var lines = new List<string>();
        foreach (var action in actions ?? Array.Empty<StatBlockAction>())
            if (action != null) lines.Add(Escape(action.name) + ": " + Escape(action.description));
        return string.Join("\n", lines);
    }

    public static StatBlockAction[] Parse(string text)
    {
        var result = new List<StatBlockAction>();
        var lines = (text ?? "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;
            int separator = Separator(line);
            if (separator <= 0) throw new FormatException("Действие в строке " + (i + 1) + ": укажите название и описание через двоеточие.");
            string name = Unescape(line.Substring(0, separator));
            string description = line.Substring(separator + 1);
            if (description.StartsWith(" ", StringComparison.Ordinal)) description = description.Substring(1);
            if (string.IsNullOrWhiteSpace(name)) throw new FormatException("Действие в строке " + (i + 1) + ": укажите название.");
            result.Add(new StatBlockAction { name = name, description = Unescape(description) });
        }
        return result.ToArray();
    }

    private static int Separator(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '\\' && i + 1 < line.Length && "\\nr:".IndexOf(line[i + 1]) >= 0) { i++; continue; }
            if (line[i] == ':') return i;
        }
        return -1;
    }
    private static string Escape(string text) => (text ?? "").Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace(":", "\\:");
    private static string Unescape(string text)
    {
        var result = new StringBuilder();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                char next = text[i + 1];
                if (next == 'n') { result.Append('\n'); i++; continue; }
                if (next == 'r') { result.Append('\r'); i++; continue; }
                if (next == '\\' || next == ':') { result.Append(next); i++; continue; }
            }
            result.Append(c);
        }
        return result.ToString();
    }
}
