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

    private void OnGUI()
    {
        if (MovementReviewUI.IsOpen) return;
        var matrix = GUI.matrix; int depth = GUI.depth;
        GUI.depth = -1000;
        float scale = Mathf.Min(LocalUiScale.Value, Mathf.Min(Screen.width / 600f, Screen.height / 460f));
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float width = Screen.width / scale, height = Screen.height / scale;
        if (!IsOpen)
        {
            if (GUI.Button(new Rect(width - 144, height - 40, 132, 28), "Настройки", VttUiSkin.ImGuiButton))
            { SelectCurrent(); SetOpen(true); }
        }
        else
        {
            GUI.Box(new Rect(0, 0, width, height), "", VttUiSkin.ImGuiPanel);
            GUILayout.BeginArea(new Rect((width - 560) / 2, (height - 420) / 2, 560, 420), VttUiSkin.ImGuiPanel);
            GUILayout.Space(10);
            GUILayout.Label("Настройки", new GUIStyle(GUI.skin.label) { fontSize = 20 });
            bool master = Unity.Netcode.NetworkManager.Singleton?.IsHost == true;
            if (!master && _section == 2) _section = 0;
            GUI.enabled = !_pending;
            int section = GUILayout.Toolbar(_section, master ? new[] { "Экран", "Управление", "Кампания" } : new[] { "Экран", "Управление" }, VttUiSkin.ImGuiChoice, GUILayout.Height(34));
            if (section != _section) { _section = section; _scroll = Vector2.zero; }
            GUI.enabled = true;
            _scroll = GUILayout.BeginScrollView(_scroll);
            var wrap = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false, fontSize = 14 };
            if (_pending)
            {
                GUILayout.Label("Сохранить изменения экрана? Возврат через " + Mathf.CeilToInt(Mathf.Max(0, _confirmationDeadline - Time.realtimeSinceStartup)) + " с.", wrap);
                if (GUILayout.Button("Сохранить", VttUiSkin.ImGuiButton, GUILayout.Height(38))) Confirm();
                if (GUILayout.Button("Вернуть прежние · Esc", VttUiSkin.ImGuiButton, GUILayout.Height(38))) Revert();
            }
            else if (_section == 0)
            {
                GUILayout.Label("Личные настройки — применяются только на этом компьютере.", wrap);
                GUILayout.Label("Минимальное окно: 960 × 640. Масштаб панелей подстраивается под доступное место.", wrap);
                GUILayout.Space(12);
                if (_resolutions.Count > 0)
                {
                    var size = _resolutions[_resolutionIndex];
                    StepRow("Разрешение", size.x + " × " + size.y,
                        () => _resolutionIndex = (_resolutionIndex + _resolutions.Count - 1) % _resolutions.Count,
                        () => _resolutionIndex = (_resolutionIndex + 1) % _resolutions.Count);
                }
                StepRow("Режим окна", _modeNames[_modeIndex],
                    () => _modeIndex = (_modeIndex + _modes.Length - 1) % _modes.Length,
                    () => _modeIndex = (_modeIndex + 1) % _modes.Length);
#if UNITY_EDITOR
                GUILayout.Label("Разрешение применяется в собранной игре.", wrap);
#else
                if (GUILayout.Button("Применить экран…", VttUiSkin.ImGuiButton, GUILayout.Height(34))) Apply();
#endif
                GUILayout.Space(14);
                GUILayout.Label("Масштаб интерфейса: " + Mathf.RoundToInt(LocalUiScale.Value * 100) + "%", wrap);
                float next = GUILayout.HorizontalSlider(LocalUiScale.Value, 0.75f, 1.5f, GUILayout.Height(24));
                if (Mathf.Abs(next - LocalUiScale.Value) > 0.005f) _uiScale.SetScale(Mathf.Round(next * 100) / 100f);
                if (GUILayout.Button("Вернуть масштаб 100%", VttUiSkin.ImGuiButton, GUILayout.Height(30))) _uiScale.SetScale(1);
            }
            else if (_section == 1)
            {
                GUILayout.Label("Скорость камеры: " + Mathf.RoundToInt(LocalControls.CameraSpeed * 100) + "%", wrap);
                float speed = GUILayout.HorizontalSlider(LocalControls.CameraSpeed, 0.5f, 2f, GUILayout.Height(24));
                if (Mathf.Abs(speed - LocalControls.CameraSpeed) > 0.01f) LocalControls.SetCameraSpeed(speed);
                bool hover = GUILayout.Toggle(LocalControls.TokenHover, " Показывать краткие сведения при наведении", GUILayout.Height(32));
                if (hover != LocalControls.TokenHover) LocalControls.SetTokenHover(hover);
                GUILayout.Label("ПКМ по токену — действия и подробности.\nПеретаскивание в режиме «По клеткам» — один шаг.\nWASD / стрелки — камера · колёсико — приближение.\nF1 — свободная камера · T — колесо приколов.\nЛКМ — выбранный инструмент · Esc / ПКМ — закончить.\nEsc во время штриха — отменить текущий штрих.", wrap);
                if (GUILayout.Button("Сбросить личные настройки управления", VttUiSkin.ImGuiButton, GUILayout.Height(32)))
                { LocalControls.SetCameraSpeed(1); LocalControls.SetTokenHover(true); }
            }
            else
            {
                GUILayout.Label("Правила мастера сохраняются в файле кампании.", wrap);
                GUILayout.Space(8);
                GUILayout.Label("Перемещение игроков", wrap);
                int policy = GUILayout.SelectionGrid((int)SceneFileStore.MovementPolicy,
                    new[] { "Свободное", "Предупреждать", "Решает мастер", "По клеткам (по умолчанию)" }, 1, VttUiSkin.ImGuiChoice, GUILayout.Height(128));
                if (policy != (int)SceneFileStore.MovementPolicy) SceneFileStore.SetMovementPolicy((PlayerMovementPolicy)policy);
                GUILayout.Label("По клеткам: одно перетаскивание — один шаг вверх, вниз, влево или вправо. Стены, закрытые двери и занятые клетки блокируют шаг. Мастер переносит без ограничений. В режимах подтверждения ожидание — до 30 секунд.", wrap);
                var fog = FogManager.Instance;
                if (fog != null)
                {
                    bool autosave = GUILayout.Toggle(fog.Autosave, " Автосохранение каждые 2 минуты", GUILayout.Height(30));
                    bool history = GUILayout.Toggle(fog.SaveWithHistory, " Включать исследованные области в экспорт", GUILayout.Height(30));
                    if (autosave != fog.Autosave || history != fog.SaveWithHistory) SceneFileStore.SetCampaignPreferences(autosave, history);
                }
            }
            GUILayout.EndScrollView();
            if (!_pending && GUILayout.Button("Готово · Esc", VttUiSkin.ImGuiButton, GUILayout.Height(36))) SetOpen(false);
            GUILayout.EndArea();
        }
        GUI.matrix = matrix; GUI.depth = depth;
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
