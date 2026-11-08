using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Host-only scene curtain: the GM panel hides the table from all clients while the host prepares.
/// Clients see an opaque full-screen panel and cannot interact with or view the scene.
/// Sync uses CustomMessagingManager (no runtime NetworkObject spawn).
/// </summary>
public partial class HostSceneCurtain : MonoBehaviour
{
    private const string MSG_STATE = "HostSceneCurtainState";

    public static HostSceneCurtain Instance { get; private set; }
    internal event System.Action LocalSyncCompleted;

    private bool _curtainDown;
    private bool _syncPending;
    private byte _syncStages;
    private int _portraitsReceived, _portraitsTotal;
    private float _nextProgressSend;
    private float _syncStartedAt, _lastProgressAt;
    private int _lastLocalBytes, _lastLocalObjects;
    private static readonly string[] SyncStageNames = { "Карта", "Токены и кубики", "Стены и препятствия", "Туман", "Портреты", "Инициатива" };
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
    private Text _tipLabel;
    private bool HasVisibleClientOverlay => _clientCanvas != null && _clientCanvas.isActiveAndEnabled
        && _overlayRoot != null && _overlayRoot.activeInHierarchy;
    private Texture2D _curtainTexture;
    private int _tipIndex = -1;
    private float _nextTipAt;
    private static readonly string[] LoadingTips = {
        "чтобы победить дракона, попробуйте снизить его здоровье до нуля",
        "зелье неизвестного происхождения становится известным после употребления",
        "этот совет не требует концентрации",
        "короткий отдых занимает меньше времени, чем длинный",
        "чтение этого совета не расходует действие",
        "советы на экране загрузки появляются во время загрузки",
        "у зомби hp обычно меньше, чем у тараска",
        "при стрельбе из лука расходуются стрелы",
        "скелет весит меньше человека, из которого он получился",
        "после продажи меча у вас становится больше денег и меньше меча"
    };
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

    public static bool IsLocalSynchronizing => Instance != null && Instance._syncPending
        && NetworkManager.Singleton != null && !NetworkManager.Singleton.IsHost;

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
            BeginLocalSync();
            SetCurtainDown(false);
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
        if (down && !_curtainDown) _nextTipAt = 0f;
        _curtainDown = down;
        if (down) UpdateLoadingTip(Time.unscaledTime);
        ApplyCurtainState();
    }

    private void ApplyCurtainState()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (nm.IsHost)
        {
            SetLoadingScreenVisible(false);
            SetClientOverlayVisible(false);
            RestoreCameras();
            SetHostIndicatorVisible(_curtainDown);
            return;
        }

        SetHostIndicatorVisible(false);
        bool loading = IsLoadingScreenVisible;
        SetLoadingScreenVisible(loading);
        SetClientOverlayVisible(_curtainDown && !loading);
        if (_overlayLabel != null)
            _overlayLabel.text = "Мастер готовит сцену...";
        if (_curtainDown || loading)
            BlockSceneCameras();
        else
            RestoreCameras();
    }

    private void BeginLocalSync()
    {
        _syncPending = true;
        _syncStages = 0;
        _portraitsReceived = _portraitsTotal = 0;
        _lastLocalBytes = _lastLocalObjects = 0;
        _syncStartedAt = _lastProgressAt = Time.unscaledTime;
    }

    private void Update()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;
        if (_curtainDown && !nm.IsServer)
        {
            UpdateLoadingTip(Time.unscaledTime);
        }
        if (nm.IsServer && Time.unscaledTime >= _nextProgressSend)
        {
            _nextProgressSend = Time.unscaledTime + 0.5f;
            foreach (ulong client in _synchronizingClients)
                if (nm.ConnectedClients.ContainsKey(client)) SendStateToClient(client, _curtainDown);
        }
        if (IsLocalSynchronizing)
        {
            int bytes = MapSync.Instance?.LocalReceivedBytes ?? 0;
            int objects = LateJoinSync.Instance?.LocalWorldObjectCount ?? 0;
            if (bytes != _lastLocalBytes || objects != _lastLocalObjects)
            {
                _lastLocalBytes = bytes;
                _lastLocalObjects = objects;
                _lastProgressAt = Time.unscaledTime;
            }
        }
        if (IsLoadingScreenVisible) UpdateLoadingScreen();
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
        bool pending = false;
        if (reader.TryBeginRead(sizeof(bool))) reader.ReadValueSafe(out pending);
        bool completed = _syncPending && !pending;
        if (pending && !_syncPending) BeginLocalSync();
        _syncPending = pending;
        if (reader.TryBeginRead(sizeof(byte) + 2 * sizeof(int)))
        {
            reader.ReadValueSafe(out byte stages);
            reader.ReadValueSafe(out int portraitsReceived);
            reader.ReadValueSafe(out int portraitsTotal);
            if (stages != _syncStages || portraitsReceived != _portraitsReceived || portraitsTotal != _portraitsTotal)
                _lastProgressAt = Time.unscaledTime;
            _syncStages = stages;
            _portraitsReceived = portraitsReceived;
            _portraitsTotal = portraitsTotal;
        }
        SetCurtainDown(down);
        if (completed) LocalSyncCompleted?.Invoke();
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
        using var writer = new FastBufferWriter(2 * sizeof(bool) + sizeof(byte) + 2 * sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(down);
        writer.WriteValueSafe(pending);
        writer.WriteValueSafe(LateJoinSync.Instance?.GetClientSyncStages(clientId) ?? (byte)0);
        TokenImageSync.GetClientPortraitProgress(clientId, out int received, out int total);
        writer.WriteValueSafe(received);
        writer.WriteValueSafe(total);
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
        var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude);
        foreach (var cam in cameras)
        {
            if (cam == null || !cam.enabled || cam.targetTexture != null) continue;
            if (!_blockedCameras.Contains(cam)) _blockedCameras.Add(cam);
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

        _curtainTexture = Resources.Load<Texture2D>("UI/HostSceneCurtain");
        var bg = _overlayRoot.AddComponent<RawImage>();
        bg.texture = _curtainTexture;
        bg.color = _curtainTexture != null ? Color.white : new Color(0.02f, 0.04f, 0.08f, 1f);
        bg.raycastTarget = true;

        var rt = _overlayRoot.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var tipPanel = new GameObject("LoadingTip", typeof(RectTransform), typeof(Image));
        tipPanel.transform.SetParent(_overlayRoot.transform, false);
        tipPanel.GetComponent<Image>().color = new Color(0, 0, 0, .8f);
        tipPanel.GetComponent<Image>().raycastTarget = false;
        var tipRect = tipPanel.GetComponent<RectTransform>();
        tipRect.anchorMin = new Vector2(.025f, 0);
        tipRect.anchorMax = new Vector2(.975f, 0);
        tipRect.pivot = new Vector2(.5f, 0);
        tipRect.offsetMin = new Vector2(0, 64);
        tipRect.offsetMax = new Vector2(0, 136);
        var tipText = new GameObject("Text", typeof(RectTransform), typeof(Text));
        tipText.transform.SetParent(tipPanel.transform, false);
        _tipLabel = tipText.GetComponent<Text>();
        _tipLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _tipLabel.fontSize = 20;
        _tipLabel.resizeTextForBestFit = true;
        _tipLabel.resizeTextMinSize = 12;
        _tipLabel.resizeTextMaxSize = 20;
        _tipLabel.alignment = TextAnchor.MiddleCenter;
        _tipLabel.color = Color.white;
        _tipLabel.raycastTarget = false;
        var tipTextRect = tipText.GetComponent<RectTransform>();
        tipTextRect.anchorMin = Vector2.zero;
        tipTextRect.anchorMax = Vector2.one;
        tipTextRect.offsetMin = new Vector2(12, 8);
        tipTextRect.offsetMax = new Vector2(-12, -8);

        // RawImage stretches the entire meme to any screen aspect ratio, including its caption.
        if (_curtainTexture != null) return;

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
        var nm = NetworkManager.Singleton;
        if (nm == null || nm.IsHost) return;
        if (!_curtainDown || IsLoadingScreenVisible) return;

        // The native canvas already draws the meme AND its caption. A second fullscreen
        // IMGUI image would paint over that caption; use IMGUI only when the canvas is unavailable.
        if (HasVisibleClientOverlay) return;

        GUI.depth = -32000;

        var oldColor = GUI.color;
        GUI.color = _curtainTexture != null ? Color.white : new Color(0.02f, 0.04f, 0.08f, 1f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height),
            _curtainTexture != null ? _curtainTexture : Texture2D.whiteTexture, ScaleMode.StretchToFill, false);
        GUI.color = Color.white;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 28,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        style.normal.textColor = new Color(0.82f, 0.86f, 0.94f, 1f);
        if (_curtainTexture == null)
            GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height), "Мастер готовит сцену...", style);
        DrawLoadingTip();
        GUI.color = oldColor;
    }

    private void UpdateLoadingTip(float now)
    {
        if (_tipIndex < 0 || now >= _nextTipAt)
        {
            int next = UnityEngine.Random.Range(0, LoadingTips.Length - 1);
            if (next >= _tipIndex) next++;
            _tipIndex = _tipIndex < 0 ? UnityEngine.Random.Range(0, LoadingTips.Length) : next;
            _nextTipAt = now + 20f;
        }
        if (_tipLabel != null) _tipLabel.text = LoadingTips[_tipIndex];
    }

    private void DrawLoadingTip()
    {
        UpdateLoadingTip(Time.unscaledTime);
        float margin = Mathf.Min(24f * LocalUiScale.Value, Screen.width * 0.05f);
        var tipStyle = new GUIStyle(GUI.skin.label) {
            fontSize = Mathf.RoundToInt(18f * LocalUiScale.Value), alignment = TextAnchor.MiddleCenter,
            wordWrap = true, padding = new RectOffset(12, 12, 8, 8)
        };
        tipStyle.normal.textColor = Color.white;
        float width = Mathf.Max(1f, Screen.width - margin * 2);
        float height = tipStyle.CalcHeight(new GUIContent(LoadingTips[_tipIndex]), width);
        var rect = new Rect(margin, Mathf.Max(0, Screen.height - height - 64f * LocalUiScale.Value), width, height);
        Color previous = GUI.color;
        GUI.color = new Color(0, 0, 0, 0.8f);
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(rect, LoadingTips[_tipIndex], tipStyle);
        GUI.color = previous;
    }
}
