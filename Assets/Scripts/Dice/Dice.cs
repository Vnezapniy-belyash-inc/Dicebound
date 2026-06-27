using System;
using UnityEngine;

/// <summary>
/// Один кубик: физика, бросок, определение результата, отображение цифр на гранях.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class Dice : MonoBehaviour
{
    [Header("Физика")]
    [Tooltip("Сила броска")]
    public float rollForce = 8f;

    [Tooltip("Максимальный крутящий момент")]
    public float maxTorque = 12f;

    [Tooltip("Порог скорости — ниже считается остановкой")]
    public float stopThreshold = 0.15f;

    [Tooltip("Сколько секунд кубик должен быть почти неподвижен чтобы считаться остановленным")]
    public float settleTime = 0.6f;

    [Header("Отображение")]
    [Tooltip("Отступ текста от поверхности грани")]
    public float textOffset = 0.02f;

    [Tooltip("Размер шрифта TextMesh")]
    public int fontSize = 48;

    [Tooltip("Цвет текста")]
    public Color textColor = Color.black;

    // ─── публичное состояние ───
    public DieType DieType { get; private set; }
    public int Result { get; private set; } = -1;
    public bool IsRolling { get; private set; }
    public bool HasResult { get; private set; }

    /// <summary>Срабатывает когда кубик остановился и результат определён.</summary>
    public event Action<Dice> OnResultReady;

    // ─── приватное ───
    private Rigidbody _rb;
    private DieFaceData[] _faces;
    private float _settleTimer;

    // ══════════════════════════════════════════════
    //  Инициализация
    // ══════════════════════════════════════════════

    /// <summary>Создаёт и настраивает кубик заданного типа.</summary>
    public void Initialize(DieType type)
    {
        DieType = type;

        // Генерируем геометрию
        var (mesh, faces) = DieMeshGenerator.Generate(type);
        _faces = faces;

        // Mesh
        GetComponent<MeshFilter>().mesh = mesh;

        // Material — единый матовый для всех типов
        var renderer = GetComponent<MeshRenderer>();
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit != null)
        {
            Material mat = new Material(urpLit);
            mat.color = new Color(0.85f, 0.15f, 0.12f); // красный
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_Metallic", 0f);
            renderer.material = mat;
        }

        // Коллайдер — выпуклая оболочка
        MeshCollider mc = GetComponent<MeshCollider>();
        if (mc == null) mc = gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = true;

        // Rigidbody
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = true;
        _rb.mass = 0.3f;
        _rb.angularDamping = 0.3f;
        _rb.linearDamping = 0.2f;

        // Текст на гранях
        CreateFaceLabels();
    }

    void CreateFaceLabels()
    {
        if (_faces == null) return;

        // Удаляем старые лейблы если есть
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name.StartsWith("FaceLabel"))
                Destroy(child.gameObject);
        }

        for (int i = 0; i < _faces.Length; i++)
        {
            var fd = _faces[i];
            string label = FormatFaceValue(DieType, fd.value);

            GameObject labelObj = new GameObject($"FaceLabel{i}");
            labelObj.transform.SetParent(transform, worldPositionStays: false);
            labelObj.transform.localPosition = fd.center + fd.normal * textOffset;
            // TextMesh смотрит в -Z, а нормаль смотрит наружу → поворачиваем на 180
            labelObj.transform.localRotation = Quaternion.LookRotation(-fd.normal);

            TextMesh tm = labelObj.AddComponent<TextMesh>();
            tm.text = label;
            tm.fontSize = fontSize;
            tm.color = textColor;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.04f;

            // Замена материала на кастомный шейдер с ZWrite On + clip
            var mr = labelObj.GetComponent<MeshRenderer>();
            Shader fontShader = Shader.Find("Custom/Font Opaque");
            if (fontShader != null)
            {
                Material mat = new Material(fontShader);
                mat.mainTexture = tm.font.material.mainTexture;
                mat.color = textColor;
                mr.material = mat;
            }
        }
    }

    // ══════════════════════════════════════════════
    //  Бросок
    // ══════════════════════════════════════════════

    /// <summary>Подбрасывает кубик со случайной силой и крутящим моментом.</summary>
    public void Roll()
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();

        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        IsRolling = true;
        HasResult = false;
        Result = -1;
        _settleTimer = 0f;

        // Случайное направление броска (вверх + в сторону)
        Vector3 forceDir = (Vector3.up * 0.8f + UnityEngine.Random.insideUnitSphere * 0.6f).normalized;
        float force = rollForce * UnityEngine.Random.Range(0.7f, 1.3f);
        _rb.AddForce(forceDir * force, ForceMode.Impulse);

        // Случайный крутящий момент
        Vector3 torque = UnityEngine.Random.insideUnitSphere * maxTorque;
        _rb.AddTorque(torque, ForceMode.Impulse);
    }

    // ══════════════════════════════════════════════
    //  Обновление
    // ══════════════════════════════════════════════

    void Update()
    {
        if (!IsRolling || HasResult) return;

        if (_rb == null) return;

        // Проверяем остановку
        bool isSettled = _rb.linearVelocity.magnitude < stopThreshold
                      && _rb.angularVelocity.magnitude < stopThreshold;

        if (isSettled)
        {
            _settleTimer += Time.deltaTime;
            if (_settleTimer >= settleTime)
            {
                DetermineResult();
            }
        }
        else
        {
            _settleTimer = 0f;
        }
    }

    // ══════════════════════════════════════════════
    //  Определение результата
    // ══════════════════════════════════════════════

    void DetermineResult()
    {
        if (_faces == null || _faces.Length == 0) return;

        float bestDot = float.MinValue;
        int bestIndex = 0;

        if (DieType == DieType.d4)
        {
            // d4: читаем НИЖНЮЮ грань (та что касается стола)
            // Ищем минимальный dot с Vector3.up (= максимальный dot с Vector3.down)
            for (int i = 0; i < _faces.Length; i++)
            {
                Vector3 worldNormal = transform.TransformDirection(_faces[i].normal);
                float dot = Vector3.Dot(worldNormal, Vector3.up);
                if (dot < bestDot) continue; // ищем самый отрицательный dot
                bestDot = dot;
                bestIndex = i;
            }
        }
        else
        {
            // Все остальные: читаем ВЕРХНЮЮ грань
            for (int i = 0; i < _faces.Length; i++)
            {
                Vector3 worldNormal = transform.TransformDirection(_faces[i].normal);
                float dot = Vector3.Dot(worldNormal, Vector3.up);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    bestIndex = i;
                }
            }
        }

        Result = _faces[bestIndex].value;
        HasResult = true;
        IsRolling = false;
        OnResultReady?.Invoke(this);
    }

    // ══════════════════════════════════════════════
    //  Утилиты
    // ══════════════════════════════════════════════

    static string FormatFaceValue(DieType type, int value)
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