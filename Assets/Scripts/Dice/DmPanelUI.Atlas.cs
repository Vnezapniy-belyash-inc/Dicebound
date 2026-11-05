using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public partial class DmPanelUI
{
    public void OpenAtlasPage(int page)
    {
        if(!IsLocalHost||_panel==null)return;
        DiceUI.Instance?.AtlasCloseContext();CharacterSheetUI.Instance?.SetSheetVisible(false);_panel.SetActive(true);ShowPage(Mathf.Clamp(page,0,7));
    }
    private void BuildAtlasWorkspace()
    {
        var canvasObject=new GameObject("AtlasMasterWorkspace",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));canvasObject.transform.SetParent(transform,false);
        _canvas=canvasObject.GetComponent<Canvas>();_canvas.renderMode=RenderMode.ScreenSpaceOverlay;_canvas.sortingOrder=30;
        var scaler=canvasObject.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
        var panel=AtlasUi.Panel(canvasObject.transform,"MasterWorkspace",AtlasUi.Paper);_panel=panel.gameObject;panel.anchorMin=panel.anchorMax=new Vector2(.5f,.5f);panel.pivot=new Vector2(.5f,.5f);panel.sizeDelta=new Vector2(920,660);_panel.SetActive(false);
        var header=AtlasUi.Panel(panel,"Header",AtlasUi.Forest,true);header.anchorMin=new Vector2(0,1);header.anchorMax=Vector2.one;header.pivot=new Vector2(.5f,1);header.sizeDelta=new Vector2(0,62);
        var title=AtlasUi.Text(header,"Мастерская кампании",28,AtlasUi.Light,true,42);AtlasUi.Place(title.rectTransform,22,8,700,44);
        var close=AtlasUi.Button(header,"×",Hide,dark:true);AtlasUi.Place((RectTransform)close.transform,858,10,44,40);
        var nav=AtlasUi.Column(panel,"WorkspaceSections",12,6);nav.gameObject.AddComponent<Image>().color=AtlasUi.Raised;
        nav.anchorMin=Vector2.zero;nav.anchorMax=new Vector2(0,1);nav.offsetMin=Vector2.zero;nav.offsetMax=new Vector2(180,-62);
        _pages=new GameObject[8];_tabs=new Image[8];var titles=new[]{"Карта","Игроки","Бой","Токены","Сцена","Туман","Статблоки","Справочник"};
        for(int i=0;i<8;i++){
            int page=i;var b=AtlasUi.Button(nav,titles[i],()=>ShowPage(page),height:44);_tabs[i]=b.GetComponent<Image>();
            var content=AtlasUi.Scroll(panel,"Workspace "+titles[i],out var scroll,24);var root=(RectTransform)scroll.transform;
            root.anchorMin=Vector2.zero;root.anchorMax=Vector2.one;root.offsetMin=new Vector2(190,10);root.offsetMax=new Vector2(-12,-72);
            var heading=AtlasUi.Text(content,titles[i],27,title:true,height:44);
            var pageRect=AtlasUi.Node(content,"Form "+titles[i]);AtlasUi.Height(pageRect,548);_pages[i]=root.gameObject;
            switch(i){case 0:BuildMapPage(pageRect);break;case 1:BuildPlayersPage(pageRect);break;case 2:BuildInitiativePage(pageRect);break;case 3:BuildTokensPage(pageRect);break;case 4:BuildScenePage(pageRect);break;case 5:BuildFogPage(pageRect);break;case 6:BuildStatBlockPage(pageRect);break;case 7:BuildReferencePage(pageRect);break;}
            ReflowAtlasForm(pageRect);
        }
        ClearStatBlockInputs();ClearReferenceInputs();WatchLibraryDrafts();ShowPage(0);
    }
    // Keep the existing field bindings, but replace the old narrow, absolute form layout.
    private void ReflowAtlasForm(RectTransform form)
    {
        var fields=new List<RectTransform>();
        foreach(RectTransform field in form){if(field.gameObject.activeSelf)fields.Add(field);else{var overlay=field.GetComponent<LayoutElement>()??field.gameObject.AddComponent<LayoutElement>();overlay.ignoreLayout=true;}}
        fields=fields.OrderByDescending(f=>f.anchoredPosition.y).ThenBy(f=>f.anchoredPosition.x).ToList();
        var groups=new List<List<RectTransform>>();
        foreach(var field in fields){
            if(groups.Count==0||Mathf.Abs(groups.Last()[0].anchoredPosition.y-field.anchoredPosition.y)>12)
                groups.Add(new List<RectTransform>());
            groups.Last().Add(field);
        }
        var element=form.GetComponent<LayoutElement>();element.minHeight=0;element.preferredHeight=-1;
        var column=form.gameObject.AddComponent<VerticalLayoutGroup>();column.spacing=10;column.childControlWidth=true;column.childForceExpandWidth=true;column.childControlHeight=true;column.childForceExpandHeight=false;
        foreach(var group in groups){
            float height=Mathf.Max(40,group.Max(f=>f.sizeDelta.y));
            var row=AtlasUi.Row(form,"FormRow",height,12);row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth=false;
            foreach(var field in group){
                var original=field.sizeDelta;
                field.SetParent(row,false);
                var layout=field.GetComponent<LayoutElement>()??field.gameObject.AddComponent<LayoutElement>();
                layout.minWidth=0;layout.preferredWidth=original.x;layout.flexibleWidth=Mathf.Max(1,original.x);layout.preferredHeight=height;
                if(field.GetComponent<Text>() is Text caption){caption.fontSize=Mathf.Max(15,caption.fontSize);caption.color=AtlasUi.Ink;}
                foreach(RectTransform child in field){
                    if(child.anchorMin.x==child.anchorMax.x&&child.sizeDelta.x>=original.x*.8f){
                        float left=child.anchoredPosition.x+child.anchorMin.x*original.x-child.pivot.x*child.sizeDelta.x;float right=original.x-left-child.sizeDelta.x;
                        child.anchorMin=new Vector2(0,child.anchorMin.y);child.anchorMax=new Vector2(1,child.anchorMax.y);
                        var min=child.offsetMin;var max=child.offsetMax;min.x=left;max.x=-right;child.offsetMin=min;child.offsetMax=max;
                    }
                }
            }
        }
    }

    private readonly InputField[] _atlasAbilities=new InputField[6];
    private readonly Text[] _atlasAbilityModifiers=new Text[6];
    private bool _atlasAbilitySync;
    private void BuildAtlasAbilityFields(Transform parent)
    {
        var row=AtlasUi.Row(parent,"AbilityScores",98,6);AtlasUi.Place(row,0,206,350,98);
        var names=new[]{"СИЛ","ЛОВ","ТЕЛ","ИНТ","МДР","ХАР"};
        for(int i=0;i<6;i++){
            int index=i;var card=AtlasUi.Column(row,names[i],5,3);card.gameObject.AddComponent<Image>().color=AtlasUi.Raised;
            var label=AtlasUi.Text(card,names[i],13,height:20);label.alignment=TextAnchor.MiddleCenter;
            var input=AtlasUi.Input(card,"10",height:34);input.contentType=InputField.ContentType.IntegerNumber;input.characterLimit=3;input.textComponent.alignment=TextAnchor.MiddleCenter;input.textComponent.fontSize=18;_atlasAbilities[i]=input;
            _atlasAbilityModifiers[i]=AtlasUi.Text(card,"+0",15,AtlasUi.Muted,height:22);_atlasAbilityModifiers[i].alignment=TextAnchor.MiddleCenter;
            input.onValueChanged.AddListener(v=>{UpdateAtlasModifier(index);if(!_atlasAbilitySync)_statBlockInputs[7].text=string.Join(",",_atlasAbilities.Select(a=>a.text));});
        }
        _statBlockInputs[7].onValueChanged.AddListener(v=>SyncAtlasAbilities());
    }
    private void SyncAtlasAbilities()
    {
        if(_atlasAbilities[0]==null)return;var values=_statBlockInputs[7].text.Split(',');_atlasAbilitySync=true;
        for(int i=0;i<6;i++){_atlasAbilities[i].SetTextWithoutNotify(values.Length==6?values[i].Trim():"10");UpdateAtlasModifier(i);}
        _atlasAbilitySync=false;
    }
    private void UpdateAtlasModifier(int index)
    {
        if(!int.TryParse(_atlasAbilities[index].text,out int score)||score<1||score>30){_atlasAbilityModifiers[index].text="—";return;}
        int modifier=Mathf.FloorToInt((score-10)/2f);_atlasAbilityModifiers[index].text=modifier>=0?"+"+modifier:modifier.ToString();
    }

}
