using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

internal sealed class LobbyChat : IDisposable
{
    internal const int TextLimit = 1000;
    internal const int ImageDimension = 1280;
    private const int ImageHeader = 2 * sizeof(ushort);
    private const int ImageLimit = 512 * 1024;
    private const int CompressedWidthFlag = 0x8000;
    private const int HistoryBudget = 2 * 1024 * 1024;
    private const string SubscribeName = "LobbyChat.Subscribe.v1";
    private const string PostName = "LobbyChat.Post.v1";
    private const string EntryName = "LobbyChat.Entry.v1";
    private const string RejectedName = "LobbyChat.Rejected.v1";
    private const NetworkDelivery Delivery = NetworkDelivery.ReliableFragmentedSequenced;

    internal readonly struct Entry
    {
        internal readonly ulong Sender;
        internal readonly long Timestamp;
        internal readonly string Author, Text;
        internal readonly byte[] Image;

        internal Entry(ulong sender, long timestamp, string author, string text, byte[] image)
        {
            Sender = sender;
            Timestamp = timestamp;
            Author = author;
            Text = text;
            Image = image;
        }
    }

    private readonly NetworkManager _network;
    private readonly HashSet<ulong> _subscribers = new();
    private readonly Dictionary<ulong, float> _nextPost = new();
    private readonly Queue<(Entry Entry, int Size)> _history = new();
    private int _historySize;
    private CustomMessagingManager _messages;
    private bool _subscribed;

    internal event Action<Entry> Received;
    internal event Action Rejected;
    internal event Action Reset;

    internal LobbyChat(NetworkManager network)
    {
        _network = network;
        _network.OnClientStarted += Start;
        _network.OnClientStopped += Stop;
        _network.OnClientDisconnectCallback += RemoveClient;
    }

    private void Start()
    {
        _messages = _network.CustomMessagingManager;
        _messages.RegisterNamedMessageHandler(SubscribeName, ReceiveSubscription);
        _messages.RegisterNamedMessageHandler(PostName, ReceivePost);
        _messages.RegisterNamedMessageHandler(EntryName, ReceiveEntry);
        _messages.RegisterNamedMessageHandler(RejectedName, ReceiveRejection);
    }

    private void Stop(bool wasHost)
    {
        _messages?.UnregisterNamedMessageHandler(SubscribeName);
        _messages?.UnregisterNamedMessageHandler(PostName);
        _messages?.UnregisterNamedMessageHandler(EntryName);
        _messages?.UnregisterNamedMessageHandler(RejectedName);
        _messages = null;
        _subscribed = false;
        _subscribers.Clear();
        _nextPost.Clear();
        _history.Clear();
        _historySize = 0;
        Reset?.Invoke();
    }

    public void Dispose()
    {
        _network.OnClientStarted -= Start;
        _network.OnClientStopped -= Stop;
        _network.OnClientDisconnectCallback -= RemoveClient;
        Stop(false);
    }

    private void RemoveClient(ulong clientId)
    {
        _subscribers.Remove(clientId);
        _nextPost.Remove(clientId);
    }

    internal void Subscribe()
    {
        if (_subscribed || _messages == null || !_network.IsConnectedClient) return;
        _subscribed = true;
        using var writer = new FastBufferWriter(1, Allocator.Temp);
        _messages.SendNamedMessage(SubscribeName, NetworkManager.ServerClientId, writer, Delivery);
    }

    internal bool Post(string text, byte[] image)
    {
        if (!_subscribed || _messages == null || !_network.IsConnectedClient || _network.ShutdownInProgress) return false;
        byte[] utf8 = Encoding.UTF8.GetBytes(text);
        using var writer = new FastBufferWriter(2 * sizeof(int) + utf8.Length + (image?.Length ?? 0), Allocator.Temp);
        WriteBlob(writer, utf8);
        WriteBlob(writer, image);
        _messages.SendNamedMessage(PostName, NetworkManager.ServerClientId, writer, Delivery);
        return true;
    }

    private bool CanChat(ulong sender) =>
        _network.IsServer && _network.ConnectedClients.ContainsKey(sender)
        && PlayerRegistry.Instance?.IsRegisteredPlayer(sender) == true;

    private void ReceiveSubscription(ulong sender, FastBufferReader reader)
    {
        if (reader.Position != reader.Length || !CanChat(sender) || !_subscribers.Add(sender)) return;
        foreach (var item in _history) SendEntry(sender, item.Entry);
    }

    private void ReceivePost(ulong sender, FastBufferReader reader)
    {
        if (!CanChat(sender) || !_subscribers.Contains(sender)) return;
        float now = Time.unscaledTime;
        if ((_nextPost.TryGetValue(sender, out float next) && now < next)
            || !TryReadBlob(ref reader, TextLimit * 3, out var text)
            || !TryReadBlob(ref reader, ImageLimit, out var image) || reader.Position != reader.Length)
        {
            Reject(sender);
            return;
        }

        _nextPost[sender] = now + (image.Length == 0 ? .35f : 2f);
        string message = Encoding.UTF8.GetString(text).Trim();
        if (message.Length > TextLimit || (message.Length == 0 && image.Length == 0)
            || (image.Length != 0 && !IsValidImage(image)))
        {
            Reject(sender);
            return;
        }

        string author = PlayerColors.GetNickname(sender) ?? $"P{sender}";
        var entry = new Entry(sender, DateTime.UtcNow.Ticks, author, message, image);
        int size = image.Length + text.Length + 80;
        _history.Enqueue((entry, size));
        _historySize += size;
        while (_history.Count > 100 || _historySize > HistoryBudget)
            _historySize -= _history.Dequeue().Size;

        foreach (ulong target in _subscribers) SendEntry(target, entry);
    }

    // Send bounded RGBA pixels, optionally losslessly compressed.
    internal static byte[] PackImage(Texture2D image)
    {
        var pixels = image.GetRawTextureData<byte>().ToArray();
        bool compressed = pixels.Length + ImageHeader > ImageLimit;
        byte[] bytes;
        if (compressed)
        {
            using var buffer = new MemoryStream();
            buffer.Write(new byte[ImageHeader], 0, ImageHeader);
            using (var deflate = new DeflateStream(buffer, System.IO.Compression.CompressionLevel.Fastest, true))
                deflate.Write(pixels, 0, pixels.Length);
            if (buffer.Length > ImageLimit) return null;
            bytes = buffer.ToArray();
        }
        else
        {
            bytes = new byte[ImageHeader + pixels.Length];
            Buffer.BlockCopy(pixels, 0, bytes, ImageHeader, pixels.Length);
        }

        int width = image.width | (compressed ? CompressedWidthFlag : 0);
        bytes[0] = (byte)width;
        bytes[1] = (byte)(width >> 8);
        bytes[2] = (byte)image.height;
        bytes[3] = (byte)(image.height >> 8);
        return bytes;
    }

    internal static Texture2D UnpackImage(byte[] bytes, bool readable = false)
    {
        if (!TryGetImageSize(bytes, out int width, out int height, out bool compressed)) return null;
        var data = compressed ? InflateImage(bytes, width * height * 4) : bytes;
        if (data == null) return null;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = texture.GetRawTextureData<byte>();
        NativeArray<byte>.Copy(data, compressed ? 0 : ImageHeader, pixels, 0, pixels.Length);
        texture.Apply(false, !readable);
        return texture;
    }

    private static bool IsValidImage(byte[] bytes) =>
        TryGetImageSize(bytes, out int width, out int height, out bool compressed)
        && (!compressed || InflateImage(bytes, width * height * 4) != null);

    private static byte[] InflateImage(byte[] bytes, int length)
    {
        var pixels = new byte[length];
        try
        {
            using var source = new MemoryStream(bytes, ImageHeader, bytes.Length - ImageHeader);
            using var deflate = new DeflateStream(source, CompressionMode.Decompress);
            int offset = 0;
            while (offset < length)
            {
                int read = deflate.Read(pixels, offset, length - offset);
                if (read == 0) return null;
                offset += read;
            }
            return deflate.ReadByte() == -1 ? pixels : null;
        }
        catch (Exception exception) when (exception is IOException || exception is InvalidDataException)
        {
            return null;
        }
    }

    private static bool TryGetImageSize(byte[] bytes, out int width, out int height, out bool compressed)
    {
        width = height = 0;
        compressed = false;
        if (bytes.Length <= ImageHeader || bytes.Length > ImageLimit) return false;
        width = bytes[0] | (bytes[1] << 8);
        compressed = (width & CompressedWidthFlag) != 0;
        width &= ~CompressedWidthFlag;
        height = bytes[2] | (bytes[3] << 8);
        return width > 0 && width <= ImageDimension && height > 0 && height <= ImageDimension
            && (compressed || bytes.Length == ImageHeader + width * height * 4);
    }

    private void Reject(ulong target)
    {
        using var writer = new FastBufferWriter(1, Allocator.Temp);
        _messages.SendNamedMessage(RejectedName, target, writer, Delivery);
    }

    private void ReceiveRejection(ulong sender, FastBufferReader reader)
    {
        if (sender == NetworkManager.ServerClientId && _subscribed) Rejected?.Invoke();
    }

    private void SendEntry(ulong target, Entry entry)
    {
        byte[] author = Encoding.UTF8.GetBytes(entry.Author);
        byte[] text = Encoding.UTF8.GetBytes(entry.Text);
        using var writer = new FastBufferWriter(32 + author.Length + text.Length + entry.Image.Length, Allocator.Temp);
        writer.WriteValueSafe(entry.Sender);
        writer.WriteValueSafe(entry.Timestamp);
        WriteBlob(writer, author);
        WriteBlob(writer, text);
        WriteBlob(writer, entry.Image);
        _messages.SendNamedMessage(EntryName, target, writer, Delivery);
    }

    private void ReceiveEntry(ulong sender, FastBufferReader reader)
    {
        if (sender != NetworkManager.ServerClientId || !_subscribed || !reader.TryBeginRead(16)) return;
        reader.ReadValueSafe(out ulong authorId);
        reader.ReadValueSafe(out long timestamp);
        if (timestamp < 0 || timestamp > DateTime.MaxValue.Ticks
            || !TryReadBlob(ref reader, 64, out var author)
            || !TryReadBlob(ref reader, TextLimit * 3, out var text)
            || !TryReadBlob(ref reader, ImageLimit, out var image) || reader.Position != reader.Length
            || (image.Length > 0 && !TryGetImageSize(image, out _, out _, out _))) return;
        Received?.Invoke(new Entry(authorId, timestamp, Encoding.UTF8.GetString(author), Encoding.UTF8.GetString(text), image));
    }

    private static void WriteBlob(FastBufferWriter writer, byte[] bytes)
    {
        int length = bytes?.Length ?? 0;
        writer.WriteValueSafe(length);
        if (length > 0) writer.WriteBytesSafe(bytes);
    }

    private static bool TryReadBlob(ref FastBufferReader reader, int max, out byte[] bytes)
    {
        bytes = null;
        if (!reader.TryBeginRead(sizeof(int))) return false;
        reader.ReadValueSafe(out int length);
        if (length < 0 || length > max || !reader.TryBeginRead(length)) return false;
        bytes = length == 0 ? Array.Empty<byte>() : new byte[length];
        if (length > 0) reader.ReadBytesSafe(ref bytes, length);
        return true;
    }
}
