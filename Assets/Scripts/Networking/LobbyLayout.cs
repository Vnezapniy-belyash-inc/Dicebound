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
        _lobbyCanvas=lobbyPanel.GetComponentInParent<Canvas>();
        string nickname=nicknameInput.text, code=joinCodeInput.text;
        var hostAction=hostButton.onClick;var joinAction=joinButton.onClick;var prepareAction=_prepareButton.onClick;
        foreach(Transform child in lobbyPanel.transform){child.gameObject.SetActive(false);Destroy(child.gameObject);}
        var backdrop=AtlasUi.Panel(lobbyPanel.transform,"AtlasLobbyBackground",AtlasUi.Paper);AtlasUi.Fill(backdrop);
        var card=AtlasUi.Node(backdrop,"AtlasLobby");_lobbyCard=card;
        card.anchorMin=card.anchorMax=new Vector2(.5f,.5f);card.pivot=new Vector2(.5f,.5f);card.sizeDelta=new Vector2(1480,860);
        var art=AtlasUi.Panel(card,"Illustration",AtlasUi.Raised);AtlasUi.Place(art,0,140,580,720);
        var texture=Resources.Load<Texture2D>("Atlas/LobbyIllustration");if(texture!=null){var picture=AtlasUi.Node(art,"Artwork");AtlasUi.Fill(picture);var image=picture.gameObject.AddComponent<RawImage>();image.texture=texture;image.raycastTarget=false;}
        var brand=AtlasUi.Panel(card,"Brand",AtlasUi.Forest);AtlasUi.Place(brand,0,0,580,140);
        var word=AtlasUi.Text(brand,"◈  Dicebound",52,AtlasUi.Light,true,76);AtlasUi.Place(word.rectTransform,30,16,525,80);
        var sub=AtlasUi.Text(brand,"ВИРТУАЛЬНЫЙ СТОЛ ДЛЯ РОЛЕВЫХ ИГР",12,AtlasUi.Light,height:28);AtlasUi.Place(sub.rectTransform,35,98,510,28);
        var heading=AtlasUi.Text(card,"Соберите свою историю",43,title:true,height:100);heading.name="StoryHeading";AtlasUi.Place(heading.rectTransform,630,93,810,112);
        var nameLabel=AtlasUi.Text(card,"Ваше имя",21,title:true,height:40);nameLabel.name="NameLabel";AtlasUi.Place(nameLabel.rectTransform,630,234,175,44);
        nicknameInput=AtlasUi.Input(card,nickname,"Имя игрока",height:48);AtlasUi.Place((RectTransform)nicknameInput.transform,810,230,625,48);
        var master=AtlasUi.Panel(card,"MasterEntry",AtlasUi.Raised);AtlasUi.Place(master,630,325,385,375);
        var player=AtlasUi.Panel(card,"PlayerEntry",AtlasUi.Raised);AtlasUi.Place(player,1040,325,395,375);
        var masterHeading=AtlasUi.Text(master,"Я мастер",34,title:true,height:58);AtlasUi.Place(masterHeading.rectTransform,24,28,337,64);
        hostButton=AtlasUi.Button(master,"Создать онлайн-сессию",null,true,height:54);hostButton.onClick=hostAction;AtlasUi.Place((RectTransform)hostButton.transform,24,145,337,54);
        _prepareButton=AtlasUi.Button(master,"Подготовить без игроков",null,height:54);_prepareButton.onClick=prepareAction;AtlasUi.Place((RectTransform)_prepareButton.transform,24,215,337,54);
        var hint=AtlasUi.Text(master,"Подготовьте карту и сохраните сессию в файл",17,AtlasUi.Muted,height:62);AtlasUi.Place(hint.rectTransform,24,290,337,62);
        var playerHeading=AtlasUi.Text(player,"Я игрок",34,title:true,height:58);AtlasUi.Place(playerHeading.rectTransform,24,28,345,64);
        var codeLabel=AtlasUi.Text(player,"Код сессии от мастера",17,height:35);AtlasUi.Place(codeLabel.rectTransform,24,121,345,35);
        joinCodeInput=AtlasUi.Input(player,code,"Код сессии",height:50);AtlasUi.Place((RectTransform)joinCodeInput.transform,24,163,345,50);
        joinButton=AtlasUi.Button(player,"Войти по коду",null,true,height:54);joinButton.onClick=joinAction;AtlasUi.Place((RectTransform)joinButton.transform,24,233,345,54);
        statusText=AtlasUi.Text(card,"",16,AtlasUi.Muted,height:68);AtlasUi.Place(statusText.rectTransform,630,714,805,68);
        var settings=AtlasUi.Button(card,"Настройки",DisplaySettingsUI.OpenAtlas);AtlasUi.Place((RectTransform)settings.transform,1050,800,180,42);
        exitButton=AtlasUi.Button(card,"Закрыть игру",null);AtlasUi.Place((RectTransform)exitButton.transform,1250,800,185,42);
        _lobbyNavigation=new Selectable[]{nicknameInput,hostButton,_prepareButton,joinCodeInput,joinButton,settings,exitButton};
        for(int i=0;i<_lobbyNavigation.Length;i++)_lobbyNavigation[i].navigation=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=_lobbyNavigation[(i+_lobbyNavigation.Length-1)%_lobbyNavigation.Length],selectOnDown=_lobbyNavigation[(i+1)%_lobbyNavigation.Length]};
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
        float factor=Mathf.Max(.01f,_lobbyCanvas.scaleFactor);
        float w=Screen.width/factor,h=Screen.height/factor;
        _lobbyCard.localScale=Vector3.one;_lobbyCard.sizeDelta=new Vector2(w,h);
        float left=w*.40f,rightX=left+44,rightW=w-left-88,cardW=(rightW-28)/2;
        var art=(RectTransform)_lobbyCard.Find("Illustration");var brand=(RectTransform)_lobbyCard.Find("Brand");
        AtlasUi.Place(brand,0,0,left,h*.16f);AtlasUi.Place(art,0,h*.16f,left,h*.84f);
        var titles=brand.GetComponentsInChildren<Text>();titles[0].fontSize=Mathf.Clamp(Mathf.RoundToInt(left*.11f),44,80);AtlasUi.Place(titles[0].rectTransform,35,12,left-65,h*.11f);titles[1].fontSize=13;AtlasUi.Place(titles[1].rectTransform,38,h*.115f,left-68,28);
        var heading=_lobbyCard.Find("StoryHeading").GetComponent<Text>();heading.fontSize=Mathf.Clamp(Mathf.RoundToInt(rightW*.065f),34,62);AtlasUi.Place(heading.rectTransform,rightX,h*.08f,rightW,h*.15f);
        AtlasUi.Place((RectTransform)_lobbyCard.Find("NameLabel"),rightX,h*.245f,155,45);AtlasUi.Place((RectTransform)nicknameInput.transform,rightX+165,h*.245f,rightW-165,48);nicknameInput.textComponent.fontSize=20;
        float top=h*.36f,ch=h*.41f;
        var master=(RectTransform)_lobbyCard.Find("MasterEntry");var player=(RectTransform)_lobbyCard.Find("PlayerEntry");AtlasUi.Place(master,rightX,top,cardW,ch);AtlasUi.Place(player,rightX+cardW+28,top,cardW,ch);
        foreach(var role in new[]{master,player}){var t=role.GetChild(0).GetComponent<Text>();t.fontSize=36;AtlasUi.Place(t.rectTransform,25,25,cardW-50,65);}
        AtlasUi.Place((RectTransform)hostButton.transform,24,ch*.37f,cardW-48,54);AtlasUi.Place((RectTransform)_prepareButton.transform,24,ch*.37f+65,cardW-48,54);
        var hint=master.GetChild(3).GetComponent<Text>();AtlasUi.Place(hint.rectTransform,24,ch-45,cardW-48,42);
        var codeLabel=player.GetChild(1).GetComponent<Text>();AtlasUi.Place(codeLabel.rectTransform,24,ch*.35f,cardW-48,35);AtlasUi.Place((RectTransform)joinCodeInput.transform,24,ch*.35f+42,cardW-48,50);AtlasUi.Place((RectTransform)joinButton.transform,24,ch*.35f+112,cardW-48,54);
        foreach(var button in new[]{hostButton,_prepareButton,joinButton})button.GetComponentInChildren<Text>().fontSize=18;joinCodeInput.textComponent.fontSize=20;
        AtlasUi.Place(statusText.rectTransform,rightX,top+ch+12,rightW,60);
        var settings=_lobbyNavigation[5];AtlasUi.Place((RectTransform)settings.transform,w-430,h-65,185,42);AtlasUi.Place((RectTransform)exitButton.transform,w-225,h-65,185,42);
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
