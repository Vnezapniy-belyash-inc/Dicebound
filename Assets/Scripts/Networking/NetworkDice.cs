using System;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Сетевой кубик. Создатель отвечает за удаление, сервер — за перетаскивание и физику.
/// Тип: InitClientRpc + NetworkVariable для late-join.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class NetworkDice : NetworkBehaviour, IDice
{
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
    private ulong _dragController = ulong.MaxValue;
    private int _dragGesture;
    private float _dragLeaseUntil;
    private ulong _lastThrower;

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

    public void BeginDrag(int gesture)
    {
        if (!IsSpawned) return;
        if (IsServer) BeginDragOnServer(NetworkManager.LocalClientId, gesture);
        else BeginDragServerRpc(gesture);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void BeginDragServerRpc(int gesture, RpcParams rpcParams = default) =>
        BeginDragOnServer(rpcParams.Receive.SenderClientId, gesture);

    private void BeginDragOnServer(ulong clientId, int gesture)
    {
        if (_dragController != ulong.MaxValue && _dragController != clientId
            && Time.unscaledTime < _dragLeaseUntil) return;
        _dragController = clientId;
        _dragGesture = gesture;
        _dragLeaseUntil = Time.unscaledTime + 2f;
        IsRolling = false;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.isKinematic = true;
    }

    public void MoveDrag(int gesture, Vector3 position)
    {
        if (!IsSpawned) return;
        if (IsServer) MoveDragOnServer(NetworkManager.LocalClientId, gesture, position);
        else MoveDragServerRpc(gesture, position);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone,
        Delivery = RpcDelivery.Unreliable)]
    private void MoveDragServerRpc(int gesture, Vector3 position, RpcParams rpcParams = default) =>
        MoveDragOnServer(rpcParams.Receive.SenderClientId, gesture, position);

    private void MoveDragOnServer(ulong clientId, int gesture, Vector3 position)
    {
        if (!CanControlDrag(clientId, gesture) || !ValidDragPosition(position)) return;
        _dragLeaseUntil = Time.unscaledTime + 2f;
        transform.position = position;
    }

    public void ThrowDrag(int gesture, Vector3 position, Vector3 velocity, Vector3 spin)
    {
        if (!IsSpawned) return;
        if (IsServer) ThrowDragOnServer(NetworkManager.LocalClientId, gesture, position, velocity, spin);
        else ThrowDragServerRpc(gesture, position, velocity, spin);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void ThrowDragServerRpc(int gesture, Vector3 position, Vector3 velocity,
        Vector3 spin, RpcParams rpcParams = default) =>
        ThrowDragOnServer(rpcParams.Receive.SenderClientId, gesture, position, velocity, spin);

    private void ThrowDragOnServer(ulong clientId, int gesture, Vector3 position,
        Vector3 velocity, Vector3 spin)
    {
        if (!CanControlDrag(clientId, gesture) || !ValidDragPosition(position)
            || !ValidDragPosition(velocity) || !ValidDragPosition(spin)) return;
        _dragController = ulong.MaxValue;
        _lastThrower = clientId;
        transform.position = position;
        StartRoll();
        _rb.isKinematic = false;
        _rb.linearVelocity = Vector3.ClampMagnitude(velocity, 30f);
        _rb.angularVelocity = Vector3.ClampMagnitude(spin, 20f);
    }

    public void CancelDrag(int gesture, Vector3 position)
    {
        if (!IsSpawned) return;
        if (IsServer) CancelDragOnServer(NetworkManager.LocalClientId, gesture, position);
        else CancelDragServerRpc(gesture, position);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void CancelDragServerRpc(int gesture, Vector3 position, RpcParams rpcParams = default) =>
        CancelDragOnServer(rpcParams.Receive.SenderClientId, gesture, position);

    private void CancelDragOnServer(ulong clientId, int gesture, Vector3 position)
    {
        if (!CanControlDrag(clientId, gesture) || !ValidDragPosition(position)) return;
        _dragController = ulong.MaxValue;
        transform.position = position;
        IsRolling = false;
        _rb.isKinematic = false;
    }

    private bool CanControlDrag(ulong clientId, int gesture) =>
        IsServer && _dragController == clientId && _dragGesture == gesture
        && Time.unscaledTime <= _dragLeaseUntil;

    private static bool ValidDragPosition(Vector3 position) =>
        !float.IsNaN(position.x) && !float.IsNaN(position.y) && !float.IsNaN(position.z)
        && Mathf.Abs(position.x) < 10000f && Mathf.Abs(position.y) < 10000f
        && Mathf.Abs(position.z) < 10000f;

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
        if (!_didInit) return;
        if (_dragController != ulong.MaxValue && _dragController != clientId
            && Time.unscaledTime < _dragLeaseUntil) return;
        _dragController = ulong.MaxValue;
        _lastThrower = clientId;

        _rb.isKinematic = false;
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
        DiceLabelSetup.UpdateBackFaceVisibility(_faceRenderers, _faces, transform, _cam);

        if (IsServer && _dragController != ulong.MaxValue && Time.unscaledTime > _dragLeaseUntil)
        {
            _dragController = ulong.MaxValue;
            _rb.isKinematic = false;
            IsRolling = false;
        }

        if (!IsServer || !_didInit || !IsRolling || HasResult) return;

        bool isSettled = _rb.linearVelocity.magnitude < stopThreshold
                      && _rb.angularVelocity.magnitude < stopThreshold;

        if (isSettled)
        {
            _settleTimer += Time.deltaTime;
            if (_settleTimer >= settleTime)
                DetermineResult();
        }
        else
        {
            _settleTimer = 0f;
        }
    }

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
