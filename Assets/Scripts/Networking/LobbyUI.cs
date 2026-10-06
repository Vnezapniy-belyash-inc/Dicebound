using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI лобби: кнопки Host / Join, поле join-кода, поле ника.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject lobbyPanel;
    public Button hostButton;
    public InputField joinCodeInput;
    public Button joinButton;
    public InputField nicknameInput;
    public Text statusText;
    public Button exitButton; // кнопка выхода из игры

    /// <summary>Никнейм текущего игрока (доступен всем после входа в лобби).</summary>
    public static string LocalNickname { get; private set; } = "Player";
    private static string _pendingReconnectCode;

    private bool _isConnecting;
    private bool _ignoreDisconnect;
    private bool _hasSessionClientId;
    private ulong _sessionClientId;
    private Button _prepareButton;

    private void Start()
    {
        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
        BuildPreparationButton();
        if (exitButton != null)
            exitButton.onClick.AddListener(() => Application.Quit());

        // Загружаем сохранённый ник
        string saved = PlayerPrefs.GetString("nickname", "");
        if (!string.IsNullOrEmpty(saved))
            nicknameInput.text = saved;
        else
            nicknameInput.text = "Player";

        lobbyPanel.SetActive(true);
        statusText.text = "";

        InvokeRepeating(nameof(TrySubscribeDisconnect), 0.2f, 0.5f);
        if (!string.IsNullOrEmpty(_pendingReconnectCode))
        {
            string code = _pendingReconnectCode;
            _pendingReconnectCode = null;
            joinCodeInput.text = code;
            SaveNickname();
            _ = JoinSession(code, true);
        }
    }

    private void BuildPreparationButton()
    {
        var go = Instantiate(hostButton.gameObject, hostButton.transform.parent);
        go.name = "Prepare scene locally";
        _prepareButton = go.GetComponent<Button>();
        _prepareButton.onClick = new Button.ButtonClickedEvent();
        _prepareButton.onClick.AddListener(PrepareScene);
        var text = go.GetComponentInChildren<Text>();
        if (text != null) { text.text = "Подготовить сцену без игроков"; text.fontSize = 16; }
        var rect = go.GetComponent<RectTransform>();
        rect.SetSiblingIndex(hostButton.transform.GetSiblingIndex() + 1);
        if (hostButton.GetComponentInParent<LayoutGroup>() == null)
        {
            var hostRect = hostButton.GetComponent<RectTransform>();
            float spacing = hostRect.sizeDelta.y + 10;
            rect.anchoredPosition = hostRect.anchoredPosition + Vector2.down * spacing;
            // Insert preparation immediately below Host, moving the existing join block together.
            foreach (var control in new Transform[] { joinButton.transform, joinCodeInput.transform, exitButton != null ? exitButton.transform : null })
                if (control != null) control.GetComponent<RectTransform>().anchoredPosition += Vector2.down * spacing;
        }
    }

    private async void PrepareScene()
    {
        if (_isConnecting) return;
        _isConnecting = true; SetInteractable(false); SaveNickname();
        try
        {
            await GameNetworkManager.Instance.ShutdownAndReset();
            RelayManager.ClearJoinCode();
            NetworkManager.Singleton.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>()
                .SetConnectionData("127.0.0.1", 7777, "127.0.0.1");
            CellMarker.EnsureRegistered();
            if (!GameNetworkManager.Instance.StartHost()) throw new System.InvalidOperationException("Не удалось открыть редактор.");
            await WaitForPlayerRegistration();
            _sessionClientId = NetworkManager.Singleton.LocalClientId; _hasSessionClientId = true;
            lobbyPanel.SetActive(false); ShowGameUI(); DmPanelUI.Instance?.ShowSceneEditor();
            DiceUI.Instance?.ShowToolNotice("Локальная подготовка: сохраните JSON, затем загрузите его в игровом лобби.");
        }
        catch (System.Exception ex)
        {
            await CleanupFailedConnection(); statusText.text = "Подготовка: " + ex.Message; SetInteractable(true);
        }
        finally { _isConnecting = false; }
    }

    void TrySubscribeDisconnect()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnDisconnected;
        CancelInvoke(nameof(TrySubscribeDisconnect));
    }

    private void OnDestroy()
    {
        CancelInvoke(nameof(TrySubscribeDisconnect));
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnDisconnected;
    }

    async void OnDisconnected(ulong clientId)
    {
        if (_isConnecting || _ignoreDisconnect) return;
        if (_hasSessionClientId && clientId != _sessionClientId)
            return;
        _ignoreDisconnect = true;
        var manager = GameNetworkManager.Instance;
        if (manager != null && !manager.LastStartedAsHost && !manager.ShutdownRequested
            && !string.IsNullOrEmpty(RelayManager.CurrentJoinCode))
            _pendingReconnectCode = RelayManager.CurrentJoinCode;
        try
        {
            if (manager != null)
                await manager.ShutdownAndReset();
            else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Lobby] Disconnect cleanup failed: {ex.Message}");
        }
        finally
        {
            CellMarker.ResetRegistration();
            PlayerColors.Reset();
            RelayManager.ClearJoinCode();
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        }
    }

    private async void OnHostClicked()
    {
        if (_isConnecting) return;
        SaveNickname();

        _isConnecting = true;
        SetInteractable(false);
        statusText.text = "Создаём сессию…";

        try
        {
            if (GameNetworkManager.Instance != null)
                await GameNetworkManager.Instance.ShutdownAndReset();
            else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            await System.Threading.Tasks.Task.Delay(200);

            string code = await RelayManager.Instance.CreateRelayAllocation(9);
            CellMarker.EnsureRegistered();
            if (!GameNetworkManager.Instance.StartHost())
                throw new System.InvalidOperationException("Не удалось запустить хост.");
            await WaitForPlayerRegistration();
            _sessionClientId = NetworkManager.Singleton.LocalClientId;
            _hasSessionClientId = true;
            lobbyPanel.SetActive(false);
            GUIUtility.systemCopyBuffer = code;
            statusText.text = $"Сессия создана. Код {code} скопирован.";
            ShowGameUI();
            _isConnecting = false;
            Debug.Log($"[Lobby] Host started. Join code: {code}");
        }
        catch (System.Exception ex)
        {
            try { await CleanupFailedConnection(); }
            catch (System.Exception cleanupError)
            {
                Debug.LogWarning($"[Lobby] Failed to clean up host attempt: {cleanupError}");
            }
            statusText.text = $"Не удалось создать сессию: {ex.Message}";
            Debug.LogError($"[Lobby] Host failed: {ex}");
            _isConnecting = false;
            SetInteractable(true);
        }
    }

    private async void OnJoinClicked()
    {
        if (_isConnecting) return;

        string code = joinCodeInput.text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            statusText.text = "Введите код сессии.";
            return;
        }

        SaveNickname();
        await JoinSession(code, false);
    }

    private async System.Threading.Tasks.Task JoinSession(string code, bool automaticReconnect)
    {
        if (_isConnecting) return;
        _isConnecting = true;
        SetInteractable(false);
        int attempts = automaticReconnect ? 3 : 1;

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            statusText.text = automaticReconnect
                ? $"Восстанавливаем соединение ({attempt}/{attempts})…"
                : "Подключаемся…";
            try
            {
                if (GameNetworkManager.Instance != null)
                    await GameNetworkManager.Instance.ShutdownAndReset();
                else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                    NetworkManager.Singleton.Shutdown();

                await System.Threading.Tasks.Task.Delay(200);
                await RelayManager.Instance.JoinRelayAllocation(code);
                CellMarker.EnsureRegistered();
                if (!GameNetworkManager.Instance.StartClient())
                    throw new System.InvalidOperationException("Не удалось запустить клиент.");
                for (int tick = 0;
                    tick < 150 && !GameNetworkManager.Instance.IsConnected; tick++)
                    await System.Threading.Tasks.Task.Delay(100);
                if (!GameNetworkManager.Instance.IsConnected)
                    throw new System.TimeoutException("Сервер не ответил в течение 15 секунд.");
                await WaitForPlayerRegistration();
                _sessionClientId = NetworkManager.Singleton.LocalClientId;
                _hasSessionClientId = true;
                lobbyPanel.SetActive(false);
                statusText.text = "Подключено.";
                ShowGameUI();
                _isConnecting = false;
                Debug.Log(automaticReconnect
                    ? "[Lobby] Client reconnected." : "[Lobby] Client connected.");
                return;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[Lobby] Join attempt {attempt}/{attempts} failed: {ex}");
                try { await CleanupFailedConnection(); }
                catch (System.Exception cleanupError)
                {
                    Debug.LogWarning($"[Lobby] Failed to clean up join attempt: {cleanupError}");
                }
                if (attempt == attempts)
                {
                    statusText.text = automaticReconnect
                        ? $"Не удалось восстановить соединение: {ex.Message}"
                        : $"Не удалось подключиться: {ex.Message}";
                    _isConnecting = false;
                    SetInteractable(true);
                    return;
                }
                await System.Threading.Tasks.Task.Delay(1000 * attempt);
            }
        }
    }

    private void SaveNickname()
    {
        string nick = nicknameInput.text.Trim();
        if (string.IsNullOrEmpty(nick)) nick = "Player";
        if (nick == "Player")
            Debug.LogWarning("[Lobby] Default nickname 'Player' is used — pick a unique name to avoid color conflicts.");
        LocalNickname = nick;
        PlayerPrefs.SetString("nickname", nick);
        PlayerPrefs.Save();
    }

    private static async System.Threading.Tasks.Task WaitForPlayerRegistration()
    {
        for (int attempt = 0; attempt < 80; attempt++)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsConnectedClient
                && PlayerColors.GetNickname(nm.LocalClientId) != null)
                return;
            await System.Threading.Tasks.Task.Delay(100);
        }
        throw new System.TimeoutException("Не удалось получить данные игрока от сервера.");
    }

    private async System.Threading.Tasks.Task CleanupFailedConnection()
    {
        _ignoreDisconnect = true;
        try
        {
            if (GameNetworkManager.Instance != null)
                await GameNetworkManager.Instance.ShutdownAndReset();
        }
        finally
        {
            PlayerColors.Reset();
            RelayManager.ClearJoinCode();
            _ignoreDisconnect = false;
        }
    }

    private void ShowGameUI()
    {
        var diceUI = FindAnyObjectByType<DiceUI>();
        if (diceUI != null)
            diceUI.ShowGamePanels();

        CharacterSheetUI.Instance?.SetSessionActive(true);
    }

    private void SetInteractable(bool interactable)
    {
        if (_prepareButton != null) _prepareButton.interactable = interactable;
        hostButton.interactable = interactable;
        joinButton.interactable = interactable;
        joinCodeInput.interactable = interactable;
        nicknameInput.interactable = interactable;
    }

    /// <summary>Принудительно показать меню (при выходе из лобби).</summary>
    public void ShowLobby()
    {
        CharacterSheetUI.Instance?.SetSessionActive(false);
        lobbyPanel.SetActive(true);
        SetInteractable(true);
        _isConnecting = false;
        statusText.text = "";
    }
}
