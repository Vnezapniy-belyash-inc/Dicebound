using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server: staggers late-join / reconnect sync to avoid transport queue overflow.
/// Client signals when scene/objects are ready; then server pushes state.
/// </summary>
public class LateJoinSync : MonoBehaviour
{
    private const string MSG_CLIENT_READY = "LateJoinClientReady";
    private const string MSG_WORLD_MANIFEST = "WorldObjectManifest";
    private const string MSG_WORLD_REPAIR = "WorldObjectRepair";
    private const string MSG_WORLD_GONE = "WorldObjectGone";
    private const string MSG_WORLD_ACK = "WorldObjectAck";
    private const int ManifestIdsPerMessage = 64;
    private const int RepairIdsPerMessage = 32;
    private const int MaxManifestObjects = 16384;

    public static LateJoinSync Instance { get; private set; }

    [Tooltip("Short pause before pushing state after client ready")]
    public float initialDelay = 0.35f;

    private readonly Dictionary<ulong, Coroutine> _syncRoutines = new();
    private readonly HashSet<ulong> _initialSyncStarted = new();
    private readonly Dictionary<ulong, Queue<NetworkObject>> _worldQueues = new();
    private readonly HashSet<ulong> _worldDeliveryClients = new();
    private readonly HashSet<(ulong Client, ulong Object)> _queuedWorldObjects = new();
    private bool _handlerRegistered;
    private bool _clientHandlersRegistered;
    private int _nextManifestVersion;
    private int _sceneWorldVersion;
    private readonly HashSet<ulong> _sceneWorldClients = new();
    private readonly Dictionary<ulong, int> _sceneWorldExpectedVersions = new();
    private readonly Dictionary<ulong, int> _sceneWorldAcks = new();
    private int _incomingManifestVersion = -1;
    private int _verifiedManifestVersion = -1;
    private int _incomingManifestChunks;
    private int _incomingManifestCount;
    private readonly Dictionary<int, ulong[]> _manifestChunks = new();
    private readonly HashSet<ulong> _goneWorldObjects = new();
    private Coroutine _verifyWorldRoutine;
    private readonly Dictionary<ulong, (float WindowStart, int Count)> _repairRate = new();
    private const int WorldObjectsPerFrame = 4;

    public bool AllSceneWorldClientsReady
    {
        get
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || _sceneWorldVersion <= 0) return false;
            foreach (ulong client in _sceneWorldClients)
                if (nm.ConnectedClients.ContainsKey(client)
                    && (!_sceneWorldExpectedVersions.TryGetValue(client, out int expected)
                        || !_sceneWorldAcks.TryGetValue(client, out int ack) || ack != expected)) return false;
            return true;
        }
    }

    public static void EnsureInstance()
    {
        if (Instance != null) return;
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        if (nm.GetComponent<LateJoinSync>() == null)
            nm.gameObject.AddComponent<LateJoinSync>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        RegisterServerHandler();
        RegisterClientHandlers();
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += RegisterServerHandler;
            NetworkManager.Singleton.OnServerStopped += OnServerStopped;
            NetworkManager.Singleton.OnClientStopped += OnClientStopped;
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted -= RegisterServerHandler;
            NetworkManager.Singleton.OnServerStopped -= OnServerStopped;
            NetworkManager.Singleton.OnClientStopped -= OnClientStopped;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }

        UnregisterServerHandler();
        UnregisterClientHandlers();
        SceneTransitionMarker.ResetRegistration();
        CellMarker.ResetRegistration();
        if (Instance == this)
            Instance = null;
    }

    private void OnServerStopped(bool wasHost)
    {
        UnregisterServerHandler();
        SceneTransitionMarker.ResetRegistration();
        CellMarker.ResetRegistration();
        foreach (var routine in _syncRoutines.Values)
            if (routine != null) StopCoroutine(routine);
        _syncRoutines.Clear();
        _worldQueues.Clear();
        _worldDeliveryClients.Clear();
        _queuedWorldObjects.Clear();
        _repairRate.Clear();
        _nextManifestVersion = 0;
        _sceneWorldVersion = 0;
        _sceneWorldClients.Clear();
        _sceneWorldExpectedVersions.Clear();
        _sceneWorldAcks.Clear();
    }

    private void OnClientStopped(bool wasHost)
    {
        UnregisterClientHandlers();
        _incomingManifestVersion = -1;
        _verifiedManifestVersion = -1;
        _manifestChunks.Clear();
        _goneWorldObjects.Clear();
        if (_verifyWorldRoutine != null) StopCoroutine(_verifyWorldRoutine);
        _verifyWorldRoutine = null;
    }

    private void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening)
        {
            CellMarker.EnsureRegistered();
            SceneTransitionMarker.EnsureRegistered();
        }
        if (nm != null && clientId == nm.LocalClientId)
            RegisterClientHandlers();
    }

    private void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        if (nm.IsListening)
        {
            CellMarker.EnsureRegistered();
            SceneTransitionMarker.EnsureRegistered();
        }
        if (!nm.IsServer) return;
        int remaining = WorldObjectsPerFrame;
        bool progressed;
        do
        {
            progressed = false;
            foreach (var entry in _worldQueues)
            {
                if (remaining == 0) break;
                if (!nm.ConnectedClients.ContainsKey(entry.Key) || entry.Value.Count == 0)
                    continue;
                var obj = entry.Value.Dequeue();
                if (obj == null) continue;
                _queuedWorldObjects.Remove((entry.Key, obj.NetworkObjectId));
                if (obj.IsSpawned && !obj.IsNetworkVisibleTo(entry.Key))
                {
                    obj.NetworkShow(entry.Key);
                    if (!_syncRoutines.ContainsKey(entry.Key)
                        && obj.GetComponent<TokenController>() != null
                        && TokenImageSync.TryGetCachedPortrait(obj.NetworkObjectId,
                            out _))
                        StartCoroutine(SendPortraitAfterSpawn(entry.Key, obj.NetworkObjectId));
                }
                remaining--;
                progressed = true;
            }
        } while (remaining > 0 && progressed);
    }

    private IEnumerator SendPortraitAfterSpawn(ulong clientId, ulong netId)
    {
        yield return new WaitForSecondsRealtime(0.1f);
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsServer && nm.ConnectedClients.ContainsKey(clientId)
            && TokenImageSync.TryGetCachedPortrait(netId, out byte[] portrait))
            TokenImageSync.SendImageToClient(clientId, netId, portrait);
    }

    public void QueueWorldObject(NetworkObject obj)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || obj == null || !obj.IsSpawned) return;
        foreach (ulong clientId in nm.ConnectedClientsIds)
            if (clientId != NetworkManager.ServerClientId
                && _worldDeliveryClients.Contains(clientId)) QueueWorldObject(clientId, obj);
    }

    private void QueueWorldObject(ulong clientId, NetworkObject obj)
    {
        if (obj == null || !obj.IsSpawned || obj.IsNetworkVisibleTo(clientId)
            || !_queuedWorldObjects.Add((clientId, obj.NetworkObjectId))) return;
        if (!_worldQueues.TryGetValue(clientId, out var queue))
            _worldQueues[clientId] = queue = new Queue<NetworkObject>();
        queue.Enqueue(obj);
    }

    private static bool IsWorldObject(NetworkObject obj) =>
        obj != null && (obj.GetComponent<TokenController>() != null
            || obj.GetComponent<NetworkDice>() != null
            || obj.GetComponent<CellMarker>() != null
            || obj.GetComponent<SceneTransitionMarker>() != null);

    private IEnumerator ShowExistingWorldToClientRoutine(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) yield break;
        _worldDeliveryClients.Add(clientId);
        foreach (var obj in nm.SpawnManager.SpawnedObjectsList)
            if (IsWorldObject(obj))
                QueueWorldObject(clientId, obj);
        while (nm.IsServer && nm.ConnectedClients.ContainsKey(clientId)
            && _worldQueues.TryGetValue(clientId, out var queue) && queue.Count > 0)
            yield return null;
        // NetworkShow schedules spawn messages; let NGO flush them before sending portraits.
        yield return new WaitForSecondsRealtime(0.1f);
    }

    private void RegisterServerHandler()
    {
        if (_handlerRegistered) return;
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm == null || !NetworkManager.Singleton.IsServer) return;

        cmm.RegisterNamedMessageHandler(MSG_CLIENT_READY, OnClientReady);
        cmm.RegisterNamedMessageHandler(MSG_WORLD_REPAIR, OnWorldRepairRequest);
        cmm.RegisterNamedMessageHandler(MSG_WORLD_ACK, OnWorldAck);
        _handlerRegistered = true;
    }

    private void UnregisterServerHandler()
    {
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm != null && _handlerRegistered)
        {
            cmm.UnregisterNamedMessageHandler(MSG_CLIENT_READY);
            cmm.UnregisterNamedMessageHandler(MSG_WORLD_REPAIR);
            cmm.UnregisterNamedMessageHandler(MSG_WORLD_ACK);
        }

        _handlerRegistered = false;
        _initialSyncStarted.Clear();
    }

    private void RegisterClientHandlers()
    {
        if (_clientHandlersRegistered) return;
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm == null) return;
        cmm.RegisterNamedMessageHandler(MSG_WORLD_MANIFEST, OnWorldManifest);
        cmm.RegisterNamedMessageHandler(MSG_WORLD_GONE, OnWorldGone);
        _clientHandlersRegistered = true;
    }

    private void UnregisterClientHandlers()
    {
        if (!_clientHandlersRegistered) return;
        NetworkManager.Singleton?.CustomMessagingManager?.UnregisterNamedMessageHandler(
            MSG_WORLD_MANIFEST);
        NetworkManager.Singleton?.CustomMessagingManager?.UnregisterNamedMessageHandler(
            MSG_WORLD_GONE);
        _clientHandlersRegistered = false;
    }

    public void OnClientLeft(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && clientId == nm.LocalClientId)
        {
            _incomingManifestVersion = -1;
            _verifiedManifestVersion = -1;
            _manifestChunks.Clear();
            _goneWorldObjects.Clear();
            if (_verifyWorldRoutine != null) StopCoroutine(_verifyWorldRoutine);
            _verifyWorldRoutine = null;
        }
        _initialSyncStarted.Remove(clientId);
        if (_syncRoutines.TryGetValue(clientId, out Coroutine running))
            StopCoroutine(running);
        _syncRoutines.Remove(clientId);
        if (_worldQueues.TryGetValue(clientId, out var queue))
            foreach (var obj in queue)
                if (obj != null) _queuedWorldObjects.Remove((clientId, obj.NetworkObjectId));
        _worldQueues.Remove(clientId);
        _worldDeliveryClients.Remove(clientId);
        _repairRate.Remove(clientId);
        _sceneWorldClients.Remove(clientId);
        _sceneWorldExpectedVersions.Remove(clientId);
        _sceneWorldAcks.Remove(clientId);
    }

    private void OnClientReady(ulong clientId, FastBufferReader reader)
    {
        if (!NetworkManager.Singleton.IsServer
            || !NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) return;

        HostSceneCurtain.Instance?.SendCurtainStateToClient(clientId);

        if (_syncRoutines.ContainsKey(clientId)) return;
        if (_initialSyncStarted.Contains(clientId))
            _syncRoutines[clientId] = StartCoroutine(PortraitsAndDiceOnlyRoutine(clientId));
        else
        {
            _initialSyncStarted.Add(clientId);
            BeginForClient(clientId);
        }
    }

    /// <summary>Client: tell server we are ready for portrait/dice catch-up sync.</summary>
    public static void NotifyServerReady()
    {
        var nm = NetworkManager.Singleton;
        var cmm = nm?.CustomMessagingManager;
        if (nm == null || cmm == null || nm.IsServer) return;

        using var writer = new FastBufferWriter(0, Allocator.Temp);
        cmm.SendNamedMessage(MSG_CLIENT_READY, NetworkManager.ServerClientId, writer);
        Debug.Log("[LateJoin] Client ready signal sent");
    }

    public static void RequestObjectRecovery(ulong netId)
    {
        var nm = NetworkManager.Singleton;
        if (Instance == null || nm == null || !nm.IsConnectedClient || nm.IsServer
            || netId == 0) return;
        Instance.StartCoroutine(Instance.VerifySingleWorldObjectRoutine(netId));
    }

    private IEnumerator VerifySingleWorldObjectRoutine(ulong netId)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            yield return new WaitForSecondsRealtime(attempt == 0 ? 30f : 5f);
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsConnectedClient || nm.IsServer) yield break;
            if (nm.SpawnManager.SpawnedObjects.ContainsKey(netId)
                || _goneWorldObjects.Contains(netId)) yield break;
            using var writer = new FastBufferWriter(sizeof(int) + sizeof(ulong), Allocator.Temp);
            writer.WriteValueSafe(1);
            writer.WriteValueSafe(netId);
            nm.CustomMessagingManager.SendNamedMessage(
                MSG_WORLD_REPAIR, NetworkManager.ServerClientId, writer);
        }
        yield return new WaitForSecondsRealtime(5f);
        var manager = NetworkManager.Singleton;
        if (manager != null && manager.IsConnectedClient
            && !manager.SpawnManager.SpawnedObjects.ContainsKey(netId)
            && !_goneWorldObjects.Contains(netId))
            DiceUI.Instance?.ShowToolNotice("Созданный объект не загрузился. Переподключитесь.");
    }

    /// <summary>
    /// Waits for NGO connection and (if applicable) map download before requesting server sync.
    /// </summary>
    public static IEnumerator SendReadyAfterSceneLoad()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.IsServer) yield break;

        for (int i = 0; i < 80; i++)
        {
            if (nm.IsConnectedClient && nm.CustomMessagingManager != null)
                break;
            yield return new WaitForSeconds(0.1f);
        }

        CellMarker.EnsureRegistered();
        SceneTransitionMarker.EnsureRegistered();
        TokenImageSync.EnsureInstance();

        // Give NGO time to spawn scene NetworkObjects (tokens, dice, etc.)
        yield return new WaitForSeconds(1.0f);

        // If a heavy map is downloading, wait for it to finish reassembling.
        // MapSync.Reassemble() also sends a ready signal; this path is the fallback.
        float waited = 0f;
        while (MapSync.Instance != null && MapSync.Instance.IsReceivingMap && waited < 90f)
        {
            yield return new WaitForSeconds(0.25f);
            waited += 0.25f;
        }

        if (MapSync.Instance == null || !MapSync.Instance.IsReceivingMap)
            NotifyServerReady();
    }

    public void BeginForClient(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        if (clientId == nm.LocalClientId) return;

        if (_syncRoutines.TryGetValue(clientId, out Coroutine running))
            StopCoroutine(running);

        _syncRoutines[clientId] = StartCoroutine(SyncClientRoutine(clientId));
    }

    private IEnumerator SyncClientRoutine(ulong clientId)
    {
        Debug.Log($"[LateJoin] Sync started for client {clientId}");
        yield return new WaitForSeconds(initialDelay);

        HostSceneCurtain.Instance?.SendCurtainStateToClient(clientId);

        PlayerRegistry.Instance?.SendFullStateToClient(clientId);
        yield return new WaitForSeconds(0.25f);

        if (MapSync.Instance != null)
            yield return MapSync.Instance.SendMapWithRetriesToClientRoutine(clientId);

        // The map coroutine waits for the client's successful decode acknowledgement.

        yield return ShowExistingWorldToClientRoutine(clientId);
        yield return SendWorldManifestRoutine(clientId);

        if (TokenImageSync.Instance != null)
            yield return TokenImageSync.Instance.SendAllPortraitsToClientRoutine(clientId);

        _syncRoutines.Remove(clientId);
        Debug.Log($"[LateJoin] Sync complete for client {clientId}");
    }

    private IEnumerator PortraitsAndDiceOnlyRoutine(ulong clientId)
    {
        Debug.Log($"[LateJoin] Portrait/dice re-sync for client {clientId}");
        HostSceneCurtain.Instance?.SendCurtainStateToClient(clientId);
        yield return new WaitForSeconds(0.25f);

        yield return ShowExistingWorldToClientRoutine(clientId);
        yield return SendWorldManifestRoutine(clientId);

        if (TokenImageSync.Instance != null)
            yield return TokenImageSync.Instance.SendAllPortraitsToClientRoutine(clientId);

        _syncRoutines.Remove(clientId);
        Debug.Log($"[LateJoin] Portrait/dice re-sync complete for client {clientId}");
    }

    private IEnumerator SendWorldManifestRoutine(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || !nm.ConnectedClients.ContainsKey(clientId))
            yield break;
        var ids = new List<ulong>();
        foreach (var obj in nm.SpawnManager.SpawnedObjectsList)
            if (obj != null && obj.IsSpawned && IsWorldObject(obj))
                ids.Add(obj.NetworkObjectId);
        if (ids.Count > MaxManifestObjects)
        {
            Debug.LogWarning("[LateJoin] World manifest exceeds 16384 objects");
            yield break;
        }
        Debug.Log($"[LateJoin] Sending world manifest: {ids.Count} objects to client {clientId}");
        int version = ++_nextManifestVersion;
        if (_sceneWorldClients.Contains(clientId)) _sceneWorldExpectedVersions[clientId] = version;
        yield return SendWorldManifestPayloadRoutine(clientId, ids, version);
    }

    public void SynchronizeCurrentWorldForAll()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        _sceneWorldVersion++;
        _sceneWorldClients.Clear();
        _sceneWorldExpectedVersions.Clear();
        _sceneWorldAcks.Clear();
        foreach (ulong clientId in nm.ConnectedClientsIds)
        {
            if (clientId == NetworkManager.ServerClientId) continue;
            _sceneWorldClients.Add(clientId);
            int manifestVersion = ++_nextManifestVersion;
            _sceneWorldExpectedVersions[clientId] = manifestVersion;
            StartCoroutine(SynchronizeCurrentWorldRoutine(clientId, _sceneWorldVersion, manifestVersion));
        }
    }

    private IEnumerator SynchronizeCurrentWorldRoutine(ulong clientId, int epoch, int manifestVersion)
    {
        yield return ShowExistingWorldToClientRoutine(clientId);
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || !nm.ConnectedClients.ContainsKey(clientId)
            || epoch != _sceneWorldVersion) yield break;
        var ids = new List<ulong>();
        foreach (var obj in nm.SpawnManager.SpawnedObjectsList)
            if (obj != null && obj.IsSpawned && IsWorldObject(obj)) ids.Add(obj.NetworkObjectId);
        if (ids.Count > MaxManifestObjects) yield break;
        yield return SendWorldManifestPayloadRoutine(clientId, ids, manifestVersion);
    }

    private IEnumerator SendWorldManifestPayloadRoutine(ulong clientId, List<ulong> ids, int version)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || !nm.ConnectedClients.ContainsKey(clientId)) yield break;
        int chunks = Mathf.Max(1, (ids.Count + ManifestIdsPerMessage - 1) / ManifestIdsPerMessage);
        for (int chunk = 0; chunk < chunks; chunk++)
        {
            if (!nm.IsServer || !nm.ConnectedClients.ContainsKey(clientId)) yield break;
            int count = Mathf.Min(ManifestIdsPerMessage,
                ids.Count - chunk * ManifestIdsPerMessage);
            using (var writer = new FastBufferWriter(4 * sizeof(int) + count * sizeof(ulong),
                Allocator.Temp))
            {
                writer.WriteValueSafe(version);
                writer.WriteValueSafe(ids.Count);
                writer.WriteValueSafe(chunks);
                writer.WriteValueSafe(chunk);
                for (int i = 0; i < count; i++)
                    writer.WriteValueSafe(ids[chunk * ManifestIdsPerMessage + i]);
                nm.CustomMessagingManager.SendNamedMessage(MSG_WORLD_MANIFEST, clientId, writer);
            }
            yield return null;
        }
    }

    private void SendWorldAck(int version)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsConnectedClient || nm.IsServer) return;
        using var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(version);
        nm.CustomMessagingManager.SendNamedMessage(MSG_WORLD_ACK, NetworkManager.ServerClientId, writer);
    }

    private void OnWorldAck(ulong clientId, FastBufferReader reader)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || !_sceneWorldClients.Contains(clientId)
            || !nm.ConnectedClients.ContainsKey(clientId) || !reader.TryBeginRead(sizeof(int))) return;
        reader.ReadValueSafe(out int version);
        if (_sceneWorldExpectedVersions.TryGetValue(clientId, out int expected) && version == expected)
            _sceneWorldAcks[clientId] = version;
    }

    private void OnWorldManifest(ulong senderId, FastBufferReader reader)
    {
        if (senderId != NetworkManager.ServerClientId || !reader.TryBeginRead(4 * sizeof(int)))
            return;
        reader.ReadValueSafe(out int version);
        reader.ReadValueSafe(out int total);
        reader.ReadValueSafe(out int chunks);
        reader.ReadValueSafe(out int index);
        if (version <= _verifiedManifestVersion || version < _incomingManifestVersion
            || total < 0 || total > MaxManifestObjects
            || chunks != Mathf.Max(1, (total + ManifestIdsPerMessage - 1) / ManifestIdsPerMessage)
            || index < 0 || index >= chunks) return;
        int count = Mathf.Min(ManifestIdsPerMessage, total - index * ManifestIdsPerMessage);
        if (!reader.TryBeginRead(count * sizeof(ulong))) return;
        if (version != _incomingManifestVersion)
        {
            _incomingManifestVersion = version;
            _incomingManifestCount = total;
            _incomingManifestChunks = chunks;
            _manifestChunks.Clear();
            _goneWorldObjects.Clear();
        }
        if (total != _incomingManifestCount || chunks != _incomingManifestChunks) return;
        var ids = new ulong[count];
        for (int i = 0; i < count; i++) reader.ReadValueSafe(out ids[i]);
        _manifestChunks[index] = ids;
        if (_manifestChunks.Count != chunks) return;
        _verifiedManifestVersion = version;
        var all = new List<ulong>(total);
        for (int i = 0; i < chunks; i++) all.AddRange(_manifestChunks[i]);
        if (_verifyWorldRoutine != null) StopCoroutine(_verifyWorldRoutine);
        _verifyWorldRoutine = StartCoroutine(VerifyWorldRoutine(version, all));
    }

    private IEnumerator VerifyWorldRoutine(int version, List<ulong> ids)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            float delay = attempt == 0 ? Mathf.Min(30f, 5f + ids.Count / 400f) : 5f;
            yield return new WaitForSecondsRealtime(delay);
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsConnectedClient || nm.IsServer
                || version != _incomingManifestVersion) yield break;
            var missing = new List<ulong>();
            foreach (ulong id in ids)
                if (!_goneWorldObjects.Contains(id)
                    && !nm.SpawnManager.SpawnedObjects.ContainsKey(id)) missing.Add(id);
            if (missing.Count == 0)
            {
                Debug.Log($"[LateJoin] World manifest verified: {ids.Count} objects");
                SendWorldAck(version);
                yield break;
            }
            Debug.LogWarning($"[LateJoin] Missing {missing.Count} world objects; repair {attempt + 1}/3");
            int repairCount = Mathf.Min(missing.Count, 512);
            for (int offset = 0; offset < repairCount; offset += RepairIdsPerMessage)
            {
                int count = Mathf.Min(RepairIdsPerMessage, repairCount - offset);
                using (var writer = new FastBufferWriter(sizeof(int) + count * sizeof(ulong),
                    Allocator.Temp))
                {
                    writer.WriteValueSafe(count);
                    for (int i = 0; i < count; i++) writer.WriteValueSafe(missing[offset + i]);
                    nm.CustomMessagingManager.SendNamedMessage(
                        MSG_WORLD_REPAIR, NetworkManager.ServerClientId, writer);
                }
                yield return null;
            }
        }
        yield return new WaitForSecondsRealtime(5f);
        var manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsConnectedClient
            || version != _incomingManifestVersion) yield break;
        foreach (ulong id in ids)
            if (!_goneWorldObjects.Contains(id)
                && !manager.SpawnManager.SpawnedObjects.ContainsKey(id))
            {
                DiceUI.Instance?.ShowToolNotice("Не все объекты карты загрузились. Переподключитесь.");
                yield break;
            }
    }

    private void OnWorldGone(ulong senderId, FastBufferReader reader)
    {
        if (senderId != NetworkManager.ServerClientId || !reader.TryBeginRead(sizeof(int)))
            return;
        reader.ReadValueSafe(out int count);
        if (count < 1 || count > RepairIdsPerMessage
            || !reader.TryBeginRead(count * sizeof(ulong))) return;
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out ulong id);
            _goneWorldObjects.Add(id);
        }
    }

    private void OnWorldRepairRequest(ulong clientId, FastBufferReader reader)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || !nm.ConnectedClients.ContainsKey(clientId)
            || !reader.TryBeginRead(sizeof(int))) return;
        reader.ReadValueSafe(out int count);
        if (count < 1 || count > RepairIdsPerMessage
            || !reader.TryBeginRead(count * sizeof(ulong))) return;
        _repairRate.TryGetValue(clientId, out var rate);
        if (Time.unscaledTime - rate.WindowStart >= 1f)
            rate = (Time.unscaledTime, 0);
        if (rate.Count >= 64) return;
        _repairRate[clientId] = (rate.WindowStart, rate.Count + 1);
        var ids = new ulong[count];
        for (int i = 0; i < count; i++) reader.ReadValueSafe(out ids[i]);
        var gone = new List<ulong>();
        var live = new List<ulong>();
        foreach (ulong id in ids)
        {
            if (nm.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj)
                && obj != null && IsWorldObject(obj)) live.Add(id);
            else gone.Add(id);
        }
        if (gone.Count > 0)
        {
            using var writer = new FastBufferWriter(sizeof(int) + gone.Count * sizeof(ulong),
                Allocator.Temp);
            writer.WriteValueSafe(gone.Count);
            foreach (ulong id in gone) writer.WriteValueSafe(id);
            nm.CustomMessagingManager.SendNamedMessage(MSG_WORLD_GONE, clientId, writer);
        }
        if (live.Count > 0) StartCoroutine(RepairWorldObjectsRoutine(clientId, live.ToArray()));
    }

    private IEnumerator RepairWorldObjectsRoutine(ulong clientId, ulong[] ids)
    {
        var nm = NetworkManager.Singleton;
        foreach (ulong id in ids)
        {
            if (nm == null || !nm.IsServer || !nm.ConnectedClients.ContainsKey(clientId))
                yield break;
            if (!nm.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj)
                || !IsWorldObject(obj)) continue;
            if (obj.IsNetworkVisibleTo(clientId))
            {
                obj.NetworkHide(clientId);
                yield return null;
            }
            if (obj.IsSpawned) obj.NetworkShow(clientId);
            yield return null;
            if (obj.GetComponent<TokenController>() != null
                && TokenImageSync.TryGetCachedPortrait(id, out byte[] portrait))
                TokenImageSync.SendImageToClient(clientId, id, portrait);
            yield return null;
        }
    }
}
