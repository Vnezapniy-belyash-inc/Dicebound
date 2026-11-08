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

    // The input has already been normalized to single spaces between words.
    public static string Wrap(string text, Func<string, float> measure, float width, out int lineCount)
    {
        int[] elements = StringInfo.ParseCombiningCharacters(text);
        var result = new StringBuilder(text.Length);
        lineCount = 0;
        int first = 0;
        while (first < elements.Length)
        {
            int start = elements[first];
            int lastSpace = -1;
            int next = first + 1;
            while (next <= elements.Length)
            {
                int end = next == elements.Length ? text.Length : elements[next];
                if (measure(text.Substring(start, end - start).TrimEnd()) > width) break;
                if (end < text.Length && text[end] == ' ') lastSpace = next;
                next++;
            }

            if (next <= elements.Length)
                next = lastSpace > first && lastSpace < next ? lastSpace : Math.Max(first + 1, next - 1);
            else
                next = elements.Length;

            int lineEnd = next == elements.Length ? text.Length : elements[next];
            if (lineCount++ > 0) result.Append('\n');
            result.Append(text.Substring(start, lineEnd - start).TrimEnd());
            first = next;
            if (first < elements.Length && text[elements[first]] == ' ') first++;
        }
        return result.ToString();
    }
}
