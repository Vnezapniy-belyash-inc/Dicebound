using UnityEngine;
using UnityEngine.UI;

public partial class CharacterSheetUI
{
    private InputField _atlasHp, _atlasMaxHp, _atlasAc, _atlasLevel, _atlasProficiency;
    private void RefreshAtlasCharacter(){if(_atlasHp)_atlasHp.SetTextWithoutNotify(_cd.currentHP.ToString());if(_atlasMaxHp)_atlasMaxHp.SetTextWithoutNotify(_cd.maxHP.ToString());if(_atlasAc)_atlasAc.SetTextWithoutNotify(_cd.armorClass.ToString());if(_atlasLevel)_atlasLevel.SetTextWithoutNotify(_cd.level.ToString());if(_atlasProficiency)_atlasProficiency.SetTextWithoutNotify(_cd.proficiencyBonus.ToString());}
    private void BuildAtlasCharacter()
    {
        _cd=characterData!=null?characterData:FindAnyObjectByType<CharacterData>();if(_cd==null)return;
        var canvas=new GameObject("AtlasCharacter",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));canvas.transform.SetParent(transform,false);
        _cv=canvas.GetComponent<Canvas>();_cv.renderMode=RenderMode.ScreenSpaceOverlay;_cv.sortingOrder=25;
        var scaler=canvas.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
        var panel=AtlasUi.Panel(canvas.transform,"CharacterSheet",AtlasUi.Paper);_pn=panel.gameObject;
        panel.anchorMin=panel.anchorMax=new Vector2(.5f,.5f);panel.pivot=new Vector2(.5f,.5f);panel.sizeDelta=new Vector2(960,710);
        var header=AtlasUi.Panel(panel,"CharacterHeader",AtlasUi.Forest,true);header.anchorMin=new Vector2(0,1);header.anchorMax=Vector2.one;header.pivot=new Vector2(.5f,1);header.sizeDelta=new Vector2(0,64);
        var title=AtlasUi.Text(header,"Мой персонаж",28,AtlasUi.Light,true,42);AtlasUi.Place(title.rectTransform,22,9,650,44);
        var close=AtlasUi.Button(header,"×",()=>SetSheetVisible(false),dark:true);AtlasUi.Place((RectTransform)close.transform,900,12,40,40);
        var identity=AtlasUi.Row(panel,"Identity",44,10);AtlasUi.Place(identity,20,79,920,44);
        _nameIF=AtlasUi.Input(identity,_cd.characterName,"Имя");_nameIF.onEndEdit.AddListener(v=>_cd.characterName=v);
        _classIF=AtlasUi.Input(identity,_cd.className,"Класс");_classIF.onEndEdit.AddListener(v=>_cd.className=v);
        _levelTxt=AtlasUi.Text(identity,_cd.level+" УРОВЕНЬ",18,title:true,height:44);
        var tabs=AtlasUi.Row(panel,"CharacterTabs",40,5);AtlasUi.Place(tabs,20,136,920,40);
        var labels=new[]{"Характеристики","Атаки","Способности","Снаряжение","Заметки"};var pages=new GameObject[5];
        for(int i=0;i<5;i++){
            int page=i;var b=AtlasUi.Button(tabs,labels[i],()=>SwitchPage(page),height:40);_tabImages[i]=b.GetComponent<Image>();_tabTexts[i]=b.GetComponentInChildren<Text>();
            var body=AtlasUi.Scroll(panel,"Character "+labels[i],out var scroll,16);var rt=(RectTransform)scroll.transform;
            rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=new Vector2(20,68);rt.offsetMax=new Vector2(-20,-190);pages[i]=scroll.gameObject;
            if(i==0){
                var vitals=AtlasUi.Row(body,"Vitals",44);AtlasUi.Text(vitals,"ХП",16,height:44);var hp=AtlasUi.Input(vitals,_cd.currentHP.ToString());var max=AtlasUi.Input(vitals,_cd.maxHP.ToString());AtlasUi.Text(vitals,"КД",16,height:44);var ac=AtlasUi.Input(vitals,_cd.armorClass.ToString());_atlasHp=hp;_atlasMaxHp=max;_atlasAc=ac;
                AtlasUi.Button(body,"Применить ХП / КД листа",()=>{if(int.TryParse(hp.text,out var h)&&int.TryParse(max.text,out var m)&&int.TryParse(ac.text,out var a)&&h>=0&&m>=h&&a>=0){_cd.currentHP=h;_cd.maxHP=m;_cd.armorClass=a;}},height:36);
                _profTxt=AtlasUi.Text(body,"Бонус мастерства: +"+_cd.proficiencyBonus,16,height:32);
                var progress=AtlasUi.Row(body,"Progress",40);AtlasUi.Text(progress,"Уровень / опыт / мастерство",15,height:40);var level=AtlasUi.Input(progress,_cd.level.ToString(),"Уровень");var xp=AtlasUi.Input(progress,_cd.currentXP.ToString(),"Опыт");var proficiency=AtlasUi.Input(progress,_cd.proficiencyBonus.ToString(),"Мастерство");_atlasLevel=level;_xpInput=xp;_atlasProficiency=proficiency;level.onEndEdit.AddListener(v=>{if(int.TryParse(v,out var n)){_cd.level=Mathf.Clamp(n,1,20);RefreshDisplay();}});xp.onEndEdit.AddListener(v=>{if(int.TryParse(v,out var n))_cd.currentXP=Mathf.Max(0,n);});proficiency.onEndEdit.AddListener(v=>{if(int.TryParse(v,out var n)){_cd.proficiencyBonus=Mathf.Clamp(n,2,6);RefreshDisplay();}});
                for(int stat=0;stat<6;stat++){
                    int si=stat;var row=AtlasUi.Row(body,"Ability"+stat,40);AtlasUi.Text(row,SN[si],15,height:40);
                    AtlasUi.Button(row,"−",()=>ChStat(si,-1),height:38);_scoreTxt[si]=AtlasUi.Text(row,StatScore(si).ToString(),20,title:true,height:40);AtlasUi.Button(row,"+",()=>ChStat(si,1),height:38);
                    _checkTxt[si]=AtlasUi.Text(row,ModS(StatMod(si)),17,height:40);_saveTxt[si]=AtlasUi.Text(row,ModS(SaveVal(si)),16,height:40);var save=AtlasUi.Button(row,"Владение спас.",()=>SetSaveProf(si,!SaveProf(si)),height:38);_saveTgl[si]=save.GetComponent<Image>();row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth=false;var widths=new[]{160f,42f,48f,42f,62f,62f,155f};for(int c=0;c<row.childCount;c++)AtlasUi.Width(row.GetChild(c),widths[c]);
                }
                AtlasUi.Text(body,"Навыки",24,title:true,height:40);
                for(int skill=0;skill<_cd.skills.Length&&skill<18;skill++){int index=skill;var row=AtlasUi.Row(body,"Skill"+skill,34);_skIndTxt[index]=AtlasUi.Text(row,"○",16,height:34);AtlasUi.Text(row,_cd.skills[index].name,15,height:34);_skBonTxt[index]=AtlasUi.Text(row,"",16,height:34);AtlasUi.Button(row,"Владение",()=>CycleProf(index),height:32);}
                var other=AtlasUi.Input(body,_cd.otherProficiencies,"Прочие владения, языки",true,100);other.onValueChanged.AddListener(v=>_cd.otherProficiencies=v);
                var passive=AtlasUi.Row(body,"Passive",36);_pasPerTxt=AtlasUi.Text(passive,"Восприятие: "+_cd.passiveWisdomPerception,14,height:36);_pasInsTxt=AtlasUi.Text(passive,"Проницательность: "+_cd.passiveWisdomInsight,14,height:36);_pasInvTxt=AtlasUi.Text(passive,"Анализ: "+_cd.passiveIntAnalysis,14,height:36);
            }
            if(i==1)_attacksInput=AtlasCharacterText(body,"Атаки и заклинания",_cd.attacksAndSpells,v=>_cd.attacksAndSpells=v);
            if(i==2){_abilitiesInput=AtlasCharacterText(body,"Особенности",_cd.featuresAndTraits,v=>_cd.featuresAndTraits=v);_extraInput=AtlasCharacterText(body,"Дополнительные способности",_cd.extraAbilities,v=>_cd.extraAbilities=v);_traitsInput=AtlasCharacterText(body,"Черты",_cd.traits,v=>_cd.traits=v);}
            if(i==3){_equipInput=AtlasCharacterText(body,"Снаряжение",_cd.equipment,v=>_cd.equipment=v);_treasureInput=AtlasCharacterText(body,"Сокровища",_cd.treasure,v=>_cd.treasure=v);}
            if(i==4){_note1Input=AtlasCharacterText(body,"Заметки 1",_cd.note1,v=>_cd.note1=v);_note2Input=AtlasCharacterText(body,"Заметки 2",_cd.note2,v=>_cd.note2=v);_note3Input=AtlasCharacterText(body,"Заметки 3",_cd.note3,v=>_cd.note3=v);_note4Input=AtlasCharacterText(body,"Заметки 4",_cd.note4,v=>_cd.note4=v);_note5Input=AtlasCharacterText(body,"Заметки 5",_cd.note5,v=>_cd.note5=v);_note6Input=AtlasCharacterText(body,"Заметки 6",_cd.note6,v=>_cd.note6=v);}
        }
        _page0=pages[0];_page1=pages[1];_page2=pages[2];_page3=pages[3];_page4=pages[4];
        var files=AtlasUi.Row(panel,"CharacterFiles",38);AtlasUi.Place(files,20,658,920,38);AtlasUi.Button(files,"Импорт LSS…",ImportCharacter);AtlasUi.Button(files,"Экспорт LSS…",ExportCharacter);AtlasUi.Text(files,"Локальный лист персонажа",13,AtlasUi.Muted,height:38);
        RefreshDisplay();SwitchPage(_currentPage);_pn.SetActive(false);
    }
    private InputField AtlasCharacterText(Transform parent,string label,string value,UnityEngine.Events.UnityAction<string> change){AtlasUi.Text(parent,label,23,title:true,height:36);var input=AtlasUi.Input(parent,value,label,true,170);input.onValueChanged.AddListener(change);return input;}
}
