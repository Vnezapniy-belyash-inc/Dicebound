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
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[Multiplayer] Client {clientId} connected. Total: {NetworkManager.Singleton.ConnectedClients.Count}");
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
