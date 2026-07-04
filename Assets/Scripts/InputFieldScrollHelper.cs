using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Добавляет скроллбар и колёсико мыши к многострочному InputField.
/// Скроллит прямым сдвигом RectTransform текста.
/// После скролла синхронизируется с InputField чтобы не мешать вводу.
/// </summary>
[RequireComponent(typeof(InputField))]
public class InputFieldScrollHelper : MonoBehaviour, IScrollHandler
{
    InputField _input;
    Text _textComp;
    Scrollbar _scrollbar;
    float _viewportH;
    bool _didInit;
    int _charsPerLine = 60;
    float _targetScroll;
    float _lastUserScrollTime = -999f;

    public void Init(InputField input, Text textComp, Scrollbar scrollbar, float viewportH)
    {
        _input = input;
        _textComp = textComp;
        _scrollbar = scrollbar;
        _viewportH = viewportH;

        if (_scrollbar != null)
            _scrollbar.onValueChanged.AddListener(OnScrollbarChanged);

        _input.onValueChanged.AddListener(_ => OnTextChanged());
    }

    void Update()
    {
        if (!_didInit)
        {
            _didInit = true;
            Canvas.ForceUpdateCanvases();
            UpdateCharsPerLine();
            OnTextChanged();
        }

        bool userScrolling = (Time.unscaledTime - _lastUserScrollTime) < 0.15f;

        if (!userScrolling)
        {
            // Sync mode: follow InputField's actual text position
            float val = CalcScrollbarValue();
            if (_scrollbar != null)
                _scrollbar.SetValueWithoutNotify(val);
            _targetScroll = 1f - val;
        }

        // Always apply target position
        ApplyManualScroll(_targetScroll);
    }

    void ApplyManualScroll(float t)
    {
        float totalHeight = GetTotalTextHeight();
        float visibleHeight = _viewportH;

        if (totalHeight <= visibleHeight)
        {
            var tr = _textComp.rectTransform;
            tr.anchoredPosition = new Vector2(tr.anchoredPosition.x, 0);
            SyncPlaceholder(0);
            return;
        }

        float maxOffset = totalHeight - visibleHeight;
        float offset = maxOffset * t;

        _textComp.rectTransform.anchoredPosition =
            new Vector2(_textComp.rectTransform.anchoredPosition.x, offset);

        SyncPlaceholder(offset);
    }

    void SyncPlaceholder(float offset)
    {
        var ph = _input.placeholder;
        if (ph != null)
        {
            var phRt = ph.rectTransform;
            phRt.anchoredPosition = new Vector2(phRt.anchoredPosition.x, offset);
        }
    }

    void UpdateCharsPerLine()
    {
        float w = GetTextWidth();
        if (w > 20f && _textComp != null)
            _charsPerLine = Mathf.Max(10, Mathf.RoundToInt(w / (_textComp.fontSize * 0.55f)));
    }

    float GetTextWidth()
    {
        if (_textComp != null)
        {
            float w = _textComp.rectTransform.rect.width;
            if (w > 20f) return w;
        }
        if (_input != null)
        {
            float w = ((RectTransform)_input.transform).rect.width;
            if (w > 20f) return w - 30f;
        }
        return 400f;
    }

    void OnTextChanged()
    {
        if (_scrollbar == null) return;
        UpdateCharsPerLine();

        float totalHeight = GetTotalTextHeight();
        float visibleHeight = _viewportH;

        bool needScrollbar = totalHeight > visibleHeight + 2f;
        _scrollbar.gameObject.SetActive(needScrollbar);
        if (needScrollbar)
            _scrollbar.size = Mathf.Clamp(visibleHeight / Mathf.Max(0.1f, totalHeight), 0.08f, 1f);
    }

    float GetTotalTextHeight()
    {
        if (_textComp == null || string.IsNullOrEmpty(_input.text))
            return _viewportH; // no text → handle fills track

        float width = GetTextWidth();
        if (width < 1f) width = 400f;

        var settings = _textComp.GetGenerationSettings(new Vector2(width, 0));
        return _textComp.cachedTextGenerator.GetPreferredHeight(_input.text, settings);
    }

    float CalcScrollbarValue()
    {
        float totalHeight = GetTotalTextHeight();
        float visibleHeight = _viewportH;
        if (totalHeight <= visibleHeight) return 0;
        float maxOffset = totalHeight - visibleHeight;
        float currentOffset = _textComp.rectTransform.anchoredPosition.y;
        return 1f - Mathf.Clamp01(currentOffset / maxOffset);
    }

    void OnScrollbarChanged(float value)
    {
        _targetScroll = 1f - value;
        _lastUserScrollTime = Time.unscaledTime;
    }

    public void OnScroll(PointerEventData eventData)
    {
        // Consume event so it doesn't affect other UI elements
        eventData.Use();

        float delta = eventData.scrollDelta.y;
        if (Mathf.Abs(delta) < 0.01f) return;

        float totalHeight = GetTotalTextHeight();
        float visibleHeight = _viewportH;
        if (totalHeight <= visibleHeight) return;

        float maxOffset = totalHeight - visibleHeight;
        float scrollStep = 42f;
        float currentOffset = maxOffset * _targetScroll;
        float newOffset = Mathf.Clamp(currentOffset - delta * scrollStep, 0, maxOffset);

        _targetScroll = newOffset / maxOffset;
        _lastUserScrollTime = Time.unscaledTime;

        if (_scrollbar != null)
            _scrollbar.SetValueWithoutNotify(1f - _targetScroll);
    }

    #region Text-based line estimation

    int EstimateTotalLines()
    {
        string text = _input.text;
        if (string.IsNullOrEmpty(text)) return 1;

        int lines = 1;
        int col = 0;
        int cpl = Mathf.Max(10, _charsPerLine);

        foreach (char c in text)
        {
            if (c == '\n') { lines++; col = 0; }
            else { col++; if (col >= cpl) { lines++; col = 0; } }
        }
        return Mathf.Max(1, lines);
    }

    #endregion
}
