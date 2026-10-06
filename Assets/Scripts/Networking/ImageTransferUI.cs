using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Local status of image transfers; does not block game input.</summary>
public class ImageTransferUI : MonoBehaviour
{
    private sealed class Row
    {
        public GameObject Root;
        public Text Label;
        public RectTransform Fill;
        public Button Cancel;
        public float RemoveAt;
    }

    private static ImageTransferUI _instance;
    private readonly Dictionary<string, Row> _rows = new();
    private RectTransform _container;
    private Font _font;

    public static void Show(string id, string title, float progress, string status, Action cancel)
    {
        if (string.IsNullOrEmpty(id)) return;
        EnsureInstance();
        if (_instance == null) return;
        _instance.ShowInternal(id, title, progress, status, cancel);
    }

    public static void Finish(string id, string status = "Готово")
    {
        if (_instance == null || !_instance._rows.TryGetValue(id, out Row row)) return;
        row.Label.text = status;
        row.Fill.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 318f);
        row.Cancel.gameObject.SetActive(false);
        row.RemoveAt = Time.unscaledTime + 2.5f;
    }

    public static void Remove(string id)
    {
        if (_instance == null || !_instance._rows.TryGetValue(id, out Row row)) return;
        Destroy(row.Root);
        _instance._rows.Remove(id);
        _instance.Arrange();
    }

    private static void EnsureInstance()
    {
        if (_instance != null) return;
        var ui = DiceUI.Instance;
        if (ui == null) return;
        _instance = ui.gameObject.AddComponent<ImageTransferUI>();
    }

    private void Awake()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var root = new GameObject("ImageTransferCanvas", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 45;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _container = root.GetComponent<RectTransform>();
    }

    private void Update()
    {
        var expired = new List<string>();
        foreach (var pair in _rows)
            if (pair.Value.RemoveAt > 0f && Time.unscaledTime >= pair.Value.RemoveAt)
                expired.Add(pair.Key);
        foreach (string id in expired) Remove(id);
    }

    private void ShowInternal(string id, string title, float progress, string status, Action cancel)
    {
        if (!_rows.TryGetValue(id, out Row row))
        {
            var panel = new GameObject("Transfer " + id, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(_container, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.sizeDelta = new Vector2(340f, 76f);
            panel.GetComponent<Image>().color = new Color(0.07f, 0.10f, 0.14f, 0.94f);

            var labelObj = new GameObject("Status", typeof(RectTransform), typeof(Text));
            labelObj.transform.SetParent(panel.transform, false);
            var labelRect = labelObj.GetComponent<RectTransform>();
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0f, 1f);
            labelRect.pivot = new Vector2(0f, 1f);
            labelRect.anchoredPosition = new Vector2(10f, -7f);
            labelRect.sizeDelta = new Vector2(258f, 42f);
            var label = labelObj.GetComponent<Text>();
            label.font = _font;
            label.fontSize = 13;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;

            var bar = new GameObject("Track", typeof(RectTransform), typeof(Image));
            bar.transform.SetParent(panel.transform, false);
            var barRect = bar.GetComponent<RectTransform>();
            barRect.anchorMin = barRect.anchorMax = new Vector2(0f, 0f);
            barRect.pivot = new Vector2(0f, 0f);
            barRect.anchoredPosition = new Vector2(10f, 10f);
            barRect.sizeDelta = new Vector2(318f, 12f);
            bar.GetComponent<Image>().color = new Color(0.18f, 0.22f, 0.27f);
            var fillObj = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObj.transform.SetParent(bar.transform, false);
            var fillRect = fillObj.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(0f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            fillRect.anchoredPosition = Vector2.zero;
            fillRect.sizeDelta = Vector2.zero;
            var fill = fillObj.GetComponent<Image>();
            fill.color = new Color(0.22f, 0.64f, 0.88f);

            var cancelObj = new GameObject("Cancel", typeof(RectTransform), typeof(Image), typeof(Button));
            cancelObj.transform.SetParent(panel.transform, false);
            var cancelRect = cancelObj.GetComponent<RectTransform>();
            cancelRect.anchorMin = cancelRect.anchorMax = new Vector2(1f, 1f);
            cancelRect.pivot = new Vector2(1f, 1f);
            cancelRect.anchoredPosition = new Vector2(-8f, -8f);
            cancelRect.sizeDelta = new Vector2(56f, 30f);
            cancelObj.GetComponent<Image>().color = new Color(0.32f, 0.16f, 0.17f);
            var button = cancelObj.GetComponent<Button>();
            var buttonText = new GameObject("Text", typeof(RectTransform), typeof(Text));
            buttonText.transform.SetParent(cancelObj.transform, false);
            var textRect = buttonText.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            var text = buttonText.GetComponent<Text>();
            text.font = _font;
            text.fontSize = 12;
            text.color = Color.white;
            text.text = "Отмена";
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            row = new Row { Root = panel, Label = label, Fill = fillRect, Cancel = button };
            _rows.Add(id, row);
            Arrange();
        }
        row.RemoveAt = 0f;
        row.Label.text = $"{title}  {Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f)}%\n{status}";
        row.Fill.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
            318f * Mathf.Clamp01(progress));
        row.Cancel.gameObject.SetActive(cancel != null);
        row.Cancel.onClick.RemoveAllListeners();
        if (cancel != null) row.Cancel.onClick.AddListener(() => cancel());
    }

    private void Arrange()
    {
        int index = 0;
        foreach (var row in _rows.Values)
        {
            row.Root.GetComponent<RectTransform>().anchoredPosition =
                new Vector2(14f, 82f + index * 84f);
            index++;
        }
    }
}
