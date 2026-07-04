using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Сетевой кубик. Физика только на владельце (client-authoritative).
/// NetworkTransform синхронизирует позицию/вращение остальным.
/// Результат: владелец определяет → ServerRpc → ClientRpc всем.
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

    /// <summary>Сброс состояния перед броском (без физики).</summary>
    public void StartRoll()
    {
        IsRolling = true;
        HasResult = false;
        Result = -1;
        _settleTimer = 0f;
    }

    [Header("Физика")]
    public float rollForce = 8f;
    public float maxTorque = 12f;
    public float stopThreshold = 0.15f;
    public float settleTime = 0.6f;

    [Header("Отображение")]
    public float textOffset = 0.02f;
    public int fontSize = 48;
    public Color textColor = Color.black;

    public DieType DieType { get; private set; }
    public int Result { get; private set; } = -1;
    public bool IsRolling { get; set; }
    public bool HasResult { get; private set; }

    public event Action<NetworkDice> OnResultReady;

    private Rigidbody _rb;
    private DieFaceData[] _faces;
    private float _settleTimer;
    private bool _didInit;

    /// <summary>Инициализация — вызывается на владельце после спавна.</summary>
    public void Init(DieType type)
    {
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

        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = true;
        _rb.mass = 0.3f;
        _rb.angularDamping = 0.3f;
        _rb.linearDamping = 0.2f;

        CreateFaceLabels();
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
        }
    }

    public override void OnNetworkSpawn()
    {
        // Только владелец включает физику, остальные — куклы
        if (_rb != null)
            _rb.isKinematic = !IsOwner;
    }

    /// <summary>Бросок — вызывается ТОЛЬКО на владельце.</summary>
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
        // ТОЛЬКО владелец определяет остановку и результат
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

        ReportResultServerRpc(Result);
        OnResultReady?.Invoke(this);
    }

    /// <summary>Владелец → сервер: кубик остановился, результат N.</summary>
    [ServerRpc]
    private void ReportResultServerRpc(int result)
    {
        BroadcastResultClientRpc(DieType.ToString(), result, OwnerClientId);
    }

    /// <summary>Сервер → всем клиентам: результат кубика.</summary>
    [ClientRpc]
    private void BroadcastResultClientRpc(string dieType, int result, ulong throwerId)
    {
        Debug.Log($"[Dice] Player {throwerId} rolled {dieType}: {result}");
        DiceUI.Instance?.ShowResult(dieType, result, throwerId);
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
