using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public partial class TokenController
{
    private const int MaxLabelIcons = 7;
    private static Canvas _tokenLabelCanvas;
    private RectTransform _labelPlate;
    private CanvasGroup _labelOpacity;
    private Text _labelName, _labelOverflow, _conditionText;
    private RawImage[] _labelIcons;
    private RectTransform _conditionTooltip;
    private string _lastLabelName;

    private void LateUpdate()
    {
        if (!IsSpawned || !GameplayInputGate.AllowsImGuiOverlays || !IsVisibleToLocalPlayer
            || !TryGetTokenLabelRect(out Rect label))
        {
            HideTokenLabel();
            return;
        }

        if (_labelPlate == null) CreateTokenLabel();
        _labelPlate.gameObject.SetActive(true);
        float scale = label.width / TokenLabelLayout.DesignWidth;
        _labelPlate.anchoredPosition = new Vector2(label.x, -label.y);
        _labelPlate.localScale = new Vector3(scale, scale, 1f);
        _labelOpacity.alpha = IsHidden ? 0.35f : 1f;
        UpdateLabelName();
        UpdateLabelConditions(label, scale);
    }

    private void OnDisable() => HideTokenLabel();

    private void HideTokenLabel()
    {
        if (_labelPlate != null) _labelPlate.gameObject.SetActive(false);
        if (_conditionTooltip != null) _conditionTooltip.gameObject.SetActive(false);
    }

    private bool TryGetTokenLabelRect(out Rect label)
    {
        label = default;
        var camera = Camera.main;
        if (camera == null || !TokenLabelLayout.Project(camera, DisplayTransform, _labelBounds, Screen.height, out label))
            return false;
        if (_labelGrid == null) _labelGrid = FindAnyObjectByType<GridManager>();
        if (_labelGrid != null)
        {
            Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < 4; i++)
            {
                Vector3 point = camera.WorldToScreenPoint(_labelGrid.GetTokenFootprintCorner(DisplayPosition, i));
                if (point.z <= 0) return false;
                Vector2 screen = new(point.x, Screen.height - point.y);
                min = Vector2.Min(min, screen);
                max = Vector2.Max(max, screen);
            }
            label = TokenLabelLayout.PlaceInsideCell(label, Rect.MinMaxRect(min.x, min.y, max.x, max.y));
        }
        return label.Overlaps(new Rect(0, 0, Screen.width, Screen.height));
    }

    private void CreateTokenLabel()
    {
        if (_tokenLabelCanvas == null)
        {
            var root = new GameObject("Board Token Labels", typeof(RectTransform), typeof(Canvas));
            _tokenLabelCanvas = root.GetComponent<Canvas>();
            _tokenLabelCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Board labels go below the scene canvas and every screen-space panel.
            _tokenLabelCanvas.sortingOrder = -10;
        }
        _labelPlate = AtlasUi.Panel(_tokenLabelCanvas.transform, "Token Label", VttUiSkin.Panel);
        _labelPlate.GetComponent<Image>().raycastTarget = false;
        AtlasUi.Place(_labelPlate, 0, 0, TokenLabelLayout.DesignWidth, TokenLabelLayout.DesignHeight);
        _labelOpacity = _labelPlate.gameObject.AddComponent<CanvasGroup>();
        _labelName = AtlasUi.Text(_labelPlate, "", TokenLabelLayout.DesignFontSize);
        AtlasUi.Fill(_labelName.rectTransform, 4, 0, 4, 0);
        _labelName.alignment = TextAnchor.MiddleCenter;
        _labelName.horizontalOverflow = HorizontalWrapMode.Overflow;
        _labelName.verticalOverflow = VerticalWrapMode.Overflow;
        _labelIcons = new RawImage[MaxLabelIcons];
        _lastLabelName = null;
    }

    private void UpdateLabelName()
    {
        string name = TokenName;
        if (_lastLabelName == name) return;
        _lastLabelName = name;
        _labelName.text = name;
        int length = name.Length;
        while (length > 0 && _labelName.preferredWidth > TokenLabelLayout.DesignWidth - 8)
        {
            length -= length >= 2 && char.IsSurrogatePair(name, length - 2) ? 2 : 1;
            _labelName.text = name.Substring(0, length).TrimEnd() + "…";
        }
    }

    private void UpdateLabelConditions(Rect label, float scale)
    {
        string[] conditions = VisibleConditionIds;
        int count = Mathf.Min(MaxLabelIcons, conditions.Length);
        Vector2? mouse = Mouse.current == null || GameplayInputGate.IsPointerOverUI
            ? null : Mouse.current.position.ReadValue();
        Vector2 local = mouse.HasValue
            ? (new Vector2(mouse.Value.x, Screen.height - mouse.Value.y) - label.position) / scale
            : new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        string hovered = null;
        for (int i = 0; i < _labelIcons.Length; i++)
        {
            if (i >= count)
            {
                if (_labelIcons[i] != null) _labelIcons[i].enabled = false;
                continue;
            }
            var bounds = new Rect(i * 20, -22, 18, 18);
            if (_labelIcons[i] == null)
            {
                var node = AtlasUi.Node(_labelPlate, "Condition Icon");
                AtlasUi.Place(node, bounds.x, bounds.y, bounds.width, bounds.height);
                _labelIcons[i] = node.gameObject.AddComponent<RawImage>();
                _labelIcons[i].raycastTarget = false;
            }
            _labelIcons[i].texture = TokenConditionIcons.GetTexture(conditions[i]);
            _labelIcons[i].enabled = true;
            if (bounds.Contains(local)) hovered = TokenConditionCatalog.DisplayName(conditions[i]);
        }
        if (conditions.Length > MaxLabelIcons)
        {
            var bounds = new Rect(140, -22, 26, 20);
            if (_labelOverflow == null)
            {
                _labelOverflow = AtlasUi.Text(_labelPlate, "", 14);
                _labelOverflow.alignment = TextAnchor.MiddleCenter;
                AtlasUi.Place(_labelOverflow.rectTransform, bounds.x, bounds.y, bounds.width, bounds.height);
            }
            _labelOverflow.enabled = true;
            _labelOverflow.text = "+" + (conditions.Length - MaxLabelIcons);
            if (bounds.Contains(local))
                hovered = string.Join(", ", Array.ConvertAll(conditions, TokenConditionCatalog.DisplayName));
        }
        else if (_labelOverflow != null) _labelOverflow.enabled = false;

        if (hovered != null && mouse.HasValue) ShowConditionTooltip(hovered, mouse.Value);
        else if (_conditionTooltip != null) _conditionTooltip.gameObject.SetActive(false);
    }

    private void ShowConditionTooltip(string caption, Vector2 mouse)
    {
        if (_conditionTooltip == null)
        {
            _conditionTooltip = AtlasUi.Panel(_tokenLabelCanvas.transform, "Token Condition Tooltip", VttUiSkin.Panel);
            _conditionTooltip.GetComponent<Image>().raycastTarget = false;
            _conditionText = AtlasUi.Text(_conditionTooltip, "", 14);
            _conditionText.verticalOverflow = VerticalWrapMode.Overflow;
            AtlasUi.Fill(_conditionText.rectTransform, 8, 8, 8, 8);
        }
        if (!_conditionTooltip.gameObject.activeSelf)
            _conditionTooltip.gameObject.SetActive(true);
        _conditionTooltip.SetAsLastSibling();
        _conditionText.text = caption;
        float width = Mathf.Min(280, Screen.width - 8, _conditionText.preferredWidth + 16);
        _conditionTooltip.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        float height = _conditionText.preferredHeight + 16;
        float x = mouse.x + 18;
        if (x + width > Screen.width - 4) x = mouse.x - width - 18;
        AtlasUi.Place(_conditionTooltip,
            Mathf.Clamp(x, 4, Mathf.Max(4, Screen.width - width - 4)),
            Mathf.Clamp(Screen.height - mouse.y + 18, 4, Mathf.Max(4, Screen.height - height - 4)),
            width, height);
    }
}
