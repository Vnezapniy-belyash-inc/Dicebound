using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Local display preferences, available in the lobby and during play.</summary>
public sealed partial class DisplaySettingsUI : MonoBehaviour
{
    private const string WidthKey = "Display.Width", HeightKey = "Display.Height", ModeKey = "Display.Mode";
    public static bool IsOpen { get; private set; }
    public static bool IsPointerOverButton
    {
        get
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return false;
            float scale = Mathf.Min(LocalUiScale.Value, Mathf.Min(Screen.width / 600f, Screen.height / 460f));
            return new Rect(Screen.width - 144 * scale, 12 * scale, 132 * scale, 28 * scale).Contains(mouse.position.ReadValue());
        }
    }
    private readonly List<Vector2Int> _resolutions = new();
    private readonly FullScreenMode[] _modes = { FullScreenMode.Windowed, FullScreenMode.FullScreenWindow, FullScreenMode.ExclusiveFullScreen };
    private readonly string[] _modeNames = { "Оконный", "Полноэкранный без рамки", "Полноэкранный" };
    private int _resolutionIndex, _modeIndex;
    private int _previousWidth, _previousHeight;
    private FullScreenMode _previousMode;
    private float _confirmationDeadline;
    private bool _pending;
    private int _section;
    private Vector2 _scroll;
    private float _nextWindowCheck;
    public static DisplaySettingsUI Instance { get; private set; }
    public static void OpenCampaign() { if (Instance != null) { Instance._section = 2; Instance.SetOpen(true); } }
    public static void OpenAtlas() { if(Instance!=null){Instance.SelectCurrent();Instance.SetOpen(true);} }
    private LocalUiScale _uiScale;
    private GameObject _inputBlocker;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() { IsOpen = false; Instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var root = new GameObject("DisplaySettings");
        DontDestroyOnLoad(root);
        root.AddComponent<DisplaySettingsUI>();
    }

    private void Awake()
    {
        Instance = this;
        _uiScale = gameObject.AddComponent<LocalUiScale>();
        _inputBlocker = new GameObject("SettingsInputBlocker", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        _inputBlocker.transform.SetParent(transform, false);
        var canvas = _inputBlocker.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32760;
        var overlay = new GameObject("Modal", typeof(RectTransform), typeof(Image));
        overlay.transform.SetParent(_inputBlocker.transform, false);
        var rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        overlay.GetComponent<Image>().color = Color.clear;
        _inputBlocker.SetActive(false);
        foreach (var resolution in Screen.resolutions) AddResolution(resolution.width, resolution.height);
        AddResolution(Screen.width, Screen.height);
        _resolutions.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        SelectCurrent();
#if !UNITY_EDITOR
        int width = PlayerPrefs.GetInt(WidthKey, Screen.width);
        int height = PlayerPrefs.GetInt(HeightKey, Screen.height);
        var mode = (FullScreenMode)PlayerPrefs.GetInt(ModeKey, (int)Screen.fullScreenMode);
        if (width >= 640 && height >= 480 && width <= 16384 && height <= 16384 && Array.IndexOf(_modes, mode) >= 0)
        {
            AddResolution(width, height);
            Screen.SetResolution(width, height, mode);
            _resolutionIndex = _resolutions.IndexOf(new Vector2Int(width, height));
            _modeIndex = Array.IndexOf(_modes, mode);
        }
#endif
    }

    private void AddResolution(int width, int height)
    {
        var size = new Vector2Int(width, height);
        if (width >= 640 && height >= 480 && !_resolutions.Contains(size)) _resolutions.Add(size);
    }

    private void SelectCurrent()
    {
        _resolutionIndex = Math.Max(0, _resolutions.IndexOf(new Vector2Int(Screen.width, Screen.height)));
        _modeIndex = Math.Max(0, Array.IndexOf(_modes, Screen.fullScreenMode));
    }

    private void Update()
    {
        UpdateAtlasSettings();
#if !UNITY_EDITOR
        // Keep controls readable in physically small resizable windows.
        if (Time.unscaledTime >= _nextWindowCheck && Screen.fullScreenMode == FullScreenMode.Windowed
            && (Screen.width < 960 || Screen.height < 640))
        {
            _nextWindowCheck = Time.unscaledTime + 1;
            Screen.SetResolution(Mathf.Max(960, Screen.width), Mathf.Max(640, Screen.height), FullScreenMode.Windowed);
        }
#endif
        if (_pending && Time.realtimeSinceStartup >= _confirmationDeadline) Revert();
        if (IsOpen && UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true)
        {
            if (_pending) Revert();
            else SetOpen(false);
            GameplayInputGate.MarkToolExit();
        }
    }

    private void SetOpen(bool open)
    {
        if (IsOpen == open) return;
        IsOpen = open;
        GameplayInputGate.MarkToolExit();
        if (open)
        {
            SceneEditor.Instance?.Deactivate(); FogManager.Instance?.StopManual();
            MeasurementTool.Instance?.Deactivate(); EffectPaintTool.Instance?.Deactivate();
        }
        _inputBlocker.SetActive(open);
    }

    private void Apply()
    {
        if (_resolutions.Count == 0) return;
        _previousWidth = Screen.width;
        _previousHeight = Screen.height;
        _previousMode = Screen.fullScreenMode;
        var size = _resolutions[_resolutionIndex];
        if (_modes[_modeIndex] == FullScreenMode.Windowed)
            size = new Vector2Int(Mathf.Max(960, size.x), Mathf.Max(640, size.y));
        Screen.SetResolution(size.x, size.y, _modes[_modeIndex]);
        _pending = true;
        _confirmationDeadline = Time.realtimeSinceStartup + 15f;
    }

    private void Confirm()
    {
        // Unity applies resolution asynchronously; persist the actual confirmed display state.
        PlayerPrefs.SetInt(WidthKey, Screen.width);
        PlayerPrefs.SetInt(HeightKey, Screen.height);
        PlayerPrefs.SetInt(ModeKey, (int)Screen.fullScreenMode);
        PlayerPrefs.Save();
        _pending = false;
        AddResolution(Screen.width, Screen.height);
        SelectCurrent();
    }

    private void Revert()
    {
        Screen.SetResolution(_previousWidth, _previousHeight, _previousMode);
        _pending = false;
        _resolutionIndex = Math.Max(0, _resolutions.IndexOf(new Vector2Int(_previousWidth, _previousHeight)));
        _modeIndex = Math.Max(0, Array.IndexOf(_modes, _previousMode));
    }

    private void OnDestroy()
    {
        if (_pending) Revert();
        IsOpen = false;
    }



    private static void StepRow(string label, string value, Action previous, Action next)
    {
        GUILayout.Label(label);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(new GUIContent("‹", "Предыдущее значение"), VttUiSkin.ImGuiButton, GUILayout.Width(36), GUILayout.Height(30))) previous();
        GUILayout.Label(value, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter }, GUILayout.Height(30));
        if (GUILayout.Button(new GUIContent("›", "Следующее значение"), VttUiSkin.ImGuiButton, GUILayout.Width(36), GUILayout.Height(30))) next();
        GUILayout.EndHorizontal();
    }
}
