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

    public void StartHost() => _networkManager.StartHost();
    public void StartClient() => _networkManager.StartClient();
    public void Shutdown() => _networkManager.Shutdown();

    public bool IsHost => _networkManager.IsHost;
    public bool IsClient => _networkManager.IsClient;
    public bool IsConnected => _networkManager.IsConnectedClient || _networkManager.IsHost;
    public ulong LocalClientId => _networkManager.LocalClientId;

    public NetworkManager NetManager => _networkManager;

    /// <summary>Полный сброс: шатдаун + ожидание завершения + сброс транспорта.</summary>
    public async System.Threading.Tasks.Task ShutdownAndReset()
    {
        if (_networkManager == null) return;

        if (_networkManager.IsListening)
        {
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
            System.Action<ulong> onDisconnect = null;
            onDisconnect = (id) =>
            {
                _networkManager.OnClientDisconnectCallback -= onDisconnect;
                tcs.TrySetResult(true);
            };

            _networkManager.OnClientDisconnectCallback += onDisconnect;
            _networkManager.Shutdown();

            var timeout = System.Threading.Tasks.Task.Delay(3000);
            await System.Threading.Tasks.Task.WhenAny(tcs.Task, timeout);
        }

        ResetTransport();
    }

    private void ResetTransport()
    {
        var transport = _networkManager.GetComponent<UnityTransport>();
        if (transport != null)
            transport.SetConnectionData("127.0.0.1", 7777);
    }
}
