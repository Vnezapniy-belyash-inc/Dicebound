using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public partial class LobbyUI
{
    private RectTransform _lobbyCard;
    private Canvas _lobbyCanvas;
    private Selectable[] _lobbyNavigation;

    private void BuildLobbyLayout()
    {
        _lobbyCanvas = lobbyPanel.GetComponentInParent<Canvas>();
        var background = lobbyPanel.transform.Find("Image");
        if (background != null && background.TryGetComponent<Image>(out var image))
        {
            image.color = new Color(0.035f, 0.05f, 0.07f, 1);
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        var card = new GameObject("LobbyCard", typeof(RectTransform), typeof(Image));
        card.transform.SetParent(lobbyPanel.transform, false);
        VttUiSkin.Surface(card.GetComponent<Image>(), VttUiSkin.Panel);
        _lobbyCard = card.GetComponent<RectTransform>();
        _lobbyCard.anchorMin = _lobbyCard.anchorMax = _lobbyCard.pivot = new Vector2(0.5f, 0.5f);
        _lobbyCard.sizeDelta = new Vector2(500, 580);
        LobbyLabel("Dicebound", 28, VttUiSkin.Text, 24, 44, true);
        LobbyLabel("Виртуальный стол для вашей истории", 15, VttUiSkin.Muted, 72, 28);
        LobbyLabel("Ваше имя за столом", 14, VttUiSkin.Muted, 110, 20);
        StyleLobbyField(nicknameInput, "Имя игрока", 136);
        LobbyLabel("Мастеру", 14, VttUiSkin.Muted, 196, 20);
        StyleLobbyButton(hostButton, "Создать онлайн-сессию", VttUiSkin.Primary, 224);
        StyleLobbyButton(_prepareButton, "Подготовить сцену без игроков", VttUiSkin.Button, 270);
        LobbyLabel("Игроку · код от мастера", 14, VttUiSkin.Muted, 328, 20);
        StyleLobbyField(joinCodeInput, "Код сессии", 354);
        StyleLobbyButton(joinButton, "Войти по коду", VttUiSkin.Primary, 400);
        PlaceLobbyControl(statusText.gameObject, 446, 64);
        statusText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        statusText.fontSize = 14; statusText.resizeTextForBestFit = false;
        statusText.color = VttUiSkin.Text; statusText.supportRichText = false;
        statusText.alignment = TextAnchor.UpperLeft;
        statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        statusText.verticalOverflow = VerticalWrapMode.Truncate;
        statusText.raycastTarget = false;
        if (exitButton != null) StyleLobbyButton(exitButton, "Закрыть игру", VttUiSkin.Button, 526, 32);
        _lobbyNavigation = exitButton != null
            ? new Selectable[] { nicknameInput, hostButton, _prepareButton, joinCodeInput, joinButton, exitButton }
            : new Selectable[] { nicknameInput, hostButton, _prepareButton, joinCodeInput, joinButton };
        for (int i = 0; i < _lobbyNavigation.Length; i++)
            _lobbyNavigation[i].navigation = new Navigation { mode = Navigation.Mode.Explicit,
                selectOnUp = _lobbyNavigation[(i + _lobbyNavigation.Length - 1) % _lobbyNavigation.Length],
                selectOnDown = _lobbyNavigation[(i + 1) % _lobbyNavigation.Length] };
        FitLobby();
    }

    private void PlaceLobbyControl(GameObject control, float top, float height)
    {
        control.transform.SetParent(_lobbyCard, false);
        var rect = control.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.sizeDelta = new Vector2(436, height); rect.anchoredPosition = new Vector2(0, -top);
    }

    private void StyleLobbyButton(Button button, string caption, Color color, float top, float height = 38)
    {
        PlaceLobbyControl(button.gameObject, top, height);
        VttUiSkin.ButtonStyle(button.GetComponent<Image>(), color);
        var text = button.GetComponentInChildren<Text>();
        text.text = caption; text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.color = VttUiSkin.Text; VttUiSkin.ButtonCaption(text, 16);
    }

    private void StyleLobbyField(InputField field, string hint, float top)
    {
        PlaceLobbyControl(field.gameObject, top, 38);
        VttUiSkin.Surface(field.GetComponent<Image>(), VttUiSkin.Raised, 6);
        field.textComponent.color = VttUiSkin.Text; field.textComponent.fontSize = 16;
        field.textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        field.textComponent.fontStyle = FontStyle.Normal;
        field.textComponent.supportRichText = false;
        field.caretColor = VttUiSkin.Text; field.customCaretColor = true;
        if (field.placeholder is Text placeholder)
        { placeholder.text = hint; placeholder.color = VttUiSkin.Muted; placeholder.fontSize = 16;
            placeholder.font = field.textComponent.font; placeholder.fontStyle = FontStyle.Normal; }
    }

    private void LobbyLabel(string value, int size, Color color, float top, float height, bool bold = false)
    {
        var label = new GameObject("LobbyLabel", typeof(RectTransform), typeof(Text));
        PlaceLobbyControl(label, top, height);
        var text = label.GetComponent<Text>(); text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size; text.color = color; text.raycastTarget = false;
        text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
    }

    private void FitLobby()
    {
        if (_lobbyCard == null || _lobbyCanvas == null) return;
        float factor = Mathf.Max(0.01f, _lobbyCanvas.scaleFactor);
        float fit = Mathf.Min(1, (Screen.width - 32f) / (500 * factor), (Screen.height - 24f) / (580 * factor));
        _lobbyCard.localScale = Vector3.one * Mathf.Max(0.1f, fit);
    }

    private void Update()
    {
        if (lobbyPanel == null || !lobbyPanel.activeInHierarchy) return;
        FitLobby();
        if (_isConnecting || GameplayInputGate.IsModalOpen || Keyboard.current?.tabKey.wasPressedThisFrame != true
            || _lobbyNavigation == null || EventSystem.current == null) return;
        int index = System.Array.FindIndex(_lobbyNavigation, s => s.gameObject == EventSystem.current.currentSelectedGameObject);
        int direction = Keyboard.current.shiftKey.isPressed ? -1 : 1;
        EventSystem.current.SetSelectedGameObject(_lobbyNavigation[(index + direction + _lobbyNavigation.Length) % _lobbyNavigation.Length].gameObject);
    }
}
