using System;
using System.Globalization;
using System.Text;

public static class StampText
{
    public const int MaxLength = 80, MaxBytes = 320;
    public static string Normalize(string text)
    {
        if (text == null || text.Length > MaxLength * 4) return null;
        var result = new StringBuilder();
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                if (result.Length > 0 && result[result.Length - 1] != ' ') result.Append(' ');
            }
            else if (!char.IsControl(c) && char.GetUnicodeCategory(c) != UnicodeCategory.Format) result.Append(c);
        }
        string normalized;
        try { normalized = result.ToString().Trim().Normalize(NormalizationForm.FormC); }
        catch (ArgumentException) { return null; }
        return normalized.Length > 0 && normalized.Length <= MaxLength && Encoding.UTF8.GetByteCount(normalized) <= MaxBytes
            ? normalized : null;
    }

    // Measure real glyph widths; split at a word boundary, or between complete graphemes for a long word.
    public static string Wrap(string text, Func<string, float> measure, float preferredWidth)
    {
        if (measure(text) <= preferredWidth) return text;
        int split = -1; float best = float.PositiveInfinity;
        for (int i = 1; i < text.Length - 1; i++)
            if (text[i] == ' ')
            {
                float width = Math.Max(measure(text.Substring(0, i)), measure(text.Substring(i + 1)));
                if (width < best) { split = i; best = width; }
            }
        bool wordBoundary = split > 0;
        if (split < 0 || best > preferredWidth)
        {
            foreach (int i in StringInfo.ParseCombiningCharacters(text))
            {
                if (i == 0) continue;
                float width = Math.Max(measure(text.Substring(0, i)), measure(text.Substring(i)));
                if (width < best) { split = i; best = width; wordBoundary = text[i] == ' '; }
            }
        }
        return split < 0 ? text : text.Substring(0, split).TrimEnd() + "\n" + text.Substring(split + (wordBoundary ? 1 : 0)).TrimStart();
    }
}
