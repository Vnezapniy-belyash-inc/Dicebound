using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Keeps existing fixed-layout panels readable by scrolling their content on short windows.</summary>
public sealed class UiPanelViewport : MonoBehaviour
{
    private RectTransform _panel, _viewport, _content;
    private Canvas _canvas;
    private float _naturalHeight;
    private GameObject _lastSelected;

    private void OnEnable()
    {
        if (_content == null)
        {
            _panel = GetComponent<RectTransform>(); _canvas = GetComponentInParent<Canvas>();
            _naturalHeight = _panel.sizeDelta.y;
            var children = new Transform[transform.childCount];
            for (int i = 0; i < children.Length; i++) children[i] = transform.GetChild(i);
            var view = new GameObject("PanelViewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            view.transform.SetParent(transform, false); _viewport = view.GetComponent<RectTransform>();
            _viewport.anchorMin = Vector2.zero; _viewport.anchorMax = Vector2.one;
            _viewport.offsetMin = _viewport.offsetMax = Vector2.zero;
            _viewport.offsetMax = new Vector2(-10, 0);
            view.GetComponent<Image>().color = Color.clear;
            var content = new GameObject("PanelContent", typeof(RectTransform));
            content.transform.SetParent(view.transform, false); _content = content.GetComponent<RectTransform>();
            _content.anchorMin = new Vector2(0, 1); _content.anchorMax = Vector2.one; _content.pivot = new Vector2(0.5f, 1);
            _content.sizeDelta = new Vector2(0, _naturalHeight);
            foreach (var child in children) child.SetParent(_content, false);
            var scroll = gameObject.AddComponent<ScrollRect>();
            scroll.viewport = _viewport; scroll.content = _content; scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30;
            var track = new GameObject("PanelScrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            track.transform.SetParent(transform, false);
            var trackRect = track.GetComponent<RectTransform>();
            trackRect.anchorMin = new Vector2(1, 0); trackRect.anchorMax = Vector2.one;
            trackRect.pivot = Vector2.one; trackRect.sizeDelta = new Vector2(6, -16);
            trackRect.anchoredPosition = new Vector2(-2, -8);
            VttUiSkin.Surface(track.GetComponent<Image>(), VttUiSkin.Raised, 3, false);
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image));
            handle.transform.SetParent(track.transform, false);
            var handleRect = handle.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero; handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = handleRect.offsetMax = Vector2.zero;
            VttUiSkin.Surface(handle.GetComponent<Image>(), VttUiSkin.Muted, 3, false);
            var bar = track.GetComponent<Scrollbar>(); bar.handleRect = handle.GetComponent<RectTransform>();
            bar.targetGraphic = handle.GetComponent<Image>(); bar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = bar; scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }
        _lastSelected = null;
        Fit();
    }

    private void Fit()
    {
        if (_canvas == null) return;
        float footer = 48 * Mathf.Min(LocalUiScale.Value, Mathf.Min(Screen.width / 600f, Screen.height / 460f));
        float available = Screen.height - (DiceUI.Instance?.SessionHeaderPixelHeight ?? 0) - 24 - footer;
        float height = Mathf.Min(_naturalHeight, Mathf.Max(160, available / Mathf.Max(0.01f, _canvas.scaleFactor)));
        _panel.sizeDelta = new Vector2(_panel.sizeDelta.x, height);
        _panel.anchoredPosition = new Vector2(_panel.anchoredPosition.x,
            -((DiceUI.Instance?.SessionHeaderPixelHeight ?? 0) + 12) / Mathf.Max(0.01f, _canvas.scaleFactor));
    }

    private void LateUpdate()
    {
        Fit();
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == _lastSelected) return;
        _lastSelected = selected;
        if (selected == null || !selected.transform.IsChildOf(_content) || selected.transform is not RectTransform rect) return;
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        float bottom = _viewport.InverseTransformPoint(corners[0]).y;
        float top = _viewport.InverseTransformPoint(corners[1]).y;
        float shift = bottom < _viewport.rect.yMin ? _viewport.rect.yMin - bottom
            : top > _viewport.rect.yMax ? _viewport.rect.yMax - top : 0;
        _content.anchoredPosition += Vector2.up * shift;
    }
}
