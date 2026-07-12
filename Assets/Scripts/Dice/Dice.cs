using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class Dice : MonoBehaviour, IDice
{
    public Rigidbody Rigidbody => _rb;
    void IDice.StartRoll() => StartRolling();
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
    public event Action<Dice> OnResultReady;

    public void StartRolling()
    {
        IsRolling = true;
        HasResult = false;
        Result = -1;
        _settleTimer = 0f;
    }

    private Rigidbody _rb;
    private DieFaceData[] _faces;
    private MeshRenderer[] _faceRenderers;
    private Camera _cam;
    private float _settleTimer;

    public void Initialize(DieType type)
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
            mat.SetFloat("_Metallic", 0f);
            mat.SetInt("_Cull", 2);
            renderer.material = mat;
        }

        MeshCollider mc = GetComponent<MeshCollider>();
        if (mc == null) mc = gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = true;

        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = true;
        _rb.mass = 0.3f;
        _rb.angularDamping = 0.3f;
        _rb.linearDamping = 0.2f;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        CreateFaceLabels();
        gameObject.AddComponent<DiceHighlight>();
    }

    void CreateFaceLabels()
    {
        if (_faces == null) return;
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
            GameObject labelObj = new GameObject($"FaceLabel{i}");
            labelObj.transform.SetParent(transform, false);
            labelObj.transform.localPosition = fd.center + fd.normal * textOffset;
            labelObj.transform.localRotation = Quaternion.LookRotation(-fd.normal);

            TextMesh tm = labelObj.AddComponent<TextMesh>();
            tm.text = FormatFaceValue(DieType, fd.value);
            tm.fontSize = fontSize;
            tm.color = textColor;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.04f;

            MeshRenderer mr = labelObj.GetComponent<MeshRenderer>();
            _faceRenderers[i] = mr;
            DiceLabelSetup.ApplyLabelMaterial(mr, textColor);
        }
        _cam = Camera.main;
    }

    void Update()
    {
        // Скрываем цифры на задних гранях
        DiceLabelSetup.UpdateBackFaceVisibility(_faceRenderers, _faces, transform, _cam);

        // Физика — проверка остановки
        if (!IsRolling || HasResult) return;
        if (_rb == null) return;

        bool isSettled = _rb.linearVelocity.magnitude < stopThreshold
                      && _rb.angularVelocity.magnitude < stopThreshold;

        if (isSettled)
        {
            _settleTimer += Time.deltaTime;
            if (_settleTimer >= settleTime) DetermineResult();
        }
        else
        {
            _settleTimer = 0f;
        }
    }

    public void Roll()
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();
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

    void DetermineResult()
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
        OnResultReady?.Invoke(this);
    }

    static string FormatFaceValue(DieType type, int value) => type switch
    {
        DieType.d100 when value == 0 => "00",
        DieType.d100 => value.ToString(),
        DieType.d10 when value == 0 => "0",
        _ => value.ToString(),
    };
}
