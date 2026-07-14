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

        int count = 0;
        foreach (var netObj in nm.SpawnManager.SpawnedObjectsList)
        {
            if (netObj == null || !netObj.IsSpawned) continue;
            if (netObj.OwnerClientId != oldClientId) continue;

            bool isPlayerObject = netObj.GetComponent<NetworkDice>() != null
                               || netObj.GetComponent<TokenController>() != null;
            if (!isPlayerObject) continue;

            try
            {
                netObj.ChangeOwnership(newClientId);

                var dice = netObj.GetComponent<NetworkDice>();
                if (dice != null)
                {
                    dice.ServerUpdateSpawnerClientId(newClientId);
                    dice.ServerRefreshPlayerColor();
                }

                var token = netObj.GetComponent<TokenController>();
                if (token != null)
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
