using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Host-only scene curtain: the GM panel hides the table from all clients while the host prepares.
/// Clients see an opaque full-screen panel and cannot interact with or view the scene.
/// Sync uses CustomMessagingManager (no runtime NetworkObject spawn).
/// </summary>
public class HostSceneCurtain : MonoBehaviour
{
    private const string MSG_STATE = "HostSceneCurtainState";

    public static HostSceneCurtain Instance { get; private set; }

    private bool _curtainDown;
    private bool _syncPending;
    private readonly System.Collections.Generic.HashSet<ulong> _synchronizingClients = new();
    public bool IsClientSynchronizing(ulong client) => _synchronizingClients.Contains(client);

    public void BeginClientSync(ulong client)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || client == NetworkManager.ServerClientId
            || !nm.ConnectedClients.ContainsKey(client)) return;
        _synchronizingClients.Add(client);
        SendStateToClient(client, _curtainDown);
    }

    public void CompleteClientSync(ulong client)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer || !nm.ConnectedClients.ContainsKey(client)) return;
        _synchronizingClients.Remove(client);
        SendStateToClient(client, _curtainDown);
    }
    private bool _handlerRegistered;

    private Canvas _clientCanvas;
    private GameObject _overlayRoot;
    private Text _overlayLabel;
    private GameObject _hostIndicatorRoot;
    private readonly System.Collections.Generic.List<Camera> _blockedCameras = new();

    /// <summary>True when clients are blinded (host always returns false).</summary>
    public static bool IsBlockingLocalPlayer =>
        Instance != null
        && Instance._curtainDown
        && NetworkManager.Singleton != null
        && !NetworkManager.Singleton.IsHost;

    public static bool IsCurtainDown =>
        Instance != null && Instance._curtainDown;

    public static void EnsureInstance()
    {
        if (Instance != null) return;

        var go = new GameObject("HostSceneCurtain");
        go.AddComponent<HostSceneCurtain>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        BuildUi();
    }

    private void Start()
    {
        InvokeRepeating(nameof(TryRegisterHandler), 0.1f, 0.5f);
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        nm.OnServerStarted += OnNetworkReady;
        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void OnNetworkReady()
    {
        _synchronizingClients.Clear();
        _handlerRegistered = false;
        CancelInvoke(nameof(TryRegisterHandler));
        InvokeRepeating(nameof(TryRegisterHandler), 0.1f, 0.5f);
    }

    private void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (clientId == nm.LocalClientId && !nm.IsHost)
        {
            _syncPending = true;
            SetCurtainDown(true);
            _handlerRegistered = false;
            CancelInvoke(nameof(TryRegisterHandler));
            InvokeRepeating(nameof(TryRegisterHandler), 0.1f, 0.5f);
        }

        if (!nm.IsServer || clientId == NetworkManager.ServerClientId) return;
        BeginClientSync(clientId);
        StartCoroutine(RetrySendStateToClient(clientId));
    }

    /// <summary>Server: push current curtain state to one client (late join / reconnect).</summary>
    public void SendCurtainStateToClient(ulong clientId)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        SendStateToClient(clientId, _curtainDown);
    }

    private System.Collections.IEnumerator RetrySendStateToClient(ulong clientId)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            if (attempt > 0)
                yield return new WaitForSeconds(0.5f);

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) yield break;
            if (!nm.ConnectedClients.ContainsKey(clientId)) yield break;

            SendStateToClient(clientId, _curtainDown);
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        _synchronizingClients.Remove(clientId);

        if (clientId == nm.LocalClientId)
        {
            _synchronizingClients.Clear();
            _syncPending = false;
            _curtainDown = false;
            ApplyCurtainState();
            UnregisterHandler();
            CancelInvoke(nameof(TryRegisterHandler));
        }
    }

    public void ToggleCurtainOnHost()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        bool next = !_curtainDown;
        SetCurtainDown(next);
        BroadcastState(next);
    }

    private void SetCurtainDown(bool down)
    {
        _curtainDown = down;
        ApplyCurtainState();
    }

    private void ApplyCurtainState()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (nm.IsHost)
        {
            SetClientOverlayVisible(false);
            RestoreCameras();
            SetHostIndicatorVisible(_curtainDown);
            return;
        }

        SetHostIndicatorVisible(false);
        SetClientOverlayVisible(_curtainDown);
        if (_overlayLabel != null)
            _overlayLabel.text = _syncPending ? "Загрузка сцены…" : "Мастер готовит сцену...";
        if (_curtainDown)
            BlockSceneCameras();
        else
            RestoreCameras();
    }

    private void TryRegisterHandler()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        if (!nm.IsServer && !nm.IsConnectedClient) return;

        var cmm = nm.CustomMessagingManager;
        if (cmm == null) return;

        cmm.UnregisterNamedMessageHandler(MSG_STATE);
        cmm.RegisterNamedMessageHandler(MSG_STATE, OnStateMessage);
        _handlerRegistered = true;
        CancelInvoke(nameof(TryRegisterHandler));
    }

    private void UnregisterHandler()
    {
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm != null && _handlerRegistered)
            cmm.UnregisterNamedMessageHandler(MSG_STATE);

        _handlerRegistered = false;
    }

    private void OnStateMessage(ulong senderId, FastBufferReader reader)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.IsServer || senderId != NetworkManager.ServerClientId || !reader.TryBeginRead(sizeof(bool))) return;
        reader.ReadValueSafe(out bool down);
        _syncPending = false;
        if (reader.TryBeginRead(sizeof(bool))) reader.ReadValueSafe(out _syncPending);
        _curtainDown = down;
        ApplyCurtainState();
    }

    private void BroadcastState(bool down)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;

        var cmm = nm.CustomMessagingManager;
        if (cmm == null) return;

        foreach (ulong client in nm.ConnectedClientsIds)
            if (client != NetworkManager.ServerClientId) SendStateToClient(client, down);
    }

    private void SendStateToClient(ulong clientId, bool down)
    {
        var cmm = NetworkManager.Singleton?.CustomMessagingManager;
        if (cmm == null) return;

        bool pending = _synchronizingClients.Contains(clientId);
        using var writer = new FastBufferWriter(2 * sizeof(bool), Allocator.Temp);
        writer.WriteValueSafe(down || pending);
        writer.WriteValueSafe(pending);
        cmm.SendNamedMessage(MSG_STATE, clientId, writer);
    }

    private void SetClientOverlayVisible(bool visible)
    {
        if (_overlayRoot != null)
            _overlayRoot.SetActive(visible);
    }

    private void SetHostIndicatorVisible(bool visible)
    {
        if (_hostIndicatorRoot != null)
            _hostIndicatorRoot.SetActive(visible);
    }

    private void BlockSceneCameras()
    {
        RestoreCameras();

        var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
        foreach (var cam in cameras)
        {
            if (cam == null || !cam.enabled || cam.targetTexture != null) continue;
            _blockedCameras.Add(cam);
            cam.enabled = false;
        }
    }

    private void RestoreCameras()
    {
        foreach (var cam in _blockedCameras)
        {
            if (cam != null)
                cam.enabled = true;
        }

        _blockedCameras.Clear();
    }

    private void BuildUi()
    {
        BuildClientOverlay();
        BuildHostIndicator();
        SetClientOverlayVisible(false);
        SetHostIndicatorVisible(false);
    }

    private void BuildClientOverlay()
    {
        var canvasGo = new GameObject("HostCurtainCanvas");
        canvasGo.transform.SetParent(transform, false);

        _clientCanvas = canvasGo.AddComponent<Canvas>();
        _clientCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _clientCanvas.sortingOrder = 32760;
        canvasGo.AddComponent<GraphicRaycaster>();

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _overlayRoot = new GameObject("Overlay");
        _overlayRoot.transform.SetParent(canvasGo.transform, false);

        var bg = _overlayRoot.AddComponent<Image>();
        bg.color = new Color(0.02f, 0.04f, 0.08f, 1f);
        bg.raycastTarget = true;

        var rt = _overlayRoot.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(_overlayRoot.transform, false);
        var label = labelGo.AddComponent<Text>();
        _overlayLabel = label;
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 28;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.82f, 0.86f, 0.94f, 1f);
        label.text = "Мастер готовит сцену...";
        label.raycastTarget = false;

        var lrt = label.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
    }

    private void BuildHostIndicator()
    {
        _hostIndicatorRoot = new GameObject("HostCurtainIndicator");
        _hostIndicatorRoot.transform.SetParent(_clientCanvas.transform, false);

        var bg = _hostIndicatorRoot.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.1f, 0.25f, 0.92f);
        bg.raycastTarget = false;

        var rt = _hostIndicatorRoot.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0f, 0f);
        rt.sizeDelta = new Vector2(320f, 42f);
        rt.anchoredPosition = new Vector2(14f, 14f);

        var labelGo = new GameObject("Label");
        labelGo.transform.SetParent(_hostIndicatorRoot.transform, false);
        var label = labelGo.AddComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 15;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(0.95f, 0.78f, 0.35f, 1f);
        label.text = "Экран игроков скрыт · откройте панель GM";
        label.raycastTarget = false;

        var lrt = label.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = Vector2.zero;
        lrt.offsetMax = Vector2.zero;
    }

    private void OnDestroy()
    {
        CancelInvoke(nameof(TryRegisterHandler));
        RestoreCameras();
        UnregisterHandler();

        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnServerStarted -= OnNetworkReady;
            nm.OnClientConnectedCallback -= OnClientConnected;
            nm.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        if (Instance == this)
            Instance = null;
    }

    private void OnGUI()
    {
        if (!_curtainDown) return;

        var nm = NetworkManager.Singleton;
        if (nm == null || nm.IsHost) return;

        GUI.depth = -32000;

        var oldColor = GUI.color;
        GUI.color = new Color(0.02f, 0.04f, 0.08f, 1f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = new Color(0.82f, 0.86f, 0.94f, 1f);
        GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height), _syncPending ? "Загрузка сцены…" : "Мастер готовит сцену...", style);
        if (_syncPending && GUI.Button(new Rect(Screen.width * 0.5f - 100, Screen.height * 0.5f + 46, 200, 36), "Выйти в лобби"))
            _ = GameNetworkManager.Instance?.ShutdownAndReset();
        GUI.color = oldColor;
    }
}
