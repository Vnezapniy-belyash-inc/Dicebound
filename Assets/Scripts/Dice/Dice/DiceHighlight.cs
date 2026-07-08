using UnityEngine;

/// <summary>
/// Подсветка дайса — emission вместо смены цвета.
/// Цвет кубика не трогаем.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class DiceHighlight : MonoBehaviour
{
    [Tooltip("Цвет подсветки")]
    public Color highlightColor = new Color(0.2f, 0.6f, 1f);

    private Material _material;
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    private bool _highlighted;

    void Awake()
    {
        _material = GetComponent<MeshRenderer>().material;
        _material.EnableKeyword("_EMISSION");
    }

    public void SetHighlighted(bool highlighted)
    {
        if (_highlighted == highlighted) return;
        _highlighted = highlighted;

        var mr = GetComponent<MeshRenderer>();
        if (mr == null) return;
        mr.material.EnableKeyword("_EMISSION");
        mr.material.SetColor(EmissionColor, highlighted ? highlightColor * 0.5f : Color.black);
    }
}
