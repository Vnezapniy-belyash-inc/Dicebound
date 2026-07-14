using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Chunked token portrait sync via CustomMessagingManager (RPC size limit ~8KB).
/// </summary>
public class TokenImageSync : MonoBehaviour
{
    private const string MSG_META = "TokenImgMeta";
    private const string MSG_CHUNK = "TokenImgChunk";
    private const string MSG_UPLOAD_META = "TokenImgUploadMeta";
    private const string MSG_UPLOAD_CHUNK = "TokenImgUploadChunk";
    private const int ChunkSize = 800;
    private const int ChunksPerFrame = 3;

    public static TokenImageSync Instance { get; private set; }

    /// <summary>Server: authoritative portrait bytes per token NetworkObjectId.</summary>
    private static readonly Dictionary<ulong, byte[]> PortraitCache = new();

    /// <summary>Client: portraits received before the token NetworkObject spawned.</summary>
    private readonly Dictionary<ulong, byte[]> _pendingApply = new();

    private readonly Dictionary<ulong, Dictionary<int, byte[]>> _incoming = new();
    private readonly Dictionary<ulong, int> _totalChunks = new();
    private readonly Dictionary<ulong, int> _receivedChunks = new();

    private readonly Dictionary<ulong, Dictionary<int, byte[]>> _uploadIncoming = new();
    private readonly Dictionary<ulong, int> _uploadTotalChunks = new();
    private readonly Dictionary<ulong, int> _uploadReceivedChunks = new();
    private readonly Dictionary<ulong, ulong> _uploadSender = new();

    private bool _registered;
    private bool _subscribed;

    public static void EnsureInstance()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (Instance == null)
        {
            Instance = nm.GetComponent<TokenImageSync>();
            if (Instance == null)
                Instance = nm.gameObject.AddComponent<TokenImageSync>();
        }

        Instance.RegisterHandlers();
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
        TrySubscribe();
        RegisterHandlers();
        StartCoroutine(RetryRegisterUntilReady());
    }

    private void TrySubscribe()
    {
        if (_subscribed || NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.OnServerStarted += OnNetworkReady;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        _subscribed = true;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        ClearClientState();
        UnregisterHandlers();
    }

    private void ClearClientState()
    {
        _incoming.Clear();
        _totalChunks.Clear();
        _receivedChunks.Clear();
        _pendingApply.Clear();
        _uploadIncoming.Clear();
        _uploadTotalChunks.Clear();
        _uploadReceivedChunks.Clear();
        _uploadSender.Clear();
    }

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        ClearClientState();
        _registered = false;
        RegisterHandlers();
        StartCoroutine(RetryRegisterUntilReady());
    }

    private IEnumerator RetryRegisterUntilReady()
    {
        for (int i = 0; i < 50; i++)
        {
            TrySubscribe();
            RegisterHandlers();
            if (_registered && NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient)
                yield break;
            yield return new WaitForSeconds(0.1f);
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null && _subscribed)
        {
            NetworkManager.Singleton.OnServerStarted -= OnNetworkReady;
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            _subscribed = false;
        }

        UnregisterHandlers();
        if (Instance == this)
            Instance = null;
    }

    private void OnNetworkReady() => RegisterHandlers();

    private void RegisterHandlers()
    {
        if (_registered) return;
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm == null) return;

        cmm.RegisterNamedMessageHandler(MSG_META, OnMetaReceived);
        cmm.RegisterNamedMessageHandler(MSG_CHUNK, OnChunkReceived);
        cmm.RegisterNamedMessageHandler(MSG_UPLOAD_META, OnUploadMeta);
        cmm.RegisterNamedMessageHandler(MSG_UPLOAD_CHUNK, OnUploadChunk);
        _registered = true;
        Debug.Log("[TokenImageSync] Message handlers registered");
    }

    private void UnregisterHandlers()
    {
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm != null && _registered)
        {
            cmm.UnregisterNamedMessageHandler(MSG_META);
            cmm.UnregisterNamedMessageHandler(MSG_CHUNK);
            cmm.UnregisterNamedMessageHandler(MSG_UPLOAD_META);
            cmm.UnregisterNamedMessageHandler(MSG_UPLOAD_CHUNK);
        }

        _registered = false;
    }

    /// <summary>Client spawner uploads image to server.</summary>
    public static void UploadToServer(ulong networkObjectId, byte[] jpgData)
    {
        EnsureInstance();
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm == null || jpgData == null || jpgData.Length == 0) return;

        int total = Mathf.CeilToInt((float)jpgData.Length / ChunkSize);

        using (var meta = new FastBufferWriter(sizeof(ulong) + sizeof(int), Allocator.Temp))
        {
            meta.WriteValueSafe(networkObjectId);
            meta.WriteValueSafe(total);
            cmm.SendNamedMessage(MSG_UPLOAD_META, NetworkManager.ServerClientId, meta);
        }

        for (int i = 0; i < total; i++)
            SendChunk(cmm, MSG_UPLOAD_CHUNK, networkObjectId, i, jpgData);
    }

    /// <summary>Server: apply locally and push to all clients.</summary>
    public static void BroadcastImage(ulong networkObjectId, byte[] jpgData)
    {
        if (!NetworkManager.Singleton.IsServer || jpgData == null || jpgData.Length == 0) return;

        CachePortrait(networkObjectId, jpgData);
        FindToken(networkObjectId)?.ApplyImageLocal(jpgData);

        foreach (var kv in NetworkManager.Singleton.ConnectedClients)
        {
            if (kv.Key == NetworkManager.ServerClientId) continue;
            SendImageToClient(kv.Key, networkObjectId, jpgData);
        }
    }

    public static void CachePortrait(ulong networkObjectId, byte[] jpgData)
    {
        if (jpgData == null || jpgData.Length == 0) return;
        PortraitCache[networkObjectId] = jpgData;
    }

    public static void RemovePortrait(ulong networkObjectId)
    {
        PortraitCache.Remove(networkObjectId);
    }

    public static bool TryApplyPending(TokenController token)
    {
        if (Instance == null || token == null || !token.IsSpawned) return false;
        ulong netId = token.NetworkObjectId;
        if (!Instance._pendingApply.TryGetValue(netId, out byte[] jpg)) return false;

        token.ApplyImageLocal(jpg);
        Instance._pendingApply.Remove(netId);
        return true;
    }

    /// <summary>Server: push one token image to a single client (late join).</summary>
    public static void SendImageToClient(ulong clientId, ulong networkObjectId, byte[] jpgData)
    {
        if (!NetworkManager.Singleton.IsServer || jpgData == null || jpgData.Length == 0) return;
        if (Instance != null)
            Instance.StartCoroutine(Instance.SendImageToClientRoutine(clientId, networkObjectId, jpgData));
    }

    public IEnumerator SendAllPortraitsToClientRoutine(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) yield break;

        var sent = new HashSet<ulong>();

        foreach (var kv in PortraitCache)
        {
            if (kv.Value == null || kv.Value.Length == 0) continue;
            yield return SendImageToClientRoutine(clientId, kv.Key, kv.Value);
            sent.Add(kv.Key);
            yield return null;
        }

        var tokens = Object.FindObjectsByType<TokenController>(FindObjectsInactive.Exclude);
        foreach (var t in tokens)
        {
            if (t == null || !t.IsSpawned) continue;
            ulong netId = t.NetworkObjectId;
            if (sent.Contains(netId)) continue;

            byte[] jpg = t.GetPortraitJpg();
            if (jpg == null || jpg.Length == 0) continue;

            CachePortrait(netId, jpg);
            yield return SendImageToClientRoutine(clientId, netId, jpg);
            yield return null;
        }
    }

    private IEnumerator SendImageToClientRoutine(ulong clientId, ulong networkObjectId, byte[] jpgData)
    {
        if (!NetworkManager.Singleton.IsServer || jpgData == null || jpgData.Length == 0)
            yield break;

        var cmm = NetworkManager.Singleton.CustomMessagingManager;
        int total = Mathf.CeilToInt((float)jpgData.Length / ChunkSize);

        using (var meta = new FastBufferWriter(sizeof(ulong) + sizeof(int), Allocator.Temp))
        {
            meta.WriteValueSafe(networkObjectId);
            meta.WriteValueSafe(total);
            cmm.SendNamedMessage(MSG_META, clientId, meta);
        }

        yield return null;

        for (int i = 0; i < total; i++)
        {
            SendChunk(cmm, MSG_CHUNK, networkObjectId, i, jpgData, clientId);
            if ((i + 1) % ChunksPerFrame == 0)
                yield return null;
        }
    }

    private static void SendChunk(
        CustomMessagingManager cmm,
        string messageName,
        ulong networkObjectId,
        int index,
        byte[] jpgData,
        ulong? targetClientId = null)
    {
        int off = index * ChunkSize;
        int size = Mathf.Min(ChunkSize, jpgData.Length - off);

        using var writer = new FastBufferWriter(sizeof(ulong) + sizeof(int) + sizeof(int) + size, Allocator.Temp);
        writer.WriteValueSafe(networkObjectId);
        writer.WriteValueSafe(index);
        writer.WriteValueSafe(size);
        for (int j = 0; j < size; j++)
            writer.WriteValueSafe(jpgData[off + j]);

        if (targetClientId.HasValue)
            cmm.SendNamedMessage(messageName, targetClientId.Value, writer);
        else
            cmm.SendNamedMessage(messageName, NetworkManager.ServerClientId, writer);
    }

    private void OnUploadMeta(ulong senderId, FastBufferReader reader)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        reader.ReadValueSafe(out ulong netId);
        reader.ReadValueSafe(out int total);

        var token = FindToken(netId);
        if (token == null || token.SpawnerClientId != senderId)
        {
            Debug.LogWarning($"[TokenImageSync] Upload rejected for token {netId} from {senderId}");
            return;
        }

        _uploadIncoming[netId] = new Dictionary<int, byte[]>();
        _uploadTotalChunks[netId] = total;
        _uploadReceivedChunks[netId] = 0;
        _uploadSender[netId] = senderId;
    }

    private void OnUploadChunk(ulong senderId, FastBufferReader reader)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        reader.ReadValueSafe(out ulong netId);
        reader.ReadValueSafe(out int index);
        reader.ReadValueSafe(out int size);
        byte[] data = new byte[size];
        for (int i = 0; i < size; i++)
            reader.ReadValueSafe(out data[i]);

        if (!_uploadSender.TryGetValue(netId, out ulong expectedSender) || expectedSender != senderId)
            return;

        if (!_uploadIncoming.ContainsKey(netId))
            _uploadIncoming[netId] = new Dictionary<int, byte[]>();

        _uploadIncoming[netId][index] = data;
        _uploadReceivedChunks[netId] = _uploadReceivedChunks.GetValueOrDefault(netId, 0) + 1;

        if (_uploadReceivedChunks[netId] >= _uploadTotalChunks.GetValueOrDefault(netId, 1))
        {
            if (TryReassemble(_uploadIncoming, _uploadTotalChunks, netId, out byte[] full))
                BroadcastImage(netId, full);

            _uploadIncoming.Remove(netId);
            _uploadTotalChunks.Remove(netId);
            _uploadReceivedChunks.Remove(netId);
            _uploadSender.Remove(netId);
        }
    }

    private void OnMetaReceived(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong netId);
        reader.ReadValueSafe(out int total);
        _incoming[netId] = new Dictionary<int, byte[]>();
        _totalChunks[netId] = total;
        _receivedChunks[netId] = 0;
    }

    private void OnChunkReceived(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong netId);
        reader.ReadValueSafe(out int index);
        reader.ReadValueSafe(out int size);
        byte[] data = new byte[size];
        for (int i = 0; i < size; i++)
            reader.ReadValueSafe(out data[i]);

        if (!_incoming.ContainsKey(netId))
            _incoming[netId] = new Dictionary<int, byte[]>();

        _incoming[netId][index] = data;
        _receivedChunks[netId] = _receivedChunks.GetValueOrDefault(netId, 0) + 1;

        if (_receivedChunks[netId] >= _totalChunks.GetValueOrDefault(netId, 1))
        {
            if (TryReassemble(_incoming, _totalChunks, netId, out byte[] full))
                ApplyPortraitToClient(netId, full);

            _incoming.Remove(netId);
            _totalChunks.Remove(netId);
            _receivedChunks.Remove(netId);
        }
    }

    private void ApplyPortraitToClient(ulong netId, byte[] jpgData)
    {
        var token = FindToken(netId);
        if (token != null)
        {
            token.ApplyImageLocal(jpgData);
            return;
        }

        _pendingApply[netId] = jpgData;
        Debug.Log($"[TokenImageSync] Portrait queued for token {netId} (not spawned yet)");
    }

    private static bool TryReassemble(
        Dictionary<ulong, Dictionary<int, byte[]>> store,
        Dictionary<ulong, int> totals,
        ulong netId,
        out byte[] full)
    {
        full = null;
        if (!store.TryGetValue(netId, out var chunks)) return false;

        int totalChunkCount = totals.GetValueOrDefault(netId, 0);
        int totalBytes = 0;
        for (int i = 0; i < totalChunkCount; i++)
        {
            if (!chunks.ContainsKey(i)) return false;
            totalBytes += chunks[i].Length;
        }

        full = new byte[totalBytes];
        int off = 0;
        for (int i = 0; i < totalChunkCount; i++)
        {
            byte[] ch = chunks[i];
            System.Array.Copy(ch, 0, full, off, ch.Length);
            off += ch.Length;
        }

        return true;
    }

    private static TokenController FindToken(ulong netId)
    {
        foreach (var t in Object.FindObjectsByType<TokenController>(FindObjectsInactive.Exclude))
        {
            if (t.IsSpawned && t.NetworkObjectId == netId)
                return t;
        }

        return null;
    }
}
