using System;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public partial class DiceUI
{
    private static readonly string[] AtlasEffectNames={"Огонь / опасность","Вода / магия","Природа / яд","Сложная местность","Тьма","Стена"};
    private RectTransform _nativeRoot, _nativeHeader, _nativeNav, _nativeRail, _nativeInitiative, _nativeToolbar;
    private RectTransform _nativeToolRow, _nativeContext, _nativePlayerContent, _nativePlayerOptions;
    private RectTransform _nativeDrawer, _nativeDrawerContent, _nativeSceneList;
    private Text _nativeSession, _nativeStatus, _nativeNotice;
    private TokenController _nativeToken;
    private TokenController _nativeSelected;
    private Button _nativeInfo;
    private RectTransform _nativeTokenRing;
    private Text _nativeTokenSummary;
    private bool _nativeBuiltRole, _nativeHost, _nativeMenuOpen=true, _nativeInitCollapsed;
    private bool _nativePreparation;
    private bool _nativeDiceOpen;
    private int _nativeTool, _nativeShape=1;
    private string _nativeInitKey, _nativeSceneKey;
    private float _nativeRefreshAt;
    private string _nativeDrawerKind;
    private Button[] _nativeEffects;
    private RectTransform _nativeReferenceList;
    private InputField _nativeReferenceSearch;
    private string _nativeReferenceKey;
    public bool AtlasInterfaceActive => _nativeRoot != null;
    public float AtlasLeft => _nativeHost ? 120 : _nativeMenuOpen ? 256 : 0;

    private void BuildAtlasNativeUi()
    {
        _nativeRoot=AtlasUi.Node(_canvas.transform,"AtlasTable");AtlasUi.Fill(_nativeRoot);
        _nativeHeader=AtlasUi.Panel(_nativeRoot,"AtlasHeader",AtlasUi.Forest);
        _topBarGO=_nativeHeader.gameObject;
        _nativeNav=AtlasUi.Panel(_nativeRoot,"PlayerNavigation",AtlasUi.Forest);
        _nativeRail=AtlasUi.Panel(_nativeRoot,"MasterNavigation",AtlasUi.Forest);
        _nativePlayerContent=AtlasUi.Scroll(_nativeNav,"PlayerMenuScroll",out var navScroll,12);
        navScroll.GetComponent<Image>().color=AtlasUi.Forest;AtlasUi.Fill((RectTransform)navScroll.transform);
        _nativeInitiative=AtlasUi.Panel(_nativeRoot,"CompactInitiative",AtlasUi.Paper);
        _nativeToolbar=AtlasUi.Column(_nativeRoot,"MasterToolbox",6,3);
        _nativeToolbar.gameObject.AddComponent<Image>().color=AtlasUi.Paper;
        _nativeToolbar.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        _nativeToolRow=AtlasUi.Row(_nativeToolbar,"Tools",42,3);
        _nativeContext=AtlasUi.Column(_nativeToolbar,"ToolSettings",4,4);
        _nativeNotice=AtlasUi.Text(_nativeRoot,"",13,AtlasUi.Muted,height:28);
        _nativeInfo=AtlasUi.Button(_nativeRoot,"ⓘ Информация",()=>AtlasInspectToken(_nativeSelected));_nativeInfo.gameObject.SetActive(false);
        _nativeTokenRing=AtlasUi.Node(_nativeRoot,"SelectedTokenRing");var ring=_nativeTokenRing.gameObject.AddComponent<AtlasGlyph>();ring.key="ring";ring.color=AtlasUi.Copper;ring.raycastTarget=false;_nativeTokenRing.gameObject.SetActive(false);

        var title=AtlasUi.Text(_nativeHeader,"◈  Dicebound",29,AtlasUi.Light,true,42);AtlasUi.Place(title.rectTransform,20,9,245,44);
        var scene=AtlasUi.Button(_nativeHeader,"Сцена ▾",()=>{if(_nativeHost)NativeOpenScenes();},dark:true);
        AtlasUi.Place((RectTransform)scene.transform,280,12,270,40);_nativeSession=scene.GetComponentInChildren<Text>();
        var people=AtlasUi.Button(_nativeHeader,"Участники",()=>NativeOpenPeople(),dark:true,icon:"users-round");AtlasUi.Place((RectTransform)people.transform,570,12,155,40);
        var menu=AtlasUi.Button(_nativeHeader,"Меню",()=>{_nativeMenuOpen=!_nativeMenuOpen;NativeLayout();},dark:true,icon:"fa-solid--tools");AtlasUi.Place((RectTransform)menu.transform,735,12,100,40);
        var session=AtlasUi.Button(_nativeHeader,"Сессия / настройки",NativeSessionMenu,dark:true);var sr=(RectTransform)session.transform;sr.anchorMin=sr.anchorMax=new Vector2(1,1);sr.pivot=new Vector2(1,1);sr.sizeDelta=new Vector2(200,40);sr.anchoredPosition=new Vector2(-16,-12);
        _nativeStatus=AtlasUi.Text(_nativeHeader,"",13,AtlasUi.Light,height:36);AtlasUi.Place(_nativeStatus.rectTransform,850,14,220,36);

        var railContent=AtlasUi.Scroll(_nativeRail,"Sections",out var railScroll,8);railScroll.GetComponent<Image>().color=AtlasUi.Forest;AtlasUi.Fill((RectTransform)railScroll.transform);
        AtlasUi.Text(railContent,"МАСТЕР",11,AtlasUi.Light,height:25);
        AtlasUi.NavButton(railContent,"Стол",()=>{NativeCloseDrawer();_nativePreparation=false;NativeLayout();},"table");
        AtlasUi.NavButton(railContent,"Сцены",NativeOpenScenes,"scenes");
        AtlasUi.NavButton(railContent,"Токены",()=>NativeOpenTokens(),"user");
        AtlasUi.NavButton(railContent,"Библиотека",()=>NativeOpenLibrary(),"book");
        AtlasUi.NavButton(railContent,"Мой лист",NativeCharacter,"user");
        AtlasUi.NavButton(railContent,"Игроки",NativeOpenPeople,"user");
        AtlasUi.NavButton(railContent,"Выход",ShowLeaveConfirm,"exit");

        var toolLabels=new[]{"Выбор","+ Токен","Линейка","Области","Отметки","Геометрия","Туман","Карта","Переходы"};
        for(int i=0;i<toolLabels.Length;i++){int index=i;AtlasUi.Button(_nativeToolRow,toolLabels[i],()=>NativeSelectTool(index),height:40);}

        _nativeDrawerContent=AtlasUi.Scroll(_nativeRoot,"ContextPanel",out var drawerScroll,18);
        _nativeDrawer=(RectTransform)drawerScroll.transform;_nativeDrawer.gameObject.SetActive(false);
        var dice=AtlasUi.Row(_nativeRoot,"DiceTray",62,5);dice.gameObject.AddComponent<Image>().color=AtlasUi.Paper;_bottomPanelGO=dice.gameObject;
        foreach(DieType type in new[]{DieType.d4,DieType.d6,DieType.d8,DieType.d10,DieType.d12,DieType.d20,DieType.d100}){
            var captured=type;AtlasUi.DiceButton(dice,type,()=>SpawnDie(captured));
        }
        var clearDice=AtlasUi.Button(dice,"Убрать…",ConfirmDiceClear,height:62);AtlasUi.Width(clearDice,92);
        var journal=AtlasUi.Scroll(_nativeRoot,"JournalDrawer",out var logScroll,16);_logScroll=logScroll;_logPanelGO=logScroll.gameObject;
        var logHeader=AtlasUi.Row(journal,"Heading",34);AtlasUi.Text(logHeader,"Журнал бросков",22,title:true);
        AtlasUi.Button(logHeader,"×",()=>_logPanelGO.SetActive(false),height:32);
        _logText=AtlasUi.Text(journal,"Бросков пока нет.",15,height:300);
        var logFit=_logText.gameObject.AddComponent<ContentSizeFitter>();logFit.verticalFit=ContentSizeFitter.FitMode.PreferredSize;
        _logText.GetComponent<LayoutElement>().minHeight=80;_logText.GetComponent<LayoutElement>().preferredHeight=-1;
        var dock=AtlasUi.Row(_nativeRoot,"SessionDock",38,7);
        AtlasUi.Button(dock,"Перемещения · 0",MovementReviewUI.OpenRequests,height:38);_movementRequestsButton=dock.GetComponentInChildren<Button>();_movementRequestsText=_movementRequestsButton.GetComponentInChildren<Text>();
        AtlasUi.Button(dock,"Журнал бросков",()=>{NativeCloseDrawer();_logPanelGO.SetActive(!_logPanelGO.activeSelf);},height:38);
        _logTab=dock.gameObject;
        BuildAtlasConfirmation();
        var board=Application.isPlaying?GameObject.Find("GameBoard"):null;if(board!=null&&board.TryGetComponent<Renderer>(out var renderer)){var material=renderer.material;if(material.HasProperty("_BaseMap"))material.SetTexture("_BaseMap",null);if(material.HasProperty("_MainTex"))material.SetTexture("_MainTex",null);if(material.HasProperty("_DetailAlbedoMap"))material.SetTexture("_DetailAlbedoMap",null);material.DisableKeyword("_DETAIL_MULX2");material.DisableKeyword("_DETAIL_SCALED");material.color=new Color(.78f,.81f,.71f);if(material.HasProperty("_BaseColor"))material.SetColor("_BaseColor",new Color(.78f,.81f,.71f));}
        _nativeRoot.gameObject.SetActive(false);_logPanelGO.SetActive(false);_bottomPanelGO.SetActive(false);
        NativeLayout();
    }

    private void NativeBuildPlayerMenu()
    {
        AtlasUi.Clear(_nativePlayerContent);
        AtlasUi.Text(_nativePlayerContent,LobbyUI.LocalNickname,25,AtlasUi.Light,true,38);
        AtlasUi.Text(_nativePlayerContent,"НА КАРТЕ",11,AtlasUi.Light,height:24);
        foreach(var item in new[]{(0,"Выбор / перемещение"),(2,"Линейка"),(3,"Области"),(4,"Отметки / ластик")}){
            int mode=item.Item1;AtlasUi.Button(_nativePlayerContent,item.Item2,()=>NativeSelectTool(mode),dark:true,height:42);
            var options=AtlasUi.Column(_nativePlayerContent,"InlineOptions"+mode,8,5);
            options.gameObject.SetActive(_nativeTool==mode&&mode!=0);
            if(_nativeTool==mode&&mode!=0)NativePopulateOptions(options,mode);
        }
        AtlasUi.Text(_nativePlayerContent,"ЗА СТОЛОМ",11,AtlasUi.Light,height:25);
        AtlasUi.Button(_nativePlayerContent,"Мой персонаж",NativeCharacter,dark:true);
        AtlasUi.Button(_nativePlayerContent,"Кубики",()=>{_nativeDiceOpen=!_nativeDiceOpen;NativeBuildPlayerMenu();},dark:true);
        if(_nativeDiceOpen){var grid=AtlasUi.Node(_nativePlayerContent,"PlayerDice");var layout=grid.gameObject.AddComponent<GridLayoutGroup>();layout.constraint=GridLayoutGroup.Constraint.FixedColumnCount;layout.constraintCount=4;layout.cellSize=new Vector2(50,55);layout.spacing=new Vector2(5,5);AtlasUi.Height(grid,115);foreach(DieType type in new[]{DieType.d4,DieType.d6,DieType.d8,DieType.d10,DieType.d12,DieType.d20,DieType.d100}){var captured=type;AtlasUi.DiceButton(grid,type,()=>SpawnDie(captured),true);}AtlasUi.Button(_nativePlayerContent,"Убрать мои кубики…",ConfirmDiceClear,dark:true,height:32);}
        AtlasUi.Button(_nativePlayerContent,"Журнал бросков",()=>{NativeCloseDrawer();_logPanelGO.SetActive(!_logPanelGO.activeSelf);},dark:true);
        AtlasUi.Button(_nativePlayerContent,"Справочник",()=>NativeOpenLibrary(),dark:true);
        AtlasUi.Button(_nativePlayerContent,"Выход",ShowLeaveConfirm,dark:true);
        AtlasUi.Text(_nativePlayerContent,"Esc — завершить / закрыть",12,AtlasUi.Light,height:40);
    }

    private void NativeSelectTool(int tool)
    {
        SelectDefaultTool();NativeCloseDrawer();_nativeTool=tool;_nativeEffects=null;
        if(tool==1){NativeOpenTokens(true);_nativeTool=0;}
        if(tool==2)ActivateMeasurement(0);
        if(tool==3)ActivateMeasurement(_nativeShape);
        NativeRebuildContext();
    }

    private void NativeRebuildContext()
    {
        AtlasUi.Clear(_nativeContext);_nativeContext.gameObject.SetActive(_nativeTool!=0);
        if(_nativeHost&&_nativeTool!=0)NativePopulateOptions(_nativeContext,_nativeTool);
        if(!_nativeHost)NativeBuildPlayerMenu();
    }

    private void NativePopulateOptions(Transform parent,int tool)
    {
        bool dark=!_nativeHost;Color text=dark?AtlasUi.Light:AtlasUi.Muted;
        var row=dark?AtlasUi.Column(parent,"Options",0,4):AtlasUi.Row(parent,"Options",38,5);
        void Action(string label,UnityEngine.Events.UnityAction callback)=>AtlasUi.Button(row,label,callback,dark:dark,height:36);
        if(tool==2){AtlasUi.Text(row,"Протяните от начала до цели",13,text,height:36);Action("Завершить · Esc",()=>NativeSelectTool(0));}
        if(tool==3){
            for(int i=1;i<=3;i++){int mode=i;var b=AtlasUi.Button(row,new[]{"Круг","Квадрат","Конус"}[i-1],()=>{_nativeShape=mode;ActivateMeasurement(mode);NativeRebuildContext();},height:36,dark:dark);AtlasUi.Select(b,_nativeShape==mode,dark);}
            var palette=dark?AtlasUi.Column(parent,"Palette",0,4):AtlasUi.Row(parent,"Palette",52,5);
            AtlasUi.Text(parent,"Задайте область на карте, затем выберите цвет / эффект",12,text,height:26);
            _nativeEffects=NativePalette(palette,true,dark);Action("Завершить",()=>NativeSelectTool(0));
        }
        if(tool==4){_nativeEffects=NativePalette(row,false,dark);Action("Ластик",()=>{EffectPaintTool.Instance?.Activate(-1);});Action("Очистить мои…",()=>ConfirmAction("Очистить мои отметки?","Удалятся ваши отметки на клетках.",CellMarker.ClearAllMyMarkers));Action("Завершить",()=>NativeSelectTool(0));}
        if(tool==5){
            Action("Стена",()=>SceneEditor.Instance?.Activate(SceneEditor.Tool.Wall));
            Action("Дверь",()=>SceneEditor.Instance?.Activate(SceneEditor.Tool.Door));
            Action("Открыть / закрыть",()=>SceneEditor.Instance?.Activate(SceneEditor.Tool.ToggleDoor));
            Action("Препятствие",()=>SceneEditor.Instance?.Activate(SceneEditor.Tool.Square));
            Action("Колонна",()=>SceneEditor.Instance?.Activate(SceneEditor.Tool.Column));
            Action("Стереть",()=>SceneEditor.Instance?.Activate(SceneEditor.Tool.EraseEdge));
            Action("Отменить",()=>SceneEditor.Instance?.Undo());Action("Завершить",()=>NativeSelectTool(0));
        }
        if(tool==6){
            Action("Включить / выключить",()=>FogManager.Instance?.ToggleEnabled());Action("Раскрыть",()=>FogManager.Instance?.SetManual(1));Action("Закрыть",()=>FogManager.Instance?.SetManual(2));
            Action("Пауза",()=>FogManager.Instance?.TogglePause());Action("Глазами игроков",()=>FogManager.Instance?.TogglePreview());Action("Источник обзора",()=>FogManager.Instance?.CyclePreviewSource());Action("Отменить",GameMasterUndo.Undo);Action("Завершить",()=>NativeSelectTool(0));
        }
        if(tool==7){
            Action("Загрузить карту",OpenTexturePicker);Action("Каталог карт",()=>DmPanelUI.Instance?.OpenAtlasPage(0));
            _scaleInput=AtlasUi.Input(row,MapController.Instance!=null?MapController.Instance.CurrentScale.ToString("0.00"):"1.00","Масштаб",height:36);_scaleInput.onEndEdit.AddListener(value=>{if(float.TryParse(value.Replace(',','.'),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var scale))MapController.Instance?.SetScale(scale);});
            Action("Поворот 90°",()=>MapController.Instance?.Rotate90());Action("Сбросить",()=>MapController.Instance?.ResetMapPosition());
            Action("Скрыть от игроков",()=>HostSceneCurtain.Instance?.ToggleCurtainOnHost());Action("Завершить",()=>NativeSelectTool(0));
        }
        if(tool==8){Action("Переходы и перенос токенов",()=>DmPanelUI.Instance?.OpenAtlasPage(4));Action("Завершить",()=>NativeSelectTool(0));}
    }

    private Button[] NativePalette(Transform parent,bool area,bool dark)
    {
        if(dark){var grid=AtlasUi.Node(parent,"EffectColors");var layout=grid.gameObject.AddComponent<GridLayoutGroup>();layout.constraint=GridLayoutGroup.Constraint.FixedColumnCount;layout.constraintCount=2;layout.cellSize=new Vector2(101,62);layout.spacing=new Vector2(5,5);AtlasUi.Height(grid,196);parent=grid;}
        var list=new Button[CellMarker.TextureColors.Length];
        for(int i=0;i<list.Length;i++){
            int index=i;var b=AtlasUi.Button(parent,AtlasEffectNames[i],()=>{
                if(area){var mt=MeasurementTool.Instance;if(mt==null||!mt.IsLocalActive||mt.CurrentMode==MeasurementTool.Mode.Ruler||mt.GetCellsInArea().Count==0)return;mt.ApplyArea(index);_nativeTool=0;NativeRebuildContext();}
                else EffectPaintTool.Instance?.Activate(index);
            },height:dark?46:50,dark:dark);
            Color swatch=CellMarker.TextureColors[i];swatch.a=1;var sw=AtlasUi.Panel(b.transform,"Swatch",swatch);AtlasUi.Place(sw,5,5,dark?91:8,dark?11:40);sw.GetComponent<Image>().raycastTarget=false;
            b.GetComponentInChildren<Text>().fontSize=dark?13:15;
            if(dark){var text=b.GetComponentInChildren<Text>().rectTransform;text.offsetMin=new Vector2(7,4);text.offsetMax=new Vector2(-7,-22);}
            b.interactable=!area;list[i]=b;
        }
        return list;
    }

    private void NativeLayout()
    {
        if(_nativeRoot==null)return;
        _nativeHeader.anchorMin=new Vector2(0,1);_nativeHeader.anchorMax=Vector2.one;_nativeHeader.pivot=new Vector2(.5f,1);_nativeHeader.sizeDelta=new Vector2(0,64);_nativeHeader.anchoredPosition=Vector2.zero;
        float left=AtlasLeft;_nativeNav.gameObject.SetActive(!_nativeHost&&_nativeMenuOpen);_nativeRail.gameObject.SetActive(_nativeHost);
        foreach(var side in new[]{_nativeNav,_nativeRail}){side.anchorMin=Vector2.zero;side.anchorMax=new Vector2(0,1);side.offsetMin=Vector2.zero;side.offsetMax=new Vector2(side==_nativeRail?120:256,-64);}
        _nativeInitiative.anchorMin=new Vector2(0,1);_nativeInitiative.anchorMax=Vector2.one;_nativeInitiative.pivot=new Vector2(0,1);_nativeInitiative.offsetMin=new Vector2(left+12,-120);_nativeInitiative.offsetMax=new Vector2(-12,-74);
        _nativeToolbar.gameObject.SetActive(_nativeHost);_nativeToolbar.anchorMin=new Vector2(0,1);_nativeToolbar.anchorMax=Vector2.one;_nativeToolbar.pivot=new Vector2(0,1);_nativeToolbar.offsetMin=new Vector2(left+12,-177);_nativeToolbar.offsetMax=new Vector2(-12,-128);
        var dice=(RectTransform)_bottomPanelGO.transform;dice.anchorMin=dice.anchorMax=Vector2.zero;dice.pivot=Vector2.zero;dice.anchoredPosition=new Vector2(left+16,16);dice.sizeDelta=new Vector2(570,64);
        var log=(RectTransform)_logPanelGO.transform;log.anchorMin=log.anchorMax=new Vector2(1,0);log.pivot=new Vector2(1,0);log.anchoredPosition=new Vector2(-16,64);log.sizeDelta=new Vector2(420,320);
        var dock=(RectTransform)_logTab.transform;dock.anchorMin=dock.anchorMax=new Vector2(1,0);dock.pivot=new Vector2(1,0);dock.anchoredPosition=new Vector2(-16,16);dock.sizeDelta=new Vector2(_nativeHost?410:220,38);
        _movementRequestsButton.gameObject.SetActive(_nativeHost);
        _nativeDrawer.anchorMin=_nativeDrawer.anchorMax=new Vector2(1,1);_nativeDrawer.pivot=new Vector2(1,1);_nativeDrawer.anchoredPosition=new Vector2(-16,_nativeHost?-245:-135);_nativeDrawer.sizeDelta=new Vector2(390,Mathf.Min(620,_nativeRoot.rect.height-310));
        AtlasUi.Place(_nativeNotice.rectTransform,left+18,_nativeRoot.rect.height-108,Mathf.Max(100,_nativeRoot.rect.width-left-450),32);
        if(_nativeSceneList!=null){_nativeSceneList.gameObject.SetActive(_nativePreparation&&_nativeHost);_nativeSceneList.anchorMin=Vector2.zero;_nativeSceneList.anchorMax=new Vector2(0,1);_nativeSceneList.offsetMin=new Vector2(132,94);_nativeSceneList.offsetMax=new Vector2(430,-245);}
    }

    private void UpdateAtlasNativeUi()
    {
        if(_nativeRoot==null||!_nativeRoot.gameObject.activeInHierarchy)return;
        bool host=IsLocalHost();if(!_nativeBuiltRole||host!=_nativeHost){_nativeHost=host;_nativeBuiltRole=true;NativeBuildPlayerMenu();NativeLayout();}
        NativeLayout();
        if(_nativeInfo!=null){bool show=_nativeSelected!=null&&_nativeSelected.IsSpawned&&_nativeSelected.IsVisibleToLocalPlayer&&_nativeTool==0;_nativeInfo.gameObject.SetActive(show);_nativeTokenRing.gameObject.SetActive(show);if(show&&Camera.main!=null){var p=Camera.main.WorldToScreenPoint(_nativeSelected.transform.position);float scale=Mathf.Max(.01f,_canvas.scaleFactor);AtlasUi.Place(_nativeTokenRing,p.x/scale-40,(Screen.height-p.y)/scale-40,80,80);AtlasUi.Place((RectTransform)_nativeInfo.transform,Mathf.Clamp(p.x/scale+30,AtlasLeft+10,_nativeRoot.rect.width-175),Mathf.Clamp((Screen.height-p.y)/scale-45,140,_nativeRoot.rect.height-100),160,36);}}
        if(!GameplayInputGate.IsTextInputFocused&&Keyboard.current?.escapeKey.wasPressedThisFrame==true&&!IsConfirmationOpen&&!GameplayInputGate.IsModalOpen){
            if(_nativeTool!=0){NativeSelectTool(0);GameplayInputGate.MarkToolExit();}
            else if(_nativeDrawer.gameObject.activeSelf)NativeCloseDrawer();
        }
        if(IsConfirmationOpen&&Keyboard.current?.escapeKey.wasPressedThisFrame==true&&!GameplayInputGate.IsTextInputFocused){_confirmPanel.SetActive(false);GameplayInputGate.MarkToolExit();}
        if(Time.unscaledTime<_nativeRefreshAt)return;_nativeRefreshAt=Time.unscaledTime+.15f;
        _nativeSession.text=SceneFileStore.ActiveSceneLabel+" ▾";
        _nativeStatus.text=(_nativeHost?"Мастер":"Игрок")+" · "+PlayerColors.GetAllPlayers().Count+" участника";
        _nativeNotice.text=_toolNoticeUntil>Time.unscaledTime?_toolNoticeText:_nativeTool==0?"Выберите токен → Информация · ПКМ — свойства":"Активен инструмент · Esc — завершить";
        _movementRequestsText.text="Перемещения · "+MovementReviewUI.PendingCount;_movementRequestsButton.interactable=MovementReviewUI.PendingCount>0;
        var mt=MeasurementTool.Instance;
        if(_nativeEffects!=null&&_nativeTool==3){bool ready=mt!=null&&mt.IsLocalActive&&mt.CurrentMode!=MeasurementTool.Mode.Ruler&&mt.GetCellsInArea().Count>0;foreach(var b in _nativeEffects)if(b!=null)b.interactable=ready;}
        NativeUpdateInitiative();if(_nativeSceneList!=null&&_nativeSceneList.gameObject.activeSelf)NativeRefreshScenes();
        if(_nativeDrawerKind=="public-library"&&_nativeReferenceList!=null){string key=string.Join("|",PublicReferenceLibrary.AtlasEntries.Select(e=>e.id+e.title+e.body));if(key!=_nativeReferenceKey){_nativeReferenceKey=key;NativePopulateReferences(_nativeReferenceSearch!=null?_nativeReferenceSearch.text:"");}}
        if(_nativeToken!=null&&(!_nativeToken.IsSpawned||!_nativeToken.IsVisibleToLocalPlayer))NativeCloseDrawer();
        else if(_nativeTokenSummary!=null&&_nativeToken!=null)_nativeTokenSummary.text=_nativeToken.GetDisplayedTokenInfo();
    }

    private void BuildAtlasConfirmation()
    {
        var modal=AtlasUi.Panel(_nativeRoot,"Confirmation",new Color(.04f,.11f,.08f,.80f));AtlasUi.Fill(modal);_confirmPanel=modal.gameObject;
        var card=AtlasUi.Column(modal,"Dialog",24,15);card.gameObject.AddComponent<Image>().color=AtlasUi.Paper;
        card.anchorMin=card.anchorMax=new Vector2(.5f,.5f);card.pivot=new Vector2(.5f,.5f);card.sizeDelta=new Vector2(540,300);
        _confirmTitle=AtlasUi.Text(card,"Подтверждение",27,title:true,height:50);_confirmBody=AtlasUi.Text(card,"",16,height:100);
        var actions=AtlasUi.Row(card,"Actions",44);AtlasUi.Button(actions,"Отмена",()=>_confirmPanel.SetActive(false));_confirmAccept=AtlasUi.Button(actions,"Подтвердить",null,true);
        modal.gameObject.AddComponent<UiModalScope>();_confirmPanel.SetActive(false);
    }

    private void NativeSessionMenu()
    {
        NativeDrawer("Сессия и настройки","session");
        if(_nativeHost){AtlasUi.Button(_nativeDrawerContent,"Сохранить сессию…",SceneFileStore.SaveCampaignDialog,true);AtlasUi.Button(_nativeDrawerContent,"Загрузить сессию…",SceneFileStore.LoadCampaignDialog);}
        AtlasUi.Button(_nativeDrawerContent,"Экран и управление",DisplaySettingsUI.OpenAtlas);AtlasUi.Button(_nativeDrawerContent,_nativeHost?"Завершить сессию":"Выйти из сессии",ShowLeaveConfirm);
    }
    private void NativeDrawer(string title,string kind)
    {
        DmPanelUI.Instance?.Hide();CharacterSheetUI.Instance?.SetSheetVisible(false);_logPanelGO.SetActive(false);
        _nativeToken=null;_nativeTokenSummary=null;_nativeDrawerKind=kind;AtlasUi.Clear(_nativeDrawerContent);
        var heading=AtlasUi.Row(_nativeDrawerContent,"Heading",40);AtlasUi.Text(heading,title,25,title:true,height:40);var close=AtlasUi.Button(heading,"×",NativeCloseDrawer,height:36);AtlasUi.Width(close,40);
        _nativeDrawer.gameObject.SetActive(true);
    }
    public void AtlasCloseContext()=>NativeCloseDrawer();
    private void NativeCloseDrawer(){if(_nativeDrawer!=null)_nativeDrawer.gameObject.SetActive(false);_nativeToken=null;_nativeDrawerKind=null;PublicReferenceLibrary.CloseAtlas();GameplayInputGate.MarkToolExit();}
    private void NativeCharacter(){NativeCloseDrawer();DmPanelUI.Instance?.Hide();CharacterSheetUI.Instance?.ToggleSheet();}
    private void NativeOpenPeople(){NativeDrawer("Участники","people");foreach(var player in PlayerColors.GetAllPlayers())AtlasUi.Text(_nativeDrawerContent,player.Value,17,height:36);if(_nativeHost)AtlasUi.Button(_nativeDrawerContent,"Права игроков",()=>DmPanelUI.Instance?.OpenAtlasPage(1));}
    private void NativeOpenLibrary(){if(_nativeHost){NativeCloseDrawer();DmPanelUI.Instance?.OpenAtlasPage(6);}else PublicReferenceLibrary.OpenAtlas();}
    private void NativeOpenTokens(bool create=false)
    {
        NativeDrawer("Токены","tokens");
        if(_nativeHost){var name=AtlasUi.Input(_nativeDrawerContent,"","Имя нового токена");AtlasUi.Button(_nativeDrawerContent,"+ Создать токен",()=>{TokenManager.Instance?.CreateTokenAsHost(name.text,false);},true);}
        foreach(var t in UnityEngine.Object.FindObjectsByType<TokenController>(FindObjectsInactive.Exclude).Where(t=>t.IsSpawned&&t.IsVisibleToLocalPlayer)){var captured=t;AtlasUi.Button(_nativeDrawerContent,t.TokenName,()=>AtlasInspectToken(captured));}
    }
    private void NativeOpenScenes(){NativeCloseDrawer();_nativePreparation=!_nativePreparation;_nativeSceneKey=null;if(_nativeSceneList==null){var content=AtlasUi.Scroll(_nativeRoot,"ScenesBrowser",out var scroll,16);_nativeSceneList=(RectTransform)scroll.transform;_nativeSceneListContent=content;}_nativeSceneList.gameObject.SetActive(_nativePreparation);NativeRefreshScenes();NativeLayout();}
    private RectTransform _nativeSceneListContent;
    private void NativeRefreshScenes(){
        var scenes=SceneFileStore.GetCampaignScenes();string key=SceneFileStore.ActiveSceneId+string.Join("|",scenes.Select(s=>s.sceneId+s.title));if(key==_nativeSceneKey)return;_nativeSceneKey=key;
        AtlasUi.Clear(_nativeSceneListContent);AtlasUi.Text(_nativeSceneListContent,"Сцены",27,title:true,height:44);
        var actions=AtlasUi.Row(_nativeSceneListContent,"Actions",38);AtlasUi.Button(actions,"+ Пустая",()=>{SceneFileStore.CreateEmptyScene();_nativeSceneKey=null;});AtlasUi.Button(actions,"Копировать",()=>{SceneFileStore.CreateSceneCopy();_nativeSceneKey=null;});
        for(int i=0;i<scenes.Length;i++){var s=scenes[i];string id=s.sceneId;AtlasUi.Button(_nativeSceneListContent,s.title,()=>{SceneFileStore.SelectScene(id);_nativeSceneKey=null;},s.sceneId==SceneFileStore.ActiveSceneId,65);}
        AtlasUi.Button(_nativeSceneListContent,"Переименовать / удалить",()=>DmPanelUI.Instance?.OpenAtlasPage(4));AtlasUi.Button(_nativeSceneListContent,"Перенести токены…",()=>DmPanelUI.Instance?.OpenAtlasPage(4));
    }
    private void NativeUpdateInitiative()
    {
        var tracker=InitiativeTracker.Instance;if(tracker==null)return;var view=tracker.GetAtlasEntries();
        string key=tracker.AtlasRound+":"+_nativeInitCollapsed+":"+string.Join("|",view.Select(e=>e.id+e.name+e.initiative+e.current));if(key==_nativeInitKey)return;_nativeInitKey=key;
        AtlasUi.Clear(_nativeInitiative);var row=AtlasUi.Row(_nativeInitiative,"Initiative",42,6);AtlasUi.Fill(row,8,2,8,2);
        row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth=false;var round=AtlasUi.Text(row,"Раунд "+tracker.AtlasRound,18,title:true,height:38);AtlasUi.Width(round,105);
        if(view.Length==0){var empty=AtlasUi.Text(row,"Бой не начат",14,AtlasUi.Muted,height:38);empty.GetComponent<LayoutElement>().flexibleWidth=1;}
        if(!_nativeInitCollapsed)foreach(var e in view.Take(6)){var captured=e;var chip=AtlasUi.Button(row,e.initiative+"  "+e.name,()=>{var t=TokenController.FindSceneToken(captured.tokenId);if(t!=null)AtlasInspectToken(t);},e.current,height:36);AtlasUi.Width(chip,150);}
        if(_nativeHost){var next=AtlasUi.Button(row,"Следующий ход →",tracker.NextTurn,true,height:36);AtlasUi.Width(next,180);next.interactable=view.Length>0;var add=AtlasUi.Button(row,"+ / список",()=>tracker.SetVisible(true),height:36);AtlasUi.Width(add,115);}
        var collapse=AtlasUi.Button(row,_nativeInitCollapsed?"▾":"▴",()=>{_nativeInitCollapsed=!_nativeInitCollapsed;_nativeInitKey=null;},height:36);AtlasUi.Width(collapse,40);
    }
    public void AtlasInspectToken(TokenController token)
    {
        if(token==null||!token.IsSpawned||!token.IsVisibleToLocalPlayer)return;
        NativeDrawer(token.TokenName,"token");_nativeToken=token;
        NativeTokenTabs(token,0);_nativeTokenSummary=AtlasUi.Paragraph(_nativeDrawerContent,token.GetDisplayedTokenInfo(),16);
        if(token.CanEditMasterStats){
            var data=token.CaptureMasterData();var stats=AtlasUi.Row(_nativeDrawerContent,"HP and AC",44);
            var hp=AtlasUi.Input(stats,data.currentHp.ToString(),"ХП");var max=AtlasUi.Input(stats,data.maxHp.ToString(),"Максимум");var ac=AtlasUi.Input(stats,data.armorClass.ToString(),"КД");
            AtlasUi.Button(_nativeDrawerContent,"Применить ХП / КД",()=>{if(int.TryParse(hp.text,out var h)&&int.TryParse(max.text,out var m)&&int.TryParse(ac.text,out var armor)&&h>=0&&m>=h&&m<=999999&&armor>=0&&armor<=999&&token.ServerSetHealth(h,m)&&token.ServerSetArmorClass(armor)){ShowToolNotice("Параметры применены");AtlasInspectToken(token);}else ShowToolNotice("Проверьте ХП, максимум и КД.");},true);
            AtlasUi.Button(_nativeDrawerContent,"Добавить в инициативу",()=>InitiativeTracker.Instance?.AddToken(token));
            AtlasUi.Button(_nativeDrawerContent,token.IsHidden?"Показать игрокам":"Скрыть от игроков",()=>token.RequestSetHidden(!token.IsHidden));
        }
        AtlasUi.Button(_nativeDrawerContent,"Все свойства / состояния…",()=>token.OpenAtlasDetails());
    }
    public void AtlasSelectToken(TokenController token){if(token!=null&&token.IsVisibleToLocalPlayer)_nativeSelected=token;}
    public void AtlasOpenPublicLibrary(){NativeDrawer("Справочник","public-library");_nativeReferenceSearch=AtlasUi.Input(_nativeDrawerContent,"","Поиск по записям");_nativeReferenceList=AtlasUi.Column(_nativeDrawerContent,"Entries",0,8);_nativeReferenceSearch.onValueChanged.AddListener(NativePopulateReferences);_nativeReferenceKey=null;NativePopulateReferences("");}
    private void NativePopulateReferences(string filter){if(_nativeReferenceList==null)return;AtlasUi.Clear(_nativeReferenceList);foreach(var e in PublicReferenceLibrary.AtlasEntries.Where(e=>(e.title+" "+e.body).IndexOf(filter,StringComparison.OrdinalIgnoreCase)>=0)){AtlasUi.Text(_nativeReferenceList,e.title,21,title:true,height:38);AtlasUi.Paragraph(_nativeReferenceList,e.body,15);}}
    public void AtlasTokenDetails(TokenController token)
    {
        if(token==null||!token.IsVisibleToLocalPlayer)return;NativeDrawer(token.TokenName,"token-details");_nativeToken=token;
        bool own=token.CanEditMasterStats||!IsLocalHost()&&NetworkManager.Singleton!=null&&NetworkPermissions.CanCopyToken(NetworkManager.Singleton.LocalClientId,token);
        NativeTokenTabs(token,1);_nativeTokenSummary=AtlasUi.Paragraph(_nativeDrawerContent,token.GetDisplayedTokenInfo());
        if(own){var name=AtlasUi.Input(_nativeDrawerContent,token.TokenName,"Имя");AtlasUi.Button(_nativeDrawerContent,"Сохранить имя",()=>token.RequestRename(name.text));AtlasUi.Button(_nativeDrawerContent,"Изображение…",token.AtlasLoadPortrait);AtlasUi.Button(_nativeDrawerContent,"Копировать токен",token.RequestCopy);}
        if(token.CanEditMasterStats){
            AtlasUi.Text(_nativeDrawerContent,"Доступ и обзор",21,title:true,height:36);
            foreach(var p in PlayerColors.GetAllPlayers()){var id=p.Key;AtlasUi.Button(_nativeDrawerContent,"Владелец: "+p.Value,()=>token.AssignController(id));}
            AtlasUi.Button(_nativeDrawerContent,"Герой / NPC",()=>token.SetHero(!token.IsHero));
            AtlasUi.Button(_nativeDrawerContent,"Разрешить общее управление",()=>token.SetEveryoneCanMove(true));AtlasUi.Button(_nativeDrawerContent,"Только владелец и мастер",()=>token.SetEveryoneCanMove(false));
            var vision=AtlasUi.Input(_nativeDrawerContent,token.VisionFeet.ToString(),"Обзор, фт");AtlasUi.Button(_nativeDrawerContent,"Применить обзор",()=>{if(int.TryParse(vision.text,out var value))token.RequestSetVision(value);});
            var data=token.CaptureMasterData();AtlasUi.Button(_nativeDrawerContent,data.hideHp?"Открыть ХП игрокам":"Скрыть ХП",()=>{token.ServerSetMasterVisibility(!data.hideHp,data.hideConditions);AtlasTokenDetails(token);});
            AtlasUi.Button(_nativeDrawerContent,data.hideArmorClass?"Открыть КД игрокам":"Скрыть КД",()=>{token.ServerSetArmorClassVisibility(!data.hideArmorClass);AtlasTokenDetails(token);});
            AtlasUi.Button(_nativeDrawerContent,data.hideConditions?"Открыть состояния":"Скрыть состояния",()=>{token.ServerSetMasterVisibility(data.hideHp,!data.hideConditions);AtlasTokenDetails(token);});
            AtlasUi.Text(_nativeDrawerContent,"Состояния",21,title:true,height:36);
            foreach(var condition in TokenConditionCatalog.Ids){var id=condition;AtlasUi.Button(_nativeDrawerContent,(data.conditionIds.Contains(id)?"✓ ":"")+TokenConditionCatalog.DisplayName(id),()=>{token.ServerToggleCondition(id);AtlasTokenDetails(token);});}
            AtlasUi.Text(_nativeDrawerContent,"Статблок",21,title:true,height:36);AtlasUi.Button(_nativeDrawerContent,"Снять статблок",()=>token.ServerSetStatBlock(""));foreach(var block in SceneFileStore.GetStatBlocks()){var id=block.id;AtlasUi.Button(_nativeDrawerContent,block.name,()=>token.ServerSetStatBlock(id));}
        }
        if(own)AtlasUi.Button(_nativeDrawerContent,"Удалить токен…",()=>ConfirmAction("Удалить токен?","Он исчезнет у участников сессии.",token.RequestDespawn));
    }
    private void NativeTokenTabs(TokenController token,int active){var tabs=AtlasUi.Row(_nativeDrawerContent,"TokenTabs",36,3);AtlasUi.Button(tabs,"Параметры",()=>AtlasInspectToken(token),active==0,height:36);AtlasUi.Button(tabs,"Доступ",()=>AtlasTokenDetails(token),active==1,height:36);AtlasUi.Button(tabs,"Статблок",()=>AtlasTokenStatBlock(token),active==2,height:36);}
    private void AtlasTokenStatBlock(TokenController token){
        if(token==null||!token.IsVisibleToLocalPlayer)return;NativeDrawer(token.TokenName,"token-statblock");_nativeToken=token;NativeTokenTabs(token,2);PublicStatBlockView view=null;
        if(token.CanEditMasterStats){var data=token.CaptureMasterData();var block=Array.Find(SceneFileStore.GetStatBlocks(),b=>b.id==data.statBlockId);if(block!=null){view=JsonUtility.FromJson<PublicStatBlockView>(JsonUtility.ToJson(block));view.visibleFields=(1<<11)-1;}}
        else if(!string.IsNullOrEmpty(token.PublicStatBlockJson))view=JsonUtility.FromJson<PublicStatBlockView>(token.PublicStatBlockJson);
        if(view==null){AtlasUi.Paragraph(_nativeDrawerContent,"Статблок не назначен или не опубликован мастером.");return;}
        void Field(int mask,string label,string value){if((view.visibleFields&mask)!=0)AtlasUi.Paragraph(_nativeDrawerContent,label+value);}
        Field(StatBlockPublicFields.Name,"",view.name);Field(StatBlockPublicFields.Size,"Размер: ",view.size);Field(StatBlockPublicFields.CreatureType,"Тип: ",view.creatureType);Field(StatBlockPublicFields.Alignment,"Мировоззрение: ",view.alignment);Field(StatBlockPublicFields.ArmorClass,"КД: ",view.armorClass.ToString());Field(StatBlockPublicFields.HitPoints,"ХП: ",view.hitPoints.ToString());Field(StatBlockPublicFields.Speed,"Скорость: ",view.speed);Field(StatBlockPublicFields.Abilities,"Характеристики: ",$"{view.strength} / {view.dexterity} / {view.constitution} / {view.intelligence} / {view.wisdom} / {view.charisma}");Field(StatBlockPublicFields.ChallengeRating,"Опасность: ",view.challengeRating);Field(StatBlockPublicFields.Description,"",view.description);if((view.visibleFields&StatBlockPublicFields.Actions)!=0)foreach(var action in view.actions??Array.Empty<StatBlockPublicAction>())AtlasUi.Paragraph(_nativeDrawerContent,action.name+"\n"+action.description);
    }
}

