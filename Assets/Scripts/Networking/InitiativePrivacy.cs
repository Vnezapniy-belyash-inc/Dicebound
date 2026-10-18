using System;
using System.Text;
using Unity.Netcode;
using UnityEngine;

public partial class InitiativeTracker
{
    private bool CanClientSeeEntry(ulong client, Entry entry, bool preview = false)
    {
        if (string.IsNullOrEmpty(entry.tokenId)) return true; // Explicitly public manual participants.
        var token = TokenController.FindSceneTokenForManager(entry.tokenId, NetworkManager);
        if (token == null || token.IsHidden) return false;
        return FogManager.Instance == null || FogManager.Instance.CanSeeToken(client, token, preview);
    }

    private string SerializeDataForClient(ulong client)
    {
        var projection = InitiativeProjection.Project(_entries, _currentIndex, entry => CanClientSeeEntry(client, entry));
        var state = new InitiativeNetworkState
        {
            currentIndex = Math.Max(0, projection.ActiveIndex), round = _round,
            activeParticipantId = projection.ActiveIndex < 0 ? null : projection.Entries[projection.ActiveIndex].id.ToString(),
            waitingForTurn = _entries.Count > 0 && projection.ActiveIndex < 0,
            entries = new InitiativeNetworkEntry[projection.Entries.Length]
        };
        for (int i = 0; i < projection.Entries.Length; i++)
        {
            var entry = projection.Entries[i];
            state.entries[i] = new InitiativeNetworkEntry { id = entry.id, name = entry.name,
                initiative = entry.initiative, colorHex = entry.colorHex, playerId = entry.playerId,
                tokenId = entry.tokenId, publicHp = GetPublicHp(entry) };
        }
        return JsonUtility.ToJson(state);
    }

    private string CaptureVisibilitySignature()
    {
        var signature = new StringBuilder();
        foreach (ulong client in NetworkManager.ConnectedClientsIds)
        {
            if (client == Unity.Netcode.NetworkManager.ServerClientId) continue;
            signature.Append(client).Append(':');
            foreach (var entry in _entries) if (CanClientSeeEntry(client, entry)) signature.Append(entry.id).Append(',');
            signature.Append(';');
        }
        return signature.ToString();
    }

    public void RefreshVisibility()
    {
        if (!IsSpawned || !IsServer || _entries.Count == 0) return;
        if (CaptureVisibilitySignature() != _visibilitySignature) Sync();
    }
    public void RefreshDisplayedView() => RebuildRows();
}
