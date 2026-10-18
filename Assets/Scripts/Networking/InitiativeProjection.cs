using System;
using System.Collections.Generic;

public readonly struct InitiativeProjectionResult<T>
{
    public readonly T[] Entries;
    public readonly int ActiveIndex;
    public InitiativeProjectionResult(T[] entries, int activeIndex) { Entries = entries; ActiveIndex = activeIndex; }
}

/// <summary>Filters before serialization and remaps the turn without exposing an excluded entry's ID.</summary>
public static class InitiativeProjection
{
    public static InitiativeProjectionResult<T> Project<T>(IEnumerable<T> source, int activeIndex, Func<T, bool> canSee)
    {
        if (source == null || canSee == null) throw new ArgumentNullException();
        var visible = new List<T>(); int index = 0, active = -1;
        foreach (T entry in source)
        {
            if (canSee(entry))
            {
                if (index == activeIndex) active = visible.Count;
                visible.Add(entry);
            }
            index++;
        }
        return new InitiativeProjectionResult<T>(visible.ToArray(), active);
    }
}
