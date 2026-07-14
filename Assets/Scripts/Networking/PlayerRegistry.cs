using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative player registry. Clients send nickname on connect;
/// server assigns color by nickname and syncs to all peers.
/// </summary>
public class PlayerRegistry : MonoBehaviour
{
    private const string MSG_REGISTER = "PlayerRegister";
    private const string MSG_SYNC = "PlayerSync";
    private const string MSG_SYNC_ALL = "PlayerSyncAll";
    private const string MSG_REMOVE = "PlayerRemove";

    private static readonly Color[] Palette =
    {
        new Color(0.9f, 0.3f, 0.3f),
        new Color(0.3f, 0.6f, 0.9f),
        new Color(0.3f, 0.9f, 0.4f),
        new Color(0.9f, 0.8f, 0.2f),
        new Color(0.7f, 0.3f, 0.9f),
        new Color(0.9f, 0.5f, 0.2f),
        new Color(0.2f, 0.8f, 0.9f),
        new Color(0.9f, 0.4f, 0.7f),
        new Color(0.5f, 0.9f, 0.3f),
    };

    public static PlayerRegistry Instance { get; private set; }

    private readonly Dictionary<ulong, PlayerEntry> _activePlayers = new();
    private readonly Dictionary<string, Color> _nicknameColors = new();
    private readonly Dictionary<string, ulong> _disconnectedByNickname = new();
    private int _nextColorIndex;
    private bool _handlersRegistered;

    private struct PlayerEntry
    {
        public ulong ClientId;
        public string Nickname;
        public Color Color;
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
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
            TryRegisterClientHandlers();
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;

        UnregisterHandlers();
        if (Instance == this)
            Instance = null;
    }

    private void OnServerStarted()
    {
        RegisterServerHandlers();
        TryRegisterClientHandlers();
    }

    public void OnLocalClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        CellMarker.EnsureRegistered();
        TokenImageSync.EnsureInstance();
        LateJoinSync.EnsureInstance();
        StartCoroutine(RegisterLocalPlayerWhenReady());
        StartCoroutine(LateJoinSync.SendReadyAfterSceneLoad());
    }

    private IEnumerator RegisterLocalPlayerWhenReady()
    {
        for (int i = 0; i < 20; i++)
        {
            TryRegisterClientHandlers();
            if (NetworkManager.Singleton?.CustomMessagingManager != null)
                break;
            yield return new WaitForSeconds(0.1f);
        }

        if (NetworkManager.Singleton == null) yield break;

        string nickname = NormalizeNickname(LobbyUI.LocalNickname);
        if (NetworkManager.Singleton.IsServer)
            RegisterPlayerOnServer(NetworkManager.Singleton.LocalClientId, nickname);
        else
            SendRegisterRequest(nickname);
    }

    public void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        if (!_activePlayers.Remove(clientId, out var removed)) return;

        _disconnectedByNickname[removed.Nickname] = clientId;

        BroadcastRemove(clientId);
        Debug.Log($"[PlayerRegistry] Disconnected {removed.Nickname} ({clientId}), kept for reconnect");
    }

    public void SendFullStateToClient(ulong clientId)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        SendSyncAll(clientId);
    }

    /// <summary>Server: authoritative player color for spawning/syncing world objects.</summary>
    public static Color GetServerPlayerColor(ulong clientId)
    {
        if (Instance != null
            && NetworkManager.Singleton != null
            && NetworkManager.Singleton.IsServer
            && Instance._activePlayers.TryGetValue(clientId, out PlayerEntry entry))
            return entry.Color;

        return PlayerColors.GetColor(clientId);
    }

    private void RegisterServerHandlers()
    {
        if (_handlersRegistered || NetworkManager.Singleton == null) return;

        var cmm = NetworkManager.Singleton.CustomMessagingManager;
        if (cmm == null) return;

        cmm.RegisterNamedMessageHandler(MSG_REGISTER, OnRegisterRequest);
        _handlersRegistered = true;
    }

    private void TryRegisterClientHandlers()
    {
        if (NetworkManager.Singleton == null) return;

        var cmm = NetworkManager.Singleton.CustomMessagingManager;
        if (cmm == null) return;

        cmm.UnregisterNamedMessageHandler(MSG_SYNC);
        cmm.UnregisterNamedMessageHandler(MSG_SYNC_ALL);
        cmm.UnregisterNamedMessageHandler(MSG_REMOVE);

        cmm.RegisterNamedMessageHandler(MSG_SYNC, OnPlayerSync);
        cmm.RegisterNamedMessageHandler(MSG_SYNC_ALL, OnPlayerSyncAll);
        cmm.RegisterNamedMessageHandler(MSG_REMOVE, OnPlayerRemove);
    }

    private void UnregisterHandlers()
    {
        if (NetworkManager.Singleton?.CustomMessagingManager == null) return;

        var cmm = NetworkManager.Singleton.CustomMessagingManager;
        if (NetworkManager.Singleton.IsServer)
            cmm.UnregisterNamedMessageHandler(MSG_REGISTER);

        cmm.UnregisterNamedMessageHandler(MSG_SYNC);
        cmm.UnregisterNamedMessageHandler(MSG_SYNC_ALL);
        cmm.UnregisterNamedMessageHandler(MSG_REMOVE);
        _handlersRegistered = false;
    }

    private void OnRegisterRequest(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out FixedString64Bytes nickFixed);
        RegisterPlayerOnServer(senderId, nickFixed.ToString());
    }

    private void RegisterPlayerOnServer(ulong clientId, string requestedNickname)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        string normalized = NormalizeNickname(requestedNickname);
        string nickname = ResolveActiveNickname(requestedNickname, clientId);
        Color color = GetOrAssignColor(nickname);

        ulong oldClientId = FindDisconnectedClientId(normalized, nickname);

        _activePlayers[clientId] = new PlayerEntry
        {
            ClientId = clientId,
            Nickname = nickname,
            Color = color,
        };

        ApplyLocalPlayer(clientId, nickname, color);
        BroadcastSync(clientId, nickname, color);
        SendSyncAll(clientId);

        if (oldClientId != 0 && oldClientId != clientId)
        {
            PlayerOwnershipReassigner.ReassignPlayerObjects(oldClientId, clientId);
            ClearDisconnectedRecord(normalized, nickname);
        }

        Debug.Log($"[PlayerRegistry] Registered {nickname} ({clientId}) color={color}"
            + (oldClientId != 0 && oldClientId != clientId ? $" [reconnect from {oldClientId}]" : ""));
    }

    private ulong FindDisconnectedClientId(string normalizedRequest, string resolvedNickname)
    {
        if (_disconnectedByNickname.TryGetValue(resolvedNickname, out ulong byResolved))
            return byResolved;

        if (normalizedRequest != resolvedNickname
            && _disconnectedByNickname.TryGetValue(normalizedRequest, out ulong byRequest))
            return byRequest;

        return 0;
    }

    private void ClearDisconnectedRecord(string normalizedRequest, string resolvedNickname)
    {
        _disconnectedByNickname.Remove(resolvedNickname);
        if (normalizedRequest != resolvedNickname)
            _disconnectedByNickname.Remove(normalizedRequest);
    }

    private static string NormalizeNickname(string nickname)
    {
        string nick = string.IsNullOrWhiteSpace(nickname) ? "Player" : nickname.Trim();
        return nick.Length > 48 ? nick.Substring(0, 48) : nick;
    }

    private string ResolveActiveNickname(string requested, ulong clientId)
    {
        requested = NormalizeNickname(requested);

        bool takenByOther = false;
        foreach (var entry in _activePlayers.Values)
        {
            if (entry.ClientId == clientId) continue;
            if (entry.Nickname == requested)
            {
                takenByOther = true;
                break;
            }
        }

        if (!takenByOther) return requested;

        int suffix = 2;
        string candidate;
        do
        {
            candidate = $"{requested} ({suffix})";
            suffix++;
        }
        while (IsActiveNicknameTaken(candidate, clientId));

        return candidate;
    }

    private bool IsActiveNicknameTaken(string nickname, ulong exceptClientId)
    {
        foreach (var entry in _activePlayers.Values)
        {
            if (entry.ClientId == exceptClientId) continue;
            if (entry.Nickname == nickname) return true;
        }

        return false;
    }

    private Color GetOrAssignColor(string nickname)
    {
        if (_nicknameColors.TryGetValue(nickname, out Color existing))
            return existing;

        Color color = Palette[_nextColorIndex % Palette.Length];
        _nextColorIndex++;
        _nicknameColors[nickname] = color;
        return color;
    }

    private void SendRegisterRequest(string nickname)
    {
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm == null) return;

        using var writer = new FastBufferWriter(64, Allocator.Temp);
        writer.WriteValueSafe(new FixedString64Bytes(nickname));
        cmm.SendNamedMessage(MSG_REGISTER, NetworkManager.ServerClientId, writer);
    }

    private void BroadcastSync(ulong clientId, string nickname, Color color)
    {
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm == null) return;

        using var writer = CreatePlayerWriter(clientId, nickname, color);
        cmm.SendNamedMessageToAll(MSG_SYNC, writer);
    }

    private void SendSyncAll(ulong targetClientId)
    {
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm == null) return;

        using var writer = new FastBufferWriter(256, Allocator.Temp);
        writer.WriteValueSafe(_activePlayers.Count);

        foreach (var entry in _activePlayers.Values)
            WritePlayer(writer, entry.ClientId, entry.Nickname, entry.Color);

        cmm.SendNamedMessage(MSG_SYNC_ALL, targetClientId, writer);
    }

    private void BroadcastRemove(ulong clientId)
    {
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm == null) return;

        using var writer = new FastBufferWriter(sizeof(ulong), Allocator.Temp);
        writer.WriteValueSafe(clientId);
        cmm.SendNamedMessageToAll(MSG_REMOVE, writer);
        ApplyLocalRemove(clientId);
    }

    private void OnPlayerSync(ulong senderId, FastBufferReader reader)
    {
        ReadPlayer(reader, out ulong clientId, out string nickname, out Color color);
        ApplyLocalPlayer(clientId, nickname, color);
    }

    private void OnPlayerSyncAll(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int count);
        for (int i = 0; i < count; i++)
        {
            ReadPlayer(reader, out ulong clientId, out string nickname, out Color color);
            ApplyLocalPlayer(clientId, nickname, color);
        }
    }

    private void OnPlayerRemove(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out ulong clientId);
        ApplyLocalRemove(clientId);
    }

    private static void ApplyLocalPlayer(ulong clientId, string nickname, Color color)
    {
        PlayerColors.SetPlayer(clientId, nickname, color);
    }

    private static void ApplyLocalRemove(ulong clientId)
    {
        PlayerColors.RemoveClient(clientId);
    }

    private static FastBufferWriter CreatePlayerWriter(ulong clientId, string nickname, Color color)
    {
        var writer = new FastBufferWriter(96, Allocator.Temp);
        WritePlayer(writer, clientId, nickname, color);
        return writer;
    }

    private static void WritePlayer(FastBufferWriter writer, ulong clientId, string nickname, Color color)
    {
        writer.WriteValueSafe(clientId);
        writer.WriteValueSafe(new FixedString64Bytes(nickname));
        writer.WriteValueSafe(color.r);
        writer.WriteValueSafe(color.g);
        writer.WriteValueSafe(color.b);
    }

    private static void ReadPlayer(FastBufferReader reader, out ulong clientId, out string nickname, out Color color)
    {
        reader.ReadValueSafe(out clientId);
        reader.ReadValueSafe(out FixedString64Bytes nickFixed);
        reader.ReadValueSafe(out float r);
        reader.ReadValueSafe(out float g);
        reader.ReadValueSafe(out float b);
        nickname = nickFixed.ToString();
        color = new Color(r, g, b);
    }
}
