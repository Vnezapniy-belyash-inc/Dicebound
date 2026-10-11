using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEngine;

/// <summary>Owns inactive map textures. The controller owns the active texture.</summary>
public sealed class MapImageCache : IDisposable
{
    public const long ResidentBudget = 192L * 1024 * 1024;
    public const long EncodedBudget = 64L * 1024 * 1024;
    private const int MaxEntries = 8;
    private sealed class Entry
    {
        public string hash;
        public Texture2D texture;
        public byte[] bytes;
        public long cost;
    }
    private readonly LinkedList<Entry> _lru = new();
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = new();
    private long _resident, _encoded, _activeCost, _activeBytes;

    public static string Hash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
    }

    public bool Take(string hash, out Texture2D texture, out byte[] bytes)
    {
        texture = null; bytes = null;
        if (hash == null || !_entries.TryGetValue(hash, out var node)) return false;
        var entry = node.Value;
        Remove(node, false);
        if (entry.texture == null || Hash(entry.bytes) != hash)
        {
            if (entry.texture != null) UnityEngine.Object.Destroy(entry.texture);
            return false;
        }
        texture = entry.texture; bytes = entry.bytes;
        return true;
    }

    public void Store(Texture2D texture, byte[] bytes)
    {
        if (texture == null) return;
        if (bytes == null || bytes.Length == 0) { UnityEngine.Object.Destroy(texture); return; }
        string hash = Hash(bytes);
        if (_entries.TryGetValue(hash, out var previous)) Remove(previous, true);
        var entry = new Entry { hash = hash, texture = texture, bytes = (byte[])bytes.Clone(), cost = Cost(texture, bytes) };
        var node = _lru.AddFirst(entry);
        _entries.Add(hash, node); _resident += entry.cost; _encoded += entry.bytes.Length;
        Trim();
    }

    public void SetActive(Texture2D texture, byte[] bytes)
    {
        _activeCost = texture == null || bytes == null ? 0 : Cost(texture, bytes);
        _activeBytes = bytes?.Length ?? 0;
        Trim();
    }

    private static long Cost(Texture2D texture, byte[] bytes) => (long)texture.width * texture.height * 8 + bytes.Length;
    private void Trim()
    {
        while (_lru.Count > 0 && (_lru.Count > MaxEntries || _resident + _activeCost > ResidentBudget || _encoded + _activeBytes > EncodedBudget))
            Remove(_lru.Last, true);
    }
    private void Remove(LinkedListNode<Entry> node, bool destroy)
    {
        var entry = node.Value;
        _entries.Remove(entry.hash); _lru.Remove(node);
        _resident -= entry.cost; _encoded -= entry.bytes.Length;
        if (destroy && entry.texture != null) UnityEngine.Object.Destroy(entry.texture);
    }
    public void Dispose()
    {
        while (_lru.Count > 0) Remove(_lru.Last, true);
    }
}
