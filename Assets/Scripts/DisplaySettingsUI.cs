using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Local display preferences, available in the lobby and during play.</summary>
public sealed class DisplaySettingsUI : MonoBehaviour
{
    private const string WidthKey = "Display.Width", HeightKey = "Display.Height", ModeKey = "Display.Mode";
    public static bool IsOpen { get; private set; }
    public static bool IsPointerOverButton
    {
        get
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return false;
            float scale = Mathf.Min(LocalUiScale.Value, Mathf.Min(Screen.width / 520f, Screen.height / 380f));
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
    private LocalUiScale _uiScale;
    private GameObject _inputBlocker;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() => IsOpen = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var root = new GameObject("DisplaySettings");
        DontDestroyOnLoad(root);
        root.AddComponent<DisplaySettingsUI>();
    }

    private void Awake()
    {
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
        if (_pending && Time.realtimeSinceStartup >= _confirmationDeadline) Revert();
    }

    private void SetOpen(bool open)
    {
        IsOpen = open;
        _inputBlocker.SetActive(open);
    }

    private void Apply()
    {
        if (_resolutions.Count == 0) return;
        _previousWidth = Screen.width;
        _previousHeight = Screen.height;
        _previousMode = Screen.fullScreenMode;
        var size = _resolutions[_resolutionIndex];
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

    private void OnGUI()
    {
        var previousMatrix = GUI.matrix;
        int previousDepth = GUI.depth;
        GUI.depth = -1000;
        float scale = Mathf.Min(LocalUiScale.Value, Mathf.Min(Screen.width / 520f, Screen.height / 380f));
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float width = Screen.width / scale, height = Screen.height / scale;
        if (!IsOpen)
        {
            if (GUI.Button(new Rect(width - 144, height - 40, 132, 28), "Настройки", VttUiSkin.ImGuiButton))
            {
                SelectCurrent();
                SetOpen(true);
            }
        }
        else
        {
            // A full-screen modal also consumes pointer events outside the settings window.
            GUI.Box(new Rect(0, 0, width, height), "", VttUiSkin.ImGuiPanel);
            var panel = new Rect((width - 480) / 2, (height - 310) / 2, 480, 310);
            GUI.BeginGroup(panel, VttUiSkin.ImGuiPanel);
            GUI.Label(new Rect(20, 16, 420, 28), "Настройки экрана");
            if (_pending)
            {
                GUI.Label(new Rect(20, 64, 440, 50), "Сохранить изменения? Возврат через " + Mathf.CeilToInt(_confirmationDeadline - Time.realtimeSinceStartup) + " с.");
                if (GUI.Button(new Rect(20, 136, 210, 36), "Сохранить", VttUiSkin.ImGuiButton)) Confirm();
                if (GUI.Button(new Rect(250, 136, 210, 36), "Вернуть", VttUiSkin.ImGuiButton)) Revert();
            }
            else
            {
                GUI.Label(new Rect(20, 56, 140, 28), "Разрешение");
                if (_resolutions.Count > 0)
                {
                    var size = _resolutions[_resolutionIndex];
                    GUI.Label(new Rect(195, 56, 190, 28), size.x + " × " + size.y);
                    if (GUI.Button(new Rect(155, 54, 30, 28), "‹")) _resolutionIndex = (_resolutionIndex + _resolutions.Count - 1) % _resolutions.Count;
                    if (GUI.Button(new Rect(420, 54, 30, 28), "›")) _resolutionIndex = (_resolutionIndex + 1) % _resolutions.Count;
                }
                GUI.Label(new Rect(20, 106, 130, 28), "Режим окна");
                GUI.Label(new Rect(195, 106, 225, 28), _modeNames[_modeIndex]);
                if (GUI.Button(new Rect(155, 104, 30, 28), "‹")) _modeIndex = (_modeIndex + _modes.Length - 1) % _modes.Length;
                if (GUI.Button(new Rect(420, 104, 30, 28), "›")) _modeIndex = (_modeIndex + 1) % _modes.Length;
#if UNITY_EDITOR
                GUI.Label(new Rect(20, 152, 440, 28), "Экран изменяется в собранной игре.");
#else
                if (GUI.Button(new Rect(20, 150, 440, 32), "Применить", VttUiSkin.ImGuiButton)) Apply();
#endif
                GUI.Label(new Rect(20, 192, 440, 24), "Масштаб интерфейса: " + Mathf.RoundToInt(LocalUiScale.Value * 100) + "%");
                float nextScale = GUI.HorizontalSlider(new Rect(20, 223, 360, 20), LocalUiScale.Value, 0.75f, 1.5f);
                if (Mathf.Abs(nextScale - LocalUiScale.Value) > 0.005f) _uiScale.SetScale(Mathf.Round(nextScale * 100f) / 100f);
                if (GUI.Button(new Rect(395, 214, 65, 28), "100%")) _uiScale.SetScale(1f);
                if (GUI.Button(new Rect(20, 250, 440, 36), "Закрыть", VttUiSkin.ImGuiButton)) SetOpen(false);
            }
            GUI.EndGroup();
        }
        GUI.matrix = previousMatrix;
        GUI.depth = previousDepth;
    }
}
