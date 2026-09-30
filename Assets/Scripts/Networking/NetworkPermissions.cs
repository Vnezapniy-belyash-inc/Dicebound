using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Rights checks based on IsHost (session) and IsSpawner (per object).
/// NetworkObject ownership does not grant movement rights; server handles all drags.
/// </summary>
public static class NetworkPermissions
{
    public static bool IsHostClient(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        return nm != null && clientId == NetworkManager.ServerClientId;
    }

    public static bool HostHasSpawnedDice(ulong hostClientId)
    {
        var dice = Object.FindObjectsByType<NetworkDice>(FindObjectsInactive.Exclude);
        foreach (var d in dice)
        {
            if (d != null && d.IsSpawned && d.SpawnerClientId == hostClientId)
                return true;
        }

        return false;
    }

    public static bool HostHasSpawnedCellMarkers(ulong hostClientId)
    {
        var markers = Object.FindObjectsByType<CellMarker>(FindObjectsInactive.Exclude);
        foreach (var m in markers)
        {
            if (m != null && m.IsSpawned && m.SpawnerClientId == hostClientId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Client: own spawner dice only.
    /// Host with own dice on field: own spawner dice only.
    /// Host with no own dice: all dice (clear table).
    /// </summary>
    public static bool ShouldDeleteInClearAll(
        ulong spawnerClientId, ulong localClientId, bool isHost, bool hostHasOwnSpawned)
    {
        if (spawnerClientId == localClientId) return true;

        if (isHost && !hostHasOwnSpawned) return true;

        return false;
    }

    public static bool ShouldDeleteDiceInClearAll(
        NetworkDice dice, ulong localClientId, bool isHost, bool hostHasOwnDice)
    {
        if (dice == null || !dice.IsSpawned) return false;

        return ShouldDeleteInClearAll(dice.SpawnerClientId, localClientId, isHost, hostHasOwnDice);
    }

    public static bool ShouldDeleteCellMarkerInClearAll(
        CellMarker marker, ulong localClientId, bool isHost, bool hostHasOwnMarkers)
    {
        if (marker == null || !marker.IsSpawned) return false;

        return ShouldDeleteInClearAll(marker.SpawnerClientId, localClientId, isHost, hostHasOwnMarkers);
    }

    public static bool CanDespawnSpawnedObject(ulong senderId, ulong spawnerClientId, bool hostHasOwnSpawned)
    {
        if (senderId == spawnerClientId) return true;

        if (IsHostClient(senderId) && !hostHasOwnSpawned) return true;

        return false;
    }

    public static bool CanDespawnDice(ulong senderId, NetworkDice dice)
    {
        if (dice == null || !dice.IsSpawned) return false;

        bool hostHasOwn = IsHostClient(senderId) && HostHasSpawnedDice(senderId);
        return CanDespawnSpawnedObject(senderId, dice.SpawnerClientId, hostHasOwn);
    }

    public static bool CanDespawnCellMarker(ulong senderId, CellMarker marker)
    {
        if (marker == null || !marker.IsSpawned) return false;

        bool hostHasOwn = IsHostClient(senderId) && HostHasSpawnedCellMarkers(senderId);
        return CanDespawnSpawnedObject(senderId, marker.SpawnerClientId, hostHasOwn);
    }

    public static bool CanRemoveSingleCellMarker(ulong senderId, CellMarker marker)
    {
        if (marker == null || !marker.IsSpawned) return false;

        if (senderId == marker.SpawnerClientId) return true;

        return IsHostClient(senderId);
    }

    public static bool CanUploadTokenPortrait(ulong senderId, TokenController token)
    {
        if (token == null || !token.IsSpawned) return false;

        if (senderId == token.SpawnerClientId) return true;

        return IsHostClient(senderId);
    }

    /// <summary>Client: own token; host: any token.</summary>
    public static bool CanCopyToken(ulong senderId, TokenController source) =>
        CanUploadTokenPortrait(senderId, source);
}
