#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using UnityEngine;
using UnityEngine.EventSystems;

internal sealed class LssWindowGrip : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    private static readonly Vector2 MinimumSize = new(320, 320);
    private static readonly Texture2D[] ResizeCursors = new Texture2D[4];
    private static LssWindowGrip hoveredEdge;
    private static LssWindowGrip draggedEdge;
    private const float Edge = 8;
    private const float Corner = 16;
    private const int CursorSize = 32;
    private RectTransform window;
    private Vector2Int direction;
    private RectTransform container;
    private Vector2 startPointer;
    private Rect startWindow;
    private bool dragging;

    internal static void Attach(RectTransform area, RectTransform window, Vector2Int direction)
    {
        var grip = area.gameObject.AddComponent<LssWindowGrip>();
        grip.window = window;
        grip.direction = direction;
    }

    internal static void AddResizeEdges(RectTransform window)
    {
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            if (x == 0 && y == 0)
                continue;

            var area = AtlasUi.Node(window, $"Resize {x},{y}");
            area.anchorMin = new Vector2(x > 0 ? 1 : 0, y > 0 ? 1 : 0);
            area.anchorMax = new Vector2(x < 0 ? 0 : 1, y < 0 ? 0 : 1);
            area.pivot = new Vector2((x + 1) * .5f, (y + 1) * .5f);
            bool corner = x != 0 && y != 0;
            area.sizeDelta = new Vector2(x == 0 ? -2 * Corner : corner ? Corner : Edge,
                y == 0 ? -2 * Corner : corner ? Corner : Edge);
            area.anchoredPosition = Vector2.zero;
            var hit = area.gameObject.AddComponent<UnityEngine.UI.Image>();
            hit.color = Color.clear;
            hit.canvasRenderer.cullTransparentMesh = false;
            Attach(area, window, new Vector2Int(x, y));
        }
    }

    public void OnBeginDrag(PointerEventData e)
    {
        container = (RectTransform)window.parent;
        dragging = e.button == PointerEventData.InputButton.Left &&
            RectTransformUtility.ScreenPointToLocalPointInRectangle(container, e.pressPosition,
                e.pressEventCamera, out startPointer);
        startWindow = new Rect(window.anchoredPosition - window.sizeDelta * .5f, window.sizeDelta);
        if (dragging && direction != Vector2Int.zero)
        {
            draggedEdge = this;
            UpdateCursor();
        }
    }

    public void OnDrag(PointerEventData e)
    {
        if (!dragging || e.button != PointerEventData.InputButton.Left ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(container, e.position,
                e.pressEventCamera, out var pointer))
            return;

        Vector2 delta = pointer - startPointer;
        if (direction == Vector2Int.zero)
        {
            window.anchoredPosition = startWindow.center + delta;
            return;
        }

        Vector2 lower = startWindow.min;
        Vector2 upper = startWindow.max;
        ResizeAxis(direction.x, delta.x, MinimumSize.x, ref lower.x, ref upper.x);
        ResizeAxis(direction.y, delta.y, MinimumSize.y, ref lower.y, ref upper.y);
        window.sizeDelta = upper - lower;
        window.anchoredPosition = (lower + upper) * .5f;
    }

    public void OnEndDrag(PointerEventData e)
    {
        dragging = false;
        if (draggedEdge != this)
            return;

        draggedEdge = null;
        UpdateCursor();
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (direction == Vector2Int.zero)
            return;

        hoveredEdge = this;
        UpdateCursor();
    }

    public void OnPointerExit(PointerEventData e)
    {
        if (hoveredEdge != this)
            return;

        hoveredEdge = null;
        UpdateCursor();
    }

    private void OnDisable()
    {
        dragging = false;
        if (hoveredEdge != this && draggedEdge != this)
            return;

        if (hoveredEdge == this) hoveredEdge = null;
        if (draggedEdge == this) draggedEdge = null;
        UpdateCursor();
    }

    private static void ResizeAxis(int side, float delta, float minSize, ref float lower, ref float upper)
    {
        if (side < 0)
            lower = Mathf.Min(lower + delta, upper - minSize);
        else if (side > 0)
            upper = Mathf.Max(upper + delta, lower + minSize);
    }

    private static void UpdateCursor()
    {
        var grip = draggedEdge != null ? draggedEdge : hoveredEdge;
        if (grip == null)
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            return;
        }

        Vector2Int direction = grip.direction;
        int index = direction.y == 0 ? 0 : direction.x == 0 ? 1 : direction.x == direction.y ? 2 : 3;
        if (ResizeCursors[index] == null)
            ResizeCursors[index] = CreateResizeCursor(((Vector2)direction).normalized);

        Cursor.SetCursor(ResizeCursors[index], Vector2.one * (CursorSize / 2), CursorMode.Auto);
    }

    private static Texture2D CreateResizeCursor(Vector2 axis)
    {
        var texture = new Texture2D(CursorSize, CursorSize, TextureFormat.RGBA32, false);
        var pixels = new Color32[CursorSize * CursorSize];
        var center = Vector2.one * (CursorSize / 2 - .5f);
        var normal = new Vector2(-axis.y, axis.x);
        for (int y = 0; y < CursorSize; y++)
        for (int x = 0; x < CursorSize; x++)
        {
            Vector2 offset = new Vector2(x, y) - center;
            float along = Mathf.Abs(Vector2.Dot(offset, axis));
            float across = Mathf.Abs(Vector2.Dot(offset, normal));
            float distance = Mathf.Min(Mathf.Max(across - 1, along - 10),
                Mathf.Max(5 - along, across - (12 - along) * .75f));
            if (distance <= 1)
                pixels[y * CursorSize + x] = distance <= 0 ? Color.white : Color.black;
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
}
#endif
