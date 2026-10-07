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
    private readonly TokenSessionState _sessionState = new();
    private readonly TokenNameRegistry _names = new();

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
            SceneTransitionMarker.EnsureRegistered();
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
        SceneTransitionMarker.EnsureRegistered();
    }

    private void OnServerStopped(bool wasHost)
    {
        if (_handlerRegistered && NetworkManager.Singleton?.CustomMessagingManager != null)
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(
                MSG_SPAWN_TOKEN);
        _handlerRegistered = false;
        _processedRequests.Clear();
        _sessionState.Clear();
        _names.Clear();
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
            if (DmPanelUI.Instance != null) DmPanelUI.Instance.ShowTokenCreation();
            else SpawnTokenForClient(pos, NetworkManager.Singleton.LocalClientId);
        }
        else
        {
            int requestId = ++_nextRequestId;
            _pendingRequests[requestId] = true;
            StartCoroutine(SendSpawnRequestRoutine(requestId));
        }
    }

    /// <summary>GM creates a named token with hidden state included before any client sees it.</summary>
    public TokenController CreateTokenAsHost(string name, bool hidden)
    {
        var manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsHost) return null;
        return SpawnTokenForClient(GetDefaultSpawnPosition(), manager.LocalClientId,
            hidden: hidden, requestedName: name);
    }

    public TokenController RestoreSceneToken(SceneToken data, GridManager grid,
        MasterTokenData masterData = null)
    {
        if (NetworkManager.Singleton?.IsHost != true) return null;
        return SpawnTokenForClient(grid.GridCoordinatesToWorld(data.position), NetworkManager.Singleton.LocalClientId,
            hidden: data.hidden, requestedName: data.name, restored: data, masterData: masterData);
    }

    private float _nextAssignmentCheck;
    private void Update()
    {
        if (NetworkManager.Singleton?.IsHost != true || Time.unscaledTime < _nextAssignmentCheck) return;
        _nextAssignmentCheck = Time.unscaledTime + 0.5f;
        RestoreAssignments();
    }
    private ulong ResolveParticipant(string nickname)
    {
        if (string.IsNullOrEmpty(nickname)) return NetworkManager.ServerClientId;
        foreach (ulong client in NetworkManager.Singleton.ConnectedClientsIds)
            if (client != NetworkManager.ServerClientId && string.Equals(PlayerColors.GetNickname(client), nickname, System.StringComparison.OrdinalIgnoreCase)) return client;
        return ulong.MaxValue;
    }
    private void RestoreAssignments()
    {
        foreach (var token in FindObjectsByType<TokenController>(FindObjectsInactive.Exclude))
        {
            if (!token.IsSpawned || string.IsNullOrEmpty(token.SavedOwnerNickname)) continue;
            ulong client = ResolveParticipant(token.SavedOwnerNickname);
            if (client == ulong.MaxValue) continue;
            if (token.ControllerClientId != client) token.ServerRestoreAssignment(client);
            if (token.IsHero) _sessionState.RestoreHero(client);
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

        TokenController copy = SpawnTokenForClient(GetDefaultSpawnPosition(), copierClientId,
            isCopy: true, hidden: source.IsHidden, requestedName: source.NameBase,
            masterData: source.CaptureMasterData());
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
        // Registration restores the previous hero record before this client can create another token.
        if (PlayerRegistry.Instance == null || !PlayerRegistry.Instance.IsRegisteredPlayer(senderId))
        {
            SendSpawnAck(senderId, requestId, 0);
            return;
        }
        RestoreAssignments();
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

    public void ReassignHeroRecord(ulong oldClientId, ulong newClientId) =>
        _sessionState.ReassignPlayer(oldClientId, newClientId);

    private TokenController SpawnTokenForClient(Vector3 pos, ulong ownerId,
        bool isCopy = false, bool hidden = false, string requestedName = null,
        SceneToken restored = null, MasterTokenData masterData = null)
    {
        if (tokenPrefab == null) return null;
        if (restored == null) pos.y = spawnHeight;
        NetworkObject netObj = Instantiate(tokenPrefab, pos, Quaternion.identity);
        if (restored != null) netObj.transform.localScale = restored.scale;
        var token = netObj.GetComponent<TokenController>();
        if (token == null)
        {
            Destroy(netObj.gameObject);
            return null;
        }
        ulong controller = restored != null ? restored.unassigned ? ulong.MaxValue : ResolveParticipant(restored.ownerNickname) : ownerId;
        bool hero = restored != null ? restored.hero : _sessionState.IssueHero(ownerId, NetworkPermissions.IsHostClient(ownerId), isCopy);
        if (hero && controller != ulong.MaxValue && controller != NetworkManager.ServerClientId) _sessionState.RestoreHero(controller);
        string basis = TokenNameRegistry.Normalize(requestedName ?? (hero ? "Герой" : "Токен"));
        var existingNames = new List<string>();
        foreach (var existing in FindObjectsByType<TokenController>(FindObjectsInactive.Exclude))
            if (existing.IsSpawned) existingNames.Add(existing.TokenName);
        bool conflicts = restored != null && existingNames.Exists(existing => string.Equals(existing, restored.name, System.StringComparison.OrdinalIgnoreCase));
        string name = restored != null && !conflicts ? restored.name : _names.Allocate(basis, existingNames);
        _names.Reserve(name);
        token.InitializeServerState(ownerId, hero, hidden, name, restored?.nameBase ?? basis,
            restored?.visionFeet ?? 0, restored?.id, controller, restored?.everyoneCanMove ?? false,
            restored != null ? restored.ownerNickname : ownerId != NetworkManager.ServerClientId ? PlayerColors.GetNickname(ownerId) : null);
        netObj.SpawnWithObservers = false;
        netObj.SpawnWithOwnership(ownerId);
        netObj.DontDestroyWithOwner = true;
        token.ServerApplyMasterData(masterData);
        if (LateJoinSync.Instance != null) LateJoinSync.Instance.QueueWorldObject(netObj);
        else
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                if (clientId != NetworkManager.ServerClientId) netObj.NetworkShow(clientId);
        if (restored == null) token.SnapToGrid();
        if (restored == null && NetworkPermissions.IsHostClient(ownerId))
        {
            string id = token.SceneId;
            GameMasterUndo.Record("создание токена", () => TokenController.FindSceneToken(id)?.RequestDespawn());
        }
        Debug.Log($"[Token] Spawned for owner {ownerId}");
        return token;
    }
}
