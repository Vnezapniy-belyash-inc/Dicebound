using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Синхронизация карты: чанковая передача через CustomMessagingManager.
/// Формат: [int chunkIndex][int dataSize][byte... data]
/// Late-join via LateJoinSync staggered pipeline.
/// </summary>
public class MapSync : NetworkBehaviour
{
    private const string MSG_MAP_META = "MapMeta";
    private const string MSG_MAP_CHUNK = "MapChunk";
    private const string MSG_MAP_REQUEST = "MapRequest";

    [Header("References")]
    public MapController mapController;

    public static MapSync Instance { get; private set; }

    private byte[] _cachedPng;
    private bool _handlerRegistered;

    // Клиент: сборка чанков
    private readonly Dictionary<int, byte[]> _incomingChunks = new();
    private int _incomingTotalChunks;
    private int _incomingChunkCount;
    private bool _receivingMap;

    public bool IsReceivingMap => _receivingMap;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        // Late-join map push is handled by LateJoinSync (staggered).
    }

    public override void OnNetworkSpawn()
    {
        var cmm = NetworkManager.Singleton.CustomMessagingManager;
        if (IsServer)
        {
            cmm.RegisterNamedMessageHandler(MSG_MAP_REQUEST, OnMapRequest);
            _handlerRegistered = true;
        }
        else
        {
            cmm.RegisterNamedMessageHandler(MSG_MAP_META, OnMapMetaReceived);
            cmm.RegisterNamedMessageHandler(MSG_MAP_CHUNK, OnMapChunkReceived);
        }
    }

    public override void OnDestroy()
    {
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm != null)
        {
            if (_handlerRegistered)
                cmm.UnregisterNamedMessageHandler(MSG_MAP_REQUEST);
            else
            {
                cmm.UnregisterNamedMessageHandler(MSG_MAP_META);
                cmm.UnregisterNamedMessageHandler(MSG_MAP_CHUNK);
            }
        }
        base.OnDestroy();
    }

    // ═══ Host → All ═══

    public void SendMapToAll(byte[] pngData)
    {
        if (!IsServer || pngData == null) return;
        _cachedPng = pngData;

        const int CHUNK_SIZE = 1000; // влезает в ~1KB лимит
        int totalChunks = Mathf.CeilToInt((float)pngData.Length / CHUNK_SIZE);

        Debug.Log($"[MapSync] Sending map: {pngData.Length} bytes in {totalChunks} chunks");

        // Шлём мету всем клиентам
        foreach (var kv in NetworkManager.Singleton.ConnectedClients)
        {
            ulong cid = kv.Key;
            if (cid == NetworkManager.ServerClientId) continue;

            SendMeta(cid, totalChunks);

            for (int i = 0; i < totalChunks; i++)
            {
                int off = i * CHUNK_SIZE;
                int size = Mathf.Min(CHUNK_SIZE, pngData.Length - off);
                SendChunk(cid, i, pngData, off, size);
            }
        }
    }

    private void SendMeta(ulong clientId, int totalChunks)
    {
        var writer = new FastBufferWriter(sizeof(int), Unity.Collections.Allocator.Temp);
        writer.WriteValueSafe(totalChunks);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            MSG_MAP_META, clientId, writer);
        writer.Dispose();
    }

    private void SendChunk(ulong clientId, int index, byte[] data, int offset, int size)
    {
        int msgSize = sizeof(int) + sizeof(int) + size;
        var writer = new FastBufferWriter(msgSize, Unity.Collections.Allocator.Temp);
        writer.WriteValueSafe(index);
        writer.WriteValueSafe(size);
        for (int i = 0; i < size; i++)
            writer.WriteValueSafe(data[offset + i]);

        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            MSG_MAP_CHUNK, clientId, writer);
        writer.Dispose();
    }

    // ═══ Client receives ═══

    private void OnMapMetaReceived(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int totalChunks);
        Debug.Log($"[MapSync] Client: receiving map in {totalChunks} chunks");
        _receivingMap = true;
        _incomingTotalChunks = totalChunks;
        _incomingChunkCount = 0;
        _incomingChunks.Clear();
    }

    private void OnMapChunkReceived(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int index);
        reader.ReadValueSafe(out int size);
        byte[] chunk = new byte[size];
        for (int i = 0; i < size; i++)
            reader.ReadValueSafe(out chunk[i]);

        _incomingChunks[index] = chunk;
        _incomingChunkCount++;

        if (_incomingChunkCount >= _incomingTotalChunks)
            Reassemble();
    }

    private void Reassemble()
    {
        int totalSize = 0;
        for (int i = 0; i < _incomingTotalChunks; i++)
        {
            if (!_incomingChunks.ContainsKey(i))
            {
                Debug.LogWarning($"[MapSync] Missing chunk {i}");
                return;
            }
            totalSize += _incomingChunks[i].Length;
        }

        byte[] full = new byte[totalSize];
        int off = 0;
        for (int i = 0; i < _incomingTotalChunks; i++)
        {
            byte[] chunk = _incomingChunks[i];
            System.Array.Copy(chunk, 0, full, off, chunk.Length);
            off += chunk.Length;
        }

        Debug.Log($"[MapSync] Client: map reassembled ({totalSize} bytes)");
        mapController?.ApplyImageLocal(full);
        _incomingChunks.Clear();
        _receivingMap = false;
        LateJoinSync.NotifyServerReady();
    }

    // ═══ Late-join ═══

    private const int ChunkSize = 1000;
    private const int ChunksPerFrame = 4;

    public IEnumerator SendMapToClientRoutine(ulong clientId)
    {
        if (!IsServer || clientId == NetworkManager.ServerClientId || _cachedPng == null)
            yield break;

        int total = Mathf.CeilToInt((float)_cachedPng.Length / ChunkSize);
        Debug.Log($"[MapSync] Sending map to client {clientId}: {_cachedPng.Length} bytes, {total} chunks");

        SendMeta(clientId, total);
        yield return null;

        for (int i = 0; i < total; i++)
        {
            int off = i * ChunkSize;
            int size = Mathf.Min(ChunkSize, _cachedPng.Length - off);
            SendChunk(clientId, i, _cachedPng, off, size);

            if ((i + 1) % ChunksPerFrame == 0)
                yield return null;
        }
    }

    private void OnMapRequest(ulong senderId, FastBufferReader reader)
    {
        if (!IsServer) return;
        Debug.Log($"[MapSync] Client {senderId} requested map");
        StartCoroutine(SendMapToClientRoutine(senderId));
    }

    public static void RequestMapFromServer()
    {
        if (NetworkManager.Singleton?.CustomMessagingManager == null) return;
        var w = new FastBufferWriter(sizeof(byte), Unity.Collections.Allocator.Temp);
        w.TryBeginWrite(sizeof(byte));
        w.WriteValueSafe((byte)0);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            MSG_MAP_REQUEST, NetworkManager.ServerClientId, w);
        w.Dispose();
    }
}
