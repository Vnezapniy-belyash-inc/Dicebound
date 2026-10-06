using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-side: transfers token and dice ownership when a player reconnects with a new clientId.
/// </summary>
public static class PlayerOwnershipReassigner
{
    public static int ReassignPlayerObjects(ulong oldClientId, ulong newClientId)
    {
        if (oldClientId == newClientId) return 0;

        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return 0;
        TokenManager.Instance?.ReassignHeroRecord(oldClientId, newClientId);

        int count = 0;
        foreach (var netObj in nm.SpawnManager.SpawnedObjectsList)
        {
            if (netObj == null || !netObj.IsSpawned) continue;
            var dice = netObj.GetComponent<NetworkDice>();
            var token = netObj.GetComponent<TokenController>();
            if (dice == null && token == null) continue;
            bool createdByPlayer = dice != null && dice.SpawnerClientId == oldClientId
                || token != null && token.SpawnerClientId == oldClientId;
            bool stillOwnedByPlayer = netObj.OwnerClientId == oldClientId;
            if (!createdByPlayer && !stillOwnedByPlayer) continue;

            try
            {
                // NGO gives persistent objects back to the server on disconnect.
                // Restore movement ownership only when no other player owns the object.
                if (netObj.OwnerClientId == oldClientId
                    || createdByPlayer && netObj.OwnerClientId == NetworkManager.ServerClientId)
                    netObj.ChangeOwnership(newClientId);

                if (dice != null && dice.SpawnerClientId == oldClientId)
                {
                    dice.ServerUpdateSpawnerClientId(newClientId);
                    dice.ServerRefreshPlayerColor();
                }

                if (token != null && token.SpawnerClientId == oldClientId)
                {
                    token.ServerUpdateSpawnerClientId(newClientId);
                    token.ServerRefreshPlayerColor();
                }

                count++;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[PlayerRegistry] Failed to reassign {netObj.name}: {ex.Message}");
            }
        }

        if (count > 0)
            Debug.Log($"[PlayerRegistry] Reassigned {count} object(s) from client {oldClientId} to {newClientId}");

        return count;
    }
}
