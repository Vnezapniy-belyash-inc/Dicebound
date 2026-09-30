using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Менеджер токенов: спавн по запросу клиента.
/// Аналогичен NetworkDiceManager.
/// </summary>
public class TokenManager : MonoBehaviour
{
    private const string MSG_SPAWN_TOKEN = "SpawnToken";
    private const string MSG_SPAWN_TOKEN_ACK = "SpawnTokenAck";
    private const int MaxSpawnAttempts = 3;

    [Header("Prefab")]
    public NetworkObject tokenPrefab;

    [Header("Spawn")]
    public float spawnHeight = 0.1f;

    public static TokenManager Instance { get; private set; }

    private bool _handlerRegistered;
    private bool _ackHandlerRegistered;
    private int _nextRequestId;
    private readonly Dictionary<int, bool> _pendingRequests = new();
    private readonly Dictionary<(ulong Client, int Request), ulong> _processedRequests = new();

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
            NetworkManager.Singleton.OnServerStopped += OnServerStopped;
            NetworkManager.Singleton.OnClientStopped += OnClientStopped;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            RegisterAckHandler();
            if (NetworkManager.Singleton.IsServer) OnServerStarted();
            TokenImageSync.EnsureInstance();
            CellMarker.EnsureRegistered();
        }
    }

    private void OnServerStarted()
    {
        if (!_handlerRegistered && NetworkManager.Singleton.CustomMessagingManager != null)
        {
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
                MSG_SPAWN_TOKEN, OnSpawnTokenRequest);
            _handlerRegistered = true;
        }
    }

    private void OnServerStopped(bool wasHost)
    {
        if (_handlerRegistered && NetworkManager.Singleton?.CustomMessagingManager != null)
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(
                MSG_SPAWN_TOKEN);
        _handlerRegistered = false;
        _processedRequests.Clear();
    }

    private void OnClientStopped(bool wasHost)
    {
        _pendingRequests.Clear();
        UnregisterAckHandler();
    }

    private void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || clientId != nm.LocalClientId) return;
        UnregisterAckHandler();
        RegisterAckHandler();
    }

    private void RegisterAckHandler()
    {
        if (_ackHandlerRegistered) return;
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm == null) return;
        cmm.RegisterNamedMessageHandler(MSG_SPAWN_TOKEN_ACK, OnSpawnTokenAck);
        _ackHandlerRegistered = true;
    }

    private void UnregisterAckHandler()
    {
        if (!_ackHandlerRegistered) return;
        NetworkManager.Singleton?.CustomMessagingManager?.UnregisterNamedMessageHandler(
            MSG_SPAWN_TOKEN_ACK);
        _ackHandlerRegistered = false;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            NetworkManager.Singleton.OnServerStopped -= OnServerStopped;
            NetworkManager.Singleton.OnClientStopped -= OnClientStopped;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        UnregisterAckHandler();
        if (_handlerRegistered && NetworkManager.Singleton?.CustomMessagingManager != null)
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(MSG_SPAWN_TOKEN);
        if (Instance == this) Instance = null;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;
        if (clientId == NetworkManager.Singleton.LocalClientId)
            _pendingRequests.Clear();
        if (!NetworkManager.Singleton.IsServer) return;
        var keys = new List<(ulong Client, int Request)>();
        foreach (var entry in _processedRequests)
            if (entry.Key.Client == clientId) keys.Add(entry.Key);
        foreach (var key in keys) _processedRequests.Remove(key);
    }

    /// <summary>Любой клиент вызывает для создания токена.</summary>
    public void RequestSpawnToken()
    {
        if (NetworkManager.Singleton == null || tokenPrefab == null) return;
        if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsConnectedClient) return;

        Vector3 pos = GetDefaultSpawnPosition();

        if (NetworkManager.Singleton.IsServer)
        {
            SpawnTokenForClient(pos, NetworkManager.Singleton.LocalClientId);
        }
        else
        {
            int requestId = ++_nextRequestId;
            _pendingRequests[requestId] = true;
            StartCoroutine(SendSpawnRequestRoutine(requestId));
        }
    }

    private IEnumerator SendSpawnRequestRoutine(int requestId)
    {
        for (int attempt = 0; attempt < MaxSpawnAttempts; attempt++)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsConnectedClient || !_pendingRequests.ContainsKey(requestId))
                yield break;
            using (var writer = new FastBufferWriter(sizeof(int),
                Unity.Collections.Allocator.Temp))
            {
                writer.WriteValueSafe(requestId);
                nm.CustomMessagingManager.SendNamedMessage(
                    MSG_SPAWN_TOKEN, NetworkManager.ServerClientId, writer);
            }
            float deadline = Time.unscaledTime + 2f;
            while (Time.unscaledTime < deadline && _pendingRequests.ContainsKey(requestId))
                yield return null;
            if (!_pendingRequests.ContainsKey(requestId)) yield break;
        }
        _pendingRequests.Remove(requestId);
        DiceUI.Instance?.ShowToolNotice("Не удалось создать токен. Попробуйте ещё раз.");
    }

    private void OnSpawnTokenAck(ulong senderId, FastBufferReader reader)
    {
        if (senderId != NetworkManager.ServerClientId
            || !reader.TryBeginRead(sizeof(int) + sizeof(bool) + sizeof(ulong))) return;
        reader.ReadValueSafe(out int requestId);
        reader.ReadValueSafe(out bool success);
        reader.ReadValueSafe(out ulong netId);
        if (!_pendingRequests.Remove(requestId)) return;
        if (!success) DiceUI.Instance?.ShowToolNotice("Сервер не смог создать токен.");
        else LateJoinSync.RequestObjectRecovery(netId);
    }

    /// <summary>Server: duplicate token at default spawn; copier becomes owner/spawner.</summary>
    public TokenController CopyToken(TokenController source, ulong copierClientId)
    {
        if (!NetworkManager.Singleton.IsServer || source == null || !source.IsSpawned || tokenPrefab == null)
            return null;

        TokenController copy = SpawnTokenForClient(GetDefaultSpawnPosition(), copierClientId);
        if (copy == null) return null;

        byte[] portrait = TokenImageSync.GetPortraitBytesForCopy(source);
        if (portrait != null && portrait.Length > 0)
            TokenImageSync.BroadcastImage(copy.NetworkObjectId, portrait);

        Debug.Log($"[Token] Copied {source.NetworkObjectId} -> {copy.NetworkObjectId} for {copierClientId}");
        return copy;
    }

    public static Vector3 GetDefaultSpawnPosition()
    {
        return MapController.Instance != null
            ? MapController.Instance.transform.position
            : Vector3.zero;
    }

    private void OnSpawnTokenRequest(ulong senderId, FastBufferReader reader)
    {
        if (!reader.TryBeginRead(sizeof(int))) return;
        reader.ReadValueSafe(out int requestId);
        var key = (senderId, requestId);
        if (_processedRequests.TryGetValue(key, out ulong previous))
        {
            SendSpawnAck(senderId, requestId, previous);
            return;
        }
        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(senderId)) return;
        var token = SpawnTokenForClient(GetDefaultSpawnPosition(), senderId);
        _processedRequests[key] = token != null ? token.NetworkObjectId : 0;
        SendSpawnAck(senderId, requestId, token != null ? token.NetworkObjectId : 0);
    }

    private static void SendSpawnAck(ulong clientId, int requestId, ulong netId)
    {
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm == null) return;
        using var writer = new FastBufferWriter(sizeof(int) + sizeof(bool) + sizeof(ulong),
            Unity.Collections.Allocator.Temp);
        writer.WriteValueSafe(requestId);
        writer.WriteValueSafe(netId != 0);
        writer.WriteValueSafe(netId);
        cmm.SendNamedMessage(MSG_SPAWN_TOKEN_ACK, clientId, writer);
    }

    private TokenController SpawnTokenForClient(Vector3 pos, ulong ownerId)
    {
        if (tokenPrefab == null) return null;
        pos.y = spawnHeight;
        NetworkObject netObj = Instantiate(tokenPrefab, pos, Quaternion.identity);
        netObj.SpawnWithObservers = false;
        netObj.SpawnWithOwnership(ownerId);
        netObj.DontDestroyWithOwner = true;
        if (LateJoinSync.Instance != null) LateJoinSync.Instance.QueueWorldObject(netObj);
        else
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                if (clientId != NetworkManager.ServerClientId) netObj.NetworkShow(clientId);
        netObj.GetComponent<TokenController>()?.SnapToGrid();
        Debug.Log($"[Token] Spawned for owner {ownerId}");
        return netObj.GetComponent<TokenController>();
    }
}
