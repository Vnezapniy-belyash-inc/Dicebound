using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// UI лобби: кнопки Host / Join, поле ввода join-кода.
/// Висит на Canvas → Panel «LobbyPanel».
/// </summary>
public class LobbyUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject lobbyPanel;
    public Button hostButton;
    public InputField joinCodeInput;
    public Button joinButton;
    public Text statusText;

    private bool _isConnecting;

    private void Start()
    {
        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
        lobbyPanel.SetActive(true);
        statusText.text = "";
    }

    private async void OnHostClicked()
    {
        if (_isConnecting) return;
        _isConnecting = true;
        SetInteractable(false);

        statusText.text = "Creating lobby...";

        try
        {
            string code = await RelayManager.Instance.CreateRelayAllocation(9);
            GameNetworkManager.Instance.StartHost();
            lobbyPanel.SetActive(false);
            statusText.text = $"Host started! Code: {code}";
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

        _isConnecting = true;
        SetInteractable(false);
        statusText.text = "Joining...";

        try
        {
            await RelayManager.Instance.JoinRelayAllocation(code);
            GameNetworkManager.Instance.StartClient();
            lobbyPanel.SetActive(false);
            statusText.text = "Connected!";
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

    private void SetInteractable(bool interactable)
    {
        hostButton.interactable = interactable;
        joinButton.interactable = interactable;
        joinCodeInput.interactable = interactable;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            lobbyPanel.SetActive(!lobbyPanel.activeSelf);
        }
    }
}
