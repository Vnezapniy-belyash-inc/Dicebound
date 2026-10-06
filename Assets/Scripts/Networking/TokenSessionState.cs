using System.Collections.Generic;

/// <summary>Session-scoped hero issuance, independent of temporary object ownership.</summary>
public sealed class TokenSessionState
{
    private readonly HashSet<ulong> _heroIssued = new();

    public bool IssueHero(ulong playerId, bool isGameMaster, bool isCopy)
    {
        if (isGameMaster || isCopy) return false;
        return _heroIssued.Add(playerId);
    }

    // Keep issuance even if the original hero was deleted; reconnect must not issue a second one.
    public void ReassignPlayer(ulong oldId, ulong newId)
    {
        if (_heroIssued.Remove(oldId)) _heroIssued.Add(newId);
    }

    public void Clear() => _heroIssued.Clear();
    public void RestoreHero(ulong playerId) => _heroIssued.Add(playerId);
}
