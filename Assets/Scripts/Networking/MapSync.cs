using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Синхронизация карты: сервер отправляет PNG через ClientRpc.
/// Late-join через OnClientConnectedCallback.
/// </summary>
public class MapSync : NetworkBehaviour
{
    [Header("References")]
    public MapController mapController;

    public static MapSync Instance { get; private set; }

    private byte[] _cachedPng;
    private bool _handlerRegistered;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // Сервер: кэшируем PNG и слушаем подключения
        NetworkManager.Singleton.OnClientConnectedCallback += OnLateJoin;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
                "MapRequest", OnMapRequest);
            _handlerRegistered = true;
        }
    }

    public override void OnDestroy()
    {
        if (_handlerRegistered && NetworkManager.Singleton?.CustomMessagingManager != null)
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler("MapRequest");
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientConnectedCallback -= OnLateJoin;
        base.OnDestroy();
    }

    // ═══ Host → All ═══

    public void SendMapToAll(byte[] pngData)
    {
        if (!IsServer || pngData == null) return;
        _cachedPng = pngData;
        Debug.Log($"[MapSync] Sending map: {pngData.Length} bytes");
        SendMapClientRpc(pngData);
    }

    [Rpc(SendTo.NotServer)]
    private void SendMapClientRpc(byte[] pngData)
    {
        Debug.Log($"[MapSync] Received map: {pngData.Length} bytes");
        mapController?.ApplyImageLocal(pngData);
    }

    // ═══ Late-join ═══

    private void OnLateJoin(ulong clientId)
    {
        if (clientId == NetworkManager.ServerClientId) return;
        if (_cachedPng == null) return;
        Debug.Log($"[MapSync] Late-join: sending map to client {clientId}");
        // Шлём всем — новый клиент получит, остальные проигнорируют
        SendMapClientRpc(_cachedPng);
    }

    private void OnMapRequest(ulong sender, FastBufferReader r)
    {
        Debug.Log($"[MapSync] Client {sender} requested map");
        OnLateJoin(sender);
    }

    // ═══ Client requests map ═══

    /// <summary>Клиент вызывает после подключения чтобы запросить карту.</summary>
    public static void RequestMapFromServer()
    {
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.CustomMessagingManager == null) return;
        var w = new FastBufferWriter(1, Unity.Collections.Allocator.Temp);
        w.WriteValueSafe((byte)0);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage("MapRequest", NetworkManager.ServerClientId, w);
        w.Dispose();
    }
}
