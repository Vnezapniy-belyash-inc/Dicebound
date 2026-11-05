using UnityEngine;
using UnityEngine.UI;

public partial class DisplaySettingsUI
{
    private RectTransform _atlasSettingsBody;
    private string _atlasSettingsKey;
    private Text _atlasCountdown;
    private void UpdateAtlasSettings()
    {
        if(!IsOpen)return;
        string key=_section+":"+_pending+":"+_resolutionIndex+":"+_modeIndex+":"+SceneFileStore.MovementPolicy+":"+LocalControls.TokenHover;
        if(key!=_atlasSettingsKey){_atlasSettingsKey=key;DrawAtlasSettings();}
        if(_atlasCountdown!=null&&_pending)_atlasCountdown.text="Подтвердите экран · "+Mathf.CeilToInt(_confirmationDeadline-Time.realtimeSinceStartup)+" с";
    }
    private void DrawAtlasSettings()
    {
        if(_atlasSettingsBody==null){
            var scaler=_inputBlocker.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
            var modal=_inputBlocker.transform.GetChild(0).GetComponent<Image>();modal.color=new Color(.03f,.10f,.07f,.82f);
            _atlasSettingsBody=AtlasUi.Scroll(_inputBlocker.transform,"AtlasSettings",out var scroll,26);var panel=(RectTransform)scroll.transform;
            panel.anchorMin=panel.anchorMax=new Vector2(.5f,.5f);panel.pivot=new Vector2(.5f,.5f);panel.sizeDelta=new Vector2(760,620);
        }
        AtlasUi.Clear(_atlasSettingsBody);_atlasCountdown=null;
        var heading=AtlasUi.Row(_atlasSettingsBody,"Heading",44);AtlasUi.Text(heading,"Настройки",29,title:true,height:44);AtlasUi.Button(heading,"×",()=>{if(_pending)Revert();SetOpen(false);},height:38);
        var tabs=AtlasUi.Row(_atlasSettingsBody,"Tabs",40);AtlasUi.Button(tabs,"Экран",()=>{_section=0;_atlasSettingsKey=null;});AtlasUi.Button(tabs,"Управление",()=>{_section=1;_atlasSettingsKey=null;});
        if(Unity.Netcode.NetworkManager.Singleton?.IsHost==true)AtlasUi.Button(tabs,"Кампания",()=>{_section=2;_atlasSettingsKey=null;});
        if(_pending){_atlasCountdown=AtlasUi.Text(_atlasSettingsBody,"Подтвердите экран",23,title:true,height:50);AtlasUi.Button(_atlasSettingsBody,"Сохранить",Confirm,true);AtlasUi.Button(_atlasSettingsBody,"Вернуть прежние · Esc",Revert);return;}
        if(_section==0){
            AtlasUi.Text(_atlasSettingsBody,"Разрешение",19,title:true,height:36);var resolution=AtlasUi.Row(_atlasSettingsBody,"Resolution",42);
            AtlasUi.Button(resolution,"‹",()=>{_resolutionIndex=(_resolutionIndex-1+_resolutions.Count)%_resolutions.Count;});AtlasUi.Text(resolution,_resolutions[_resolutionIndex].x+" × "+_resolutions[_resolutionIndex].y,18,height:42);AtlasUi.Button(resolution,"›",()=>{_resolutionIndex=(_resolutionIndex+1)%_resolutions.Count;});
            AtlasUi.Text(_atlasSettingsBody,"Режим окна",19,title:true,height:36);var mode=AtlasUi.Row(_atlasSettingsBody,"Mode",42);AtlasUi.Button(mode,"‹",()=>{_modeIndex=(_modeIndex+_modes.Length-1)%_modes.Length;});AtlasUi.Text(mode,_modeNames[_modeIndex],16,height:42);AtlasUi.Button(mode,"›",()=>{_modeIndex=(_modeIndex+1)%_modes.Length;});
            AtlasUi.Button(_atlasSettingsBody,"Применить экран…",Apply,true);
            var scale=AtlasUi.Row(_atlasSettingsBody,"Scale",42);AtlasUi.Text(scale,"Масштаб UI",17,height:42);AtlasUi.Button(scale,"−",()=>_uiScale.SetScale(LocalUiScale.Value-.1f));AtlasUi.Button(scale,"100%",()=>_uiScale.SetScale(1));AtlasUi.Button(scale,"+",()=>_uiScale.SetScale(LocalUiScale.Value+.1f));
        }else if(_section==1){
            AtlasUi.Text(_atlasSettingsBody,"ЛКМ — инструмент · ПКМ / Esc — завершить\nWASD — камера · F1 — вид камеры\nПКМ по токену — свойства",17,height:120);
            var speed=AtlasUi.Row(_atlasSettingsBody,"CameraSpeed",40);AtlasUi.Text(speed,"Скорость камеры",16,height:40);AtlasUi.Button(speed,"−",()=>LocalControls.SetCameraSpeed(LocalControls.CameraSpeed-.1f));AtlasUi.Button(speed,"+",()=>LocalControls.SetCameraSpeed(LocalControls.CameraSpeed+.1f));
            AtlasUi.Button(_atlasSettingsBody,LocalControls.TokenHover?"Убрать подсказки":"Показать подсказки",()=>LocalControls.SetTokenHover(!LocalControls.TokenHover));AtlasUi.Button(_atlasSettingsBody,"Сбросить управление",()=>{LocalControls.SetCameraSpeed(1);LocalControls.SetTokenHover(true);});
        }else{
            AtlasUi.Text(_atlasSettingsBody,"Правила перемещения",23,title:true,height:44);var names=new[]{"Свободное","Предупреждать","Решает мастер","По клеткам"};for(int i=0;i<4;i++){int value=i;AtlasUi.Button(_atlasSettingsBody,names[i],()=>SceneFileStore.SetMovementPolicy((PlayerMovementPolicy)value),(int)SceneFileStore.MovementPolicy==i);}
            AtlasUi.Button(_atlasSettingsBody,"Автосохранение: включить / выключить",()=>{var fog=FogManager.Instance;if(fog!=null)SceneFileStore.SetCampaignPreferences(!fog.Autosave,fog.SaveWithHistory);});AtlasUi.Button(_atlasSettingsBody,"История тумана в экспорте",()=>{var fog=FogManager.Instance;if(fog!=null)SceneFileStore.SetCampaignPreferences(fog.Autosave,!fog.SaveWithHistory);});
        }
        AtlasUi.Button(_atlasSettingsBody,"Готово · Esc",()=>SetOpen(false));
    }
}
