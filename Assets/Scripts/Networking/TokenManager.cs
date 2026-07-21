using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Менеджер токенов: спавн по запросу клиента.
/// Аналогичен NetworkDiceManager.
/// </summary>
public class TokenManager : MonoBehaviour
{
    private const string MSG_SPAWN_TOKEN = "SpawnToken";

    [Header("Prefab")]
    public NetworkObject tokenPrefab;

    [Header("Spawn")]
    public float spawnHeight = 0.1f;

    public static TokenManager Instance { get; private set; }

    private bool _handlerRegistered;

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

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        if (_handlerRegistered && NetworkManager.Singleton?.CustomMessagingManager != null)
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(MSG_SPAWN_TOKEN);
    }

    /// <summary>Любой клиент вызывает для создания токена.</summary>
    public void RequestSpawnToken()
    {
        if (NetworkManager.Singleton == null || tokenPrefab == null) return;

        Vector3 pos = GetDefaultSpawnPosition();

        if (NetworkManager.Singleton.IsServer)
        {
            SpawnTokenForClient(pos, NetworkManager.Singleton.LocalClientId);
        }
        else
        {
            var writer = new FastBufferWriter(sizeof(float) * 3, Unity.Collections.Allocator.Temp);
            writer.WriteValueSafe(pos.x);
            writer.WriteValueSafe(pos.y);
            writer.WriteValueSafe(pos.z);

            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                MSG_SPAWN_TOKEN, NetworkManager.ServerClientId, writer);
            writer.Dispose();
        }
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
        reader.ReadValueSafe(out float x);
        reader.ReadValueSafe(out float y);
        reader.ReadValueSafe(out float z);
        SpawnTokenForClient(new Vector3(x, y, z), senderId);
    }

    private TokenController SpawnTokenForClient(Vector3 pos, ulong ownerId)
    {
        pos.y = spawnHeight;
        NetworkObject netObj = Instantiate(tokenPrefab, pos, Quaternion.identity);
        netObj.SpawnWithOwnership(ownerId);
        netObj.DontDestroyWithOwner = true;
        Debug.Log($"[Token] Spawned for owner {ownerId}");
        return netObj.GetComponent<TokenController>();
    }
}
