using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Only the generic decision reaches the owner; geometry stays on the server.</summary>
public sealed partial class MovementReviewUI : MonoBehaviour
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
        UpdateAtlasMovement();
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

}
