using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server: staggers late-join / reconnect sync to avoid transport queue overflow.
/// Client signals when scene/objects are ready; then server pushes state.
/// </summary>
public class LateJoinSync : MonoBehaviour
{
    private const string MSG_CLIENT_READY = "LateJoinClientReady";

    public static LateJoinSync Instance { get; private set; }

    [Tooltip("Short pause before pushing state after client ready")]
    public float initialDelay = 0.35f;

    private readonly Dictionary<ulong, Coroutine> _syncRoutines = new();
    private readonly HashSet<ulong> _initialSyncStarted = new();
    private bool _handlerRegistered;

    public static void EnsureInstance()
    {
        if (Instance != null) return;
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        if (nm.GetComponent<LateJoinSync>() == null)
            nm.gameObject.AddComponent<LateJoinSync>();
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
        RegisterServerHandler();
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted += RegisterServerHandler;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted -= RegisterServerHandler;

        UnregisterServerHandler();
        if (Instance == this)
            Instance = null;
    }

    private void RegisterServerHandler()
    {
        if (_handlerRegistered) return;
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm == null || !NetworkManager.Singleton.IsServer) return;

        cmm.RegisterNamedMessageHandler(MSG_CLIENT_READY, OnClientReady);
        _handlerRegistered = true;
    }

    private void UnregisterServerHandler()
    {
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm != null && _handlerRegistered)
            cmm.UnregisterNamedMessageHandler(MSG_CLIENT_READY);

        _handlerRegistered = false;
        _initialSyncStarted.Clear();
    }

    public void OnClientLeft(ulong clientId)
    {
        _initialSyncStarted.Remove(clientId);
        if (_syncRoutines.TryGetValue(clientId, out Coroutine running))
            StopCoroutine(running);
        _syncRoutines.Remove(clientId);
    }

    private void OnClientReady(ulong clientId, FastBufferReader reader)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (_initialSyncStarted.Contains(clientId))
            StartCoroutine(PortraitsAndDiceOnlyRoutine(clientId));
        else
        {
            _initialSyncStarted.Add(clientId);
            BeginForClient(clientId);
        }
    }

    /// <summary>Client: tell server we are ready for portrait/dice catch-up sync.</summary>
    public static void NotifyServerReady()
    {
        var nm = NetworkManager.Singleton;
        var cmm = nm?.CustomMessagingManager;
        if (nm == null || cmm == null || nm.IsServer) return;

        using var writer = new FastBufferWriter(0, Allocator.Temp);
        cmm.SendNamedMessage(MSG_CLIENT_READY, NetworkManager.ServerClientId, writer);
        Debug.Log("[LateJoin] Client ready signal sent");
    }

    /// <summary>
    /// Waits for NGO connection and (if applicable) map download before requesting server sync.
    /// </summary>
    public static IEnumerator SendReadyAfterSceneLoad()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.IsServer) yield break;

        for (int i = 0; i < 80; i++)
        {
            if (nm.IsConnectedClient && nm.CustomMessagingManager != null)
                break;
            yield return new WaitForSeconds(0.1f);
        }

        CellMarker.EnsureRegistered();
        TokenImageSync.EnsureInstance();

        // Give NGO time to spawn scene NetworkObjects (tokens, dice, etc.)
        yield return new WaitForSeconds(1.0f);

        // If a heavy map is downloading, wait for it to finish reassembling.
        // MapSync.Reassemble() also sends a ready signal; this path is the fallback.
        float waited = 0f;
        while (MapSync.Instance != null && MapSync.Instance.IsReceivingMap && waited < 90f)
        {
            yield return new WaitForSeconds(0.25f);
            waited += 0.25f;
        }

        if (MapSync.Instance == null || !MapSync.Instance.IsReceivingMap)
            NotifyServerReady();
    }

    public void BeginForClient(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        if (clientId == nm.LocalClientId) return;

        if (_syncRoutines.TryGetValue(clientId, out Coroutine running))
            StopCoroutine(running);

        _syncRoutines[clientId] = StartCoroutine(SyncClientRoutine(clientId));
    }

    private IEnumerator SyncClientRoutine(ulong clientId)
    {
        Debug.Log($"[LateJoin] Sync started for client {clientId}");
        yield return new WaitForSeconds(initialDelay);

        PlayerRegistry.Instance?.SendFullStateToClient(clientId);
        yield return new WaitForSeconds(0.25f);

        if (MapSync.Instance != null)
            yield return MapSync.Instance.SendMapToClientRoutine(clientId);

        // Wait for client to finish receiving the map before pushing portraits.
        yield return new WaitForSeconds(0.5f);

        if (TokenImageSync.Instance != null)
            yield return TokenImageSync.Instance.SendAllPortraitsToClientRoutine(clientId);
        yield return new WaitForSeconds(0.25f);

        yield return NetworkDiceLateSync.PushAllToClientRoutine(clientId);

        _syncRoutines.Remove(clientId);
        Debug.Log($"[LateJoin] Sync complete for client {clientId}");
    }

    private IEnumerator PortraitsAndDiceOnlyRoutine(ulong clientId)
    {
        Debug.Log($"[LateJoin] Portrait/dice re-sync for client {clientId}");
        yield return new WaitForSeconds(0.25f);

        if (TokenImageSync.Instance != null)
            yield return TokenImageSync.Instance.SendAllPortraitsToClientRoutine(clientId);
        yield return new WaitForSeconds(0.25f);

        yield return NetworkDiceLateSync.PushAllToClientRoutine(clientId);

        Debug.Log($"[LateJoin] Portrait/dice re-sync complete for client {clientId}");
    }
}
