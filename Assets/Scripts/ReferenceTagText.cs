using System;
using System.Collections.Generic;
using System.Text;

/// <summary>CSV-style tags, preserving commas and quotes in imported library tags.</summary>
public static class ReferenceTagText
{
    public static string Format(string[] tags)
    {
        var result = new List<string>();
        foreach (string raw in tags ?? Array.Empty<string>())
        {
            string value = raw ?? "";
            bool quote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || value.Trim() != value;
            result.Add(quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value);
        }
        return string.Join(", ", result);
    }
    public static string[] Parse(string text)
    {
        var result = new List<string>(); var token = new StringBuilder();
        bool quoted = false, wasQuoted = false, closed = false;
        void Finish()
        {
            string value = wasQuoted ? token.ToString() : token.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(value)) result.Add(value);
            token.Clear(); wasQuoted = closed = false;
        }
        text ??= "";
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c != '"') { token.Append(c); continue; }
                if (i + 1 < text.Length && text[i + 1] == '"') { token.Append('"'); i++; continue; }
                quoted = false; closed = true; continue;
            }
            if (c == ',') { Finish(); continue; }
            if (closed)
            {
                if (!char.IsWhiteSpace(c)) throw new FormatException("После кавычек тега ожидается запятая.");
                continue;
            }
            if (c == '"')
            {
                if (token.ToString().Trim().Length != 0) throw new FormatException("Кавычки должны охватывать весь тег.");
                token.Clear(); quoted = wasQuoted = true; continue;
            }
            token.Append(c);
        }
        if (quoted) throw new FormatException("Закройте кавычки в списке тегов.");
        Finish(); return result.ToArray();
    }
}
