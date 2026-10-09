using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>Transfers the current map in bounded, versioned chunks.</summary>
public class MapSync : NetworkBehaviour
{
    private const string MSG_MAP_META = "MapMeta";
    private const string MSG_MAP_CHUNK = "MapChunk";
    private const string MSG_MAP_REQUEST = "MapRequest";
    private const string MSG_MAP_ACK = "MapAck";
    private const string MSG_MAP_PROGRESS = "MapProgress";
    private const string MSG_MAP_CANCEL = "MapCancel";
    private const int ChunkSize = 1000;
    private const int ChunksPerFrame = 4;
    private const int BatchChunks = 32;
    public const int MaxMapBytes = 16 * 1024 * 1024;
    private const int MaxRetries = 3;
    private const float AckTimeoutSeconds = 20f;
    private const float ClientRetryAfterSeconds = 12f;

    [Header("References")]
    public MapController mapController;

    public static MapSync Instance { get; private set; }
    public bool IsReceivingMap => _receivingMap;
    public bool HasClientCurrentMap(ulong client) => IsServer && _cachedPng != null
        && NetworkManager.ConnectedClients.ContainsKey(client)
        && _acknowledgedVersions.TryGetValue(client, out int version) && version == _mapVersion;
    public float TransferWaitSeconds
    {
        get
        {
            int players = NetworkManager != null ? Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count - 1) : 1;
            return Mathf.Max(40, 60 + (_cachedPng?.Length ?? 0) / (128f * 1024) * Mathf.Max(1, players / 4f));
        }
    }
    public bool AllClientsHaveCurrentMap
    {
        get
        {
            if (!IsServer || _cachedPng == null) return false;
            foreach (ulong client in NetworkManager.ConnectedClientsIds)
                if (client != Unity.Netcode.NetworkManager.ServerClientId
                    && (!_acknowledgedVersions.TryGetValue(client, out int version) || version != _mapVersion)) return false;
            return true;
        }
    }

    private byte[] _cachedPng;
    private int _mapVersion;
    private uint _mapChecksum;
    private readonly Dictionary<ulong, int> _acknowledgedVersions = new();
    private readonly Dictionary<ulong, (int Version, int Count)> _receivedProgress = new();
    private readonly HashSet<(ulong Client, int Version)> _cancelledSends = new();
    private readonly HashSet<(ulong Client, int Version)> _activeSends = new();
    private readonly Dictionary<ulong, int> _sendGenerations = new();

    private int SendGeneration(ulong client) => _sendGenerations.TryGetValue(client, out int generation) ? generation : 0;

    public void AbortTransferForClient(ulong client)
    {
        // Unity does not dispose nested iterators when their parent coroutine is stopped.
        // Invalidate them explicitly before releasing the slot for a replacement.
        _sendGenerations[client] = unchecked(SendGeneration(client) + 1);
        _activeSends.RemoveWhere(item => item.Client == client);
        _cancelledSends.RemoveWhere(item => item.Client == client);
        _receivedProgress.Remove(client);
        ImageTransferUI.Remove($"map-send-{client}");
    }
    private readonly Dictionary<ulong, float> _lastMapRequestTime = new();

    private readonly Dictionary<int, byte[]> _incomingChunks = new();
    private int _incomingVersion = -1;
    private int _incomingTotalChunks;
    private int _incomingBytes;
    private uint _incomingChecksum;
    private int _appliedVersion = -1;
    private int _retryCount;
    private float _lastProgressTime;
    private bool _receivingMap;
    private int _cancelledIncomingVersion = -1;
    private bool _handlersRegistered;
    private bool _registeredAsServer;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn() => RegisterHandlers();

    public override void OnNetworkDespawn()
    {
        StopAllCoroutines();
        UnregisterHandlers();
        _receivingMap = false;
        _incomingChunks.Clear();
        _acknowledgedVersions.Clear();
        _receivedProgress.Clear();
        _cancelledSends.Clear();
        _activeSends.Clear();
        ImageTransferUI.Remove("map-receive");
        _lastMapRequestTime.Clear();
        base.OnNetworkDespawn();
    }

    public override void OnDestroy()
    {
        UnregisterHandlers();
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    private void RegisterHandlers()
    {
        if (_handlersRegistered) return;
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm == null) return;
        _registeredAsServer = IsServer;
        if (IsServer)
        {
            cmm.RegisterNamedMessageHandler(MSG_MAP_REQUEST, OnMapRequest);
            cmm.RegisterNamedMessageHandler(MSG_MAP_ACK, OnMapAck);
            cmm.RegisterNamedMessageHandler(MSG_MAP_PROGRESS, OnMapProgress);
        }
        else
        {
            cmm.RegisterNamedMessageHandler(MSG_MAP_META, OnMapMetaReceived);
            cmm.RegisterNamedMessageHandler(MSG_MAP_CHUNK, OnMapChunkReceived);
        }
        cmm.RegisterNamedMessageHandler(MSG_MAP_CANCEL, OnMapCancel);
        _handlersRegistered = true;
    }

    private void UnregisterHandlers()
    {
        if (!_handlersRegistered) return;
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm != null)
        {
            if (_registeredAsServer)
            {
                cmm.UnregisterNamedMessageHandler(MSG_MAP_REQUEST);
                cmm.UnregisterNamedMessageHandler(MSG_MAP_ACK);
                cmm.UnregisterNamedMessageHandler(MSG_MAP_PROGRESS);
            }
            else
            {
                cmm.UnregisterNamedMessageHandler(MSG_MAP_META);
                cmm.UnregisterNamedMessageHandler(MSG_MAP_CHUNK);
            }
            cmm.UnregisterNamedMessageHandler(MSG_MAP_CANCEL);
        }
        _handlersRegistered = false;
    }

    private void Update()
    {
        if (!_receivingMap || !IsSpawned
            || Time.unscaledTime - _lastProgressTime < ClientRetryAfterSeconds)
            return;
        if (_retryCount >= MaxRetries)
        {
            Debug.LogError($"[MapSync] Map version {_incomingVersion} failed after {MaxRetries} retries");
            DiceUI.Instance?.ShowToolNotice("Не удалось загрузить карту. Переподключитесь к сессии.");
            _receivingMap = false;
            _incomingChunks.Clear();
            ImageTransferUI.Finish("map-receive", "Не удалось загрузить карту");
            return;
        }
        _retryCount++;
        _lastProgressTime = Time.unscaledTime;
        Debug.LogWarning($"[MapSync] Requesting map retry {_retryCount}/{MaxRetries}, version {_incomingVersion}");
        RequestMapFromServer();
    }

    public void SendMapToAll(byte[] pngData)
    {
        if (!IsServer || pngData == null || pngData.Length == 0 || pngData.Length > MaxMapBytes)
        {
            Debug.LogError("[MapSync] Map is empty or exceeds the 16 MB network limit");
            return;
        }
        _cachedPng = pngData;
        _mapVersion++;
        _mapChecksum = ComputeChecksum(pngData);
        _acknowledgedVersions.Clear();
        _receivedProgress.Clear();
        _cancelledSends.Clear();
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (clientId != NetworkManager.ServerClientId)
                StartCoroutine(SendMapWithRetriesToClientRoutine(clientId));
        }
    }

    public IEnumerator SendMapWithRetriesToClientRoutine(ulong clientId)
    {
        int version = _mapVersion;
        int generation = SendGeneration(clientId);
        if (!_activeSends.Add((clientId, version)))
        {
            while (generation == SendGeneration(clientId) && _activeSends.Contains((clientId, version))
                && version == _mapVersion
                && NetworkManager.Singleton != null
                && NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId))
                yield return new WaitForSecondsRealtime(0.25f);
            yield break;
        }
        try { yield return SendMapWithRetriesCore(clientId, generation); }
        finally
        {
            if (generation == SendGeneration(clientId)) _activeSends.Remove((clientId, version));
        }
    }

    private IEnumerator SendMapWithRetriesCore(ulong clientId, int generation)
    {
        if (!IsServer || _cachedPng == null) yield break;
        int version = _mapVersion;
        for (int attempt = 1; attempt <= MaxRetries + 1 && version == _mapVersion && generation == SendGeneration(clientId); attempt++)
        {
            if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) yield break;
            if (_cancelledSends.Contains((clientId, version))) yield break;
            ImageTransferUI.Show($"map-send-{clientId}", "Отправка карты", 0f,
                $"Игроку {clientId}, попытка {attempt}/{MaxRetries + 1}",
                () => CancelSend(clientId, version));
            yield return SendMapToClientRoutine(clientId, generation);
            float deadline = Time.unscaledTime + AckTimeoutSeconds;
            while (Time.unscaledTime < deadline && generation == SendGeneration(clientId))
            {
                if (_acknowledgedVersions.TryGetValue(clientId, out int ack) && ack == version)
                {
                    ImageTransferUI.Finish($"map-send-{clientId}", "Карта отправлена");
                    yield break;
                }
                if (_cancelledSends.Contains((clientId, version))) yield break;
                var manager = NetworkManager.Singleton;
                if (manager == null || !manager.IsListening || !manager.ConnectedClients.ContainsKey(clientId)) yield break;
                yield return new WaitForSecondsRealtime(0.25f);
            }
            if (attempt <= MaxRetries)
                Debug.LogWarning($"[MapSync] No map acknowledgement from {clientId}; retry {attempt}/{MaxRetries}");
        }
        if (version == _mapVersion && generation == SendGeneration(clientId))
        {
            Debug.LogError($"[MapSync] Client {clientId} did not apply map version {version}");
            DiceUI.Instance?.ShowToolNotice($"Игрок {clientId} не получил карту.");
            ImageTransferUI.Finish($"map-send-{clientId}", "Не удалось отправить карту");
        }
    }

    public IEnumerator SendMapToClientRoutine(ulong clientId) => SendMapToClientRoutine(clientId, SendGeneration(clientId));

    private IEnumerator SendMapToClientRoutine(ulong clientId, int generation)
    {
        if (generation != SendGeneration(clientId) || !IsServer || clientId == NetworkManager.ServerClientId || _cachedPng == null)
            yield break;
        int version = _mapVersion;
        byte[] data = _cachedPng;
        uint checksum = _mapChecksum;
        int total = (data.Length + ChunkSize - 1) / ChunkSize;
        var cmm = NetworkManager.Singleton.CustomMessagingManager;

        using (var writer = new FastBufferWriter(4 * sizeof(int), Allocator.Temp))
        {
            writer.WriteValueSafe(version);
            writer.WriteValueSafe(total);
            writer.WriteValueSafe(data.Length);
            writer.WriteValueSafe(checksum);
            cmm.SendNamedMessage(MSG_MAP_META, clientId, writer);
        }
        yield return null;
        int first = _receivedProgress.TryGetValue(clientId, out var progress) && progress.Version == version ? progress.Count : 0;
        for (int i = first; i < total && version == _mapVersion && generation == SendGeneration(clientId); i++)
        {
            if (_cancelledSends.Contains((clientId, version))) yield break;
            if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) yield break;
            if (_acknowledgedVersions.TryGetValue(clientId, out int ack) && ack == version)
                yield break;
            int offset = i * ChunkSize;
            int size = Mathf.Min(ChunkSize, data.Length - offset);
            while (!NetworkTransferBudget.TryConsume(clientId, size + 12))
            {
                if (generation != SendGeneration(clientId)) yield break;
                yield return null;
            }
            if (generation != SendGeneration(clientId) || version != _mapVersion || !NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId))
                yield break;
            if (_acknowledgedVersions.TryGetValue(clientId, out ack) && ack == version)
                yield break;
            using (var writer = new FastBufferWriter(3 * sizeof(int) + size, Allocator.Temp))
            {
                writer.WriteValueSafe(version);
                writer.WriteValueSafe(i);
                writer.WriteValueSafe(size);
                writer.WriteBytesSafe(data, size, offset);
                cmm.SendNamedMessage(MSG_MAP_CHUNK, clientId, writer);
            }
            if ((i + 1) % BatchChunks == 0 || i + 1 == total)
                ImageTransferUI.Show($"map-send-{clientId}", "Отправка карты",
                    (float)(i + 1) / total, $"Игроку {clientId}",
                    () => CancelSend(clientId, version));
            if ((i + 1) % BatchChunks == 0 && i + 1 < total)
            {
                float deadline = Time.unscaledTime + AckTimeoutSeconds;
                while (Time.unscaledTime < deadline && generation == SendGeneration(clientId) && version == _mapVersion
                    && !_cancelledSends.Contains((clientId, version))
                    && (!_receivedProgress.TryGetValue(clientId, out var received)
                        || received.Version != version || received.Count < i + 1))
                    yield return null;
                if (!_receivedProgress.TryGetValue(clientId, out var receivedAfter)
                    || receivedAfter.Version != version || receivedAfter.Count < i + 1)
                    yield break;
            }
            else if ((i + 1) % ChunksPerFrame == 0) yield return null;
        }
    }

    private void OnMapMetaReceived(ulong senderId, FastBufferReader reader)
    {
        if (senderId != NetworkManager.ServerClientId) return;
        if (!reader.TryBeginRead(4 * sizeof(int))) return;
        reader.ReadValueSafe(out int version);
        reader.ReadValueSafe(out int total);
        reader.ReadValueSafe(out int bytes);
        reader.ReadValueSafe(out uint checksum);
        if (bytes <= 0 || bytes > MaxMapBytes || total != (bytes + ChunkSize - 1) / ChunkSize)
        {
            Debug.LogWarning("[MapSync] Invalid map metadata");
            return;
        }
        if (version <= _appliedVersion) { SendAck(version); return; }
        if (version <= _cancelledIncomingVersion) return;
        if (version < _incomingVersion) return;
        if (version == _incomingVersion && !_receivingMap && _retryCount >= MaxRetries)
            return;
        bool isNewVersion = version != _incomingVersion;
        if (version != _incomingVersion || !_receivingMap)
        {
            _incomingChunks.Clear();
            _incomingVersion = version;
            _incomingTotalChunks = total;
            _incomingBytes = bytes;
            _incomingChecksum = checksum;
            if (isNewVersion) _retryCount = 0;
        }
        else SendProgress(version, _incomingChunks.Count);
        _receivingMap = true;
        _lastProgressTime = Time.unscaledTime;
        ImageTransferUI.Show("map-receive", "Загрузка карты",
            (float)_incomingChunks.Count / _incomingTotalChunks, "Получение от сервера",
            () => CancelReceive(version));
    }

    private void OnMapChunkReceived(ulong senderId, FastBufferReader reader)
    {
        if (senderId != NetworkManager.ServerClientId || !_receivingMap) return;
        if (!reader.TryBeginRead(3 * sizeof(int))) return;
        reader.ReadValueSafe(out int version);
        reader.ReadValueSafe(out int index);
        reader.ReadValueSafe(out int size);
        if (version != _incomingVersion || index < 0 || index >= _incomingTotalChunks) return;
        int expected = Mathf.Min(ChunkSize, _incomingBytes - index * ChunkSize);
        if (size != expected || !reader.TryBeginRead(size)) return;
        byte[] chunk = new byte[size];
        reader.ReadBytesSafe(ref chunk, size);
        if (_incomingChunks.ContainsKey(index)) return;
        _incomingChunks.Add(index, chunk);
        _lastProgressTime = Time.unscaledTime;
        if (_incomingChunks.Count % BatchChunks == 0 || _incomingChunks.Count == _incomingTotalChunks)
        {
            ImageTransferUI.Show("map-receive", "Загрузка карты",
                (float)_incomingChunks.Count / _incomingTotalChunks, "Получение от сервера",
                () => CancelReceive(version));
            SendProgress(version, _incomingChunks.Count);
        }
        if (_incomingChunks.Count == _incomingTotalChunks) Reassemble();
    }

    private void Reassemble()
    {
        byte[] full = new byte[_incomingBytes];
        int offset = 0;
        for (int i = 0; i < _incomingTotalChunks; i++)
        {
            if (!_incomingChunks.TryGetValue(i, out byte[] chunk)) return;
            System.Array.Copy(chunk, 0, full, offset, chunk.Length);
            offset += chunk.Length;
        }
        if (offset != _incomingBytes || ComputeChecksum(full) != _incomingChecksum
            || mapController == null || !mapController.ApplyImageLocal(full))
        {
            Debug.LogWarning($"[MapSync] Map version {_incomingVersion} failed validation or decoding");
            _incomingChunks.Clear();
            _lastProgressTime = Time.unscaledTime - ClientRetryAfterSeconds;
            return;
        }
        _appliedVersion = _incomingVersion;
        _receivingMap = false;
        _incomingChunks.Clear();
        SendAck(_appliedVersion);
        ImageTransferUI.Finish("map-receive", "Карта загружена");
        Debug.Log($"[MapSync] Applied map version {_appliedVersion}, {_incomingBytes} bytes");
    }

    private void SendAck(int version)
    {
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm == null) return;
        using var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(version);
        cmm.SendNamedMessage(MSG_MAP_ACK, NetworkManager.ServerClientId, writer);
    }

    private void OnMapAck(ulong senderId, FastBufferReader reader)
    {
        if (!IsServer) return;
        if (!reader.TryBeginRead(sizeof(int))) return;
        reader.ReadValueSafe(out int version);
        if (version == _mapVersion) _acknowledgedVersions[senderId] = version;
    }

    private void SendProgress(int version, int count)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsConnectedClient || nm.IsServer) return;
        using var writer = new FastBufferWriter(2 * sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(version);
        writer.WriteValueSafe(count);
        nm.CustomMessagingManager.SendNamedMessage(MSG_MAP_PROGRESS,
            NetworkManager.ServerClientId, writer);
    }

    private void OnMapProgress(ulong senderId, FastBufferReader reader)
    {
        if (!IsServer || !reader.TryBeginRead(2 * sizeof(int))) return;
        reader.ReadValueSafe(out int version);
        reader.ReadValueSafe(out int count);
        if (version != _mapVersion || count < 0 || _cachedPng == null
            || count > (_cachedPng.Length + ChunkSize - 1) / ChunkSize
            || !NetworkManager.Singleton.ConnectedClients.ContainsKey(senderId))
            return;
        _receivedProgress[senderId] = (version, count);
    }

    private void CancelSend(ulong clientId, int version, bool notify = true)
    {
        if (version != _mapVersion) return;
        _cancelledSends.Add((clientId, version));
        if (notify) SendCancel(clientId, version);
        ImageTransferUI.Finish($"map-send-{clientId}", "Отправка карты отменена");
    }

    private void CancelReceive(int version, bool notify = true)
    {
        if (version != _incomingVersion) return;
        _receivingMap = false;
        _incomingChunks.Clear();
        _cancelledIncomingVersion = Mathf.Max(_cancelledIncomingVersion, version);
        if (notify) SendCancel(NetworkManager.ServerClientId, version);
        ImageTransferUI.Finish("map-receive", "Загрузка карты отменена");
    }

    private static void SendCancel(ulong target, int version)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsConnectedClient) return;
        using var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(version);
        nm.CustomMessagingManager.SendNamedMessage(MSG_MAP_CANCEL, target, writer);
    }

    private void OnMapCancel(ulong senderId, FastBufferReader reader)
    {
        if (!reader.TryBeginRead(sizeof(int))) return;
        reader.ReadValueSafe(out int version);
        if (IsServer)
        {
            if (version == _mapVersion
                && NetworkManager.Singleton.ConnectedClients.ContainsKey(senderId))
                CancelSend(senderId, version, false);
        }
        else if (senderId == NetworkManager.ServerClientId)
            CancelReceive(version, false);
    }

    private void OnMapRequest(ulong senderId, FastBufferReader reader)
    {
        if (!IsServer || !NetworkManager.Singleton.ConnectedClients.ContainsKey(senderId))
            return;
        if (_lastMapRequestTime.TryGetValue(senderId, out float last)
            && Time.unscaledTime - last < 3f) return;
        _lastMapRequestTime[senderId] = Time.unscaledTime;
        if (!_activeSends.Contains((senderId, _mapVersion)))
            StartCoroutine(SendMapWithRetriesToClientRoutine(senderId));
    }

    public static void RequestMapFromServer()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsConnectedClient || nm.IsServer) return;
        using var writer = new FastBufferWriter(0, Allocator.Temp);
        nm.CustomMessagingManager.SendNamedMessage(MSG_MAP_REQUEST, NetworkManager.ServerClientId, writer);
    }

    private static uint ComputeChecksum(byte[] data)
    {
        const uint prime = 16777619;
        uint hash = 2166136261;
        foreach (byte value in data) hash = unchecked((hash ^ value) * prime);
        return hash;
    }
}

/// <summary>Shared per-frame cap for bulk image messages on this peer.</summary>
internal static class NetworkTransferBudget
{
    private const int MaxBytesPerTargetPerFrame = 6 * 1024;
    private const int MaxBytesPerFrame = 24 * 1024;
    private static readonly Dictionary<ulong, int> BytesByTarget = new();
    private static int _frame = -1;
    private static int _totalBytes;
    private static readonly Dictionary<ulong, (double Time, double Credit)> Credits = new();
    private static double _globalTime, _globalCredit;
    private const double BytesPerSecond = 128 * 1024;
    private const double GlobalBytesPerSecond = 512 * 1024;
    private const double Burst = 6 * 1024;

    public static bool TryConsume(ulong target, int bytes)
        => TryConsumeAt(target, bytes, Time.realtimeSinceStartupAsDouble, Time.frameCount);
    public static bool TryConsumeAt(ulong target, int bytes, double now, int frame)
    {
        if (_frame != frame)
        {
            _frame = frame;
            _totalBytes = 0;
            BytesByTarget.Clear();
        }
        BytesByTarget.TryGetValue(target, out int used);
        if (used + bytes > MaxBytesPerTargetPerFrame || _totalBytes + bytes > MaxBytesPerFrame)
            return false;
        if (!Credits.TryGetValue(target, out var bucket)) bucket = (now, Burst);
        double credit = System.Math.Min(Burst, bucket.Credit + System.Math.Max(0, now - bucket.Time) * BytesPerSecond);
        _globalCredit = System.Math.Min(MaxBytesPerFrame, _globalCredit + System.Math.Max(0, now - _globalTime) * GlobalBytesPerSecond);
        _globalTime = now;
        Credits[target] = (now, credit);
        if (credit < bytes || _globalCredit < bytes) return false;
        Credits[target] = (now, credit - bytes); _globalCredit -= bytes;
        BytesByTarget[target] = used + bytes;
        _totalBytes += bytes;
        return true;
    }
    public static void Reset()
    { Credits.Clear(); BytesByTarget.Clear(); _frame = -1; _totalBytes = 0; _globalTime = Time.realtimeSinceStartupAsDouble; _globalCredit = MaxBytesPerFrame; }
}
