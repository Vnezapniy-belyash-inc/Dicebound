using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Сетевой кубик. Физика на владельце, позиция через NetworkTransform.
/// Тип: InitClientRpc для текущих клиентов + NetworkVariable для late-join.
/// Результат: владелец → ServerRpc → ClientRpc всем.
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

    private Rigidbody _rb;
    private DieFaceData[] _faces;
    private MeshRenderer[] _faceRenderers;
    private Camera _cam;
    private float _settleTimer;
    private bool _didInit;

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

    public override void OnGainedOwnership()
    {
        UpdatePhysicsAuthority();
    }

    public override void OnLostOwnership()
    {
        UpdatePhysicsAuthority();
        IsRolling = false;
    }

    private void UpdatePhysicsAuthority()
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();
        if (_rb != null) _rb.isKinematic = !IsOwner;
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

    public void RequestOwnership()
    {
        if (IsOwner) return;
        RequestOwnershipServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestOwnershipServerRpc(RpcParams rpcParams = default)
    {
        GetComponent<NetworkObject>().ChangeOwnership(rpcParams.Receive.SenderClientId);
    }

    public void Roll()
    {
        if (!IsOwner || !_didInit) return;

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

        if (!IsOwner || !_didInit || !IsRolling || HasResult) return;

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

        ReportResultServerRpc(Result, LobbyUI.LocalNickname);
        OnResultReady?.Invoke(this);
    }

    [Rpc(SendTo.Server)]
    private void ReportResultServerRpc(int result, string ownerNickname)
    {
        BroadcastResultClientRpc(DieType.ToString(), result, OwnerClientId, ownerNickname);
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
