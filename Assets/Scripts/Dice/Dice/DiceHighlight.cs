using UnityEngine;

/// <summary>
/// Подсветка выделенного дайса: смещение _BaseColor (работает в билде)
/// + emission как дополнительный эффект в редакторе.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class DiceHighlight : MonoBehaviour
{
    [Tooltip("Цвет подсветки (светлый — виден на любом цвете кубика)")]
    public Color highlightColor = new Color(0.97f, 0.98f, 1f);

    [Tooltip("Насколько смешивать highlightColor с базовым цветом кубика")]
    [Range(0f, 1f)]
    public float highlightBlend = 0.35f;

    [Tooltip("Яркость emission при выделении (может не работать в билде из-за strip шейдеров)")]
    public float highlightEmission = 0.25f;

    private Material _material;
    private Color _baseColor;
    private bool _highlighted;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        CacheMaterial();
    }

    void CacheMaterial()
    {
        var renderer = GetComponent<MeshRenderer>();
        if (renderer == null) return;

        _material = renderer.material;
        _baseColor = ReadBaseColor(_material);
    }

    public void RefreshBaseColor(Color color)
    {
        _baseColor = color;
        if (_material == null) CacheMaterial();
        ApplyVisual();
    }

    public void SetHighlighted(bool highlighted)
    {
        if (_highlighted == highlighted) return;
        _highlighted = highlighted;
        if (_material == null) CacheMaterial();
        ApplyVisual();
    }

    void ApplyVisual()
    {
        if (_material == null) return;

        Color display = _highlighted
            ? Color.Lerp(_baseColor, highlightColor, highlightBlend)
            : _baseColor;

        _material.SetColor(BaseColorId, display);
        SetEmission(_highlighted ? highlightColor * highlightEmission : Color.black);
    }

    static Color ReadBaseColor(Material material)
    {
        if (material == null) return Color.white;
        if (material.HasProperty(BaseColorId))
            return material.GetColor(BaseColorId);
        return material.color;
    }

    void SetEmission(Color emission)
    {
        if (!_material.HasProperty(EmissionColorId)) return;

        _material.SetColor(EmissionColorId, emission);
        if (emission.maxColorComponent > 0f)
            _material.EnableKeyword("_EMISSION");
        else
            _material.DisableKeyword("_EMISSION");
    }
}
