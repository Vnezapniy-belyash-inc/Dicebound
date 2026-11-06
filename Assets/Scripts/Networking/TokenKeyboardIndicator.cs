using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Local, quiet selection rim. Kept outside the token hierarchy so drag previews cannot clone it.</summary>
[DefaultExecutionOrder(11010)]
public sealed class TokenKeyboardIndicator : MonoBehaviour
{
    private TokenController _token;
    private LineRenderer _ring;
    private Material _material;

    private void Awake() => _token=GetComponent<TokenController>();

    private void LateUpdate()
    {
        bool visible=_token!=null && _token.isActiveAndEnabled && _token.IsKeyboardMovementSelected;
        if(!visible){if(_ring!=null)_ring.enabled=false;return;}
        if(_ring==null)CreateRing();
        _ring.enabled=true;
        var scale=_token.transform.lossyScale;
        _ring.transform.position=_token.DisplayPosition+Vector3.up*(Mathf.Abs(scale.y)+.008f);
        _ring.transform.localScale=new Vector3(Mathf.Abs(scale.x)*.5f+.025f,Mathf.Abs(scale.z)*.5f+.025f,1);
    }

    private void CreateRing()
    {
        var go=new GameObject("Local keyboard selection rim");
        go.layer=gameObject.layer;
        _ring=go.AddComponent<LineRenderer>();
        _material=new Material(Shader.Find("Sprites/Default"));
        _ring.sharedMaterial=_material;
        _ring.useWorldSpace=false;_ring.loop=true;_ring.positionCount=64;
        _ring.widthMultiplier=.012f;
        _ring.startColor=_ring.endColor=new Color(.78f,.65f,.40f,.45f);
        _ring.shadowCastingMode=ShadowCastingMode.Off;_ring.receiveShadows=false;
        _ring.lightProbeUsage=LightProbeUsage.Off;_ring.reflectionProbeUsage=ReflectionProbeUsage.Off;
        _ring.alignment=LineAlignment.TransformZ;
        // A horizontal ribbon: its facing normal is the token's up direction.
        go.transform.rotation=Quaternion.Euler(90,0,0);
        for(int i=0;i<64;i++){
            float angle=i*Mathf.PI*2/64;
            _ring.SetPosition(i,new Vector3(Mathf.Cos(angle),Mathf.Sin(angle),0));
        }
    }

    private void OnDisable(){if(_ring!=null)_ring.enabled=false;}
    private void OnDestroy()
    {
        if(_ring!=null)Destroy(_ring.gameObject);
        if(_material!=null)Destroy(_material);
    }
}
