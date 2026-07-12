using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Отслеживает подключения/отключения игроков (MonoBehaviour).
/// Вешать на отдельный GameObject (НЕ на NetworkManager).
/// </summary>
public class NetworkPlayerManager : MonoBehaviour
{
    public static NetworkPlayerManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        // Подписываемся в Awake (раньше Start)
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    private void Start() { }

    private void OnClientConnected(ulong clientId)
    {
        // Для локального клиента — используем его ник. Для остальных — фолбэк.
        string nick = clientId == NetworkManager.Singleton.LocalClientId
            ? (LobbyUI.LocalNickname ?? $"Player_{clientId}")
            : $"Player_{clientId}";
        var color = PlayerColors.GetOrAssignColor(clientId, nick);
        Debug.Log($"[Multiplayer] Client {clientId} ({nick}) connected. Color: {color}. Total: {NetworkManager.Singleton.ConnectedClients.Count}");

        // Перепривязать старые токены этого ника к новому clientId
        ReassignTokens(nick, clientId);
    }

    private static void ReassignTokens(string nickname, ulong newClientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        var tokens = FindObjectsByType<TokenController>(FindObjectsInactive.Exclude);
        foreach (var t in tokens)
        {
            if (!t.IsSpawned) continue;
            string ownerNick = PlayerColors.GetNickname(t.OwnerClientId);
            // Если токен принадлежал этому же нику (но старому clientId) — передаём владение
            if (ownerNick == nickname && t.OwnerClientId != newClientId)
            {
                try
                {
                    t.GetComponent<NetworkObject>().ChangeOwnership(newClientId);
                    Debug.Log($"[Multiplayer] Reassigned token to {nickname} (new client {newClientId})");
                }
                catch (System.Exception) { /* владелец мог отключиться */ }
            }
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"[Multiplayer] Client {clientId} disconnected. Total: {NetworkManager.Singleton.ConnectedClients.Count}");
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }
}
