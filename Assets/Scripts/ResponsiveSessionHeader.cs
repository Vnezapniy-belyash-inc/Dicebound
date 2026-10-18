using System.Collections.Generic;
using UnityEngine;

/// <summary>Wraps session controls into readable rows when the logical viewport is narrow.</summary>
public sealed class ResponsiveSessionHeader : MonoBehaviour
{
    private struct Placement
    {
        public RectTransform rect;
        public Vector2 min, max, pivot, size, position;
    }
    private readonly List<Placement> _items = new();
    private RectTransform _bar;
    private float _lastWidth = -1;
    private void LateUpdate() => RefreshLayout();
    public void RefreshLayout()
    {
        if (_bar == null)
        {
            _bar = GetComponent<RectTransform>();
            foreach (Transform child in transform)
                if (child is RectTransform rect) _items.Add(new Placement { rect = rect, min = rect.anchorMin, max = rect.anchorMax,
                    pivot = rect.pivot, size = rect.sizeDelta, position = rect.anchoredPosition });
        }
        float width = _bar.rect.width;
        if (Mathf.Abs(width - _lastWidth) < 0.5f) return;
        _lastWidth = width;
        bool compact = width < 1280;
        int columns = Mathf.Clamp(Mathf.FloorToInt((width - 24) / 196), 2, 4);
        float cellWidth = (width - 24 - (columns - 1) * 8) / columns;
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i]; if (item.rect == null) continue;
            item.rect.anchorMin = compact ? new Vector2(0, 1) : item.min;
            item.rect.anchorMax = compact ? new Vector2(0, 1) : item.max;
            item.rect.pivot = compact ? new Vector2(0, 1) : item.pivot;
            item.rect.sizeDelta = compact ? new Vector2(cellWidth, 34) : item.size;
            item.rect.anchoredPosition = compact ? new Vector2(12 + i % columns * (cellWidth + 8), -6 - i / columns * 42) : item.position;
        }
        int rows = Mathf.CeilToInt(_items.Count / (float)columns);
        _bar.sizeDelta = new Vector2(_bar.sizeDelta.x, compact ? 12 + rows * 42 - 8 : 56);
    }
}
