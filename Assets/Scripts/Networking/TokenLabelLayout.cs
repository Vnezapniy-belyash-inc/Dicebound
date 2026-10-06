using UnityEngine;

/// <summary>Label matches the projected token width and scales as one unit with its text and spacing.</summary>
public static class TokenLabelLayout
{
    public const float DesignWidth = 160;
    public const float DesignHeight = 52;
    public const int DesignFontSize = 18;

    public static bool Project(Camera camera, Transform display, Bounds localBounds,
        float screenHeight, out Rect label)
    {
        label = default;
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = localBounds.center + Vector3.Scale(localBounds.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 projected = camera.WorldToScreenPoint(display.TransformPoint(corner));
            if (projected.z <= 0) return false;
            Vector2 point = new(projected.x, screenHeight - projected.y);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        float width = max.x - min.x;
        if (width <= 0) return false;
        float height = width * DesignHeight / DesignWidth;
        float inset = width * 0.01f;
        label = new Rect((min.x + max.x - width) * 0.5f, max.y - height - inset, width, height);
        return true;
    }

    public static Rect PlaceInsideCell(Rect label, Rect cell)
    {
        float inset = cell.width * 0.01f;
        float scale = Mathf.Min(1, Mathf.Min((cell.width - inset * 2) / label.width,
            (cell.height - inset * 2) / label.height));
        label.size *= Mathf.Max(0, scale);
        label.x = Mathf.Clamp(label.x, cell.xMin + inset, cell.xMax - inset - label.width);
        label.y = cell.yMax - inset - label.height;
        return label;
    }
}
