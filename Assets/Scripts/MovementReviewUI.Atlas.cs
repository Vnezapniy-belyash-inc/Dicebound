using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class MovementReviewUI
{
    private RectTransform _atlasMovementBody;
    private Text _atlasMovementCountdown;
    private string _atlasMovementKey;
    private void UpdateAtlasMovement()
    {
        if(!IsOpen)return;
        string key=(_token!=null?_token.SceneId:"")+":"+_approval+":"+string.Join("|",_pending.Where(t=>t!=null).Select(t=>t.SceneId));
        if(key!=_atlasMovementKey){_atlasMovementKey=key;DrawAtlasMovement();}
        if(_atlasMovementCountdown!=null&&_token!=null)_atlasMovementCountdown.text="Осталось: "+Mathf.CeilToInt(Mathf.Max(0,_deadline-Time.unscaledTime))+" с";
    }
    private void DrawAtlasMovement()
    {
        if(_atlasMovementBody==null){
            var scaler=_blocker.AddComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
            _blocker.transform.GetChild(0).GetComponent<Image>().color=new Color(.04f,.11f,.08f,.8f);
            _atlasMovementBody=AtlasUi.Scroll(_blocker.transform,"MovementDecision",out var scroll,26);var panel=(RectTransform)scroll.transform;panel.anchorMin=panel.anchorMax=new Vector2(.5f,.5f);panel.pivot=new Vector2(.5f,.5f);panel.sizeDelta=new Vector2(640,440);
        }
        AtlasUi.Clear(_atlasMovementBody);_atlasMovementCountdown=null;
        AtlasUi.Text(_atlasMovementBody,_token!=null?(_approval?"Ожидание мастера":"Проверить перемещение"):"Запросы игроков",28,title:true,height:50);
        if(_token!=null){
            AtlasUi.Text(_atlasMovementBody,"До подтверждения токен остаётся на исходной клетке, карта не раскрывается.",16,height:80);_atlasMovementCountdown=AtlasUi.Text(_atlasMovementBody,"",17,height:36);
            if(!_approval)AtlasUi.Button(_atlasMovementBody,"Подтвердить перемещение",()=>{if(_token!=null)_token.AnswerMovement(_request,true);_token=null;},true);
            AtlasUi.Button(_atlasMovementBody,"Отменить · Esc",Cancel);
        }else{
            foreach(var t in _pending){if(t==null||!t.HasMovementRequest)continue;var token=t;var grid=FindAnyObjectByType<GridManager>();var cell=grid==null?Vector2Int.zero:grid.GetGridPosition(t.RequestedMovementPosition);
                AtlasUi.Text(_atlasMovementBody,t.TokenName+" → клетка "+(cell.x+1)+", "+(cell.y+1),18,height:38);var row=AtlasUi.Row(_atlasMovementBody,"Decision",42);AtlasUi.Button(row,"Разрешить",()=>token.ResolveMovementAsMaster(true),true);AtlasUi.Button(row,"Отклонить",()=>token.ResolveMovementAsMaster(false));
            }
            if(_pending.Length==0)AtlasUi.Text(_atlasMovementBody,"Нет ожидающих запросов.",16,height:44);
            AtlasUi.Button(_atlasMovementBody,"Закрыть · Esc",()=>_hostOpen=false);
        }
    }
}
