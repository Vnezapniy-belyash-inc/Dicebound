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
        if(IsHost&&FogManager.Instance?.Preview==true){var view=InitiativeProjection.Project(_entries,_currentIndex,e=>CanClientSeeEntry(NetworkManager.LocalClientId,e,true));entries=view.Entries;index=view.ActiveIndex;}
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
    private void RebuildAtlasRows()
    {
        if(_content==null)return;AtlasUi.Clear(_content);var shown=GetAtlasEntries();bool master=IsHost&&FogManager.Instance?.Preview!=true;
        _roundText.text="Раунд "+_round+" · Очерёдность ходов";_emptyMessage.SetActive(shown.Length==0);_viewport.SetActive(shown.Length>0);
        foreach(var e in shown){int id=e.id;var row=AtlasUi.Row(_content,"Participant "+id,56);AtlasUi.Text(row,(e.current?"▶ ":"")+e.name,18,title:e.current,height:56);
            if(master){var score=AtlasUi.Input(row,e.initiative.ToString());score.onEndEdit.AddListener(v=>SetInitiativeById(id,v));if(!string.IsNullOrEmpty(e.tokenId)){string token=e.tokenId;AtlasUi.Button(row,"d20",()=>ArmInitiativeFromJournal(token),height:40);}var linked=TokenController.FindSceneToken(e.tokenId);var hp=AtlasUi.Input(row,(linked!=null?linked.VisibleCurrentHp:_hpById.GetValueOrDefault(id)).ToString());hp.onEndEdit.AddListener(v=>SetHpById(id,v));AtlasUi.Button(row,"×",()=>RemoveEntryById(id),height:40);}
            else AtlasUi.Text(row,e.initiative.ToString(),19,height:56);
        }
    }
}

