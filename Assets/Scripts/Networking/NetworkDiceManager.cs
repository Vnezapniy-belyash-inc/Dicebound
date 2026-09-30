using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Менеджер сетевых кубиков (MonoBehaviour).
/// Клиент шлёт запрос через CustomMessagingManager, сервер спавнит.
/// Кубики остаются на столе (не авто-бросок, не авто-удаление).
/// </summary>
public class NetworkDiceManager : MonoBehaviour
{
    private const string MSG_SPAWN_DICE = "SpawnDice";
    private const string MSG_SPAWN_DICE_ACK = "SpawnDiceAck";
    private const int MaxSpawnAttempts = 3;

    [Header("Prefab")]
    public NetworkObject dicePrefab;

    [Header("Spawn settings")]
    public float spawnHeight = 2f;

    public static NetworkDiceManager Instance { get; private set; }

    private bool _handlerRegistered;
    private bool _ackHandlerRegistered;
    private int _nextRequestId;
    private readonly HashSet<int> _pendingRequests = new();
    private readonly Dictionary<(ulong Client, int Request), ulong> _processedRequests = new();

    // Очередь спавнов из сетевого потока → main thread
    private struct PendingSpawn
    {
        public DieType type;
        public Vector3 pos;
        public ulong ownerId;
        public int requestId;
    }
    private readonly Queue<PendingSpawn> _pendingSpawns = new();

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
        }
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
        {
            _pendingSpawns.Clear();
            return;
        }
        // Обрабатываем спавны из очереди (пришли из сетевого потока)
        lock (_pendingSpawns)
        {
            for (int i = 0; i < 4 && _pendingSpawns.Count > 0; i++)
            {
                var s = _pendingSpawns.Dequeue();
                if (NetworkManager.Singleton == null
                    || !NetworkManager.Singleton.ConnectedClients.ContainsKey(s.ownerId))
                    continue;
                var spawned = DoSpawn(s.type, s.pos, s.ownerId);
                _processedRequests[(s.ownerId, s.requestId)] = spawned != null
                    ? spawned.NetworkObjectId : 0;
                SendSpawnAck(s.ownerId, s.requestId,
                    spawned != null ? spawned.NetworkObjectId : 0);
            }
        }
    }

    private void OnServerStarted()
    {
        if (!_handlerRegistered && NetworkManager.Singleton.CustomMessagingManager != null)
        {
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
                MSG_SPAWN_DICE, OnSpawnDiceRequest);
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
                "DespawnDice", OnDespawnDiceRequest);
            _handlerRegistered = true;
        }

        Debug.Log("[DiceManager] Spawn handler registered");
    }

    private void OnServerStopped(bool wasHost)
    {
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (_handlerRegistered && cmm != null)
        {
            cmm.UnregisterNamedMessageHandler(MSG_SPAWN_DICE);
            cmm.UnregisterNamedMessageHandler("DespawnDice");
        }
        _handlerRegistered = false;
        _pendingSpawns.Clear();
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
        cmm.RegisterNamedMessageHandler(MSG_SPAWN_DICE_ACK, OnSpawnDiceAck);
        _ackHandlerRegistered = true;
    }

    private void UnregisterAckHandler()
    {
        if (!_ackHandlerRegistered) return;
        NetworkManager.Singleton?.CustomMessagingManager?.UnregisterNamedMessageHandler(
            MSG_SPAWN_DICE_ACK);
        _ackHandlerRegistered = false;
    }

    private void OnDespawnDiceRequest(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong netId);
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(netId, out var netObj))
            return;

        if (!netObj.TryGetComponent<NetworkDice>(out var dice)
            || !NetworkPermissions.CanDespawnDice(senderId, dice))
        {
            Debug.LogWarning($"[DiceManager] Despawn rejected for {netId} from {senderId}");
            return;
        }

        netObj.Despawn();
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
            if (_handlerRegistered && NetworkManager.Singleton.CustomMessagingManager != null)
            {
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(MSG_SPAWN_DICE);
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler("DespawnDice");
            }
        }
        UnregisterAckHandler();
        if (Instance == this) Instance = null;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;
        if (clientId == NetworkManager.Singleton.LocalClientId) _pendingRequests.Clear();
        if (!NetworkManager.Singleton.IsServer) return;
        var keys = new List<(ulong Client, int Request)>();
        foreach (var entry in _processedRequests)
            if (entry.Key.Client == clientId) keys.Add(entry.Key);
        foreach (var key in keys) _processedRequests.Remove(key);
    }

    /// <summary>
    /// Спавнит кубик на столе. Вызывать ТОЛЬКО из main thread (UI).
    /// </summary>
    public void RequestSpawnDie(DieType type, Vector3 spawnPos)
    {
        if (NetworkManager.Singleton == null || dicePrefab == null) return;
        if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsConnectedClient) return;

        if (NetworkManager.Singleton.IsServer)
        {
            DoSpawn(type, spawnPos, NetworkManager.Singleton.LocalClientId);
        }
        else
        {
            int requestId = ++_nextRequestId;
            _pendingRequests.Add(requestId);
            StartCoroutine(SendSpawnRequestRoutine(requestId, type, spawnPos));
        }
    }

    private IEnumerator SendSpawnRequestRoutine(int requestId, DieType type, Vector3 pos)
    {
        for (int attempt = 0; attempt < MaxSpawnAttempts; attempt++)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsConnectedClient || !_pendingRequests.Contains(requestId))
                yield break;
            using (var writer = new FastBufferWriter(2 * sizeof(int) + 3 * sizeof(float),
                Unity.Collections.Allocator.Temp))
            {
                writer.WriteValueSafe(requestId);
                writer.WriteValueSafe((int)type);
                writer.WriteValueSafe(pos.x);
                writer.WriteValueSafe(pos.y);
                writer.WriteValueSafe(pos.z);
                nm.CustomMessagingManager.SendNamedMessage(
                    MSG_SPAWN_DICE, NetworkManager.ServerClientId, writer);
            }
            float deadline = Time.unscaledTime + 2f;
            while (Time.unscaledTime < deadline && _pendingRequests.Contains(requestId))
                yield return null;
            if (!_pendingRequests.Contains(requestId)) yield break;
        }
        _pendingRequests.Remove(requestId);
        DiceUI.Instance?.ShowToolNotice("Не удалось создать кубик. Попробуйте ещё раз.");
    }

    private void OnSpawnDiceAck(ulong senderId, FastBufferReader reader)
    {
        if (senderId != NetworkManager.ServerClientId
            || !reader.TryBeginRead(sizeof(int) + sizeof(bool) + sizeof(ulong))) return;
        reader.ReadValueSafe(out int requestId);
        reader.ReadValueSafe(out bool success);
        reader.ReadValueSafe(out ulong netId);
        if (!_pendingRequests.Remove(requestId)) return;
        if (!success) DiceUI.Instance?.ShowToolNotice("Сервер не смог создать кубик.");
        else LateJoinSync.RequestObjectRecovery(netId);
    }

    /// <summary>Сетевой поток: кладём запрос в очередь на main thread.</summary>
    private void OnSpawnDiceRequest(ulong senderId, FastBufferReader reader)
    {
        if (!reader.TryBeginRead(2 * sizeof(int) + 3 * sizeof(float))) return;
        reader.ReadValueSafe(out int requestId);
        reader.ReadValueSafe(out int typeInt);
        reader.ReadValueSafe(out float posX);
        reader.ReadValueSafe(out float posY);
        reader.ReadValueSafe(out float posZ);

        if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(senderId)) return;
        if (typeInt < (int)DieType.d4 || typeInt > (int)DieType.d20
            || !ValidPosition(posX) || !ValidPosition(posY) || !ValidPosition(posZ))
        {
            SendSpawnAck(senderId, requestId, 0);
            return;
        }
        var key = (senderId, requestId);
        if (_processedRequests.TryGetValue(key, out ulong previous))
        {
            if (previous != ulong.MaxValue) SendSpawnAck(senderId, requestId, previous);
            return;
        }
        _processedRequests[key] = ulong.MaxValue;
        var pending = new PendingSpawn
        {
            type = (DieType)typeInt,
            pos = new Vector3(posX, posY, posZ),
            ownerId = senderId,
            requestId = requestId
        };

        lock (_pendingSpawns)
        {
            _pendingSpawns.Enqueue(pending);
        }

        Debug.Log($"[DiceManager] Queued spawn {pending.type} for player {senderId}");
    }

    private static bool ValidPosition(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) < 10000f;

    private static void SendSpawnAck(ulong clientId, int requestId, ulong netId)
    {
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm == null) return;
        using var writer = new FastBufferWriter(sizeof(int) + sizeof(bool) + sizeof(ulong),
            Unity.Collections.Allocator.Temp);
        writer.WriteValueSafe(requestId);
        writer.WriteValueSafe(netId != 0);
        writer.WriteValueSafe(netId);
        cmm.SendNamedMessage(MSG_SPAWN_DICE_ACK, clientId, writer);
    }

    /// <summary>Main thread: фактический спавн через InstantiateAndSpawn.</summary>
    private NetworkObject DoSpawn(DieType type, Vector3 spawnPos, ulong ownerId)
    {
        if (dicePrefab == null) return null;
        Vector3 pos = spawnPos + Random.insideUnitSphere * 0.3f;
        pos.y = spawnHeight;

        NetworkObject netObj = Instantiate(dicePrefab.gameObject, pos, Random.rotation).GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError("[DiceManager] Instantiate returned null!");
            return null;
        }

        // Спавним объект с правильным владельцем сразу (до OnNetworkSpawn)
        netObj.SpawnWithObservers = false;
        netObj.SpawnWithOwnership(ownerId);
        netObj.DontDestroyWithOwner = true;
        if (LateJoinSync.Instance != null) LateJoinSync.Instance.QueueWorldObject(netObj);
        else
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                if (clientId != NetworkManager.ServerClientId) netObj.NetworkShow(clientId);

        var dice = netObj.GetComponent<NetworkDice>();
        if (dice != null) dice.Init(type);
        return netObj;
    }
}
