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
        var texture=Resources.Load<Texture2D>("Atlas/LobbyIllustration");if(texture!=null){var picture=AtlasUi.Node(art,"Artwork");AtlasUi.Fill(picture);var image=picture.gameObject.AddComponent<RawImage>();image.texture=texture;image.raycastTarget=false;art.gameObject.AddComponent<RectMask2D>();var fit=picture.gameObject.AddComponent<AspectRatioFitter>();fit.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent;fit.aspectRatio=(float)texture.width/texture.height;}
        var brand=AtlasUi.Panel(card,"Brand",AtlasUi.Forest);AtlasUi.Place(brand,0,0,580,140);
        var logo=AtlasUi.Node(brand,"Logo");AtlasUi.Place(logo,30,4,520,94);AtlasUi.BrandImage(logo,"Branding/dicebound-logo",0,0,520,94);
        var sub=AtlasUi.Text(brand,"ВИРТУАЛЬНЫЙ СТОЛ ДЛЯ РОЛЕВЫХ ИГР",12,AtlasUi.Light,height:28);sub.name="BrandSubtitle";AtlasUi.Place(sub.rectTransform,35,98,510,28);
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
        float left=w*.40f,available=w-left,rightW=Mathf.Min(900,available-80),rightX=left+(available-rightW)*.5f,cardW=(rightW-24)/2;
        var art=(RectTransform)_lobbyCard.Find("Illustration");var brand=(RectTransform)_lobbyCard.Find("Brand");
        float brandHeight=Mathf.Clamp(h*.16f,90,140);AtlasUi.Place(brand,0,0,left,brandHeight);AtlasUi.Place(art,0,brandHeight,left,h-brandHeight);
        var logo=(RectTransform)brand.Find("Logo");AtlasUi.Place(logo,30,4,left-60,brandHeight-40);var logoImage=(RectTransform)logo.Find("DiceboundBrand");AtlasUi.Place(logoImage,0,0,left-60,brandHeight-40);var subtitle=brand.Find("BrandSubtitle").GetComponent<Text>();subtitle.fontSize=12;AtlasUi.Place(subtitle.rectTransform,34,brandHeight-32,left-60,22);
        float top=Mathf.Max(32,(h-520)*.5f);
        var heading=_lobbyCard.Find("StoryHeading").GetComponent<Text>();heading.fontSize=42;AtlasUi.Place(heading.rectTransform,rightX,top,rightW,70);
        AtlasUi.Place((RectTransform)_lobbyCard.Find("NameLabel"),rightX,top+100,120,40);AtlasUi.Place((RectTransform)nicknameInput.transform,rightX+130,top+100,rightW-130,40);nicknameInput.textComponent.fontSize=17;
        float cardsTop=top+172,ch=300;
        var master=(RectTransform)_lobbyCard.Find("MasterEntry");var player=(RectTransform)_lobbyCard.Find("PlayerEntry");AtlasUi.Place(master,rightX,cardsTop,cardW,ch);AtlasUi.Place(player,rightX+cardW+24,cardsTop,cardW,ch);
        foreach(var role in new[]{master,player}){var title=role.GetChild(0).GetComponent<Text>();title.fontSize=28;AtlasUi.Place(title.rectTransform,22,20,cardW-44,48);}
        AtlasUi.Place((RectTransform)hostButton.transform,22,104,cardW-44,42);AtlasUi.Place((RectTransform)_prepareButton.transform,22,158,cardW-44,42);
        var hint=master.GetChild(3).GetComponent<Text>();hint.fontSize=14;AtlasUi.Place(hint.rectTransform,22,230,cardW-44,48);
        var codeLabel=player.GetChild(1).GetComponent<Text>();codeLabel.fontSize=14;AtlasUi.Place(codeLabel.rectTransform,22,91,cardW-44,26);AtlasUi.Place((RectTransform)joinCodeInput.transform,22,124,cardW-44,40);AtlasUi.Place((RectTransform)joinButton.transform,22,178,cardW-44,42);
        foreach(var button in new[]{hostButton,_prepareButton,joinButton})button.GetComponentInChildren<Text>().fontSize=16;joinCodeInput.textComponent.fontSize=17;
        AtlasUi.Place(statusText.rectTransform,rightX,cardsTop+ch+10,rightW,44);
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
