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

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.CustomMessagingManager != null)
        {
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
                MSG_SPAWN_DICE, OnSpawnDiceRequest);
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(MSG_SPAWN_DICE);
        }
    }

    /// <summary>
    /// Спавнит кубик на столе. Без авто-броска, без авто-удаления.
    /// Кубик виден всем, владелец может бросить его позже.
    /// </summary>
    public void RequestSpawnDie(DieType type, Vector3 spawnPos)
    {
        if (NetworkManager.Singleton == null || dicePrefab == null) return;

        if (NetworkManager.Singleton.IsServer)
        {
            SpawnDieForClient(type, spawnPos, NetworkManager.Singleton.LocalClientId);
        }
        else
        {
            using var writer = new FastBufferWriter(
                sizeof(int) + sizeof(float) * 3, Unity.Collections.Allocator.Temp);
            writer.WriteValue((int)type);
            writer.WriteValue(spawnPos.x);
            writer.WriteValue(spawnPos.y);
            writer.WriteValue(spawnPos.z);

            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                MSG_SPAWN_DICE, NetworkManager.ServerClientId, writer);
        }
    }

    private void OnSpawnDiceRequest(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValue(out int typeInt);
        reader.ReadValue(out float posX);
        reader.ReadValue(out float posY);
        reader.ReadValue(out float posZ);

        DieType type = (DieType)typeInt;
        Vector3 spawnPos = new Vector3(posX, posY, posZ);

        Debug.Log($"[DiceManager] Server: Player {senderId} spawns {type}");
        SpawnDieForClient(type, spawnPos, senderId);
    }

    private void SpawnDieForClient(DieType type, Vector3 spawnPos, ulong ownerId)
    {
        Vector3 pos = spawnPos + Random.insideUnitSphere * 0.3f;
        pos.y = spawnHeight;

        NetworkObject netObj = Instantiate(dicePrefab, pos, Random.rotation);
        netObj.SpawnWithOwnership(ownerId);

        var dice = netObj.GetComponent<NetworkDice>();
        dice.Init(type);
        // Кубик лежит на столе. Владелец бросит через dice.Roll() позже.
    }
}
