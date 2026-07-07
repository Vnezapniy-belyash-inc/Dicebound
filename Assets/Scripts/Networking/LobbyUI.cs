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

    /// <summary>Никнейм текущего игрока (доступен всем после входа в лобби).</summary>
    public static string LocalNickname { get; private set; } = "Player";

    private bool _isConnecting;

    private void Start()
    {
        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);

        // Загружаем сохранённый ник
        string saved = PlayerPrefs.GetString("nickname", "");
        if (!string.IsNullOrEmpty(saved))
            nicknameInput.text = saved;
        else
            nicknameInput.text = "Player";

        lobbyPanel.SetActive(true);
        statusText.text = "";
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
            string code = await RelayManager.Instance.CreateRelayAllocation(9);
            GameNetworkManager.Instance.StartHost();
            lobbyPanel.SetActive(false);
            GUIUtility.systemCopyBuffer = code;
            statusText.text = $"Host started! Code copied: {code}";
            ShowGameUI();
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
            await RelayManager.Instance.JoinRelayAllocation(code);
            GameNetworkManager.Instance.StartClient();
            lobbyPanel.SetActive(false);
            statusText.text = "Connected!";
            ShowGameUI();
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
        LocalNickname = nick;
        PlayerPrefs.SetString("nickname", nick);
        PlayerPrefs.Save();
    }

    private void ShowGameUI()
    {
        var diceUI = FindAnyObjectByType<DiceUI>();
        if (diceUI != null)
            diceUI.ShowGamePanels();
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
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            lobbyPanel.SetActive(!lobbyPanel.activeSelf);
        }
    }
}
