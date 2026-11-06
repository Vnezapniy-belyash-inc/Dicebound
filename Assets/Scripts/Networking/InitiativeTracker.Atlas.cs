using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public partial class InitiativeTracker
{
    public struct AtlasEntry { public int id, initiative; public string name,tokenId; public bool current; }
    public int AtlasRound => _round;
    public AtlasEntry[] GetAtlasEntries()
    {
        var entries=_entries.ToArray();int index=_waitingForTurn?-1:_currentIndex;
        if(IsHost&&FogManager.Instance?.Preview==true){var view=InitiativeProjection.Project(_entries,index,e=>CanClientSeeEntry(NetworkManager.LocalClientId,e,true));entries=view.Entries;index=view.ActiveIndex;}
        var result=new AtlasEntry[entries.Length];
        for(int i=0;i<entries.Length;i++)result[i]=new AtlasEntry{id=entries[i].id,name=entries[i].name,initiative=entries[i].initiative,tokenId=entries[i].tokenId,current=i==index};
        return result;
    }
    private void BuildAtlasInitiative()
    {
        var root=new GameObject("AtlasInitiative",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));root.transform.SetParent(transform,false);
        _canvas=root.GetComponent<Canvas>();_canvas.renderMode=RenderMode.ScreenSpaceOverlay;_canvas.sortingOrder=35;
        var scaler=root.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
        var panel=AtlasUi.Panel(root.transform,"InitiativeEditor",AtlasUi.Paper);_panel=panel.gameObject;panel.anchorMin=panel.anchorMax=new Vector2(.5f,.5f);panel.pivot=new Vector2(.5f,.5f);panel.sizeDelta=new Vector2(960,620);
        var heading=AtlasUi.Row(panel,"Heading",46);AtlasUi.Place(heading,20,16,920,46);_roundText=AtlasUi.Text(heading,"Инициатива",26,title:true,height:46);AtlasUi.Button(heading,"×",()=>SetVisible(false),height:40);
        var buttons=AtlasUi.Row(panel,"Actions",40);AtlasUi.Place(buttons,20,78,920,40);_hostButtons=buttons.gameObject;
        AtlasUi.Button(buttons,"+ Добавить",ShowAddPanel);AtlasUi.Button(buttons,"Следующий ход →",NextTurn,true);AtlasUi.Button(buttons,"Очистить…",RequestClearAll);_cancelRollButton=AtlasUi.Button(buttons,"Отмена броска",CancelInitiativeFromJournal).gameObject;
        _content=AtlasUi.Scroll(panel,"Participants",out var scroll,16);_viewport=scroll.gameObject;var viewport=(RectTransform)scroll.transform;AtlasUi.Fill(viewport,20,130,20,16);
        _emptyMessage=AtlasUi.Text(panel,"Нет участников боя",19,AtlasUi.Muted,height:60).gameObject;AtlasUi.Place((RectTransform)_emptyMessage.transform,30,150,900,60);
        var add=AtlasUi.Column(root.transform,"AddParticipant",24,10);add.gameObject.AddComponent<Image>().color=AtlasUi.Paper;add.anchorMin=add.anchorMax=new Vector2(.5f,.5f);add.pivot=new Vector2(.5f,.5f);add.sizeDelta=new Vector2(550,440);
        AtlasUi.Text(add,"Добавить участника",27,title:true,height:45);AtlasUi.Text(add,"Имя",15,height:25);_nameInput=AtlasUi.Input(add,"","Имя участника");AtlasUi.Text(add,"Инициатива / ХП",15,height:25);var values=AtlasUi.Row(add,"Values",40);_initInput=AtlasUi.Input(values,"0","Инициатива");_hpInput=AtlasUi.Input(values,"0","ХП");_addErrorText=AtlasUi.Text(add,"",13,AtlasUi.Copper,height:36);
        var actions=AtlasUi.Row(add,"Actions",42);AtlasUi.Button(actions,"Отмена",()=>_addPanel.SetActive(false));AtlasUi.Button(actions,"Добавить",SubmitAdd,true);
        _addPanel=UiModalScope.Wrap(add.gameObject,_nameInput);_addPanel.SetActive(false);_panel.SetActive(false);RebuildAtlasRows();UpdateHostControls();
    }
    private bool _atlasRebuildingRows;
    private int _atlasActiveEntry=-1;
    private void RebuildAtlasRows()
    {
        if(_content==null||_atlasRebuildingRows)return;
        _atlasRebuildingRows=true;
        try{
            var scroll=_content.GetComponentInParent<ScrollRect>(true);float position=scroll!=null?scroll.verticalNormalizedPosition:1;
            AtlasUi.Clear(_content);var shown=GetAtlasEntries();bool master=IsHost&&FogManager.Instance?.Preview!=true;
            int current=Array.FindIndex(shown,e=>e.current);int currentId=current>=0?shown[current].id:-1;bool reveal=currentId!=_atlasActiveEntry;_atlasActiveEntry=currentId;
            _roundText.text="Раунд "+_round+" · Очерёдность ходов";_emptyMessage.SetActive(shown.Length==0);_viewport.SetActive(shown.Length>0);RectTransform active=null;
            foreach(var e in shown){
                int id=e.id;var row=AtlasUi.Row(_content,"Participant "+id,62,10);var layout=row.GetComponent<HorizontalLayoutGroup>();layout.childForceExpandWidth=false;layout.childAlignment=TextAnchor.MiddleLeft;layout.padding=new RectOffset(8,6,3,3);
                var background=row.gameObject.AddComponent<Image>();VttUiSkin.Surface(background,e.current?AtlasUi.Raised:AtlasUi.Paper,5,false);background.raycastTarget=false;
                if(e.current){active=row;var accent=AtlasUi.Panel(row,"CurrentTurnAccent",AtlasUi.Copper,true);accent.gameObject.AddComponent<LayoutElement>().ignoreLayout=true;accent.anchorMin=Vector2.zero;accent.anchorMax=new Vector2(0,1);accent.sizeDelta=new Vector2(3,0);accent.anchoredPosition=Vector2.zero;accent.GetComponent<Image>().raycastTarget=false;}
                var name=AtlasUi.Text(row,(e.current?"▶ ":"")+e.name,17,title:e.current,height:56);var nameLayout=name.GetComponent<LayoutElement>();nameLayout.minWidth=0;nameLayout.preferredWidth=0;nameLayout.flexibleWidth=1;name.raycastTarget=true;UiOverflowTooltip.Attach(name,name.gameObject);
                if(master){
                    var scoreColumn=AtlasUi.Column(row,"Initiative",0,2);AtlasUi.Width(scoreColumn,84);AtlasUi.Text(scoreColumn,"Инициатива",12,AtlasUi.Muted,height:18);
                    var score=AtlasUi.Input(scoreColumn,e.initiative.ToString(),height:34);score.name="InitiativeValue";score.contentType=InputField.ContentType.IntegerNumber;score.characterLimit=4;score.onEndEdit.AddListener(v=>{if(!_atlasRebuildingRows)SetInitiativeById(id,v);});
                    if(!string.IsNullOrEmpty(e.tokenId)){string token=e.tokenId;var roll=AtlasUi.Button(row,"d20",()=>RollTokenInitiative(token),height:34);AtlasUi.Width(roll,48);}
                    var hpColumn=AtlasUi.Column(row,"HitPoints",0,2);AtlasUi.Width(hpColumn,90);AtlasUi.Text(hpColumn,"ХП",12,AtlasUi.Muted,height:18);
                    var linked=TokenController.FindSceneToken(e.tokenId);var hp=AtlasUi.Input(hpColumn,(linked!=null?linked.VisibleCurrentHp:_hpById.GetValueOrDefault(id)).ToString(),height:34);hp.contentType=InputField.ContentType.IntegerNumber;hp.onEndEdit.AddListener(v=>{if(!_atlasRebuildingRows)SetHpById(id,v);});
                    AtlasUi.Button(row,"×",()=>RemoveEntryById(id),height:34);
                }else{var score=AtlasUi.Text(row,e.initiative.ToString(),19,height:56);AtlasUi.Width(score,80);}
            }
            if(scroll!=null){var focus=scroll.GetComponent<AtlasScrollFocus>()??scroll.gameObject.AddComponent<AtlasScrollFocus>();focus.Initialize(scroll,active,position,false,reveal);}
        }finally{_atlasRebuildingRows=false;}
    }
}
