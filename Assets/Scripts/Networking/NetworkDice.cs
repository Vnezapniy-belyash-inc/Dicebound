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

    // Late-join: сервер шлёт тип новым клиентам автоматически
    private readonly NetworkVariable<int> _netDieType = new(
        (int)DieType.d20, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Rigidbody _rb;
    private DieFaceData[] _faces;
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
        _netDieType.Value = (int)type;       // для late-join
        InitializeMesh(type);                 // локально на сервере
        InitClientRpc((int)type);            // всем подключённым клиентам
    }

    public override void OnNetworkSpawn()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = !IsOwner;

        // Late-join: NetworkVariable уже содержит правильное значение
        int typeInt = _netDieType.Value;
        if (typeInt != (int)DieType.d20)
            InitializeMesh((DieType)typeInt);

        // На случай если тип изменится после спавна
        _netDieType.OnValueChanged += (oldVal, newVal) =>
        {
            if (!_didInit)
                InitializeMesh((DieType)newVal);
        };
    }

    [Rpc(SendTo.NotServer)]
    private void InitClientRpc(int typeInt)
    {
        if (!_didInit)
            InitializeMesh((DieType)typeInt);
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
            mat.color = new Color(0.85f, 0.15f, 0.12f);
            mat.SetFloat("_Smoothness", 0f);
            renderer.material = mat;
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
        _didInit = true;
    }

    private void CreateFaceLabels()
    {
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
            if (mr != null)
            {
                Shader shader = Shader.Find("MeshokSGovnom/FontFaceUnlit");
                if (shader != null)
                    mr.material.shader = shader;
            }
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

        float bestDot = float.MinValue;
        int bestIndex = 0;

        if (DieType == DieType.d4)
        {
            for (int i = 0; i < _faces.Length; i++)
            {
                Vector3 worldNormal = transform.TransformDirection(_faces[i].normal);
                float dot = Vector3.Dot(worldNormal, Vector3.up);
                if (dot < bestDot) continue;
                bestDot = dot;
                bestIndex = i;
            }
        }
        else
        {
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

    [Rpc(SendTo.NotServer)]
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
            DieType.d10 when value == 0 => "0",
            _ => value.ToString(),
        };
    }
}
