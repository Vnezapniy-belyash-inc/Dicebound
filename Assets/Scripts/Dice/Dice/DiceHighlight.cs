using UnityEngine;

/// <summary>
/// Подсветка дайса — меняет цвет материала на голубой.
/// Максимально просто и надёжно.
/// </summary>
[RequireComponent(typeof(MeshRenderer))]
public class DiceHighlight : MonoBehaviour
{
    [Tooltip("Цвет подсветки")]
    public Color highlightColor = new Color(0.2f, 0.6f, 1f);

    private Color _originalColor;
    private Material _material;
    private bool _highlighted;

    void Awake()
    {
        _material = GetComponent<MeshRenderer>().material;
        _originalColor = _material.GetColor("_BaseColor");
    }

    public void SetHighlighted(bool highlighted)
    {
        if (_highlighted == highlighted) return;
        _highlighted = highlighted;
        _material.SetColor("_BaseColor", highlighted ? highlightColor : _originalColor);
    }

    void OnDestroy()
    {
        if (_material != null)
            _material.SetColor("_BaseColor", _originalColor);
    }
}
