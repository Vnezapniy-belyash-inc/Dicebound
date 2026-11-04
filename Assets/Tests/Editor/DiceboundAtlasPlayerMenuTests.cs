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
}
