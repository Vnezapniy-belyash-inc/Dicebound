using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Only the generic decision reaches the owner; geometry stays on the server.</summary>
public sealed class MovementReviewUI : MonoBehaviour
{
    private static TokenController _token;
    private static MovementReviewUI _instance;
    private static int _request;
    private static bool _approval, _hostOpen;
    private static float _deadline;
    private Vector2 _scroll;
    private TokenController[] _pending = System.Array.Empty<TokenController>();
    private float _nextRefresh;
    private GameObject _blocker;
    public static bool IsOpen => _token != null || _hostOpen;
    public static bool IsPointerOverButton => false; // The session header now owns a normal raycastable button.
    public static int PendingCount => _instance == null ? 0 : _instance._pending.Length;
    public static void OpenRequests() { if (NetworkManager.Singleton?.IsHost == true && PendingCount > 0) _hostOpen = true; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() { _token = null; _hostOpen = false; _instance = null; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap() => new GameObject("MovementReviewUI").AddComponent<MovementReviewUI>();
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        _instance = this;
        _blocker = new GameObject("MovementInputBlocker", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        _blocker.transform.SetParent(transform, false);
        var canvas = _blocker.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32761;
        var overlay = new GameObject("Modal", typeof(RectTransform), typeof(Image)); overlay.transform.SetParent(_blocker.transform, false);
        var rect = overlay.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        overlay.GetComponent<Image>().color = Color.clear; _blocker.SetActive(false);
    }

    public static void Show(TokenController token, int request, bool approval)
    {
        if (_token != null && _token.IsSpawned) _token.AnswerMovement(_request, false);
        _token = token; _request = request; _approval = approval; _deadline = Time.unscaledTime + 30;
    }
    public static void Complete(TokenController token, int request)
    { if (_token == token && _request == request) _token = null; }

    private void Update()
    {
        _blocker.SetActive(IsOpen);
        if (NetworkManager.Singleton?.IsListening != true) { _token = null; _hostOpen = false; _blocker.SetActive(false); return; }
        if (_token != null && (!_token.IsSpawned || Time.unscaledTime >= _deadline || !Application.isFocused)) Cancel();
        if (IsOpen && Keyboard.current?.escapeKey.wasPressedThisFrame == true) { Cancel(); _hostOpen = false; GameplayInputGate.MarkToolExit(); }
        if (Time.unscaledTime < _nextRefresh) return;
        _nextRefresh = Time.unscaledTime + 0.25f;
        _pending = NetworkManager.Singleton.IsHost
            ? System.Array.FindAll(FindObjectsByType<TokenController>(FindObjectsInactive.Exclude), t => t.HasMovementRequest)
            : System.Array.Empty<TokenController>();
    }
    private static void Cancel()
    {
        if (_token != null && _token.IsSpawned) _token.AnswerMovement(_request, false);
        _token = null;
    }
    private void OnGUI()
    {
        if (NetworkManager.Singleton?.IsListening != true || HostSceneCurtain.IsBlockingLocalPlayer) return;
        var matrix = GUI.matrix; int depth = GUI.depth; GUI.depth = -1100;
        if (!IsOpen) { GUI.depth = depth; return; }
        float scale = Mathf.Min(LocalUiScale.Value, Mathf.Min(Screen.width / 540f, Screen.height / 340f));
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        float w = Screen.width / scale, h = Screen.height / scale;
        GUI.Box(new Rect(0, 0, w, h), "", VttUiSkin.ImGuiPanel);
        GUILayout.BeginArea(new Rect((w - 500) / 2, (h - 300) / 2, 500, 300), VttUiSkin.ImGuiPanel);
        var wrap = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false, fontSize = 16 };
        GUILayout.Space(12);
        if (_token != null)
        {
            GUILayout.Label(_approval ? "Ожидание решения мастера" : "Проверить перемещение", wrap);
            GUILayout.Label("Выбранный путь требует внимания. До подтверждения токен остаётся на исходной клетке, карта не раскрывается.", wrap);
            GUILayout.Label("Осталось: " + Mathf.CeilToInt(Mathf.Max(0, _deadline - Time.unscaledTime)) + " с.", wrap);
            GUILayout.FlexibleSpace();
            if (!_approval && GUILayout.Button("Подтвердить перемещение", VttUiSkin.ImGuiButton, GUILayout.Height(38)))
            { _token.AnswerMovement(_request, true); _token = null; }
            if (GUILayout.Button("Отменить · Esc", VttUiSkin.ImGuiButton, GUILayout.Height(38))) Cancel();
        }
        else
        {
            GUILayout.Label("Запросы игроков · решение действует один раз", wrap);
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var token in _pending)
            {
                if (token == null || !token.HasMovementRequest) continue;
                var grid = FindAnyObjectByType<GridManager>();
                var cell = grid == null ? Vector2Int.zero : grid.GetGridPosition(token.RequestedMovementPosition);
                GUILayout.Label(token.TokenName + " → клетка " + (cell.x + 1) + ", " + (cell.y + 1) + " · " + Mathf.CeilToInt(token.MovementSecondsLeft) + " с.", wrap);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Разрешить", GUILayout.Height(32))) token.ResolveMovementAsMaster(true);
                if (GUILayout.Button("Отклонить", GUILayout.Height(32))) token.ResolveMovementAsMaster(false);
                GUILayout.EndHorizontal();
            }
            if (_pending.Length == 0) GUILayout.Label("Нет ожидающих запросов.", wrap);
            GUILayout.EndScrollView();
            if (GUILayout.Button("Закрыть · Esc", VttUiSkin.ImGuiButton, GUILayout.Height(36))) _hostOpen = false;
        }
        GUILayout.EndArea(); GUI.matrix = matrix; GUI.depth = depth;
    }
}
