using Unity.Netcode;
using UnityEngine;

/// <summary>Shared server lease and reliable drag completion for tokens and dice.</summary>
[RequireComponent(typeof(NetworkDragTransform))]
public abstract class NetworkDraggable : NetworkBehaviour
{
    private static int _nextGesture;
    public static int NextGesture() => ++_nextGesture;
    private readonly NetworkDragLease _lease = new();
    private NetworkDragPreview _preview;
    private NetworkDragTransform _dragTransform;
    private Vector3 _serverStartPosition;
    private int _moveSequence;
    protected NetworkDragLease DragLease => _lease;
    protected NetworkDragPreview DragPreview => _preview;
    protected ulong FinishingDragClient { get; private set; }
    protected int FinishingDragGesture { get; private set; }
    public Vector3 DisplayPosition => _preview != null ? _preview.Position : transform.position;
    protected Transform DisplayTransform => _preview != null ? _preview.DisplayTransform : transform;
    public bool IsLocalDragActive(int gesture) =>
        _preview != null && _preview.IsDragging && _preview.Gesture == gesture;
    public bool IsLocalDragActiveAny() => _lease.IsHeld || _preview != null && _preview.IsActive;
    protected bool IsPredictingStep => _preview != null && _preview.IsPredictingStep;
    protected bool PredictStep(int gesture, Vector3 position) =>
        _preview != null && _preview.PredictStep(gesture, position);

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _preview = GetComponent<NetworkDragPreview>();
        if (_preview == null) _preview = gameObject.AddComponent<NetworkDragPreview>();
        _dragTransform = GetComponent<NetworkDragTransform>();
        NetworkManager.OnClientDisconnectCallback += OnDragClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnDragClientDisconnected;
        _lease.Release();
        _preview?.Stop();
        base.OnNetworkDespawn();
    }

    public bool BeginDrag(int gesture)
    {
        if (!IsSpawned || !CanStartLocalDrag || _preview == null || !_preview.Begin(gesture)) return false;
        _moveSequence = 0;
        if (IsServer) BeginOnServer(NetworkManager.LocalClientId, gesture);
        else BeginDragServerRpc(gesture);
        return IsLocalDragActive(gesture);
    }

    public void SetDragPreview(int gesture, Vector3 position) => _preview?.Move(gesture, ConstrainDragPreview(position));

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void BeginDragServerRpc(int gesture, RpcParams rpcParams = default) =>
        BeginOnServer(rpcParams.Receive.SenderClientId, gesture);

    private void BeginOnServer(ulong client, int gesture)
    {
        bool accepted = TryBeginServerDrag(client, gesture);
        BeginDragResultRpc(gesture, accepted, RpcTarget.Single(client, RpcTargetUse.Temp));
    }

    protected bool TryBeginServerDrag(ulong client, int gesture)
    {
        ExpireServerLease();
        if (!CanStartServerDrag || !CanClientStartDrag(client)
            || !_lease.TryBegin(client, gesture, Time.unscaledTimeAsDouble)) return false;
        _serverStartPosition = transform.position;
        OnServerDragStarted();
        return true;
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void BeginDragResultRpc(int gesture, bool accepted, RpcParams rpcParams = default)
    {
        if (_preview == null || _preview.Gesture != gesture) return;
        if (accepted) _preview.Confirm(gesture);
        else _preview.Reject(gesture);
    }

    public void MoveDrag(int gesture, Vector3 position)
    {
        if (!IsSpawned || !IsLocalDragActive(gesture)) return;
        int sequence = ++_moveSequence;
        if (IsServer) MoveOnServer(NetworkManager.LocalClientId, gesture, sequence, position);
        else MoveDragServerRpc(gesture, sequence, position);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone, Delivery = RpcDelivery.Unreliable)]
    private void MoveDragServerRpc(int gesture, int sequence, Vector3 position, RpcParams rpcParams = default) =>
        MoveOnServer(rpcParams.Receive.SenderClientId, gesture, sequence, position);

    private void MoveOnServer(ulong client, int gesture, int sequence, Vector3 position)
    {
        if (!ValidDragVector(position) || !_lease.TryMove(client, gesture, sequence, Time.unscaledTimeAsDouble)) return;
        if (ReplicateDragPreview(client)) transform.position = position;
    }

    public void EndDrag(int gesture, Vector3 position) => FinishDrag(gesture, position, Vector3.zero, Vector3.zero, false);
    public void CancelDrag(int gesture, Vector3 position) => FinishDrag(gesture, position, Vector3.zero, Vector3.zero, true);
    protected void FinishDrag(int gesture, Vector3 position, Vector3 velocity, Vector3 spin, bool cancel)
    {
        if (!IsSpawned || !IsLocalDragActive(gesture)) return;
        _preview.WaitForFinish(gesture);
        if (IsServer) FinishOnServer(NetworkManager.LocalClientId, gesture, position, velocity, spin, cancel);
        else FinishDragServerRpc(gesture, position, velocity, spin, cancel);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void FinishDragServerRpc(int gesture, Vector3 position, Vector3 velocity,
        Vector3 spin, bool cancel, RpcParams rpcParams = default) =>
        FinishOnServer(rpcParams.Receive.SenderClientId, gesture, position, velocity, spin, cancel);

    protected void FinishOnServer(ulong client, int gesture, Vector3 position, Vector3 velocity, Vector3 spin, bool cancel)
    {
        bool expiredOwnGesture = _lease.HasExpired(Time.unscaledTimeAsDouble)
            && _lease.Controller == client && _lease.Gesture == gesture;
        ExpireServerLease();
        if (expiredOwnGesture)
        {
            // Stop an optimistic local throw as well as committing the aborted drag.
            RejectFinishRpc(gesture, RpcTarget.Single(client, RpcTargetUse.Temp));
            return;
        }
        if (!_lease.CanControl(client, gesture, Time.unscaledTimeAsDouble)
            || !ValidDragVector(position) || !ValidDragVector(velocity) || !ValidDragVector(spin))
        {
            RejectFinishRpc(gesture, RpcTarget.Single(client, RpcTargetUse.Temp));
            return;
        }
        FinishingDragClient = client;
        FinishingDragGesture = gesture;
        _lease.Release();
        // Cancellation uses the server's starting position, not a potentially stale client snapshot.
        transform.position = cancel ? _serverStartPosition : position;
        OnServerDragFinished(velocity, spin, cancel);
        CommitAndNotify(client, gesture);
    }

    [Rpc(SendTo.SpecifiedInParams)]
    protected void RejectFinishRpc(int gesture, RpcParams rpcParams = default)
    {
        _preview?.Reject(gesture);
    }

    private void CommitAndNotify(ulong client, int gesture)
    {
        _dragTransform.Teleport(transform.position, transform.rotation, transform.localScale);
        int tick = NetworkManager.NetworkTickSystem.ServerTime.Tick;
        if (NetworkManager.ConnectedClients.ContainsKey(client))
            FinishDragResultRpc(gesture, tick, transform.position, transform.rotation,
                RpcTarget.Single(client, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void FinishDragResultRpc(int gesture, int tick, Vector3 position,
        Quaternion rotation, RpcParams rpcParams = default) =>
        _preview?.Complete(gesture, tick, position, rotation, IsServer);

    protected void UpdateDragState()
    {
        if (IsServer) ExpireServerLease();
        if (_preview == null || !_preview.HasTimedOut) return;
        int gesture = _preview.Gesture;
        // Also release a request whose acknowledgement did not arrive locally.
        if (IsSpawned && !_preview.IsPredictingStep)
        {
            if (IsServer) FinishOnServer(NetworkManager.LocalClientId, gesture, DisplayPosition, Vector3.zero, Vector3.zero, true);
            else FinishDragServerRpc(gesture, DisplayPosition, Vector3.zero, Vector3.zero, true);
        }
        _preview.Stop();
    }

    private void ExpireServerLease()
    {
        if (_lease.HasExpired(Time.unscaledTimeAsDouble)) AbortServerDrag();
    }

    private void OnDragClientDisconnected(ulong client)
    {
        if (IsServer && _lease.IsHeld && _lease.Controller == client) AbortServerDrag();
        if (IsServer) _lease.ForgetClient(client);
        if (client == NetworkManager.LocalClientId) _preview?.Stop();
    }

    protected void AbortServerDrag()
    {
        if (!_lease.IsHeld) return;
        ulong client = _lease.Controller;
        int gesture = _lease.Gesture;
        _lease.Release();
        OnServerDragAborted();
        CommitAndNotify(client, gesture);
    }

    protected virtual void OnServerDragStarted() { }
    // Token intentions must not move authoritative vision or reveal objects before release.
    protected virtual bool ReplicateDragPreview(ulong client) => true;
    protected virtual Vector3 ConstrainDragPreview(Vector3 position) => position;
    protected virtual bool CanStartServerDrag => true;
    protected virtual bool CanStartLocalDrag => true;
    protected virtual bool CanClientStartDrag(ulong client) => true;
    protected abstract void OnServerDragFinished(Vector3 velocity, Vector3 spin, bool cancel);
    protected abstract void OnServerDragAborted();
    protected static bool ValidDragVector(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
        && Mathf.Abs(value.x) < 10000f && Mathf.Abs(value.y) < 10000f && Mathf.Abs(value.z) < 10000f;
}
