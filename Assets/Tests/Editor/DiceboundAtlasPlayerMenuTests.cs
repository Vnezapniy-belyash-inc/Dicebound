using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class DiceboundAtlasPlayerMenuTests
{
    private GameObject _root;
    private Component _ui;
    private Type _type;
    private EventSystem _previousEvents;
    private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    private T Field<T>(string name)=>(T)_type.GetField(name,Private).GetValue(_ui);
    [SetUp] public void BuildNativeInterface(){
        _previousEvents=EventSystem.current;_root=new GameObject("Atlas native UI regression");_root.SetActive(false);
        _type=Type.GetType("DiceUI, Assembly-CSharp",true);_ui=_root.AddComponent(_type);
        _type.GetMethod("BuildUIInternal",Private).Invoke(_ui,null);
    }
    [TearDown] public void Cleanup(){UnityEngine.Object.DestroyImmediate(_root);if(_previousEvents==null&&EventSystem.current!=null)UnityEngine.Object.DestroyImmediate(EventSystem.current.gameObject);}
    [Test] public void NativeRootReplacesLegacyBuildersAndStartsHidden(){
        Assert.That(Field<RectTransform>("_nativeRoot").gameObject.activeSelf,Is.False);
        Assert.That(_type.GetMethod("BuildLeftSidebar",Private),Is.Null);
        Assert.That(_type.GetMethod("BuildTopBar",Private),Is.Null);
        Assert.That(Field<RectTransform>("_nativeHeader"),Is.Not.Null);
        Assert.That(Field<RectTransform>("_nativeToolbar"),Is.Not.Null);
        Assert.That(Field<RectTransform>("_nativeInitiative"),Is.Not.Null);
    }
    [Test] public void ReopeningMapToolDoesNotDisablePlaceholderDuringCanvasRebuild()
    {
        _root.SetActive(true);Field<RectTransform>("_nativeRoot").gameObject.SetActive(true);
        _type.GetField("_nativeHost",Private).SetValue(_ui,true);
        for(int i=0;i<3;i++){
            _type.GetMethod("NativeSelectTool",Private).Invoke(_ui,new object[]{7});
            Canvas.ForceUpdateCanvases();
        }
    }
    [TestCase("1.00",false)] [TestCase("",true)] [TestCase(null,true)]
    public void InputPlaceholderHasCorrectVisibilityBeforeFirstLayout(string value,bool visible)
    {
        var widgets=Type.GetType("AtlasUi, Assembly-CSharp",true);
        var input=(InputField)widgets.GetMethod("Input").Invoke(null,new object[]{Field<RectTransform>("_nativeRoot"),value,"Hint",false,40f});
        Assert.That(input.placeholder.enabled,Is.EqualTo(visible));
        Assert.That(input.placeholder.gameObject.activeSelf,Is.True,"InputField controls the Graphic, not the placeholder GameObject.");
        input.SetTextWithoutNotify("");Assert.That(input.placeholder.enabled,Is.True);
        input.SetTextWithoutNotify("2.00");Assert.That(input.placeholder.enabled,Is.False);
    }
    private sealed class InputLabelLayoutProbe : ICanvasElement
    {
        public Transform transform {get;set;}
        public Action DirtyLabel;
        public void Rebuild(CanvasUpdate update){if(update==CanvasUpdate.Layout)DirtyLabel();}
        public void LayoutComplete(){}
        public void GraphicUpdateComplete(){}
        public bool IsDestroyed()=>transform==null;
    }
    [Test] public void PopulatedInputCanRefreshItsLabelInsideLayoutWithoutRemovingPlaceholder()
    {
        _root.SetActive(true);Field<RectTransform>("_nativeRoot").gameObject.SetActive(true);
        var widgets=Type.GetType("AtlasUi, Assembly-CSharp",true);
        var input=(InputField)widgets.GetMethod("Input").Invoke(null,new object[]{Field<RectTransform>("_nativeRoot"),"1.00","Масштаб",false,40f});
        var probe=new InputLabelLayoutProbe{transform=input.transform,DirtyLabel=()=>input.textComponent.SetVerticesDirty()};
        CanvasUpdateRegistry.RegisterCanvasElementForLayoutRebuild(probe);
        Canvas.ForceUpdateCanvases();
        Assert.That(input.placeholder.enabled,Is.False);
    }
    [TestCase(true)] [TestCase(false)]
    public void RightSectionsReplaceEachOtherAndRepeatedSectionCloses(bool host)
    {
        _type.GetField("_nativeHost",Private).SetValue(_ui,host);
        _type.GetMethod("NativeOpenPeople",Private).Invoke(_ui,null);
        Assert.That(Field<RectTransform>("_nativeDrawer").gameObject.activeSelf,Is.True);
        _type.GetMethod("NativeToggleJournal",Private).Invoke(_ui,null);
        Assert.That(Field<RectTransform>("_nativeDrawer").gameObject.activeSelf,Is.False);
        Assert.That(Field<GameObject>("_logPanelGO").activeSelf,Is.True);
        string section=host?"NativeOpenTokens":"NativeSessionMenu";
        object[] args=host?new object[]{false}:null;
        _type.GetMethod(section,Private).Invoke(_ui,args);
        Assert.That(Field<GameObject>("_logPanelGO").activeSelf,Is.False);
        Assert.That(Field<RectTransform>("_nativeDrawer").gameObject.activeSelf,Is.True);
        var inputs=Field<RectTransform>("_nativeDrawerContent").GetComponentsInChildren<InputField>(true);
        Assert.That(inputs.Length,Is.EqualTo(host?3:0));
        _type.GetMethod(section,Private).Invoke(_ui,args);
        Assert.That(Field<RectTransform>("_nativeDrawer").gameObject.activeSelf,Is.False);
    }
    [Test] public void PlayerCreationIsOneClickWithoutOpeningAForm()
    {
        _type.GetMethod("NativeBuildPlayerMenu",Private).Invoke(_ui,null);
        var button=Field<RectTransform>("_nativePlayerContent").GetComponentsInChildren<Button>(true).Single(b=>b.name=="Button Создать токен");
        Assert.That(button.transform.Find("Icon"),Is.Not.Null);
        button.onClick.Invoke();
        Assert.That(Field<RectTransform>("_nativeDrawer").gameObject.activeSelf,Is.False);
        Assert.That(Field<RectTransform>("_nativeDrawerContent").GetComponentsInChildren<InputField>(true),Is.Empty);
    }
    [Test] public void ScenesCannotCoverTokensOrJournalAndToggleClosed()
    {
        var campaign=Type.GetType("SceneFileStore, Assembly-CSharp",true).GetField("_campaign",BindingFlags.NonPublic|BindingFlags.Static);
        var previous=campaign.GetValue(null);
        try{
            _type.GetField("_nativeHost",Private).SetValue(_ui,true);
            _type.GetMethod("NativeOpenScenes",Private).Invoke(_ui,null);
            var scenes=Field<RectTransform>("_nativeSceneList");Assert.That(scenes.gameObject.activeSelf,Is.True);
            _type.GetMethod("NativeOpenTokens",Private).Invoke(_ui,new object[]{false});
            Assert.That(scenes.gameObject.activeSelf,Is.False);
            _type.GetMethod("NativeOpenScenes",Private).Invoke(_ui,null);
            Assert.That(Field<RectTransform>("_nativeDrawer").gameObject.activeSelf,Is.False);
            _type.GetMethod("NativeToggleJournal",Private).Invoke(_ui,null);
            Assert.That(scenes.gameObject.activeSelf,Is.False);
            _type.GetMethod("NativeOpenScenes",Private).Invoke(_ui,null);
            Assert.That(Field<GameObject>("_logPanelGO").activeSelf,Is.False);
            _type.GetMethod("NativeOpenScenes",Private).Invoke(_ui,null);
            Assert.That(scenes.gameObject.activeSelf,Is.False);
        }finally{campaign.SetValue(null,previous);}
    }
    [Test] public void AreaPaletteUsesCatalogAndNeedsReadyArea(){
        _type.GetMethod("NativeSelectTool",Private).Invoke(_ui,new object[]{3});
        var area=Field<Button[]>("_nativeEffects");
        var colors=(Color[])Type.GetType("CellMarker, Assembly-CSharp",true).GetField("TextureColors").GetValue(null);
        Assert.That(area.Length,Is.EqualTo(colors.Length));
        for(int i=0;i<area.Length;i++){Assert.That(area[i].interactable,Is.False);var expected=colors[i];expected.a=1;Assert.That(area[i].transform.Find("Swatch").GetComponent<Image>().color,Is.EqualTo(expected));}
        _type.GetMethod("NativeSelectTool",Private).Invoke(_ui,new object[]{4});
        Assert.That(Field<Button[]>("_nativeEffects").All(b=>b.interactable),Is.True);
    }
    [Test] public void DrawersAndPlayerNavigationHaveRealScrollAndClipping(){
        var drawer=Field<RectTransform>("_nativeDrawer").GetComponent<ScrollRect>();Assert.That(drawer.vertical,Is.True);Assert.That(drawer.horizontal,Is.False);Assert.That(drawer.verticalScrollbar,Is.Not.Null);Assert.That(drawer.viewport.GetComponent<RectMask2D>(),Is.Not.Null);
        var content=Field<RectTransform>("_nativePlayerContent");Assert.That(content.GetComponent<VerticalLayoutGroup>(),Is.Not.Null);Assert.That(content.GetComponent<ContentSizeFitter>().verticalFit,Is.EqualTo(ContentSizeFitter.FitMode.PreferredSize));
    }
    [Test] public void CloseControlKeepsItsOwnWidthAndTransparentRaycastSurface()
    {
        var ui=Type.GetType("AtlasUi, Assembly-CSharp",true);
        var drawer=Field<RectTransform>("_nativeDrawerContent");
        ui.GetMethod("Heading").Invoke(null,new object[]{drawer,"Long heading",new UnityEngine.Events.UnityAction(()=>{})});
        var heading=drawer.Find("Heading").GetComponent<RectTransform>();
        heading.sizeDelta=new Vector2(360,43);
        LayoutRebuilder.ForceRebuildLayoutImmediate(heading);
        var close=heading.Find("Close").GetComponent<Button>();
        Assert.That(((RectTransform)close.transform).rect.width,Is.EqualTo(34).Within(.1f));
        Assert.That(heading.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth,Is.False);
        Assert.That(close.targetGraphic.canvasRenderer.cullTransparentMesh,Is.False);
        Assert.That(close.targetGraphic.raycastTarget,Is.True);
        Assert.That(close.colors.highlightedColor.a,Is.GreaterThan(close.colors.normalColor.a));
        Assert.That(drawer.Find("HeadingDivider"),Is.Not.Null);
    }
    [Test] public void MasterKeepsMovementRequestsButOmitsPlayerSheetAndMenuToggle()
    {
        _type.GetField("_nativeHost",Private).SetValue(_ui,true);_type.GetMethod("NativeLayout",Private).Invoke(_ui,null);
        Assert.That(Field<Button>("_nativeMenuButton").gameObject.activeSelf,Is.False);
        Assert.That(Field<RectTransform>("_nativeRail").GetComponentsInChildren<Text>(true).Any(t=>t.text=="Мой лист"),Is.False);
        Assert.That(Field<Button>("_movementRequestsButton").gameObject.activeSelf,Is.True);
        _type.GetField("_nativeHost",Private).SetValue(_ui,false);_type.GetMethod("NativeLayout",Private).Invoke(_ui,null);
        Assert.That(Field<Button>("_nativeMenuButton").gameObject.activeSelf,Is.True);
    }

    [Test] public void ConfirmationCanRenderAndReceiveClicksAboveMasterWorkspace()
    {
        _root.SetActive(true);Field<RectTransform>("_nativeRoot").gameObject.SetActive(true);var confirmation=Field<GameObject>("_confirmPanel");_type.GetMethod("ConfirmAction").Invoke(_ui,new object[]{"Delete scene?","Test",new Action(()=>{})});Canvas.ForceUpdateCanvases();var canvas=confirmation.GetComponent<Canvas>();
        Assert.That(canvas.overrideSorting,Is.True);Assert.That(canvas.sortingOrder,Is.GreaterThan(30));
        Assert.That(confirmation.GetComponent<GraphicRaycaster>(),Is.Not.Null);
    }

    [TestCase(true,780)] [TestCase(true,1200)] [TestCase(false,780)] [TestCase(false,1200)]
    public void LargeInitiativeStripKeepsEveryEntryClippedAndControlsInside(bool host,int width)
    {
        var trackerType=Type.GetType("InitiativeTracker, Assembly-CSharp",true);
        var singleton=trackerType.GetProperty("Instance");var previous=singleton.GetValue(null);
        var trackerObject=new GameObject("Large initiative regression");trackerObject.SetActive(false);
        var tracker=trackerObject.AddComponent(trackerType);
        try{
            singleton.SetValue(null,tracker);
            string entries=string.Join(",",Enumerable.Range(1,128).Select(i=>"{\"id\":"+i+",\"name\":\"Long participant name number "+i+" with extra text\",\"initiative\":"+(129-i)+"}"));
            trackerType.GetMethod("ParseData",Private).Invoke(tracker,new object[]{"{\"currentIndex\":93,\"round\":12,\"activeParticipantId\":\"94\",\"entries\":["+entries+"]}"});
            _root.SetActive(true);Field<RectTransform>("_nativeRoot").gameObject.SetActive(true);
            _type.GetField("_nativeHost",Private).SetValue(_ui,host);
            var strip=Field<RectTransform>("_nativeInitiative");strip.anchorMin=strip.anchorMax=new Vector2(0,1);strip.pivot=new Vector2(0,1);strip.sizeDelta=new Vector2(width,46);
            _type.GetMethod("NativeUpdateInitiative",Private).Invoke(_ui,null);
            Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(strip);
            var scroll=Field<ScrollRect>("_nativeInitiativeScroll");
            Assert.That(scroll.content.childCount,Is.EqualTo(128));
            Assert.That(scroll.content.rect.width,Is.GreaterThan(scroll.viewport.rect.width));
            Assert.That(scroll.viewport.GetComponent<RectMask2D>(),Is.Not.Null);
            var focus=scroll.GetComponent(Type.GetType("AtlasScrollFocus, Assembly-CSharp",true));focus.GetType().GetMethod("LateUpdate",Private).Invoke(focus,null);
            var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,scroll.content.GetChild(93));
            Assert.That(bounds.min.x,Is.GreaterThanOrEqualTo(scroll.viewport.rect.xMin-.1f));
            Assert.That(bounds.max.x,Is.LessThanOrEqualTo(scroll.viewport.rect.xMax+.1f));
            Assert.That(scroll.content.GetChild(93).GetComponentInChildren<Text>().text,Does.StartWith("▶ "));
            var row=strip.Find("Initiative");
            Assert.That(row.Find("NextTurn")!=null,Is.EqualTo(host));
            foreach(Transform control in row){if(control.name=="InitiativeOrder")continue;
                var box=RectTransformUtility.CalculateRelativeRectTransformBounds(strip,(RectTransform)control);
                Assert.That(box.min.x,Is.GreaterThanOrEqualTo(strip.rect.xMin-.1f),control.name);
                Assert.That(box.max.x,Is.LessThanOrEqualTo(strip.rect.xMax+.1f),control.name);
            }
        }finally{singleton.SetValue(null,previous);UnityEngine.Object.DestroyImmediate(trackerObject);}
    }
    [Test] public void ClosedInitiativeListRevealsCurrentActorWhenOpened()
    {
        var type=Type.GetType("InitiativeTracker, Assembly-CSharp",true);var singleton=type.GetProperty("Instance");var previous=singleton.GetValue(null);
        var root=new GameObject("Closed initiative list regression");root.SetActive(false);var tracker=root.AddComponent(type);
        try{
            singleton.SetValue(null,tracker);
            string entries=string.Join(",",Enumerable.Range(1,128).Select(i=>"{\"id\":"+i+",\"name\":\"Participant "+i+"\",\"initiative\":"+(129-i)+"}"));
            type.GetMethod("ParseData",Private).Invoke(tracker,new object[]{"{\"currentIndex\":93,\"round\":12,\"entries\":["+entries+"]}"});
            type.GetMethod("BuildAtlasInitiative",Private).Invoke(tracker,null);
            var content=(RectTransform)type.GetField("_content",Private).GetValue(tracker);
            var scroll=content.GetComponentInParent<ScrollRect>(true);
            var focus=scroll.GetComponent(Type.GetType("AtlasScrollFocus, Assembly-CSharp",true));
            Assert.That(focus,Is.Not.Null,"Closed ancestors must not prevent focus setup.");
            root.SetActive(true);((GameObject)type.GetField("_panel",Private).GetValue(tracker)).SetActive(true);
            Canvas.ForceUpdateCanvases();LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            focus.GetType().GetMethod("LateUpdate",Private).Invoke(focus,null);
            var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,content.GetChild(93));
            Assert.That(bounds.min.y,Is.GreaterThanOrEqualTo(scroll.viewport.rect.yMin-.1f));
            Assert.That(bounds.max.y,Is.LessThanOrEqualTo(scroll.viewport.rect.yMax+.1f));
            Assert.That(content.childCount,Is.EqualTo(128));
            Assert.That(content.GetChild(93).Find("CurrentTurnAccent"),Is.Not.Null);
        }finally{singleton.SetValue(null,previous);UnityEngine.Object.DestroyImmediate(root);}
    }

}
