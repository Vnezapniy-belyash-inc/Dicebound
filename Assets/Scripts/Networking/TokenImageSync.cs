using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Синхронизация изображений токенов через чанки.
/// </summary>
public class TokenImageSync : MonoBehaviour
{
    private const string MSG_TOKEN_IMG_META = "TokenImgMeta";
    private const string MSG_TOKEN_IMG_CHUNK = "TokenImgChunk";

    public static TokenImageSync Instance { get; private set; }

    // Клиент: сборка чанков
    private readonly Dictionary<ulong, Dictionary<int, byte[]>> _incoming = new();
    private readonly Dictionary<ulong, int> _totalChunks = new();
    private readonly Dictionary<ulong, int> _receivedChunks = new();

    private bool _registered;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm != null)
        {
            cmm.RegisterNamedMessageHandler(MSG_TOKEN_IMG_META, OnMetaReceived);
            cmm.RegisterNamedMessageHandler(MSG_TOKEN_IMG_CHUNK, OnChunkReceived);
            _registered = true;
        }
    }

    private void OnDestroy()
    {
        if (_registered && NetworkManager.Singleton?.CustomMessagingManager != null)
        {
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(MSG_TOKEN_IMG_META);
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(MSG_TOKEN_IMG_CHUNK);
        }
    }

    /// <summary>Сервер вызывает для рассылки картинки всем клиентам.</summary>
    public static void SendImageToAll(ulong networkObjectId, byte[] jpgData)
    {
        if (Instance == null || !NetworkManager.Singleton.IsServer) return;
        const int CHUNK = 800;
        int total = Mathf.CeilToInt((float)jpgData.Length / CHUNK);

        foreach (var kv in NetworkManager.Singleton.ConnectedClients)
        {
            ulong cid = kv.Key;
            if (cid == NetworkManager.ServerClientId) continue;

            // Мета
            var w = new FastBufferWriter(sizeof(ulong) + sizeof(int), Unity.Collections.Allocator.Temp);
            w.WriteValueSafe(networkObjectId);
            w.WriteValueSafe(total);
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MSG_TOKEN_IMG_META, cid, w);
            w.Dispose();

            // Чанки
            for (int i = 0; i < total; i++)
            {
                int off = i * CHUNK;
                int size = Mathf.Min(CHUNK, jpgData.Length - off);
                var cw = new FastBufferWriter(sizeof(ulong) + sizeof(int) + sizeof(int) + size, Unity.Collections.Allocator.Temp);
                cw.WriteValueSafe(networkObjectId);
                cw.WriteValueSafe(i);
                cw.WriteValueSafe(size);
                for (int j = 0; j < size; j++)
                    cw.WriteValueSafe(jpgData[off + j]);
                NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MSG_TOKEN_IMG_CHUNK, cid, cw);
                cw.Dispose();
            }
        }
    }

    private void OnMetaReceived(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong netId);
        reader.ReadValueSafe(out int total);
        if (!_incoming.ContainsKey(netId))
            _incoming[netId] = new Dictionary<int, byte[]>();
        _totalChunks[netId] = total;
        _receivedChunks[netId] = 0;
        _incoming[netId].Clear();
    }

    private void OnChunkReceived(ulong sender, FastBufferReader reader)
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
            Reassemble(netId);
    }

    private void Reassemble(ulong netId)
    {
        if (!_incoming.ContainsKey(netId)) return;
        int total = 0;
        for (int i = 0; i < _totalChunks[netId]; i++)
        {
            if (!_incoming[netId].ContainsKey(i)) return;
            total += _incoming[netId][i].Length;
        }

        byte[] full = new byte[total];
        int off = 0;
        for (int i = 0; i < _totalChunks[netId]; i++)
        {
            byte[] ch = _incoming[netId][i];
            System.Array.Copy(ch, 0, full, off, ch.Length);
            off += ch.Length;
        }

        // Найти токен и применить
        var tokens = FindObjectsByType<TokenController>(FindObjectsInactive.Exclude);
        foreach (var t in tokens)
        {
            if (t.NetworkObjectId == netId)
            {
                t.ApplyImageLocal(full);
                break;
            }
        }

        _incoming.Remove(netId);
        _totalChunks.Remove(netId);
        _receivedChunks.Remove(netId);
    }
}
