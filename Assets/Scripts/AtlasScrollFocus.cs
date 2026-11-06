using UnityEngine;
using UnityEngine.UI;

/// <summary>Restores the user's scroll, revealing a new turn once after layout has settled.</summary>
public sealed class AtlasScrollFocus : MonoBehaviour
{
    private ScrollRect _scroll;
    private RectTransform _target;
    private float _position;
    private bool _horizontal, _reveal, _pending;

    public void Initialize(ScrollRect scroll,RectTransform target,float position,bool horizontal,bool reveal)
    {
        _scroll=scroll;_target=target;_position=position;_horizontal=horizontal;_reveal=reveal;_pending=true;
    }
    private void LateUpdate()
    {
        if(!_pending||_scroll==null||_scroll.viewport==null)return;
        Canvas.ForceUpdateCanvases();
        if(_scroll.viewport.rect.width<=1||_scroll.viewport.rect.height<=1)return;
        _pending=false;_scroll.StopMovement();
        if(_horizontal)_scroll.horizontalNormalizedPosition=_position;else _scroll.verticalNormalizedPosition=_position;
        if(!_reveal||_target==null)return;
        var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(_scroll.viewport,_target);
        var view=_scroll.viewport.rect;
        if(_horizontal){
            float shift=bounds.min.x<view.xMin?view.xMin-bounds.min.x:bounds.max.x>view.xMax?view.xMax-bounds.max.x:0;
            _scroll.content.anchoredPosition+=Vector2.right*shift;
        }else{
            float shift=bounds.max.y>view.yMax?view.yMax-bounds.max.y:bounds.min.y<view.yMin?view.yMin-bounds.min.y:0;
            _scroll.content.anchoredPosition+=Vector2.up*shift;
        }
    }
}
