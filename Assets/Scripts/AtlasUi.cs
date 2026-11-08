using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Native atlas widgets. All screens are built from these containers, not legacy HUD prefabs.</summary>
public static class AtlasUi
{
    public static readonly Color Forest = new Color(.075f,.19f,.145f,1);
    public static readonly Color Paper = new Color(251/255f,245/255f,231/255f,1);
    public static readonly Color Raised = new Color(.93f,.88f,.77f,1);
    public static readonly Color Ink = new Color(.10f,.24f,.19f,1);
    public static readonly Color Muted = new Color(.38f,.43f,.33f,1);
    public static readonly Color Copper = new Color(.66f,.39f,.20f,1);
    public static readonly Color Light = new Color(.96f,.93f,.83f,1);
    private static Font _body, _title;
    private static TMPro.TMP_FontAsset _fileBrowserBody, _fileBrowserTitle;
    private static bool _fileBrowserFontsInitialized;
    public static Font Body => _body != null ? _body : _body = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    public static Font Title => _title != null ? _title : _title = Font.CreateDynamicFontFromOSFont(new[]{"Georgia","Times New Roman"},32);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void RegisterFileBrowserStyle()
    {
        SimpleFileBrowser.FileBrowser.StyleSurface -= StyleFileBrowserSurface;
        SimpleFileBrowser.FileBrowser.StyleSurface += StyleFileBrowserSurface;
        SimpleFileBrowser.FileBrowser.StyleText -= StyleFileBrowserText;
        SimpleFileBrowser.FileBrowser.StyleText += StyleFileBrowserText;
        SimpleFileBrowser.FileBrowser.StyleIcon -= StyleFileBrowserIcon;
        SimpleFileBrowser.FileBrowser.StyleIcon += StyleFileBrowserIcon;
    }

    private static void StyleFileBrowserSurface(Image image, int radius, bool outline)
    {
        VttUiSkin.Surface(image, image.color, radius, false);
        if (!outline || image.transform.Find("AtlasOutline") != null) return;

        image.gameObject.AddComponent<AtlasGradient>().bottom = .97f;
        var edge = Node(image.transform, "AtlasOutline");
        Fill(edge);
        edge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var border = edge.gameObject.AddComponent<AtlasBorder>();
        border.radius = radius;
        border.color = new Color(.77f, .70f, .57f);
        border.raycastTarget = false;
    }

    private static void StyleFileBrowserText(TMPro.TMP_Text text, bool heading)
    {
        if (!_fileBrowserFontsInitialized)
        {
            _fileBrowserFontsInitialized = true;
            _fileBrowserBody = CreateFileBrowserFont(Body);
            _fileBrowserTitle = CreateFileBrowserFont(Title);
        }
        var font = heading && _fileBrowserTitle != null ? _fileBrowserTitle : _fileBrowserBody;
        if (font != null) text.font = font;
        if (heading) text.fontSize += 4;
    }

    private static TMPro.TMP_FontAsset CreateFileBrowserFont(Font source)
    {
        if (source == null) return null;
        foreach (string family in source.fontNames)
        {
            var font = TMPro.TMP_FontAsset.CreateFontAsset(family, "Regular");
            if (font != null) return font;
        }
        return null;
    }

    private static void StyleFileBrowserIcon(Image image, string key, Color tint)
    {
        var glyph = image.GetComponentInChildren<AtlasGlyph>(true);
        var sprite = key == "map" || key == "menu" ? ReferenceIcon(key) : null;
        image.enabled = sprite != null;
        if (sprite != null)
        {
            image.sprite = sprite;
            image.color = tint;
            if (glyph != null) glyph.enabled = false;
            return;
        }
        if (glyph == null)
        {
            var node = Node(image.transform, "AtlasGlyph");
            Fill(node);
            glyph = node.gameObject.AddComponent<AtlasGlyph>();
            glyph.raycastTarget = false;
        }
        glyph.rectTransform.localRotation = Quaternion.Inverse(image.transform.localRotation);
        glyph.enabled = true;
        glyph.key = key;
        glyph.color = tint;
        glyph.SetVerticesDirty();
    }

    public static void BrandImage(Transform parent, string resource, float x, float y, float width, float height)
    {
        var texture = Resources.Load<Texture2D>(resource);
        if (texture == null) return;
        var node = Node(parent, "DiceboundBrand");
        Place(node, x, y, width, height);
        var picture = Node(node, "Image");
        Fill(picture);
        var image = picture.gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        var fit = picture.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = (float)texture.width / texture.height;
    }

    public static RectTransform Node(Transform parent,string name)
    {
        var go=new GameObject(name,typeof(RectTransform)); go.transform.SetParent(parent,false);
        return go.GetComponent<RectTransform>();
    }
    public static RectTransform Panel(Transform parent,string name,Color color,bool square=false)
    {
        var rt=Node(parent,name); var image=rt.gameObject.AddComponent<Image>();
        if(!square)VttUiSkin.Surface(image,color,7,false);else image.color=color; return rt;
    }
    public static void Fill(RectTransform rt,float left=0,float top=0,float right=0,float bottom=0)
    {
        rt.anchorMin=Vector2.zero; rt.anchorMax=Vector2.one;
        rt.offsetMin=new Vector2(left,bottom); rt.offsetMax=new Vector2(-right,-top);
    }
    public static void Place(RectTransform rt,float x,float y,float width,float height)
    {
        rt.anchorMin=rt.anchorMax=new Vector2(0,1);rt.pivot=new Vector2(0,1);
        rt.anchoredPosition=new Vector2(x,-y);rt.sizeDelta=new Vector2(width,height);
    }
    public static void Height(Component c,float height)
    {
        var el=c.GetComponent<LayoutElement>()??c.gameObject.AddComponent<LayoutElement>();el.preferredHeight=height;el.minHeight=height;
    }
    public static void Width(Component c,float width){var el=c.GetComponent<LayoutElement>()??c.gameObject.AddComponent<LayoutElement>();el.preferredWidth=width;el.minWidth=width;el.flexibleWidth=0;}
    public static RectTransform Column(Transform parent,string name,int padding=12,float gap=8)
    {
        var rt=Node(parent,name);var layout=rt.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding=new RectOffset(padding,padding,padding,padding);layout.spacing=gap;
        layout.childControlHeight=true;layout.childForceExpandHeight=false;layout.childControlWidth=true;layout.childForceExpandWidth=true;
        return rt;
    }
    public static RectTransform Row(Transform parent,string name,float height=40,float gap=8)
    {
        var rt=Node(parent,name);var layout=rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing=gap;layout.childControlWidth=true;layout.childForceExpandWidth=true;
        layout.childControlHeight=true;layout.childForceExpandHeight=true;Height(rt,height);return rt;
    }
    public static RectTransform Scroll(Transform parent,string name,out ScrollRect scroll,int padding=14)
    {
        var root=Panel(parent,name,Paper);scroll=root.gameObject.AddComponent<ScrollRect>();
        if(name=="ContextPanel"||name=="JournalDrawer"){
            root.gameObject.AddComponent<AtlasGradient>().bottom=.98f;
            var outline=Node(root,"PanelBorder");Fill(outline);var border=outline.gameObject.AddComponent<AtlasBorder>();border.radius=7;border.color=new Color(.67f,.48f,.27f);border.raycastTarget=false;
        }
        var viewport=Panel(root,"Viewport",Color.clear);Fill(viewport,4,4,10,4);viewport.gameObject.AddComponent<RectMask2D>();
        var content=Column(viewport,"Content",padding);content.anchorMin=new Vector2(0,1);content.anchorMax=Vector2.one;content.pivot=new Vector2(.5f,1);content.sizeDelta=Vector2.zero;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport=viewport;scroll.content=content;scroll.horizontal=false;scroll.movementType=ScrollRect.MovementType.Clamped;scroll.scrollSensitivity=30;
        var track=Panel(root,"ScrollTrack",new Color(.4f,.4f,.25f,.10f));track.anchorMin=new Vector2(1,0);track.anchorMax=Vector2.one;track.pivot=Vector2.one;track.sizeDelta=new Vector2(4,-16);track.anchoredPosition=new Vector2(-3,-8);
        var handle=Panel(track,"Handle",new Color(.64f,.49f,.29f,.7f));Fill(handle);var bar=track.gameObject.AddComponent<Scrollbar>();bar.handleRect=handle;bar.targetGraphic=handle.GetComponent<Image>();bar.direction=Scrollbar.Direction.BottomToTop;scroll.verticalScrollbar=bar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
        root.gameObject.AddComponent<AtlasPlayerMenuFocus>().Initialize(viewport,content);return content;
    }
    public static Text Text(Transform parent,string value,int size=16,Color? color=null,bool title=false,float height=30)
    {
        var rt=Node(parent,"Text");var t=rt.gameObject.AddComponent<Text>();
        t.text=value;t.font=title?Title:Body;t.fontSize=size;t.color=color??Ink;t.raycastTarget=false;t.supportRichText=false;
        t.font.RequestCharactersInTexture(value??"",size,t.fontStyle);t.alignment=TextAnchor.MiddleLeft;t.horizontalOverflow=HorizontalWrapMode.Wrap;t.verticalOverflow=VerticalWrapMode.Truncate;Height(t,height);return t;
    }
    public static Text Paragraph(Transform parent,string value,int size=15){var text=Text(parent,value,size,height:28);var el=text.GetComponent<LayoutElement>();el.minHeight=28;el.preferredHeight=-1;text.verticalOverflow=VerticalWrapMode.Overflow;return text;}
    public static Button Button(Transform parent,string label,UnityAction action,bool primary=false,float height=40,bool dark=false,string icon=null)
    {
        if(label=="×")return CloseButton(parent,action,dark);
        var rt=Panel(parent,"Button "+label,primary?new Color(.17f,.34f,.28f):dark?Color.white:new Color(1,.98f,.94f));
        VttUiSkin.Surface(rt.GetComponent<Image>(),rt.GetComponent<Image>().color,5,false);
        if(!dark)rt.gameObject.AddComponent<AtlasGradient>();
        var b=rt.gameObject.AddComponent<Button>();b.targetGraphic=rt.GetComponent<Image>();Height(b,height);
        var t=Text(rt,label,14,primary||dark?Light:Ink,height:height);Fill(t.rectTransform,icon==null?12:37,3,10,3);
        var edge=Node(rt,"Border");Fill(edge);var border=edge.gameObject.AddComponent<AtlasBorder>();border.radius=5;border.color=dark?new Color(.52f,.58f,.48f,.35f):new Color(.77f,.70f,.57f,1);border.raycastTarget=false;edge.gameObject.AddComponent<LayoutElement>().ignoreLayout=true;
        var c=b.colors;c.highlightedColor=new Color(.86f,.83f,.73f);c.selectedColor=new Color(.80f,.79f,.65f);c.pressedColor=new Color(.68f,.71f,.56f);c.disabledColor=new Color(.6f,.6f,.6f,.5f);c.fadeDuration=.14f;
        if(dark){c.normalColor=new Color(.89f,.87f,.72f,.03f);c.highlightedColor=new Color(.72f,.77f,.64f,.14f);c.selectedColor=c.highlightedColor;c.pressedColor=new Color(.72f,.77f,.64f,.24f);}
        b.colors=c;
        // Runtime-created buttons should start at their normal tint instead of fading from white.
        b.targetGraphic.CrossFadeColor(c.normalColor * c.colorMultiplier, 0f, true, true);
        if(action!=null)b.onClick.AddListener(action);
        if(icon!=null){var sprite=ReferenceIcon(icon)??VttUiSkin.Icon(icon);if(sprite!=null){var ir=Node(rt,"Icon");ir.anchorMin=ir.anchorMax=new Vector2(0,.5f);ir.pivot=new Vector2(0,.5f);ir.anchoredPosition=new Vector2(10,0);ir.sizeDelta=new Vector2(19,19);var im=ir.gameObject.AddComponent<Image>();im.sprite=sprite;im.color=dark||primary?Light:Ink;im.raycastTarget=false;}}
        UiOverflowTooltip.Attach(t,b.gameObject);return b;
    }
    public static void ToolState(Button button,bool active)
    {
        button.targetGraphic.canvasRenderer.cullTransparentMesh=false;
        button.GetComponent<Image>().color=Color.white;
        var colors=button.colors;colors.normalColor=active?new Color(.14f,.29f,.23f):Color.clear;
        colors.highlightedColor=active?new Color(.19f,.36f,.28f):new Color(.90f,.84f,.73f);
        colors.selectedColor=colors.normalColor;colors.pressedColor=new Color(.77f,.69f,.55f);button.colors=colors;
        button.GetComponentInChildren<Text>().color=active?Light:Ink;
        var icon=button.transform.Find("Icon");if(icon!=null)icon.GetComponent<Image>().color=active?Light:Ink;
    }
    public static InputField Input(Transform parent,string value,string placeholder=null,bool multiline=false,float height=40)
    {
        var rt=Panel(parent,"Input",new Color(1,.98f,.945f));var edge=Node(rt,"Border");Fill(edge);var border=edge.gameObject.AddComponent<AtlasBorder>();border.radius=4;border.color=new Color(.74f,.66f,.51f);border.raycastTarget=false;edge.gameObject.AddComponent<LayoutElement>().ignoreLayout=true;var f=rt.gameObject.AddComponent<InputField>();Height(f,height);
        var text=Text(rt,value,15,height:height);Fill(text.rectTransform,10,5,10,5);text.alignment=multiline?TextAnchor.UpperLeft:TextAnchor.MiddleLeft;
        f.textComponent=text;f.text=value;f.lineType=multiline?InputField.LineType.MultiLineNewline:InputField.LineType.SingleLine;
        if(placeholder!=null){
            var p=Text(rt,placeholder,14,Muted,height:height);Fill(p.rectTransform,10,5,10,5);
            // Assigning InputField.placeholder does not refresh its visibility. Initialize
            // the Graphic now so a later layout/font callback need not disable it mid-rebuild.
            p.enabled=string.IsNullOrEmpty(f.text);f.placeholder=p;
        }
        return f;
    }
    public static void Clear(Transform root)
    {
        var children=new Transform[root.childCount];for(int i=0;i<children.Length;i++)children[i]=root.GetChild(i);
        foreach(var child in children){child.gameObject.SetActive(false);if(Application.isPlaying)Object.Destroy(child.gameObject);else Object.DestroyImmediate(child.gameObject);}
    }
    public static Button NavButton(Transform parent,string label,UnityAction action,string key)
    {
        var button=Button(parent,label,action,dark:true,height:72);
        button.transform.Find("Border").gameObject.SetActive(false);
        button.targetGraphic.canvasRenderer.cullTransparentMesh=false;var colors=button.colors;colors.normalColor=Color.clear;button.colors=colors;
        button.targetGraphic.CrossFadeColor(Color.clear, 0f, true, true);
        var text=button.GetComponentInChildren<Text>();text.fontSize=12;text.alignment=TextAnchor.MiddleCenter;Fill(text.rectTransform,2,38,2,4);
        var icon=Node(button.transform,"Icon");icon.anchorMin=icon.anchorMax=new Vector2(.5f,1);icon.pivot=new Vector2(.5f,1);icon.sizeDelta=new Vector2(22,22);icon.anchoredPosition=new Vector2(0,-8);
        var picture=icon.gameObject.AddComponent<Image>();picture.sprite=ReferenceIcon(key);picture.color=Light;picture.raycastTarget=false;
        return button;
    }
    private static readonly System.Collections.Generic.Dictionary<string,Sprite> ReferenceIcons=new System.Collections.Generic.Dictionary<string,Sprite>();
    public static Sprite ReferenceIcon(string key)
    {
        key=key=="table"?"home":key=="user"?"tokens":key=="users"||key=="users-round"?"players":key=="book"?"library":key=="fa-solid--tools"?"menu":key;
        if(ReferenceIcons.TryGetValue(key,out var cached)&&cached!=null)return cached;
        var texture=Resources.Load<Texture2D>("Atlas/ReferenceIcons/"+key)??Resources.Load<Texture2D>("Atlas/Icons/"+key);if(texture==null)return null;
        return ReferenceIcons[key]=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100);
    }
    public static void Divider(Transform parent,string name="Divider")
    {
        var line=Panel(parent,name,new Color(.80f,.71f,.54f),true);Height(line,1);line.GetComponent<Image>().raycastTarget=false;
    }
    public static void Heading(Transform parent,string title,UnityAction close)
    {
        var heading=Row(parent,"Heading",43);Text(heading,title,25,title:true,height:43);CloseButton(heading,close);Divider(parent,"HeadingDivider");
    }
    public static Button CloseButton(Transform parent,UnityAction close,bool dark=false)
    {
        var rt=Node(parent,"Close");var image=rt.gameObject.AddComponent<Image>();image.color=Color.white;
        // Transparent normal state must remain raycastable after CanvasRenderer culling.
        image.canvasRenderer.cullTransparentMesh=false;
        rt.sizeDelta=new Vector2(34,34);
        var row=parent.GetComponent<HorizontalLayoutGroup>();
        if(row!=null){row.childForceExpandWidth=false;row.childForceExpandHeight=false;row.childAlignment=TextAnchor.MiddleLeft;foreach(Transform child in parent){if(child==rt)continue;var el=child.GetComponent<LayoutElement>();if(el!=null&&el.preferredWidth<0)el.flexibleWidth=1;}}
        var b=rt.gameObject.AddComponent<Button>();b.targetGraphic=image;Height(b,34);Width(b,34);
        var caption=Text(rt,"×",26,dark?Light:Muted,height:36);caption.alignment=TextAnchor.MiddleCenter;Fill(caption.rectTransform);
        var colors=b.colors;colors.normalColor=Color.clear;colors.highlightedColor=dark?new Color(1,1,1,.14f):new Color(.65f,.42f,.22f,.14f);colors.selectedColor=colors.highlightedColor;colors.pressedColor=new Color(.65f,.42f,.22f,.25f);colors.fadeDuration=.14f;b.colors=colors;if(close!=null)b.onClick.AddListener(close);return b;
    }
    public static Button CompactDie(Transform parent,DieType type,UnityAction action)
    {
        var button=Button(parent,type==DieType.d100?"d%":type.ToString(),action,height:38,dark:true);
        var text=button.GetComponentInChildren<Text>();text.fontSize=16;text.fontStyle=FontStyle.Normal;text.alignment=TextAnchor.MiddleCenter;Fill(text.rectTransform,4,2,4,2);Width(button,48);return button;
    }
    public static Button DiceButton(Transform parent,DieType type,UnityAction action,bool dark=false)
    {
        var button=Button(parent,type.ToString(),action,height:50,dark:dark);var caption=button.GetComponentInChildren<Text>();caption.fontSize=12;caption.fontStyle=FontStyle.Normal;caption.alignment=TextAnchor.MiddleCenter;caption.horizontalOverflow=HorizontalWrapMode.Overflow;caption.verticalOverflow=VerticalWrapMode.Truncate;Fill(caption.rectTransform,2,30,2,3);
        var icon=Node(button.transform,"DieIcon");icon.anchorMin=icon.anchorMax=new Vector2(.5f,1);icon.pivot=new Vector2(.5f,1);icon.sizeDelta=new Vector2(22,22);icon.anchoredPosition=new Vector2(0,-5);
        var sprite=VttUiSkin.Icon(type==DieType.d100?"fa6-solid--percent":"mdi--dice-"+type+"-outline");if(sprite!=null){var image=icon.gameObject.AddComponent<Image>();image.sprite=sprite;image.preserveAspect=true;image.color=dark?Light:Ink;image.raycastTarget=false;}else{var glyph=icon.gameObject.AddComponent<AtlasGlyph>();glyph.key="die";glyph.color=dark?Light:Ink;glyph.raycastTarget=false;}return button;
    }
    public static void Select(Button b,bool active,bool dark=false)
    {
        if(b==null)return;
        b.GetComponent<Image>().color=dark?Color.white:active?new Color(.17f,.34f,.28f):Paper;
        if(dark){var colors=b.colors;colors.normalColor=active?new Color(.26f,.38f,.28f,.85f):new Color(.89f,.87f,.72f,.03f);b.colors=colors;}

        var t=b.GetComponentInChildren<Text>();if(t!=null)t.color=active||dark?Light:Ink;
    }
}
