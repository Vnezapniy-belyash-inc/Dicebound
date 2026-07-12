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

    /// <summary>Полный сброс: шатдаун + ожидание завершения.</summary>
    public async System.Threading.Tasks.Task ShutdownAndReset()
    {
        if (_networkManager == null || !_networkManager.IsListening) return;

        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        System.Action<ulong> onDisconnect = null;
        onDisconnect = (id) =>
        {
            _networkManager.OnClientDisconnectCallback -= onDisconnect;
            tcs.TrySetResult(true);
        };

        _networkManager.OnClientDisconnectCallback += onDisconnect;
        _networkManager.Shutdown();

        // Ждём до 3 секунд
        var timeout = System.Threading.Tasks.Task.Delay(3000);
        await System.Threading.Tasks.Task.WhenAny(tcs.Task, timeout);
    }
}
