using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class AtlasPlayerMenuFocus : MonoBehaviour
{
    private RectTransform _viewport, _content;
    private GameObject _selected;
    public void Initialize(RectTransform viewport, RectTransform content)
    { _viewport = viewport; _content = content; }
    private void OnEnable() => _selected = null;
    private void LateUpdate()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == _selected || selected == null || _content == null || _viewport == null) return;
        _selected = selected;
        if (!selected.transform.IsChildOf(_content) || selected.transform is not RectTransform rect) return;
        Canvas.ForceUpdateCanvases();
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        float bottom = _viewport.InverseTransformPoint(corners[0]).y;
        float top = _viewport.InverseTransformPoint(corners[1]).y;
        float shift = bottom < _viewport.rect.yMin ? _viewport.rect.yMin - bottom
            : top > _viewport.rect.yMax ? _viewport.rect.yMax - top : 0;
        _content.anchoredPosition += Vector2.up * shift;
    }
}
