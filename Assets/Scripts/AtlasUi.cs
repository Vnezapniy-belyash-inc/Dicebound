using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Native atlas widgets. All screens are built from these containers, not legacy HUD prefabs.</summary>
public static class AtlasUi
{
    public static readonly Color Forest = new Color(.075f,.19f,.145f,1);
    public static readonly Color Paper = new Color(.98f,.95f,.88f,1);
    public static readonly Color Raised = new Color(.93f,.88f,.77f,1);
    public static readonly Color Ink = new Color(.10f,.24f,.19f,1);
    public static readonly Color Muted = new Color(.38f,.43f,.33f,1);
    public static readonly Color Copper = new Color(.66f,.39f,.20f,1);
    public static readonly Color Light = new Color(.96f,.93f,.83f,1);
    private static Font _body, _title;
    public static Font Body => _body != null ? _body : _body = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    public static Font Title => _title != null ? _title : _title = Font.CreateDynamicFontFromOSFont(new[]{"Georgia","Times New Roman"},32);

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
        if(!square)VttUiSkin.Surface(image,color,10,false);else image.color=color; return rt;
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
        var rt=Panel(parent,"Button "+label,primary?Forest:dark?new Color(.11f,.25f,.18f):Paper);
        var b=rt.gameObject.AddComponent<Button>();b.targetGraphic=rt.GetComponent<Image>();Height(b,height);
        var t=Text(rt,label,16,primary||dark?Light:Ink,height:height);Fill(t.rectTransform,icon==null?12:37,3,10,3);
        var edge=Node(rt,"Border");Fill(edge);var border=edge.gameObject.AddComponent<AtlasBorder>();border.color=new Color(.60f,.43f,.24f,.45f);border.raycastTarget=false;edge.gameObject.AddComponent<LayoutElement>().ignoreLayout=true;
        var c=b.colors;c.highlightedColor=new Color(.86f,.83f,.73f);c.selectedColor=new Color(.80f,.79f,.65f);c.pressedColor=new Color(.68f,.71f,.56f);c.disabledColor=new Color(.6f,.6f,.6f,.5f);b.colors=c;
        if(action!=null)b.onClick.AddListener(action);
        if(icon!=null){var sprite=VttUiSkin.Icon(icon);if(sprite!=null){var ir=Node(rt,"Icon");Place(ir,11,11,18,18);var im=ir.gameObject.AddComponent<Image>();im.sprite=sprite;im.color=dark||primary?Light:Ink;im.raycastTarget=false;}}
        UiOverflowTooltip.Attach(t,b.gameObject);return b;
    }
    public static InputField Input(Transform parent,string value,string placeholder=null,bool multiline=false,float height=40)
    {
        var rt=Panel(parent,"Input",new Color(1,.985f,.95f));var f=rt.gameObject.AddComponent<InputField>();Height(f,height);
        var text=Text(rt,value,15,height:height);Fill(text.rectTransform,10,5,10,5);text.alignment=multiline?TextAnchor.UpperLeft:TextAnchor.MiddleLeft;
        f.textComponent=text;f.text=value;f.lineType=multiline?InputField.LineType.MultiLineNewline:InputField.LineType.SingleLine;
        if(placeholder!=null){var p=Text(rt,placeholder,14,Muted,height:height);Fill(p.rectTransform,10,5,10,5);f.placeholder=p;}
        return f;
    }
    public static void Clear(Transform root)
    {
        var children=new Transform[root.childCount];for(int i=0;i<children.Length;i++)children[i]=root.GetChild(i);
        foreach(var child in children){child.gameObject.SetActive(false);if(Application.isPlaying)Object.Destroy(child.gameObject);else Object.DestroyImmediate(child.gameObject);}
    }
    public static Button NavButton(Transform parent,string label,UnityAction action,string key)
    {
        var button=Button(parent,label,action,dark:true,height:76);var text=button.GetComponentInChildren<Text>();text.fontSize=14;text.alignment=TextAnchor.MiddleCenter;Fill(text.rectTransform,2,42,2,3);
        var icon=Node(button.transform,"Icon");icon.anchorMin=icon.anchorMax=new Vector2(.5f,1);icon.pivot=new Vector2(.5f,1);icon.sizeDelta=new Vector2(30,30);icon.anchoredPosition=new Vector2(0,-8);var texture=Resources.Load<Texture2D>("Atlas/Icons/"+key);if(texture!=null){texture.filterMode=FilterMode.Bilinear;var picture=icon.gameObject.AddComponent<RawImage>();picture.texture=texture;picture.color=Light;picture.raycastTarget=false;}else{var glyph=icon.gameObject.AddComponent<AtlasGlyph>();glyph.key=key;glyph.color=Light;glyph.raycastTarget=false;}return button;
    }
    public static Button CloseButton(Transform parent,UnityAction close,bool dark=false)
    {
        var rt=Node(parent,"Close");var image=rt.gameObject.AddComponent<Image>();image.color=Color.white;
        var b=rt.gameObject.AddComponent<Button>();b.targetGraphic=image;Height(b,36);Width(b,36);
        var caption=Text(rt,"×",26,dark?Light:Muted,height:36);caption.alignment=TextAnchor.MiddleCenter;Fill(caption.rectTransform);
        var colors=b.colors;colors.normalColor=Color.clear;colors.highlightedColor=new Color(1,1,1,.10f);colors.selectedColor=new Color(1,1,1,.10f);colors.pressedColor=new Color(1,1,1,.18f);b.colors=colors;if(close!=null)b.onClick.AddListener(close);return b;
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
        if(b==null)return;b.GetComponent<Image>().color=active?Copper:dark?new Color(.11f,.25f,.18f):Paper;
        var t=b.GetComponentInChildren<Text>();if(t!=null)t.color=active||dark?Light:Ink;
    }
}
