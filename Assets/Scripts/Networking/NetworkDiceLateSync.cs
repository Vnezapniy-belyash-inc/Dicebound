using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-side: pushes dice mesh state to reconnecting or late-join clients.
/// </summary>
public static class NetworkDiceLateSync
{
    public static void PushAllToClient(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        if (clientId == nm.LocalClientId) return;

        foreach (var dice in Object.FindObjectsByType<NetworkDice>(FindObjectsInactive.Exclude))
        {
            if (dice != null && dice.IsSpawned)
                dice.ServerPushMeshToClient(clientId);
        }
    }

    public static IEnumerator PushAllToClientRoutine(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) yield break;
        if (clientId == nm.LocalClientId) yield break;

        foreach (var dice in Object.FindObjectsByType<NetworkDice>(FindObjectsInactive.Exclude))
        {
            if (dice == null || !dice.IsSpawned) continue;
            dice.ServerPushMeshToClient(clientId);
            yield return null;
        }
    }
}
