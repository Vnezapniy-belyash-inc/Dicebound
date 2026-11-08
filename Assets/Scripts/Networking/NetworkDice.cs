using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Сетевой кубик. Сервер выдаёт право броска, бросающий считает его физику.
/// Тип: InitClientRpc + NetworkVariable для late-join.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class NetworkDice : NetworkDraggable, IDice
{
    private const float MaxLaunchSpeed = 30f;
    private const float MaxLaunchSpin = 20f;
    private const float ThrowSendInterval = 1f / 30f;
    private const float ThrowPlaybackDelay = .1f;
    private const float ThrowPacketTimeout = 3f;
    private const float MaxThrowDuration = 30f;

    private readonly struct ThrowPose
    {
        public readonly float Time;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly bool Final;

        public ThrowPose(float time, Vector3 position, Quaternion rotation, bool final)
        {
            Time = time;
            Position = position;
            Rotation = rotation;
            Final = final;
        }
    }
    // IDice
    public Rigidbody Rigidbody => _rb;
    public DieType DieType { get; private set; }
    public int Result { get; private set; } = -1;
    public bool IsRolling { get; set; }
    public bool HasResult { get; private set; }

    public event Action<NetworkDice> OnResultReady;

    [Header("Физика")]
    public float rollForce = 8f;
    public float maxTorque = 12f;
    public float stopThreshold = 0.15f;
    public float settleTime = 0.6f;

    [Header("Отображение")]
    public float textOffset = 0.02f;
    public int fontSize = 48;
    public Color textColor = Color.black;

    // Late-join: -1 = mesh not yet assigned
    private readonly NetworkVariable<int> _netDieType = new(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Late-join: цвет синхронизируется автоматически
    private readonly NetworkVariable<Vector3> _netDiceColor = new(
        new Vector3(0.5f, 0.5f, 0.5f), NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<ulong> _netSpawnerClientId = new(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public ulong SpawnerClientId => _netSpawnerClientId.Value;

    public bool IsSpawner =>
        NetworkManager.Singleton != null
        && SpawnerClientId != ulong.MaxValue
        && NetworkManager.Singleton.LocalClientId == SpawnerClientId;

    private Rigidbody _rb;
    private DieFaceData[] _faces;
    private MeshRenderer[] _faceRenderers;
    private Camera _cam;
    private float _settleTimer;
    private bool _didInit;
    public bool IsReady => IsSpawned && _didInit;
    private ulong _lastThrower;
    private Rigidbody _localThrowBody;
    private int _localThrowGesture, _remoteThrowGesture;
    private float _localThrowStart, _nextThrowSend;
    private ulong _remoteThrowClient = NetworkDragLease.NoController;
    private float _remoteThrowStart, _lastThrowPacket, _playbackStart, _latestPoseTime;
    private bool _receivedFinalPose;
    private ThrowPose _playedPose;
    private readonly Queue<ThrowPose> _throwPoses = new();
    private bool HasRemoteThrow => _remoteThrowClient != NetworkDragLease.NoController;

    public void StartRoll()
    {
        IsRolling = true;
        HasResult = false;
        Result = -1;
        _settleTimer = 0f;
    }

    /// <summary>Вызывается на сервере при спавне.</summary>
    public void Init(DieType type)
    {
        if (!IsServer) return;
        _netDieType.Value = (int)type;

        if (_netSpawnerClientId.Value == ulong.MaxValue)
            _netSpawnerClientId.Value = OwnerClientId;

        ServerRefreshPlayerColor();
        InitializeMesh(type);
        InitClientRpc((int)type);
    }

    /// <summary>Server: update spawner after reconnect ownership transfer.</summary>
    public void ServerUpdateSpawnerClientId(ulong clientId)
    {
        if (!IsServer) return;
        _netSpawnerClientId.Value = clientId;
    }

    /// <summary>Server: sync baked dice color from player registry to all clients.</summary>
    public void ServerRefreshPlayerColor()
    {
        if (!IsServer) return;

        ulong colorOwner = _netSpawnerClientId.Value != ulong.MaxValue
            ? _netSpawnerClientId.Value
            : OwnerClientId;
        Color c = PlayerRegistry.GetServerPlayerColor(colorOwner);
        var rgb = new Vector3(c.r, c.g, c.b);
        _netDiceColor.Value = rgb;
        ApplyDiceColor(rgb);
        RefreshColorClientRpc(rgb);
    }

    [Rpc(SendTo.Everyone)]
    private void RefreshColorClientRpc(Vector3 rgb)
    {
        ApplyDiceColor(rgb);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _rb = GetComponent<Rigidbody>();
        UpdatePhysicsAuthority();

        ApplyDiceColor(_netDiceColor.Value);
        _netDiceColor.OnValueChanged += (old, val) => ApplyDiceColor(val);

        if (IsServer && _netSpawnerClientId.Value == ulong.MaxValue)
            _netSpawnerClientId.Value = OwnerClientId;

        TryInitializeMeshFromNetworkType(_netDieType.Value);
        _netDieType.OnValueChanged += (oldVal, newVal) => TryInitializeMeshFromNetworkType(newVal);

        StartCoroutine(DeferredMeshInit());
    }

    public override void OnNetworkDespawn()
    {
        _localThrowGesture = 0;
        _localThrowBody = null;
        _remoteThrowClient = NetworkDragLease.NoController;
        _throwPoses.Clear();
        IsRolling = false;
        base.OnNetworkDespawn();
    }

    private System.Collections.IEnumerator DeferredMeshInit()
    {
        for (int i = 0; i < 60 && !_didInit; i++)
        {
            TryInitializeMeshFromNetworkType(_netDieType.Value);
            if (_didInit) yield break;
            yield return null;
        }
    }

    private void TryInitializeMeshFromNetworkType(int typeInt)
    {
        if (_didInit || typeInt < 0) return;
        InitializeMesh((DieType)typeInt);
    }

    private void UpdatePhysicsAuthority()
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_rb != null) _rb.isKinematic = !IsServer;
    }

    private void ApplyDiceColor(Vector3 rgb)
    {
        var renderer = GetComponent<MeshRenderer>();
        if (renderer == null) return;

        var color = new Color(rgb.x, rgb.y, rgb.z);
        renderer.material.SetColor("_BaseColor", color);

        var highlight = GetComponent<DiceHighlight>();
        if (highlight != null)
            highlight.RefreshBaseColor(color);
    }

    [Rpc(SendTo.NotServer)]
    private void InitClientRpc(int typeInt)
    {
        TryInitializeMeshFromNetworkType(typeInt);
    }

    /// <summary>Server: ensure reconnecting/late-join client builds mesh.</summary>
    public void ServerPushMeshToClient(ulong clientId)
    {
        if (!IsServer) return;
        int typeInt = _netDieType.Value;
        if (typeInt < 0) return;

        var rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };
        PushMeshClientRpc(typeInt, rpcParams);
    }

    [ClientRpc]
    private void PushMeshClientRpc(int typeInt, ClientRpcParams clientRpcParams = default)
    {
        TryInitializeMeshFromNetworkType(typeInt);
    }

    private void InitializeMesh(DieType type)
    {
        if (_didInit) return;
        DieType = type;

        var (mesh, faces) = DieMeshGenerator.Generate(type);
        _faces = faces;
        GetComponent<MeshFilter>().mesh = mesh;

        var renderer = GetComponent<MeshRenderer>();
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit != null)
        {
            Material mat = new Material(urpLit);
            mat.SetColor("_BaseColor", new Color(0.5f, 0.5f, 0.5f)); // серый
            mat.SetFloat("_Smoothness", 0f);
            mat.SetInt("_Cull", 2);
            renderer.material = mat;

            // Восстанавливаем цвет из NetworkVariable (если уже установлен)
            Vector3 savedColor = _netDiceColor.Value;
            if (savedColor != new Vector3(0.5f, 0.5f, 0.5f))
                mat.SetColor("_BaseColor", new Color(savedColor.x, savedColor.y, savedColor.z));
        }

        MeshCollider mc = GetComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = true;

        _rb.useGravity = true;
        _rb.mass = 0.3f;
        _rb.angularDamping = 0.3f;
        _rb.linearDamping = 0.2f;

        CreateFaceLabels();
        gameObject.AddComponent<DiceHighlight>();
        _cam = Camera.main;
        _didInit = true;

        ApplyDiceColor(_netDiceColor.Value);
    }

    private void CreateFaceLabels()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name.StartsWith("FaceLabel"))
                Destroy(child.gameObject);
        }

        _faceRenderers = new MeshRenderer[_faces.Length];
        for (int i = 0; i < _faces.Length; i++)
        {
            var fd = _faces[i];
            string label = FormatFaceValue(DieType, fd.value);
            GameObject labelObj = new GameObject($"FaceLabel{i}");
            labelObj.transform.SetParent(transform, false);
            labelObj.transform.localPosition = fd.center + fd.normal * textOffset;
            labelObj.transform.localRotation = Quaternion.LookRotation(-fd.normal);
            TextMesh tm = labelObj.AddComponent<TextMesh>();
            tm.text = label;
            tm.fontSize = fontSize;
            tm.color = textColor;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.04f;

            MeshRenderer mr = labelObj.GetComponent<MeshRenderer>();
            _faceRenderers[i] = mr;
            DiceLabelSetup.ApplyLabelMaterial(mr, textColor);
        }
    }

    protected override bool CanStartLocalDrag => _localThrowBody == null;
    protected override bool CanStartServerDrag => _didInit && _rb != null && !HasRemoteThrow;

    public void ThrowDrag(int gesture, Vector3 position, Vector3 velocity, Vector3 spin)
    {
        if (!IsSpawned || !IsLocalDragActive(gesture) || !ValidDragVector(position)
            || !ValidDragVector(velocity) || !ValidDragVector(spin)) return;
        velocity = Vector3.ClampMagnitude(velocity, MaxLaunchSpeed);
        spin = Vector3.ClampMagnitude(spin, MaxLaunchSpin);
        FinishDrag(gesture, position, velocity, spin, cancel: false);
        if (IsServer) return;

        _localThrowBody = DragPreview.BeginPhysicalThrow(position, _rb, GetComponent<MeshCollider>());
        _localThrowBody.linearVelocity = velocity;
        _localThrowBody.angularVelocity = spin;
        _localThrowGesture = gesture;
        _localThrowStart = _nextThrowSend = Time.unscaledTime;
        StartRoll();
    }

    protected override void OnServerDragStarted()
    {
        IsRolling = false;
        _rb.interpolation = RigidbodyInterpolation.None;
        if (!_rb.isKinematic)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
        _rb.isKinematic = true;
    }

    protected override void OnServerDragFinished(Vector3 velocity, Vector3 spin, bool cancel)
    {
        _rb.isKinematic = false;
        if (cancel)
        {
            IsRolling = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            return;
        }
        _lastThrower = FinishingDragClient;
        StartRoll();
        if (FinishingDragClient != NetworkManager.LocalClientId)
        {
            _rb.isKinematic = true;
            _remoteThrowClient = FinishingDragClient;
            _remoteThrowGesture = FinishingDragGesture;
            _remoteThrowStart = _lastThrowPacket = Time.unscaledTime;
            _latestPoseTime = -1f;
            _receivedFinalPose = false;
            _throwPoses.Clear();
            _playedPose = new ThrowPose(0f, transform.position, transform.rotation, false);
            return;
        }
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.linearVelocity = Vector3.ClampMagnitude(velocity, MaxLaunchSpeed);
        _rb.angularVelocity = Vector3.ClampMagnitude(spin, MaxLaunchSpin);
    }

    protected override void OnServerDragAborted()
    {
        _rb.isKinematic = false;
        IsRolling = false;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
    }

    public void Roll()
    {
        if (!IsSpawned) return;
        if (!IsServer) { RollServerRpc(); return; }
        RollOnServer(NetworkManager.LocalClientId);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RollServerRpc(RpcParams rpcParams = default) =>
        RollOnServer(rpcParams.Receive.SenderClientId);

    private void RollOnServer(ulong clientId)
    {
        if (!_didInit || HasRemoteThrow) return;
        UpdateDragState();
        if (DragLease.IsHeld && DragLease.Controller != clientId) return;
        if (DragLease.IsHeld) AbortServerDrag();
        _lastThrower = clientId;

        _rb.isKinematic = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        IsRolling = true;
        HasResult = false;
        Result = -1;
        _settleTimer = 0f;

        Vector3 forceDir = (Vector3.up * 0.8f + UnityEngine.Random.insideUnitSphere * 0.6f).normalized;
        float force = rollForce * UnityEngine.Random.Range(0.7f, 1.3f);
        _rb.AddForce(forceDir * force, ForceMode.Impulse);

        Vector3 torque = UnityEngine.Random.insideUnitSphere * maxTorque;
        _rb.AddTorque(torque, ForceMode.Impulse);
    }

    private void Update()
    {
        UpdateDragState();

        if (!IsSpawned || !_didInit) return;
        if (!IsServer)
        {
            UpdateLocalThrow();
            return;
        }
        if (HasRemoteThrow)
        {
            UpdateRemoteThrow();
            return;
        }
        if (!IsRolling || HasResult) return;

        if (IsSettled(_rb)) DetermineResult();
    }

    private bool IsSettled(Rigidbody body)
    {
        bool stopped = body.linearVelocity.sqrMagnitude < stopThreshold * stopThreshold
            && body.angularVelocity.sqrMagnitude < stopThreshold * stopThreshold;
        _settleTimer = stopped ? _settleTimer + Time.deltaTime : 0f;
        return _settleTimer >= settleTime;
    }

    private void UpdateLocalThrow()
    {
        if (_localThrowGesture == 0) return;
        if (_localThrowBody == null || !DragPreview.IsActive || DragPreview.Gesture != _localThrowGesture)
        {
            _localThrowGesture = 0;
            _localThrowBody = null;
            IsRolling = false;
            return;
        }

        float now = Time.unscaledTime;
        float elapsed = now - _localThrowStart;
        if (elapsed > MaxThrowDuration + ThrowPacketTimeout)
        {
            DragPreview.Stop();
            _localThrowGesture = 0;
            _localThrowBody = null;
            IsRolling = false;
            return;
        }
        if (!IsRolling) return;

        if (IsSettled(_localThrowBody) || elapsed >= MaxThrowDuration)
        {
            Vector3 position = _localThrowBody.position;
            Quaternion rotation = _localThrowBody.rotation;
            _localThrowBody.isKinematic = true;
            _localThrowBody.interpolation = RigidbodyInterpolation.None;
            _localThrowBody.transform.SetPositionAndRotation(position, rotation);
            IsRolling = false;
            FinishPhysicalThrowRpc(_localThrowGesture, elapsed, position, rotation);
        }
        else if (now >= _nextThrowSend)
        {
            _nextThrowSend = Mathf.Max(now, _nextThrowSend + ThrowSendInterval);
            PhysicalThrowPoseRpc(_localThrowGesture, elapsed, _localThrowBody.transform.position,
                _localThrowBody.transform.rotation);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone, Delivery = RpcDelivery.Unreliable)]
    private void PhysicalThrowPoseRpc(int gesture, float elapsed, Vector3 position, Quaternion rotation,
        RpcParams rpcParams = default) =>
        ReceiveThrowPose(rpcParams.Receive.SenderClientId, gesture, new ThrowPose(elapsed, position, rotation, false));

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void FinishPhysicalThrowRpc(int gesture, float elapsed, Vector3 position, Quaternion rotation,
        RpcParams rpcParams = default) =>
        ReceiveThrowPose(rpcParams.Receive.SenderClientId, gesture, new ThrowPose(elapsed, position, rotation, true));

    private void ReceiveThrowPose(ulong sender, int gesture, ThrowPose pose)
    {
        float now = Time.unscaledTime;
        if (!HasRemoteThrow || sender != _remoteThrowClient || gesture != _remoteThrowGesture || _receivedFinalPose
            || !float.IsFinite(pose.Time) || pose.Time < 0f || pose.Time <= _latestPoseTime
            || pose.Time > now - _remoteThrowStart + ThrowPacketTimeout
            || !ValidDragVector(pose.Position)) return;
        Quaternion rotation = pose.Rotation;
        if (!(Mathf.Abs(Quaternion.Dot(rotation, rotation) - 1f) < .01f)) return;
        if (!pose.Final && (_throwPoses.Count >= 32 || pose.Time - _latestPoseTime < ThrowSendInterval * .5f)) return;

        if (_latestPoseTime < 0f) _playbackStart = now - pose.Time + ThrowPlaybackDelay;
        _lastThrowPacket = now;
        _latestPoseTime = pose.Time;
        _receivedFinalPose = pose.Final;
        _throwPoses.Enqueue(pose);
    }

    private void UpdateRemoteThrow()
    {
        float now = Time.unscaledTime;
        if (!_receivedFinalPose && (!NetworkManager.ConnectedClients.ContainsKey(_remoteThrowClient)
            || now - _lastThrowPacket > ThrowPacketTimeout
            || now - _remoteThrowStart > MaxThrowDuration + ThrowPacketTimeout))
        {
            ulong client = _remoteThrowClient;
            int gesture = _remoteThrowGesture;
            _remoteThrowClient = NetworkDragLease.NoController;
            _throwPoses.Clear();
            _rb.isKinematic = false;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            if (NetworkManager.ConnectedClients.ContainsKey(client))
                RejectFinishRpc(gesture, RpcTarget.Single(client, RpcTargetUse.Temp));
            return;
        }

        if (_latestPoseTime < 0f) return;
        float time = now - _playbackStart;
        while (_throwPoses.Count > 0 && _throwPoses.Peek().Time <= time)
            _playedPose = _throwPoses.Dequeue();

        if (_throwPoses.TryPeek(out ThrowPose next))
        {
            float blend = Mathf.InverseLerp(_playedPose.Time, next.Time, time);
            transform.SetPositionAndRotation(Vector3.Lerp(_playedPose.Position, next.Position, blend),
                Quaternion.Slerp(_playedPose.Rotation, next.Rotation, blend));
            return;
        }

        transform.SetPositionAndRotation(_playedPose.Position, _playedPose.Rotation);
        if (!_playedPose.Final) return;

        ulong thrower = _remoteThrowClient;
        _remoteThrowClient = NetworkDragLease.NoController;
        var networkTransform = GetComponent<NetworkDragTransform>();
        networkTransform.Teleport(transform.position, transform.rotation, transform.localScale);
        if (NetworkManager.ConnectedClients.ContainsKey(thrower))
            CompletePhysicalThrowRpc(_remoteThrowGesture, NetworkManager.NetworkTickSystem.ServerTime.Tick,
                transform.position, RpcTarget.Single(thrower, RpcTargetUse.Temp));
        DetermineResult();
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void CompletePhysicalThrowRpc(int gesture, int tick, Vector3 position, RpcParams rpcParams = default) =>
        DragPreview.CompletePhysicalThrow(gesture, tick, position);

    private void LateUpdate() =>
        DiceLabelSetup.UpdateBackFaceVisibility(_faceRenderers, _faces, DisplayTransform, _cam);

    private void DetermineResult()
    {
        if (_faces == null || _faces.Length == 0) return;

        int bestIndex = 0;

        if (DieType == DieType.d4)
        {
            // d4: результат на грани, лежащей на столе (нормаль вниз)
            float lowestDot = float.MaxValue;
            for (int i = 0; i < _faces.Length; i++)
            {
                Vector3 worldNormal = transform.TransformDirection(_faces[i].normal);
                float dot = Vector3.Dot(worldNormal, Vector3.up);
                if (dot < lowestDot) { lowestDot = dot; bestIndex = i; }
            }
        }
        else
        {
            float bestDot = float.MinValue;
            for (int i = 0; i < _faces.Length; i++)
            {
                Vector3 worldNormal = transform.TransformDirection(_faces[i].normal);
                float dot = Vector3.Dot(worldNormal, Vector3.up);
                if (dot > bestDot) { bestDot = dot; bestIndex = i; }
            }
        }

        Result = _faces[bestIndex].value;
        HasResult = true;
        IsRolling = false;

        string nickname = PlayerColors.GetNickname(_lastThrower);
        BroadcastResultClientRpc(DieType.ToString(), Result, _lastThrower, nickname);
        OnResultReady?.Invoke(this);
    }

    [Rpc(SendTo.Everyone)]
    private void BroadcastResultClientRpc(string dieType, int result, ulong throwerId, string ownerNickname)
    {
        Debug.Log($"[Dice] Player {throwerId} ({ownerNickname}) rolled {dieType}: {result}");
        DiceUI.Instance?.ShowResult(dieType, result, throwerId, ownerNickname);
    }

    private static string FormatFaceValue(DieType type, int value)
    {
        return type switch
        {
            DieType.d100 when value == 0 => "00",
            DieType.d100 => value.ToString(),
            _ => value.ToString(),
        };
    }
}
