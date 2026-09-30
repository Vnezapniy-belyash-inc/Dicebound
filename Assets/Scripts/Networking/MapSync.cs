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
    private const int ChunkSize = 1000;
    private const int ChunksPerFrame = 4;
    public const int MaxMapBytes = 16 * 1024 * 1024;
    private const int MaxRetries = 3;
    private const float AckTimeoutSeconds = 6f;
    private const float ClientRetryAfterSeconds = 12f;

    [Header("References")]
    public MapController mapController;

    public static MapSync Instance { get; private set; }
    public bool IsReceivingMap => _receivingMap;

    private byte[] _cachedPng;
    private int _mapVersion;
    private uint _mapChecksum;
    private readonly Dictionary<ulong, int> _acknowledgedVersions = new();
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
        UnregisterHandlers();
        _receivingMap = false;
        _incomingChunks.Clear();
        _acknowledgedVersions.Clear();
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
        }
        else
        {
            cmm.RegisterNamedMessageHandler(MSG_MAP_META, OnMapMetaReceived);
            cmm.RegisterNamedMessageHandler(MSG_MAP_CHUNK, OnMapChunkReceived);
        }
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
            }
            else
            {
                cmm.UnregisterNamedMessageHandler(MSG_MAP_META);
                cmm.UnregisterNamedMessageHandler(MSG_MAP_CHUNK);
            }
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
        foreach (var clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            if (clientId != NetworkManager.ServerClientId)
                StartCoroutine(SendMapWithRetriesToClientRoutine(clientId));
        }
    }

    public IEnumerator SendMapWithRetriesToClientRoutine(ulong clientId)
    {
        if (!IsServer || _cachedPng == null) yield break;
        int version = _mapVersion;
        for (int attempt = 1; attempt <= MaxRetries + 1 && version == _mapVersion; attempt++)
        {
            if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) yield break;
            yield return SendMapToClientRoutine(clientId);
            float deadline = Time.unscaledTime + AckTimeoutSeconds;
            while (Time.unscaledTime < deadline)
            {
                if (_acknowledgedVersions.TryGetValue(clientId, out int ack) && ack == version)
                    yield break;
                if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) yield break;
                yield return new WaitForSecondsRealtime(0.25f);
            }
            if (attempt <= MaxRetries)
                Debug.LogWarning($"[MapSync] No map acknowledgement from {clientId}; retry {attempt}/{MaxRetries}");
        }
        if (version == _mapVersion)
        {
            Debug.LogError($"[MapSync] Client {clientId} did not apply map version {version}");
            DiceUI.Instance?.ShowToolNotice($"Игрок {clientId} не получил карту.");
        }
    }

    public IEnumerator SendMapToClientRoutine(ulong clientId)
    {
        if (!IsServer || clientId == NetworkManager.ServerClientId || _cachedPng == null)
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
        for (int i = 0; i < total && version == _mapVersion; i++)
        {
            if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId)) yield break;
            if (_acknowledgedVersions.TryGetValue(clientId, out int ack) && ack == version)
                yield break;
            int offset = i * ChunkSize;
            int size = Mathf.Min(ChunkSize, data.Length - offset);
            while (!NetworkTransferBudget.TryConsume(clientId, size + 12))
                yield return null;
            if (version != _mapVersion || !NetworkManager.Singleton.ConnectedClients.ContainsKey(clientId))
                yield break;
            if (_acknowledgedVersions.TryGetValue(clientId, out ack) && ack == version)
                yield break;
            using (var writer = new FastBufferWriter(3 * sizeof(int) + size, Allocator.Temp))
            {
                writer.WriteValueSafe(version);
                writer.WriteValueSafe(i);
                writer.WriteValueSafe(size);
                for (int j = 0; j < size; j++) writer.WriteValueSafe(data[offset + j]);
                cmm.SendNamedMessage(MSG_MAP_CHUNK, clientId, writer);
            }
            if ((i + 1) % ChunksPerFrame == 0) yield return null;
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
        _receivingMap = true;
        _lastProgressTime = Time.unscaledTime;
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
        for (int i = 0; i < size; i++) reader.ReadValueSafe(out chunk[i]);
        if (_incomingChunks.ContainsKey(index)) return;
        _incomingChunks.Add(index, chunk);
        _lastProgressTime = Time.unscaledTime;
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
        TokenController.RebuildCellOccupancy();
        SendAck(_appliedVersion);
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

    private void OnMapRequest(ulong senderId, FastBufferReader reader)
    {
        if (!IsServer || !NetworkManager.Singleton.ConnectedClients.ContainsKey(senderId))
            return;
        if (_lastMapRequestTime.TryGetValue(senderId, out float last)
            && Time.unscaledTime - last < 3f) return;
        _lastMapRequestTime[senderId] = Time.unscaledTime;
        StartCoroutine(SendMapToClientRoutine(senderId));
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

    public static bool TryConsume(ulong target, int bytes)
    {
        if (_frame != Time.frameCount)
        {
            _frame = Time.frameCount;
            _totalBytes = 0;
            BytesByTarget.Clear();
        }
        BytesByTarget.TryGetValue(target, out int used);
        if (used + bytes > MaxBytesPerTargetPerFrame || _totalBytes + bytes > MaxBytesPerFrame)
            return false;
        BytesByTarget[target] = used + bytes;
        _totalBytes += bytes;
        return true;
    }
}
