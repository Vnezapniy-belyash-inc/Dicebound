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
