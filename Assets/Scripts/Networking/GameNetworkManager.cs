using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// Враппер над NetworkManager — хост/клиент/шатдаун.
/// </summary>
public class GameNetworkManager : MonoBehaviour
{
    public static GameNetworkManager Instance { get; private set; }

    private NetworkManager _networkManager;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        _networkManager = GetComponent<NetworkManager>();
    }

    private const string HeartbeatRequest = "SessionHeartbeatRequestV1";
    private const string HeartbeatReply = "SessionHeartbeatReplyV1";
    private CustomMessagingManager _heartbeatMessaging;
    private bool _heartbeatConnected;
    private float _lastHeartbeatReply, _nextHeartbeat;
    private int _heartbeatSequence, _acknowledgedHeartbeat;

    private static bool HeartbeatExpired(float now, float lastReply) => now - lastReply >= 30f;

    private void Update()
    {
        var nm = _networkManager;
        if (nm == null || !nm.IsListening) { _heartbeatConnected = false; return; }
        if (_heartbeatMessaging != nm.CustomMessagingManager)
        {
            UnregisterHeartbeatHandlers();
            _heartbeatMessaging = nm.CustomMessagingManager;
            _heartbeatMessaging?.RegisterNamedMessageHandler(HeartbeatRequest, ReceiveHeartbeatRequest);
            _heartbeatMessaging?.RegisterNamedMessageHandler(HeartbeatReply, ReceiveHeartbeatReply);
        }
        if (nm.IsServer || !nm.IsConnectedClient) { _heartbeatConnected = false; return; }
        float now = Time.unscaledTime;
        if (!_heartbeatConnected)
        {
            _heartbeatConnected = true;
            _acknowledgedHeartbeat = _heartbeatSequence;
            _lastHeartbeatReply = now;
            _nextHeartbeat = now;
        }
        if (HeartbeatExpired(now, _lastHeartbeatReply))
        {
            // Relay can keep a transport alive after the server has removed its peer.
            // Leave ShutdownRequested false so LobbyUI retains the automatic reconnect code.
            _heartbeatConnected = false;
            Debug.LogWarning("[Session] Server heartbeat timed out; reconnecting.");
            nm.Shutdown();
            return;
        }
        if (now < _nextHeartbeat || _heartbeatMessaging == null) return;
        _nextHeartbeat = now + 2f;
        using var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(++_heartbeatSequence);
        _heartbeatMessaging.SendNamedMessage(HeartbeatRequest, NetworkManager.ServerClientId, writer, NetworkDelivery.Unreliable);
    }

    private void ReceiveHeartbeatRequest(ulong sender, FastBufferReader reader)
    {
        if (_networkManager == null || !_networkManager.IsServer
            || !_networkManager.ConnectedClients.ContainsKey(sender) || !reader.TryBeginRead(sizeof(int))) return;
        reader.ReadValueSafe(out int sequence);
        using var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(sequence);
        _heartbeatMessaging.SendNamedMessage(HeartbeatReply, sender, writer, NetworkDelivery.Unreliable);
    }

    private void ReceiveHeartbeatReply(ulong sender, FastBufferReader reader)
    {
        if (_networkManager == null || _networkManager.IsServer || !_networkManager.IsConnectedClient
            || sender != NetworkManager.ServerClientId || !reader.TryBeginRead(sizeof(int))) return;
        reader.ReadValueSafe(out int sequence);
        if (sequence <= _acknowledgedHeartbeat || sequence > _heartbeatSequence) return;
        _acknowledgedHeartbeat = sequence;
        _lastHeartbeatReply = Time.unscaledTime;
    }

    private void UnregisterHeartbeatHandlers()
    {
        _heartbeatMessaging?.UnregisterNamedMessageHandler(HeartbeatRequest);
        _heartbeatMessaging?.UnregisterNamedMessageHandler(HeartbeatReply);
        _heartbeatMessaging = null;
    }

    private void OnDestroy()
    {
        UnregisterHeartbeatHandlers();
        if (Instance == this) Instance = null;
    }

    public bool ShutdownRequested { get; private set; }
    public bool LastStartedAsHost { get; private set; }

    public bool StartHost()
    {
        ConfigureTransport();
        ShutdownRequested = false;
        bool started = _networkManager != null && _networkManager.StartHost();
        if (started) LastStartedAsHost = true;
        return started;
    }

    public bool StartClient()
    {
        ConfigureTransport();
        ShutdownRequested = false;
        bool started = _networkManager != null && _networkManager.StartClient();
        if (started) LastStartedAsHost = false;
        return started;
    }

    public void Shutdown()
    {
        ShutdownRequested = true;
        _networkManager.Shutdown();
    }

    public bool IsHost => _networkManager.IsHost;
    public bool IsClient => _networkManager.IsClient;
    public bool IsConnected => _networkManager.IsConnectedClient || _networkManager.IsHost;
    public ulong LocalClientId => _networkManager.LocalClientId;

    public NetworkManager NetManager => _networkManager;

    /// <summary>Полный сброс: шатдаун + ожидание завершения + сброс транспорта.</summary>
    public async System.Threading.Tasks.Task ShutdownAndReset()
    {
        if (_networkManager == null) return;
        ShutdownRequested = true;

        if (_networkManager.IsListening || _networkManager.ShutdownInProgress)
        {
            if (_networkManager.IsListening) _networkManager.Shutdown();
            for (int tick = 0; tick < 30
                && (_networkManager.IsListening || _networkManager.ShutdownInProgress); tick++)
                await System.Threading.Tasks.Task.Delay(100);
            if (_networkManager.IsListening || _networkManager.ShutdownInProgress)
                throw new System.TimeoutException("NetworkManager did not finish shutting down.");
        }

        ResetTransport();
    }

    private void ResetTransport()
    {
        var transport = _networkManager.GetComponent<UnityTransport>();
        if (transport != null)
            transport.SetConnectionData("127.0.0.1", 7777);
    }
    private void ConfigureTransport()
    {
        var transport = _networkManager != null ? _networkManager.GetComponent<UnityTransport>() : null;
        if (transport == null) return;
        transport.MaxPacketQueueSize = System.Math.Max(transport.MaxPacketQueueSize, 4096);
        NetworkTransferBudget.Reset();
        Application.runInBackground = true;
    }
}
