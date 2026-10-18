using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Local scaling for existing and newly created screen-space canvases.</summary>
public sealed class LocalUiScale : MonoBehaviour
{
    private const string PreferenceKey = "Display.UiScale";
    public static float Value { get; private set; } = 1f;
    private readonly Dictionary<CanvasScaler, Baseline> _canvases = new();
    private float _nextRefresh;
    private int _lastWidth, _lastHeight;
    private struct Baseline
    {
        public Vector2 reference;
        public float factor;
    }

    private void Awake()
    {
        float stored = PlayerPrefs.GetFloat(PreferenceKey, 1f);
        Value = float.IsNaN(stored) || float.IsInfinity(stored) ? 1f : Mathf.Clamp(stored, 0.75f, 1.5f);
        Refresh();
    }

    public void SetScale(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return;
        Value = Mathf.Clamp(value, 0.75f, 1.5f);
        PlayerPrefs.SetFloat(PreferenceKey, Value);
        PlayerPrefs.Save();
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextRefresh && _lastWidth == Screen.width && _lastHeight == Screen.height) return;
        _lastWidth = Screen.width; _lastHeight = Screen.height;
        _nextRefresh = Time.unscaledTime + 0.5f;
        Refresh();
    }

    private void Refresh()
    {
        var removed = new List<CanvasScaler>();
        foreach (var entry in _canvases) if (entry.Key == null) removed.Add(entry.Key);
        foreach (var scaler in removed) _canvases.Remove(scaler);
        foreach (var scaler in Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include))
        {
            var canvas = scaler.GetComponent<Canvas>();
            if (canvas == null || canvas.renderMode == RenderMode.WorldSpace || !canvas.isRootCanvas) continue;
            if (!_canvases.TryGetValue(scaler, out var baseline))
            {
                baseline = new Baseline { reference = scaler.referenceResolution, factor = scaler.scaleFactor };
                _canvases.Add(scaler, baseline);
            }
            if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                scaler.referenceResolution = baseline.reference / Value;
            else if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize)
                scaler.scaleFactor = baseline.factor * Value;
            ApplyScalerNow(scaler, canvas);
        }
        foreach (var header in Object.FindObjectsByType<ResponsiveSessionHeader>(FindObjectsInactive.Exclude)) header.RefreshLayout();
        Canvas.ForceUpdateCanvases();
        foreach (var entry in _canvases)
        {
            var scaler = entry.Key;
            var canvas = scaler.GetComponent<Canvas>();
            float fit = 1f;
            foreach (Transform child in canvas.transform)
            {
                if (child is not RectTransform panel || !panel.gameObject.activeInHierarchy || panel.anchorMin != panel.anchorMax) continue;
                Rect bounds = ScreenBounds(panel, canvas);
                float reserve = panel.anchorMin.y > 0.99f ? (DiceUI.Instance?.SessionHeaderPixelHeight ?? 0) + 12 : 0;
                fit = Mathf.Min(fit, (Screen.width - 12f) / Mathf.Max(1f, bounds.width),
                    (Screen.height - reserve - 12f) / Mathf.Max(1f, bounds.height));
            }
            if (fit < 1f)
            {
                if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
                    scaler.referenceResolution /= Mathf.Max(0.1f, fit);
                else if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize)
                    scaler.scaleFactor *= Mathf.Max(0.1f, fit);
                ApplyScalerNow(scaler, canvas);
                Canvas.ForceUpdateCanvases();
            }
            foreach (Transform child in canvas.transform)
            {
                if (child is not RectTransform panel || !panel.gameObject.activeInHierarchy || panel.anchorMin != panel.anchorMax) continue;
                Rect bounds = ScreenBounds(panel, canvas);
                Vector2 shift = Vector2.zero;
                if (bounds.xMin < 6) shift.x = 6 - bounds.xMin;
                else if (bounds.xMax > Screen.width - 6) shift.x = Screen.width - 6 - bounds.xMax;
                if (bounds.yMin < 6) shift.y = 6 - bounds.yMin;
                else if (bounds.yMax > Screen.height - 6) shift.y = Screen.height - 6 - bounds.yMax;
                if (panel.anchorMin.y > 0.99f)
                {
                    float top = Screen.height - (DiceUI.Instance?.SessionHeaderPixelHeight ?? 0) - 12;
                    if (bounds.yMax + shift.y > top) shift.y = top - bounds.yMax;
                }
                if (shift == Vector2.zero) continue;
                var parent = panel.parent as RectTransform;
                Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, bounds.center, camera, out var before)
                    && RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, bounds.center + shift, camera, out var after))
                    panel.anchoredPosition += after - before;
            }
        }
    }

    private static void ApplyScalerNow(CanvasScaler scaler, Canvas canvas)
    {
        if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize)
            canvas.scaleFactor = scaler.scaleFactor;
        else if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
        {
            float x = Screen.width / Mathf.Max(1f, scaler.referenceResolution.x);
            float y = Screen.height / Mathf.Max(1f, scaler.referenceResolution.y);
            canvas.scaleFactor = scaler.screenMatchMode switch
            {
                CanvasScaler.ScreenMatchMode.Expand => Mathf.Min(x, y),
                CanvasScaler.ScreenMatchMode.Shrink => Mathf.Max(x, y),
                _ => Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(x, 2f), Mathf.Log(y, 2f), scaler.matchWidthOrHeight))
            };
        }
    }

    private static Rect ScreenBounds(RectTransform panel, Canvas canvas)
    {
        var corners = new Vector3[4];
        panel.GetWorldCorners(corners);
        Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        foreach (var corner in corners)
        {
            var point = RectTransformUtility.WorldToScreenPoint(camera, corner);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
}
