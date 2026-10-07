using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>Bounded, acknowledged portrait uploads and downloads.</summary>
public class TokenImageSync : MonoBehaviour
{
    private const string MSG_META = "TokenImgMeta";
    private const string MSG_CHUNK = "TokenImgChunk";
    private const string MSG_ACK = "TokenImgAck";
    private const string MSG_UPLOAD_META = "TokenImgUploadMeta";
    private const string MSG_UPLOAD_CHUNK = "TokenImgUploadChunk";
    private const string MSG_UPLOAD_ACK = "TokenImgUploadAck";
    private const string MSG_PROGRESS = "TokenImgProgress";
    private const string MSG_UPLOAD_PROGRESS = "TokenImgUploadProgress";
    private const string MSG_CANCEL = "TokenImgCancel";
    private const int ChunkSize = 800;
    private const int BatchChunks = 16;
    public const int MaxPortraitBytes = 2 * 1024 * 1024;
    private const int MaxRetries = 3;
    private const float AckTimeoutSeconds = 6f;
    private const float StaleReceiveSeconds = 30f;
    private const float BatchTimeoutSeconds = 8f;

    private sealed class ReceiveState
    {
        public int Version;
        public int TotalChunks;
        public int TotalBytes;
        public uint Checksum;
        public ulong Sender;
        public float LastProgress;
        public readonly Dictionary<int, byte[]> Chunks = new();
    }

    private sealed class UploadState
    {
        public int Version;
        public byte[] Data;
        public byte[] PreviousData;
        public bool Acknowledged;
        public int ReceivedChunks;
    }

    private sealed class PendingPortrait
    {
        public int Version;
        public byte[] Data;
        public float ReceivedAt;
    }

    public static TokenImageSync Instance { get; private set; }
    public static bool AllClientsHaveCurrentPortraits
    {
        get
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || !nm.IsListening) return false;
            foreach (var entry in PortraitVersions)
            {
                var token = FindToken(entry.Key);
                if (token == null || !token.IsSpawned) continue;
                foreach (ulong client in nm.ConnectedClientsIds)
                {
                    if (client == NetworkManager.ServerClientId
                        || !token.NetworkObject.IsNetworkVisibleTo(client)) continue;
                    if (Instance == null || !Instance._downloadAcks.TryGetValue((client, entry.Key), out int ack)
                        || ack != entry.Value) return false;
                }
            }
            return true;
        }
    }
    private static readonly Dictionary<ulong, byte[]> PortraitCache = new();
    private static readonly Dictionary<ulong, int> PortraitVersions = new();

    private readonly Dictionary<ulong, ReceiveState> _incoming = new();
    private readonly Dictionary<ulong, ReceiveState> _uploadIncoming = new();
    private readonly Dictionary<ulong, UploadState> _outboundUploads = new();
    private readonly Dictionary<ulong, PendingPortrait> _pendingApply = new();
    private readonly Dictionary<ulong, int> _appliedDownloads = new();
    private readonly Dictionary<ulong, (ulong Sender, int Version)> _appliedUploads = new();
    private readonly Dictionary<(ulong Client, ulong Token), int> _downloadAcks = new();
    private readonly Dictionary<(ulong Client, ulong Token), (int Version, int Count)> _downloadProgress = new();
    private readonly HashSet<(ulong Client, ulong Token, int Version)> _cancelledDownloads = new();
    private readonly Dictionary<ulong, int> _cancelledIncomingVersions = new();
    private readonly Dictionary<(ulong Sender, ulong Token), int> _cancelledUploadVersions = new();
    private int _nextUploadVersion;
    private bool _registered;
    private bool _subscribed;

    public static void EnsureInstance()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        if (Instance == null)
        {
            Instance = nm.GetComponent<TokenImageSync>();
            if (Instance == null) Instance = nm.gameObject.AddComponent<TokenImageSync>();
        }
        Instance.TrySubscribe();
        Instance.RegisterHandlers();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        TrySubscribe();
        RegisterHandlers();
        StartCoroutine(RetryRegisterUntilReady());
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        RemoveStale(_incoming, now, false);
        RemoveStale(_uploadIncoming, now, true);
        var expired = new List<ulong>();
        foreach (var entry in _pendingApply)
            if (now - entry.Value.ReceivedAt > StaleReceiveSeconds) expired.Add(entry.Key);
        foreach (ulong id in expired)
        {
            _pendingApply.Remove(id);
            Debug.LogWarning($"[TokenImageSync] Portrait for missing token {id} expired");
        }
    }

    private static void RemoveStale(Dictionary<ulong, ReceiveState> states, float now, bool upload)
    {
        var expired = new List<ulong>();
        foreach (var entry in states)
            if (now - entry.Value.LastProgress > StaleReceiveSeconds) expired.Add(entry.Key);
        foreach (ulong id in expired)
        {
            states.Remove(id);
            ImageTransferUI.Finish(upload ? $"portrait-receive-upload-{id}"
                : $"portrait-receive-{id}", "Передача изображения прервалась");
            Debug.LogWarning($"[TokenImageSync] Incomplete transfer for token {id} expired");
        }
    }

    private void TrySubscribe()
    {
        if (_subscribed || NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnServerStarted += OnNetworkReady;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        _subscribed = true;
    }

    private void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || clientId != nm.LocalClientId) return;
        ClearClientState();
        UnregisterHandlers();
        RegisterHandlers();
        StartCoroutine(RetryRegisterUntilReady());
    }

    private void OnClientDisconnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        if (clientId == nm.LocalClientId)
        {
            ClearClientState();
            PortraitCache.Clear();
            PortraitVersions.Clear();
            UnregisterHandlers();
            return;
        }

        var abandoned = new List<ulong>();
        foreach (var entry in _uploadIncoming)
            if (entry.Value.Sender == clientId) abandoned.Add(entry.Key);
        foreach (ulong id in abandoned) _uploadIncoming.Remove(id);
        var ackKeys = new List<(ulong Client, ulong Token)>();
        foreach (var entry in _downloadAcks)
            if (entry.Key.Client == clientId) ackKeys.Add(entry.Key);
        foreach (var key in ackKeys) _downloadAcks.Remove(key);
    }

    private void ClearClientState()
    {
        foreach (ulong id in _incoming.Keys) ImageTransferUI.Remove($"portrait-receive-{id}");
        foreach (ulong id in _uploadIncoming.Keys) ImageTransferUI.Remove($"portrait-receive-upload-{id}");
        foreach (ulong id in _outboundUploads.Keys) ImageTransferUI.Remove($"portrait-upload-{id}");
        _incoming.Clear();
        _uploadIncoming.Clear();
        _outboundUploads.Clear();
        _pendingApply.Clear();
        _appliedDownloads.Clear();
        _appliedUploads.Clear();
        _downloadAcks.Clear();
        _downloadProgress.Clear();
        _cancelledDownloads.Clear();
        _cancelledIncomingVersions.Clear();
        _cancelledUploadVersions.Clear();
        _nextUploadVersion = 0;
    }

    private IEnumerator RetryRegisterUntilReady()
    {
        for (int i = 0; i < 50; i++)
        {
            TrySubscribe();
            RegisterHandlers();
            if (_registered && NetworkManager.Singleton != null
                && NetworkManager.Singleton.IsConnectedClient) yield break;
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
        }
        UnregisterHandlers();
        if (Instance == this) Instance = null;
    }

    private void OnNetworkReady() => RegisterHandlers();

    private void RegisterHandlers()
    {
        if (_registered) return;
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm == null) return;
        cmm.RegisterNamedMessageHandler(MSG_META, OnMetaReceived);
        cmm.RegisterNamedMessageHandler(MSG_CHUNK, OnChunkReceived);
        cmm.RegisterNamedMessageHandler(MSG_ACK, OnDownloadAck);
        cmm.RegisterNamedMessageHandler(MSG_UPLOAD_META, OnUploadMeta);
        cmm.RegisterNamedMessageHandler(MSG_UPLOAD_CHUNK, OnUploadChunk);
        cmm.RegisterNamedMessageHandler(MSG_UPLOAD_ACK, OnUploadAck);
        cmm.RegisterNamedMessageHandler(MSG_PROGRESS, OnDownloadProgress);
        cmm.RegisterNamedMessageHandler(MSG_UPLOAD_PROGRESS, OnUploadProgress);
        cmm.RegisterNamedMessageHandler(MSG_CANCEL, OnCancel);
        _registered = true;
    }

    private void UnregisterHandlers()
    {
        if (!_registered) return;
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm != null)
        {
            cmm.UnregisterNamedMessageHandler(MSG_META);
            cmm.UnregisterNamedMessageHandler(MSG_CHUNK);
            cmm.UnregisterNamedMessageHandler(MSG_ACK);
            cmm.UnregisterNamedMessageHandler(MSG_UPLOAD_META);
            cmm.UnregisterNamedMessageHandler(MSG_UPLOAD_CHUNK);
            cmm.UnregisterNamedMessageHandler(MSG_UPLOAD_ACK);
            cmm.UnregisterNamedMessageHandler(MSG_PROGRESS);
            cmm.UnregisterNamedMessageHandler(MSG_UPLOAD_PROGRESS);
            cmm.UnregisterNamedMessageHandler(MSG_CANCEL);
        }
        _registered = false;
    }

    public static void UploadToServer(ulong networkObjectId, byte[] jpgData,
        byte[] previousData = null)
    {
        EnsureInstance();
        var nm = NetworkManager.Singleton;
        if (Instance == null || nm == null || !nm.IsConnectedClient || nm.IsServer) return;
        if (!ValidPortrait(jpgData))
        {
            Debug.LogError("[TokenImageSync] Portrait exceeds the 2 MB network limit");
            DiceUI.Instance?.ShowToolNotice("Изображение токена слишком большое (максимум 2 МБ).");
            return;
        }
        if (Instance._outboundUploads.TryGetValue(networkObjectId, out UploadState previous))
        {
            previousData = previous.PreviousData;
            CancelTransfer(NetworkManager.ServerClientId, networkObjectId,
                previous.Version, true);
        }
        var upload = new UploadState
            { Version = ++Instance._nextUploadVersion, Data = jpgData, PreviousData = previousData };
        Instance._outboundUploads[networkObjectId] = upload;
        Instance.StartCoroutine(Instance.UploadRoutine(networkObjectId, upload));
    }

    private IEnumerator UploadRoutine(ulong netId, UploadState upload)
    {
        for (int attempt = 1; attempt <= MaxRetries + 1; attempt++)
        {
            if (!CurrentUpload(netId, upload)) yield break;
            ImageTransferUI.Show($"portrait-upload-{netId}", "Отправка токена",
                0f, $"Попытка {attempt}/{MaxRetries + 1}",
                () => CancelUpload(netId, upload.Version));
            yield return SendTransferRoutine(NetworkManager.ServerClientId, netId, upload.Version,
                upload.Data, MSG_UPLOAD_META, MSG_UPLOAD_CHUNK);
            float deadline = Time.unscaledTime + AckTimeoutSeconds;
            while (Time.unscaledTime < deadline && CurrentUpload(netId, upload))
            {
                if (upload.Acknowledged)
                {
                    _outboundUploads.Remove(netId);
                    ImageTransferUI.Finish($"portrait-upload-{netId}", "Изображение токена отправлено");
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.25f);
            }
            if (attempt <= MaxRetries && CurrentUpload(netId, upload))
                Debug.LogWarning($"[TokenImageSync] Upload retry {attempt}/{MaxRetries} for token {netId}");
        }
        if (CurrentUpload(netId, upload))
        {
            _outboundUploads.Remove(netId);
            RestorePreviousImage(netId, upload);
            ImageTransferUI.Finish($"portrait-upload-{netId}", "Не удалось отправить изображение токена");
            Debug.LogError($"[TokenImageSync] Upload failed for token {netId} after {MaxRetries} retries");
            DiceUI.Instance?.ShowToolNotice("Не удалось отправить изображение токена.");
        }
    }

    private bool CurrentUpload(ulong netId, UploadState upload) =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient
        && _outboundUploads.TryGetValue(netId, out UploadState current) && current == upload;

    private static void RestorePreviousImage(ulong netId, UploadState upload)
    {
        FindToken(netId)?.RestoreImageLocal(upload.PreviousData);
    }

    public static bool BroadcastImage(ulong networkObjectId, byte[] jpgData)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || !ValidPortrait(jpgData)) return false;
        var token = FindToken(networkObjectId);
        if (token == null || !token.ApplyImageLocal(jpgData)) return false;

        PortraitCache[networkObjectId] = jpgData;
        PortraitVersions[networkObjectId] = PortraitVersions.GetValueOrDefault(networkObjectId) + 1;
        foreach (ulong clientId in nm.ConnectedClientsIds)
        {
            if (clientId != NetworkManager.ServerClientId
                && token.NetworkObject.IsNetworkVisibleTo(clientId))
                SendImageToClient(clientId, networkObjectId, jpgData);
        }
        return true;
    }

    public static void CachePortrait(ulong networkObjectId, byte[] jpgData)
    {
        if (!ValidPortrait(jpgData)) return;
        PortraitCache[networkObjectId] = jpgData;
        if (!PortraitVersions.ContainsKey(networkObjectId)) PortraitVersions[networkObjectId] = 1;
    }

    public static void RemovePortrait(ulong networkObjectId)
    {
        PortraitCache.Remove(networkObjectId);
        PortraitVersions.Remove(networkObjectId);
        if (Instance == null) return;
        Instance._pendingApply.Remove(networkObjectId);
        Instance._incoming.Remove(networkObjectId);
        Instance._uploadIncoming.Remove(networkObjectId);
        Instance._outboundUploads.Remove(networkObjectId);
        Instance._appliedDownloads.Remove(networkObjectId);
        Instance._appliedUploads.Remove(networkObjectId);
        Instance._cancelledIncomingVersions.Remove(networkObjectId);
        ImageTransferUI.Remove($"portrait-upload-{networkObjectId}");
        ImageTransferUI.Remove($"portrait-receive-{networkObjectId}");
        ImageTransferUI.Remove($"portrait-receive-upload-{networkObjectId}");
        var ackKeys = new List<(ulong Client, ulong Token)>();
        foreach (var entry in Instance._downloadAcks)
            if (entry.Key.Token == networkObjectId) ackKeys.Add(entry.Key);
        foreach (var key in ackKeys) Instance._downloadAcks.Remove(key);
        Instance._cancelledDownloads.RemoveWhere(key => key.Token == networkObjectId);
        var cancelledUploads = new List<(ulong Sender, ulong Token)>();
        foreach (var entry in Instance._cancelledUploadVersions)
            if (entry.Key.Token == networkObjectId) cancelledUploads.Add(entry.Key);
        foreach (var key in cancelledUploads) Instance._cancelledUploadVersions.Remove(key);
        var progressKeys = new List<(ulong Client, ulong Token)>();
        foreach (var entry in Instance._downloadProgress)
            if (entry.Key.Token == networkObjectId) progressKeys.Add(entry.Key);
        foreach (var key in progressKeys) Instance._downloadProgress.Remove(key);
    }

    public static bool TryGetCachedPortrait(ulong networkObjectId, out byte[] jpgData)
    {
        if (PortraitCache.TryGetValue(networkObjectId, out jpgData) && ValidPortrait(jpgData))
            return true;
        jpgData = null;
        return false;
    }

    public static byte[] GetPortraitBytesForCopy(TokenController source)
    {
        if (source == null || !source.IsSpawned) return null;
        if (TryGetCachedPortrait(source.NetworkObjectId, out byte[] cached)) return cached;
        return source.GetPortraitJpg();
    }

    public static bool TryApplyPending(TokenController token)
    {
        if (Instance == null || token == null || !token.IsSpawned) return false;
        ulong netId = token.NetworkObjectId;
        if (!Instance._pendingApply.TryGetValue(netId, out PendingPortrait pending)) return false;
        if (!token.ApplyImageLocal(pending.Data)) return false;
        Instance._pendingApply.Remove(netId);
        Instance._appliedDownloads[netId] = pending.Version;
        Instance.SendDownloadAck(netId, pending.Version);
        ImageTransferUI.Finish($"portrait-receive-{netId}", "Токен получен");
        return true;
    }

    public static void SendImageToClient(ulong clientId, ulong networkObjectId, byte[] jpgData)
    {
        if (Instance == null || NetworkManager.Singleton == null
            || !NetworkManager.Singleton.IsServer || !ValidPortrait(jpgData)) return;
        if (!PortraitVersions.ContainsKey(networkObjectId)) PortraitVersions[networkObjectId] = 1;
        Instance.StartCoroutine(Instance.SendPortraitWithRetriesRoutine(
            clientId, networkObjectId, jpgData, PortraitVersions[networkObjectId]));
    }

    public IEnumerator SendAllPortraitsToClientRoutine(ulong clientId)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) yield break;
        var sent = new HashSet<ulong>();
        var cached = new List<KeyValuePair<ulong, byte[]>>(PortraitCache);
        foreach (var entry in cached)
        {
            if (!ValidPortrait(entry.Value) || FindToken(entry.Key) == null) continue;
            if (!PortraitVersions.ContainsKey(entry.Key)) PortraitVersions[entry.Key] = 1;
            yield return SendPortraitWithRetriesRoutine(clientId, entry.Key, entry.Value,
                PortraitVersions[entry.Key]);
            sent.Add(entry.Key);
        }

        var tokens = Object.FindObjectsByType<TokenController>(FindObjectsInactive.Exclude);
        foreach (var token in tokens)
        {
            if (token == null || !token.IsSpawned || sent.Contains(token.NetworkObjectId)) continue;
            byte[] jpg = token.GetPortraitJpg();
            if (!ValidPortrait(jpg)) continue;
            CachePortrait(token.NetworkObjectId, jpg);
            yield return SendPortraitWithRetriesRoutine(clientId, token.NetworkObjectId, jpg,
                PortraitVersions[token.NetworkObjectId]);
        }
    }

    private IEnumerator SendPortraitWithRetriesRoutine(
        ulong clientId, ulong netId, byte[] data, int version)
    {
        for (int attempt = 1; attempt <= MaxRetries + 1; attempt++)
        {
            if (!CanSendPortrait(clientId, netId, version)) yield break;
            yield return SendTransferRoutine(clientId, netId, version, data, MSG_META, MSG_CHUNK);
            float deadline = Time.unscaledTime + AckTimeoutSeconds;
            while (Time.unscaledTime < deadline)
            {
                if (_downloadAcks.TryGetValue((clientId, netId), out int ack) && ack == version)
                {
                    ImageTransferUI.Finish($"portrait-send-{clientId}-{netId}", "Токен отправлен");
                    yield break;
                }
                if (!CanSendPortrait(clientId, netId, version)) yield break;
                yield return new WaitForSecondsRealtime(0.25f);
            }
            if (attempt <= MaxRetries)
                Debug.LogWarning($"[TokenImageSync] Download retry {attempt}/{MaxRetries} for token {netId}, client {clientId}");
        }
        if (CanSendPortrait(clientId, netId, version))
        {
            ImageTransferUI.Finish($"portrait-send-{clientId}-{netId}", "Не удалось отправить токен");
            Debug.LogError($"[TokenImageSync] Client {clientId} did not apply portrait for token {netId}");
            DiceUI.Instance?.ShowToolNotice($"Игрок {clientId} не получил изображение токена.");
        }
    }

    private static bool CanSendPortrait(ulong clientId, ulong netId, int version)
    {
        var nm = NetworkManager.Singleton;
        return nm != null && nm.IsServer && nm.ConnectedClients.ContainsKey(clientId)
            && PortraitVersions.TryGetValue(netId, out int current) && current == version
            && (Instance == null || !Instance._cancelledDownloads.Contains((clientId, netId, version)));
    }

    private IEnumerator SendTransferRoutine(
        ulong target, ulong netId, int version, byte[] data, string metaName, string chunkName)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsConnectedClient) yield break;
        if (nm.IsServer && !nm.ConnectedClients.ContainsKey(target)) yield break;
        var cmm = nm.CustomMessagingManager;
        int total = (data.Length + ChunkSize - 1) / ChunkSize;
        uint checksum = ComputeChecksum(data);
        using (var writer = new FastBufferWriter(sizeof(ulong) + 4 * sizeof(int), Allocator.Temp))
        {
            writer.WriteValueSafe(netId);
            writer.WriteValueSafe(version);
            writer.WriteValueSafe(total);
            writer.WriteValueSafe(data.Length);
            writer.WriteValueSafe(checksum);
            cmm.SendNamedMessage(metaName, target, writer);
        }
        yield return null;
        for (int i = 0; i < total; i++)
        {
            nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsConnectedClient
                || nm.IsServer && !nm.ConnectedClients.ContainsKey(target))
                yield break;
            if (!TransferStillNeeded(metaName, target, netId, version)) yield break;
            int offset = i * ChunkSize;
            int size = Mathf.Min(ChunkSize, data.Length - offset);
            while (!NetworkTransferBudget.TryConsume(target, size + 20))
                yield return null;
            nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsConnectedClient
                || nm.IsServer && !nm.ConnectedClients.ContainsKey(target)
                || !TransferStillNeeded(metaName, target, netId, version))
                yield break;
            using (var writer = new FastBufferWriter(sizeof(ulong) + 3 * sizeof(int) + size,
                Allocator.Temp))
            {
                writer.WriteValueSafe(netId);
                writer.WriteValueSafe(version);
                writer.WriteValueSafe(i);
                writer.WriteValueSafe(size);
                for (int j = 0; j < size; j++) writer.WriteValueSafe(data[offset + j]);
                cmm.SendNamedMessage(chunkName, target, writer);
            }
            if ((i + 1) % BatchChunks == 0 || i + 1 == total)
            {
                if (metaName == MSG_UPLOAD_META)
                    ImageTransferUI.Show($"portrait-upload-{netId}", "Отправка токена",
                        (float)(i + 1) / total, "Передача изображения на сервер",
                        () => CancelUpload(netId, version));
                else
                    ImageTransferUI.Show($"portrait-send-{target}-{netId}", "Отправка токена",
                        (float)(i + 1) / total, $"Игроку {target}",
                        () => CancelDownload(target, netId, version));
            }
            // Wait for receiver progress before queuing another batch of reliable messages.
            if ((i + 1) % BatchChunks == 0 && i + 1 < total)
            {
                float deadline = Time.unscaledTime + BatchTimeoutSeconds;
                while (Time.unscaledTime < deadline && TransferStillNeeded(metaName, target, netId, version)
                    && ReceivedProgress(metaName, target, netId, version) < i + 1)
                    yield return null;
                if (ReceivedProgress(metaName, target, netId, version) < i + 1)
                    yield break;
            }
            else if ((i + 1) % 3 == 0) yield return null;
        }
    }

    private int ReceivedProgress(string metaName, ulong target, ulong netId, int version)
    {
        if (metaName == MSG_UPLOAD_META)
            return _outboundUploads.TryGetValue(netId, out UploadState upload)
                && upload.Version == version ? upload.ReceivedChunks : 0;
        return _downloadProgress.TryGetValue((target, netId), out var progress)
            && progress.Version == version ? progress.Count : 0;
    }

    private bool TransferStillNeeded(string metaName, ulong target, ulong netId, int version)
    {
        if (metaName == MSG_META)
            return PortraitVersions.TryGetValue(netId, out int currentVersion)
                && currentVersion == version
                && !_cancelledDownloads.Contains((target, netId, version))
                && (!_downloadAcks.TryGetValue((target, netId), out int ack) || ack != version);
        return _outboundUploads.TryGetValue(netId, out UploadState upload)
            && upload.Version == version && !upload.Acknowledged;
    }

    private void ReportReceiveProgress(ulong sender, ulong netId, ReceiveState state, bool upload)
    {
        int count = state.Chunks.Count;
        string id = upload ? $"portrait-receive-upload-{netId}" : $"portrait-receive-{netId}";
        if (count % BatchChunks != 0 && count != state.TotalChunks) return;
        ImageTransferUI.Show(id, "Получение токена", (float)count / state.TotalChunks,
            upload ? $"От игрока {sender}" : "От сервера",
            upload ? () => CancelIncomingUpload(sender, netId, state.Version)
                : () => CancelIncomingDownload(netId, state.Version));
        SendReceiveCount(sender, netId, state.Version, count, upload);
    }

    private static void SendReceiveCount(ulong sender, ulong netId, int version, int count, bool upload)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        using var writer = new FastBufferWriter(sizeof(ulong) + 2 * sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(netId);
        writer.WriteValueSafe(version);
        writer.WriteValueSafe(count);
        nm.CustomMessagingManager.SendNamedMessage(upload ? MSG_UPLOAD_PROGRESS : MSG_PROGRESS,
            sender, writer);
    }

    private void OnUploadProgress(ulong sender, FastBufferReader reader)
    {
        if (sender != NetworkManager.ServerClientId || !TryReadProgress(reader,
            out ulong netId, out int version, out int count)) return;
        if (_outboundUploads.TryGetValue(netId, out UploadState upload)
            && upload.Version == version
            && count <= (upload.Data.Length + ChunkSize - 1) / ChunkSize)
            upload.ReceivedChunks = Mathf.Max(upload.ReceivedChunks, count);
    }

    private void OnDownloadProgress(ulong sender, FastBufferReader reader)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer
            || !TryReadProgress(reader, out ulong netId, out int version, out int count)) return;
        if (PortraitVersions.TryGetValue(netId, out int current) && current == version
            && PortraitCache.TryGetValue(netId, out byte[] data)
            && count <= (data.Length + ChunkSize - 1) / ChunkSize)
            _downloadProgress[(sender, netId)] = (version, count);
    }

    private static bool TryReadProgress(FastBufferReader reader,
        out ulong netId, out int version, out int count)
    {
        netId = 0;
        version = count = 0;
        if (!reader.TryBeginRead(sizeof(ulong) + 2 * sizeof(int))) return false;
        reader.ReadValueSafe(out netId);
        reader.ReadValueSafe(out version);
        reader.ReadValueSafe(out count);
        return count >= 0;
    }

    private void CancelUpload(ulong netId, int version, bool notify = true)
    {
        if (!_outboundUploads.TryGetValue(netId, out UploadState upload)
            || upload.Version != version) return;
        _outboundUploads.Remove(netId);
        RestorePreviousImage(netId, upload);
        if (notify) CancelTransfer(NetworkManager.ServerClientId, netId, version, true);
        ImageTransferUI.Finish($"portrait-upload-{netId}", "Отправка токена отменена");
    }

    private void CancelIncomingUpload(ulong sender, ulong netId, int version, bool notify = true)
    {
        if (!_uploadIncoming.TryGetValue(netId, out ReceiveState state)
            || state.Version != version || state.Sender != sender) return;
        _uploadIncoming.Remove(netId);
        _cancelledUploadVersions[(sender, netId)] = version;
        if (notify) CancelTransfer(sender, netId, version, true);
        ImageTransferUI.Finish($"portrait-receive-upload-{netId}", "Получение токена отменено");
    }

    private void CancelDownload(ulong clientId, ulong netId, int version, bool notify = true)
    {
        _cancelledDownloads.Add((clientId, netId, version));
        if (notify) CancelTransfer(clientId, netId, version, false);
        ImageTransferUI.Finish($"portrait-send-{clientId}-{netId}", "Отправка токена отменена");
    }

    private void CancelIncomingDownload(ulong netId, int version, bool notify = true)
    {
        if (_incoming.TryGetValue(netId, out ReceiveState incoming)
            && incoming.Version == version) _incoming.Remove(netId);
        if (_pendingApply.TryGetValue(netId, out PendingPortrait pending)
            && pending.Version == version) _pendingApply.Remove(netId);
        _cancelledIncomingVersions[netId] = version;
        if (notify) CancelTransfer(NetworkManager.ServerClientId, netId, version, false);
        ImageTransferUI.Finish($"portrait-receive-{netId}", "Получение токена отменено");
    }

    private static void CancelTransfer(ulong target, ulong netId, int version, bool upload)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsConnectedClient) return;
        using var writer = new FastBufferWriter(sizeof(ulong) + sizeof(int) + sizeof(bool), Allocator.Temp);
        writer.WriteValueSafe(netId);
        writer.WriteValueSafe(version);
        writer.WriteValueSafe(upload);
        nm.CustomMessagingManager.SendNamedMessage(MSG_CANCEL, target, writer);
    }

    private void OnCancel(ulong sender, FastBufferReader reader)
    {
        if (!reader.TryBeginRead(sizeof(ulong) + sizeof(int) + sizeof(bool))) return;
        reader.ReadValueSafe(out ulong netId);
        reader.ReadValueSafe(out int version);
        reader.ReadValueSafe(out bool upload);
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        if (upload)
        {
            if (nm.IsServer)
            {
                _cancelledUploadVersions[(sender, netId)] = version;
                if (_uploadIncoming.TryGetValue(netId, out ReceiveState state)
                    && state.Sender == sender && state.Version == version)
                    CancelIncomingUpload(sender, netId, version, false);
            }
            else if (sender == NetworkManager.ServerClientId)
                CancelUpload(netId, version, false);
        }
        else if (nm.IsServer && PortraitVersions.TryGetValue(netId, out int current)
            && current == version)
            CancelDownload(sender, netId, version, false);
        else if (sender == NetworkManager.ServerClientId)
            CancelIncomingDownload(netId, version, false);
    }

    private void OnUploadMeta(ulong senderId, FastBufferReader reader)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (!TryReadMeta(reader, out ulong netId, out int version, out int total,
            out int bytes, out uint checksum)) return;
        if (!ValidMetadata(total, bytes)
            || !NetworkPermissions.CanUploadTokenPortrait(senderId, FindToken(netId))) return;
        if (_cancelledUploadVersions.TryGetValue((senderId, netId), out int cancelled)
            && version <= cancelled) return;

        if (_appliedUploads.TryGetValue(netId, out var applied)
            && applied.Sender == senderId && version <= applied.Version)
        {
            if (version == applied.Version) SendUploadAck(senderId, netId, version);
            return;
        }
        if (_uploadIncoming.TryGetValue(netId, out ReceiveState existing)
            && existing.Sender == senderId && existing.Version == version)
        {
            existing.LastProgress = Time.unscaledTime;
            SendReceiveCount(senderId, netId, existing.Version, existing.Chunks.Count, true);
            return;
        }
        if (existing != null && existing.Sender == senderId && existing.Version > version)
            return;
        _uploadIncoming[netId] = NewReceiveState(senderId, version, total, bytes, checksum);
        ImageTransferUI.Show($"portrait-receive-upload-{netId}", "Получение токена",
            0f, $"От игрока {senderId}", () => CancelIncomingUpload(senderId, netId, version));
    }

    private void OnUploadChunk(ulong senderId, FastBufferReader reader)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (!ReadChunk(reader, _uploadIncoming, senderId, out ulong netId, out ReceiveState state))
            return;
        ReportReceiveProgress(senderId, netId, state, true);
        if (state.Chunks.Count != state.TotalChunks) return;
        _uploadIncoming.Remove(netId);
        if (!TryReassemble(state, out byte[] full))
        {
            Debug.LogWarning($"[TokenImageSync] Invalid upload for token {netId}");
            return;
        }
        var token = FindToken(netId);
        if (!NetworkPermissions.CanUploadTokenPortrait(senderId, token)
            || !BroadcastImage(netId, full)) return;
        _appliedUploads[netId] = (senderId, state.Version);
        SendUploadAck(senderId, netId, state.Version);
        ImageTransferUI.Finish($"portrait-receive-upload-{netId}", "Токен получен");
    }

    private void OnUploadAck(ulong senderId, FastBufferReader reader)
    {
        if (senderId != NetworkManager.ServerClientId) return;
        if (!reader.TryBeginRead(sizeof(ulong) + sizeof(int))) return;
        reader.ReadValueSafe(out ulong netId);
        reader.ReadValueSafe(out int version);
        if (_outboundUploads.TryGetValue(netId, out UploadState upload)
            && upload.Version == version) upload.Acknowledged = true;
    }

    private void OnMetaReceived(ulong senderId, FastBufferReader reader)
    {
        if (senderId != NetworkManager.ServerClientId) return;
        if (!TryReadMeta(reader, out ulong netId, out int version, out int total,
            out int bytes, out uint checksum)) return;
        if (!ValidMetadata(total, bytes)) return;
        if (_cancelledIncomingVersions.TryGetValue(netId, out int cancelled)
            && version <= cancelled) return;
        if (_appliedDownloads.TryGetValue(netId, out int applied) && version <= applied)
        {
            if (version == applied) SendDownloadAck(netId, version);
            return;
        }
        if (_pendingApply.TryGetValue(netId, out PendingPortrait pending)
            && pending.Version >= version) return;
        if (_incoming.TryGetValue(netId, out ReceiveState existing)
            && existing.Version == version)
        {
            existing.LastProgress = Time.unscaledTime;
            SendReceiveCount(senderId, netId, existing.Version, existing.Chunks.Count, false);
            return;
        }
        if (existing != null && existing.Version > version) return;
        _incoming[netId] = NewReceiveState(senderId, version, total, bytes, checksum);
        ImageTransferUI.Show($"portrait-receive-{netId}", "Получение токена",
            0f, "От сервера", () => CancelIncomingDownload(netId, version));
    }

    private void OnChunkReceived(ulong senderId, FastBufferReader reader)
    {
        if (senderId != NetworkManager.ServerClientId) return;
        if (!ReadChunk(reader, _incoming, senderId, out ulong netId, out ReceiveState state))
            return;
        ReportReceiveProgress(senderId, netId, state, false);
        if (state.Chunks.Count != state.TotalChunks) return;
        _incoming.Remove(netId);
        if (!TryReassemble(state, out byte[] full))
        {
            Debug.LogWarning($"[TokenImageSync] Invalid download for token {netId}");
            return;
        }
        var token = FindToken(netId);
        if (token == null)
        {
            _pendingApply[netId] = new PendingPortrait
                { Version = state.Version, Data = full, ReceivedAt = Time.unscaledTime };
            return;
        }
        if (!token.ApplyImageLocal(full)) return;
        _appliedDownloads[netId] = state.Version;
        SendDownloadAck(netId, state.Version);
        ImageTransferUI.Finish($"portrait-receive-{netId}", "Токен получен");
    }

    private void OnDownloadAck(ulong senderId, FastBufferReader reader)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (!reader.TryBeginRead(sizeof(ulong) + sizeof(int))) return;
        reader.ReadValueSafe(out ulong netId);
        reader.ReadValueSafe(out int version);
        if (PortraitVersions.TryGetValue(netId, out int current) && current == version)
            _downloadAcks[(senderId, netId)] = version;
    }

    private void SendUploadAck(ulong clientId, ulong netId, int version)
    {
        using var writer = new FastBufferWriter(sizeof(ulong) + sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(netId);
        writer.WriteValueSafe(version);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(MSG_UPLOAD_ACK, clientId,
            writer);
    }

    private void SendDownloadAck(ulong netId, int version)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsConnectedClient || nm.IsServer) return;
        using var writer = new FastBufferWriter(sizeof(ulong) + sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(netId);
        writer.WriteValueSafe(version);
        nm.CustomMessagingManager.SendNamedMessage(MSG_ACK, NetworkManager.ServerClientId, writer);
    }

    private static ReceiveState NewReceiveState(
        ulong sender, int version, int total, int bytes, uint checksum) =>
        new ReceiveState
        {
            Sender = sender, Version = version, TotalChunks = total,
            TotalBytes = bytes, Checksum = checksum, LastProgress = Time.unscaledTime
        };

    private static bool TryReadMeta(FastBufferReader reader, out ulong netId, out int version,
        out int total, out int bytes, out uint checksum)
    {
        netId = 0;
        version = total = bytes = 0;
        checksum = 0;
        if (!reader.TryBeginRead(sizeof(ulong) + 4 * sizeof(int))) return false;
        reader.ReadValueSafe(out netId);
        reader.ReadValueSafe(out version);
        reader.ReadValueSafe(out total);
        reader.ReadValueSafe(out bytes);
        reader.ReadValueSafe(out checksum);
        return true;
    }

    private static bool ValidMetadata(int total, int bytes) =>
        bytes > 0 && bytes <= MaxPortraitBytes
        && total == (bytes + ChunkSize - 1) / ChunkSize;

    private static bool ReadChunk(FastBufferReader reader,
        Dictionary<ulong, ReceiveState> states, ulong sender,
        out ulong netId, out ReceiveState state)
    {
        netId = 0;
        state = null;
        if (!reader.TryBeginRead(sizeof(ulong) + 3 * sizeof(int))) return false;
        reader.ReadValueSafe(out netId);
        reader.ReadValueSafe(out int version);
        reader.ReadValueSafe(out int index);
        reader.ReadValueSafe(out int size);
        if (!states.TryGetValue(netId, out state) || state.Sender != sender
            || state.Version != version || index < 0 || index >= state.TotalChunks
            || size != Mathf.Min(ChunkSize, state.TotalBytes - index * ChunkSize)
            || !reader.TryBeginRead(size))
            return false;
        byte[] chunk = new byte[size];
        for (int i = 0; i < size; i++) reader.ReadValueSafe(out chunk[i]);
        if (!state.Chunks.ContainsKey(index)) state.Chunks.Add(index, chunk);
        state.LastProgress = Time.unscaledTime;
        return true;
    }

    private static bool TryReassemble(ReceiveState state, out byte[] full)
    {
        full = new byte[state.TotalBytes];
        int offset = 0;
        for (int i = 0; i < state.TotalChunks; i++)
        {
            if (!state.Chunks.TryGetValue(i, out byte[] chunk)) return false;
            System.Array.Copy(chunk, 0, full, offset, chunk.Length);
            offset += chunk.Length;
        }
        return offset == full.Length && ComputeChecksum(full) == state.Checksum;
    }

    private static bool ValidPortrait(byte[] data) =>
        data != null && data.Length > 0 && data.Length <= MaxPortraitBytes;

    private static uint ComputeChecksum(byte[] data)
    {
        const uint prime = 16777619;
        uint hash = 2166136261;
        foreach (byte value in data) hash = unchecked((hash ^ value) * prime);
        return hash;
    }

    private static TokenController FindToken(ulong netId)
    {
        var nm = NetworkManager.Singleton;
        if (nm?.SpawnManager != null
            && nm.SpawnManager.SpawnedObjects.TryGetValue(netId, out NetworkObject netObj))
            return netObj.GetComponent<TokenController>();
        return null;
    }
}
