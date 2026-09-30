using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Wires NGO connect/disconnect events to PlayerRegistry and late-join sync.
/// </summary>
public class NetworkPlayerManager : MonoBehaviour
{
    public static NetworkPlayerManager Instance { get; private set; }

    private PlayerRegistry _registry;
    private bool _subscribed;
    private bool _handledLocalConnection;
    private ulong _handledLocalClientId;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _registry = GetComponent<PlayerRegistry>();
        if (_registry == null)
            _registry = gameObject.AddComponent<PlayerRegistry>();

        LateJoinSync.EnsureInstance();
        TokenImageSync.EnsureInstance();
        HostSceneCurtain.EnsureInstance();

        TrySubscribe();
    }

    private void Start()
    {
        TrySubscribe();
    }

    private void Update()
    {
        TrySubscribe();
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsConnectedClient)
        {
            _handledLocalConnection = false;
            return;
        }
        if (!_handledLocalConnection || _handledLocalClientId != nm.LocalClientId)
            OnClientConnected(nm.LocalClientId);
    }

    private void TrySubscribe()
    {
        if (_subscribed || NetworkManager.Singleton == null) return;

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        _subscribed = true;
    }

    private void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && clientId == nm.LocalClientId)
        {
            if (_handledLocalConnection && _handledLocalClientId == clientId) return;
            _handledLocalConnection = true;
            _handledLocalClientId = clientId;
        }
        _registry?.OnLocalClientConnected(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (_handledLocalConnection && clientId == _handledLocalClientId)
            _handledLocalConnection = false;
        _registry?.OnClientDisconnected(clientId);
        LateJoinSync.Instance?.OnClientLeft(clientId);
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null && _subscribed)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        if (Instance == this)
            Instance = null;
    }
}
