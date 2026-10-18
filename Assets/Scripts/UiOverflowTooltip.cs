using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Shows the full, already permitted UI caption when it does not fit its control.</summary>
public sealed class UiOverflowTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
{
    public Text source;
    private bool _hovered, _selected;
    private float _since;
    public void OnPointerEnter(PointerEventData data) { _hovered = true; _since = Time.unscaledTime; }
    public void OnPointerExit(PointerEventData data) => _hovered = false;
    public void OnSelect(BaseEventData data) { _selected = true; _since = Time.unscaledTime; }
    public void OnDeselect(BaseEventData data) => _selected = false;

    public static void Attach(Text text, GameObject target)
    {
        var tip = target.GetComponent<UiOverflowTooltip>();
        if (tip == null) tip = target.AddComponent<UiOverflowTooltip>();
        tip.source = text;
    }

    private void OnDisable() { _hovered = _selected = false; }
    private void OnGUI()
    {
        if ((!_hovered && !_selected) || source == null || !source.isActiveAndEnabled || Time.unscaledTime - _since < 0.35f
            || !GameplayInputGate.AllowsImGuiOverlays || TokenController.IsMenuOpen || string.IsNullOrEmpty(source.text)) return;
        if (source.preferredWidth <= source.rectTransform.rect.width && source.preferredHeight <= source.rectTransform.rect.height) return;
        float scale = Mathf.Min(LocalUiScale.Value, Mathf.Min(Screen.width / 600f, Screen.height / 460f));
        var matrix = GUI.matrix; int depth = GUI.depth;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1)); GUI.depth = -800;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, richText = false };
        style.normal.textColor = VttUiSkin.Text;
        float width = Mathf.Min(360, Screen.width / scale - 16);
        float height = Mathf.Min(Screen.height / scale - 16, style.CalcHeight(new GUIContent(source.text), width - 20) + 16);
        var canvas = source.canvas;
        Vector2 point = _hovered && Mouse.current != null ? Mouse.current.position.ReadValue()
            : RectTransformUtility.WorldToScreenPoint(canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, source.transform.position);
        point /= scale; point.y = Screen.height / scale - point.y;
        float x = point.x + 16;
        if (x + width > Screen.width / scale - 8) x = point.x - width - 16;
        float y = point.y + 20;
        if (y + height > Screen.height / scale - 8) y = point.y - height - 12;
        var rect = new Rect(Mathf.Clamp(x, 8, Screen.width / scale - width - 8), Mathf.Clamp(y, 8, Screen.height / scale - height - 8), width, height);
        GUI.Box(rect, "", VttUiSkin.ImGuiPanel);
        GUI.Label(new Rect(rect.x + 10, rect.y + 8, width - 20, height - 16), source.text, style);
        GUI.matrix = matrix; GUI.depth = depth;
    }
}
