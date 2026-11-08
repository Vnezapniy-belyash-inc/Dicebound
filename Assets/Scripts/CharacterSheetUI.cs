using UnityEngine;
using UnityEngine.UI;

public sealed class CharacterSheetUI : MonoBehaviour
{
    public static CharacterSheetUI Instance { get; private set; }
    public bool IsOpen => panel != null && panel.activeInHierarchy;

    private GameObject panel;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    private LssWebViewLauncher browser;
    internal bool HasPinnedKeyboardFocus => browser != null && browser.HasPinnedKeyboardFocus;
#endif

    private static bool IsSessionActive =>
        GameNetworkManager.Instance != null && GameNetworkManager.Instance.IsConnected;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        var root = new GameObject("Character Sheet", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 25;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        scaler.matchWidthOrHeight = .5f;
        panel = LssWebViewLauncher.Create(root.transform);
        browser = panel.GetComponentInChildren<LssWebViewLauncher>(true);
#endif
    }

    private void Update()
    {
        if (IsOpen && !IsSessionActive)
            SetSessionActive(false);
    }

    public void SetSessionActive(bool active)
    {
        if (!active)
            panel?.SetActive(false);
    }

    public void ToggleSheet()
    {
        if (IsOpen)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (browser != null && browser.RecenterIfControlsAreHidden())
                return;
#endif
            panel.SetActive(false);
            return;
        }

        SetSheetVisible(true);
    }

    public void SetSheetVisible(bool show)
    {
        if (panel == null || (show && !IsSessionActive))
            return;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (!show && browser != null && browser.IsPinned)
            return;
#endif
        if (show)
        {
            DiceUI.Instance?.AtlasCloseContext();
            DmPanelUI.Instance?.Hide();
        }

        panel.SetActive(show);
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        if (show)
            browser?.RecenterIfControlsAreHidden();
#endif
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
