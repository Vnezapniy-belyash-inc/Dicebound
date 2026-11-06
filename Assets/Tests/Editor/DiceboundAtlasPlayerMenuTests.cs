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

}
