using UnityEngine;

/// <summary>
/// Подсветка дайса через emission — базовый цвет кубика не меняется.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class DiceHighlight : MonoBehaviour
{
    [Tooltip("Цвет подсветки")]
    public Color highlightColor = new Color(0.2f, 0.6f, 1f);

    [Tooltip("Яркость emission при выделении")]
    public float highlightEmission = 0.35f;

    private Material _material;
    private Color _baseColor;
    private bool _highlighted;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    void Awake()
    {
        _material = GetComponent<MeshRenderer>().material;
        _baseColor = ReadBaseColor(_material);
    }

    public void RefreshBaseColor(Color color)
    {
        _baseColor = color;
        if (_material == null)
            _material = GetComponent<MeshRenderer>().material;

        _material.SetColor(BaseColorId, _baseColor);
        if (!_highlighted)
            SetEmission(Color.black);
    }

    public void SetHighlighted(bool highlighted)
    {
        if (_highlighted == highlighted) return;
        _highlighted = highlighted;

        if (_material == null)
            _material = GetComponent<MeshRenderer>().material;

        _material.SetColor(BaseColorId, _baseColor);
        SetEmission(highlighted ? highlightColor * highlightEmission : Color.black);
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
        if (_material.HasProperty(EmissionColorId))
        {
            _material.SetColor(EmissionColorId, emission);
            if (emission.maxColorComponent > 0f)
                _material.EnableKeyword("_EMISSION");
            else
                _material.DisableKeyword("_EMISSION");
        }
    }
}
