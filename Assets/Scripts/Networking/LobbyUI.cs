using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
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

    private bool _isConnecting;

    private void Start()
    {
        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
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
    }

    void TrySubscribeDisconnect()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnDisconnected;
        CancelInvoke(nameof(TrySubscribeDisconnect));
    }

    async void OnDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton != null && clientId != NetworkManager.Singleton.LocalClientId)
            return;

        if (GameNetworkManager.Instance != null)
            await GameNetworkManager.Instance.ShutdownAndReset();
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
            NetworkManager.Singleton.Shutdown();

        CellMarker.ResetRegistration();
        PlayerColors.Reset();
        RelayManager.ClearJoinCode();
        UnityEngine.SceneManagement.SceneManager.LoadScene(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }

    private async void OnHostClicked()
    {
        if (_isConnecting) return;
        SaveNickname();

        _isConnecting = true;
        SetInteractable(false);
        statusText.text = "Creating lobby...";

        try
        {
            if (GameNetworkManager.Instance != null)
                await GameNetworkManager.Instance.ShutdownAndReset();
            else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            await System.Threading.Tasks.Task.Delay(200);

            string code = await RelayManager.Instance.CreateRelayAllocation(9);
            CellMarker.EnsureRegistered();
            GameNetworkManager.Instance.StartHost();
            lobbyPanel.SetActive(false);
            GUIUtility.systemCopyBuffer = code;
            statusText.text = $"Host started! Code copied: {code}";
            ShowGameUI();
            _isConnecting = false;
            Debug.Log($"[Lobby] Host started. Join code: {code}");
        }
        catch (System.Exception ex)
        {
            statusText.text = $"Error: {ex.Message}";
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
            statusText.text = "Enter join code!";
            return;
        }

        SaveNickname();

        _isConnecting = true;
        SetInteractable(false);
        statusText.text = "Joining...";

        try
        {
            if (GameNetworkManager.Instance != null)
                await GameNetworkManager.Instance.ShutdownAndReset();
            else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                NetworkManager.Singleton.Shutdown();

            await System.Threading.Tasks.Task.Delay(200);

            await RelayManager.Instance.JoinRelayAllocation(code);
            CellMarker.EnsureRegistered();
            GameNetworkManager.Instance.StartClient();
            lobbyPanel.SetActive(false);
            statusText.text = "Connected!";
            ShowGameUI();
            _isConnecting = false;
            Debug.Log("[Lobby] Client connected.");
        }
        catch (System.Exception ex)
        {
            statusText.text = $"Error: {ex.Message}";
            Debug.LogError($"[Lobby] Join failed: {ex}");
            _isConnecting = false;
            SetInteractable(true);
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

    private void ShowGameUI()
    {
        var diceUI = FindAnyObjectByType<DiceUI>();
        if (diceUI != null)
            diceUI.ShowGamePanels();

        CharacterSheetUI.Instance?.SetSessionActive(true);
    }

    private void SetInteractable(bool interactable)
    {
        hostButton.interactable = interactable;
        joinButton.interactable = interactable;
        joinCodeInput.interactable = interactable;
        nicknameInput.interactable = interactable;
    }

    private void Update()
    {
        if (!GameplayInputGate.AllowsKeyboardHotkeys) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            lobbyPanel.SetActive(!lobbyPanel.activeSelf);
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
