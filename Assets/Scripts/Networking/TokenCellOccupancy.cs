using System.Collections.Generic;

/// <summary>Visible tokens reserve cells; counts preserve reservations after an overlap is revealed.</summary>
public sealed class TokenCellOccupancy
{
    private readonly Dictionary<(int X, int Y), int> _counts = new();
    public bool IsOccupied(int x, int y) => _counts.ContainsKey((x, y));
    public bool TryOccupy(int x, int y)
    {
        if (IsOccupied(x, y)) return false;
        Add(x, y);
        return true;
    }
    public void Add(int x, int y)
    {
        var key = (x, y);
        _counts.TryGetValue(key, out int count);
        _counts[key] = count + 1;
    }
    public void Release(int x, int y)
    {
        var key = (x, y);
        if (!_counts.TryGetValue(key, out int count)) return;
        if (count <= 1) _counts.Remove(key);
        else _counts[key] = count - 1;
    }
    public void Clear() => _counts.Clear();
}
