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

    [Header("Prefab")]
    public NetworkObject dicePrefab;

    [Header("Spawn settings")]
    public float spawnHeight = 2f;

    public static NetworkDiceManager Instance { get; private set; }

    private bool _handlerRegistered;

    // Очередь спавнов из сетевого потока → main thread
    private struct PendingSpawn
    {
        public DieType type;
        public Vector3 pos;
        public ulong ownerId;
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
        }
    }

    private void Update()
    {
        // Обрабатываем спавны из очереди (пришли из сетевого потока)
        lock (_pendingSpawns)
        {
            while (_pendingSpawns.Count > 0)
            {
                var s = _pendingSpawns.Dequeue();
                DoSpawn(s.type, s.pos, s.ownerId);
            }
        }
    }

    private void OnServerStarted()
    {
        if (!_handlerRegistered && NetworkManager.Singleton.CustomMessagingManager != null)
        {
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
                MSG_SPAWN_DICE, OnSpawnDiceRequest);
            _handlerRegistered = true;
        }

        Debug.Log("[DiceManager] Spawn handler registered");
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
            if (_handlerRegistered)
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(MSG_SPAWN_DICE);
        }
    }

    /// <summary>
    /// Спавнит кубик на столе. Вызывать ТОЛЬКО из main thread (UI).
    /// </summary>
    public void RequestSpawnDie(DieType type, Vector3 spawnPos)
    {
        if (NetworkManager.Singleton == null || dicePrefab == null) return;

        if (NetworkManager.Singleton.IsServer)
        {
            DoSpawn(type, spawnPos, NetworkManager.Singleton.LocalClientId);
        }
        else
        {
            int totalSize = sizeof(int) + sizeof(float) * 3;
            var writer = new FastBufferWriter(totalSize, Unity.Collections.Allocator.Temp);
            writer.WriteValueSafe((int)type);
            writer.WriteValueSafe(spawnPos.x);
            writer.WriteValueSafe(spawnPos.y);
            writer.WriteValueSafe(spawnPos.z);

            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                MSG_SPAWN_DICE, NetworkManager.ServerClientId, writer);
            writer.Dispose();
        }
    }

    /// <summary>Сетевой поток: кладём запрос в очередь на main thread.</summary>
    private void OnSpawnDiceRequest(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int typeInt);
        reader.ReadValueSafe(out float posX);
        reader.ReadValueSafe(out float posY);
        reader.ReadValueSafe(out float posZ);

        var pending = new PendingSpawn
        {
            type = (DieType)typeInt,
            pos = new Vector3(posX, posY, posZ),
            ownerId = senderId
        };

        lock (_pendingSpawns)
        {
            _pendingSpawns.Enqueue(pending);
        }

        Debug.Log($"[DiceManager] Queued spawn {pending.type} for player {senderId}");
    }

    /// <summary>Main thread: фактический спавн через InstantiateAndSpawn.</summary>
    private void DoSpawn(DieType type, Vector3 spawnPos, ulong ownerId)
    {
        Vector3 pos = spawnPos + Random.insideUnitSphere * 0.3f;
        pos.y = spawnHeight;

        NetworkObject netObj = Instantiate(dicePrefab.gameObject, pos, Random.rotation).GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError("[DiceManager] Instantiate returned null!");
            return;
        }

        // Спавним объект (виден всем)
        netObj.Spawn();

        // Назначаем владельца
        if (ownerId != NetworkManager.Singleton.LocalClientId)
            netObj.ChangeOwnership(ownerId);

        // Инициализируем тип ПОСЛЕ спавна — ClientRpc требует спавненный объект
        var dice = netObj.GetComponent<NetworkDice>();
        if (dice != null) dice.Init(type);
    }
}
