using System;
using System.Collections.Generic;
using System.Text;

/// <summary>Host-local name allocation; suffixes are never reused during a lobby.</summary>
public sealed class TokenNameRegistry
{
    public const int MaxBaseNameBytes = 96;
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _next = new(StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string value)
    {
        var result = new StringBuilder();
        bool space = false;
        foreach (char character in (value ?? "").Trim())
        {
            if (char.IsWhiteSpace(character)) { space = result.Length > 0; continue; }
            if (char.IsControl(character)) continue;
            if (space) { result.Append(' '); space = false; }
            result.Append(character);
        }
        string name = result.ToString();
        while (Encoding.UTF8.GetByteCount(name) > MaxBaseNameBytes)
        {
            int length = name.Length - 1;
            if (length > 0 && char.IsHighSurrogate(name[length - 1]) && char.IsLowSurrogate(name[length])) length--;
            name = name.Substring(0, length);
        }
        return string.IsNullOrWhiteSpace(name) ? "Токен" : name.TrimEnd();
    }

    public string Allocate(string requested, IEnumerable<string> existingNames)
    {
        foreach (string existing in existingNames)
            if (!string.IsNullOrWhiteSpace(existing)) _used.Add(existing);
        string basis = Normalize(requested);
        int number = _next.TryGetValue(basis, out int next) ? next : 1;
        string name;
        do
        {
            name = number == 1 ? basis : basis + " " + number;
            number++;
        } while (!_used.Add(name));
        _next[basis] = number;
        return name;
    }

    public void Clear() { _used.Clear(); _next.Clear(); }
    public void Reserve(string name) => _used.Add(name);
}
