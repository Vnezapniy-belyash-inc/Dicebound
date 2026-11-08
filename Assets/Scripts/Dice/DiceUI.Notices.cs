using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public enum TableNoticeKind { Info, Success, Warning, Error, Roll }

public partial class DiceUI
{
    private const float TableNoticeSeconds = 6f;
    private RectTransform _nativeNoticePanel;
    private Text _nativeNotice, _nativeNoticeGlyph, _nativeToolHint;
    private Image _nativeNoticeBadge, _nativeNoticeDie;
    private string _toolNoticeText;
    private float _toolNoticeUntil;
    private TableNoticeKind _toolNoticeKind;
    private float _lastRollNoticeAt;
    private ulong _lastRollNoticePlayer;
    private string _rollNoticeResults;
    private int _rollNoticeCount;

    public void ShowToolNotice(string message, TableNoticeKind kind = TableNoticeKind.Info)
    {
        _toolNoticeText = message;
        _toolNoticeKind = kind;
        _toolNoticeUntil = Time.unscaledTime + TableNoticeSeconds;
        if (kind != TableNoticeKind.Roll) _rollNoticeCount = 0;
        if (_nativeNoticePanel == null) return;
        UpdateNoticeContent();
        RefreshTableNotice();
    }

    private void ShowRollNotice(string who, string result, ulong throwerId)
    {
        float now = Time.unscaledTime;
        if (_rollNoticeCount == 0 || throwerId != _lastRollNoticePlayer || now - _lastRollNoticeAt > .6f)
        {
            _rollNoticeResults = result;
            _rollNoticeCount = 1;
        }
        else if (++_rollNoticeCount <= 5)
            _rollNoticeResults += " · " + result;

        _lastRollNoticePlayer = throwerId;
        _lastRollNoticeAt = now;
        string roller = NetworkManager.Singleton?.LocalClientId == throwerId ? "Вы" : who;
        string more = _rollNoticeCount > 5 ? $" · ещё {_rollNoticeCount - 5}" : "";
        ShowToolNotice($"{roller} · {_rollNoticeResults}{more}", TableNoticeKind.Roll);
    }

    private void BuildTableNotice()
    {
        _nativeToolHint = AtlasUi.Text(_nativeRoot, "Активен инструмент · Esc — завершить", 13, AtlasUi.Muted);
        _nativeToolHint.gameObject.SetActive(false);
        _nativeNoticePanel = AtlasUi.Panel(_nativeRoot, "TableNotice", AtlasUi.Paper);
        _nativeNoticePanel.GetComponent<Image>().raycastTarget = false;
        _nativeNotice = AtlasUi.Text(_nativeNoticePanel, "", 16, AtlasUi.Ink);
        AtlasUi.Fill(_nativeNotice.rectTransform, 56, 7, 13, 7);

        var badge = AtlasUi.Panel(_nativeNoticePanel, "NoticeBadge", AtlasUi.Forest);
        _nativeNoticeBadge = badge.GetComponent<Image>();
        badge.anchorMin = badge.anchorMax = new Vector2(0f, .5f);
        badge.pivot = new Vector2(0f, .5f);
        badge.anchoredPosition = new Vector2(12, 0);
        badge.sizeDelta = new Vector2(32, 32);
        _nativeNoticeGlyph = AtlasUi.Text(badge, "", 22, Color.white);
        _nativeNoticeGlyph.alignment = TextAnchor.MiddleCenter;
        _nativeNoticeGlyph.fontStyle = FontStyle.Bold;
        _nativeNoticeGlyph.verticalOverflow = VerticalWrapMode.Overflow;
        AtlasUi.Fill(_nativeNoticeGlyph.rectTransform);
        _nativeNoticeDie = AtlasUi.Node(badge, "DiceIcon").gameObject.AddComponent<Image>();
        _nativeNoticeDie.sprite = AtlasUi.ReferenceIcon("dice");
        _nativeNoticeDie.preserveAspect = true;
        _nativeNoticeDie.raycastTarget = false;
        _nativeNoticeDie.color = Color.white;
        AtlasUi.Fill(_nativeNoticeDie.rectTransform, 5, 5, 5, 5);
        UpdateNoticeContent();
        _nativeNoticePanel.gameObject.SetActive(false);
    }

    private void LayoutTableNotice(float left)
    {
        var anchor = new Vector2(0, 0);
        _nativeNoticePanel.anchorMin = _nativeNoticePanel.anchorMax = _nativeNoticePanel.pivot = anchor;
        float width = Mathf.Clamp(_nativeRoot.rect.width - left - 36, 1, 446);
        _nativeNoticePanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        _nativeNoticePanel.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
            Mathf.Clamp(_nativeNotice.preferredHeight + 18, 54, 106));
        _nativeNoticePanel.anchoredPosition = new Vector2(left + 18, _nativeHost ? 74 : 16);
        var hint = _nativeToolHint.rectTransform;
        hint.anchorMin = hint.anchorMax = hint.pivot = anchor;
        hint.anchoredPosition = _nativeNoticePanel.anchoredPosition;
        hint.sizeDelta = new Vector2(width, 28);
    }

    private void UpdateNoticeContent()
    {
        _nativeNotice.text = _toolNoticeText ?? "";
        var (color, glyph) = _toolNoticeKind switch
        {
            TableNoticeKind.Error => (new Color(.67f, .17f, .16f), "×"),
            TableNoticeKind.Warning => (new Color(.62f, .37f, .13f), "!"),
            TableNoticeKind.Success => (new Color(.20f, .42f, .27f), "✓"),
            TableNoticeKind.Roll => (AtlasUi.Copper, "◆"),
            _ => (AtlasUi.Forest, "i")
        };
        VttUiSkin.Surface(_nativeNoticeBadge, color, 30, false);
        _nativeNoticeBadge.raycastTarget = false;
        bool showDie = _toolNoticeKind == TableNoticeKind.Roll && _nativeNoticeDie.sprite != null;
        _nativeNoticeDie.enabled = showDie;
        _nativeNoticeGlyph.text = glyph;
        _nativeNoticeGlyph.enabled = !showDie;
    }

    private void RefreshTableNotice()
    {
        bool allowed = GameplayInputGate.AllowsImGuiOverlays;
        bool show = allowed && Time.unscaledTime < _toolNoticeUntil && !string.IsNullOrEmpty(_toolNoticeText);
        _nativeNoticePanel.gameObject.SetActive(show);
        _nativeToolHint.gameObject.SetActive(allowed && !show && LocalControls.TokenHover && _nativeTool != 0);
    }
}
