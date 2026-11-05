using UnityEngine;
using UnityEngine.UI;

/// <summary>Continuous rounded outline, with an antialiased inner and outer edge.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class AtlasBorder : MaskableGraphic
{
    public float radius = 10f;
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();var r=rectTransform.rect;
        float pixel=1f/Mathf.Max(.1f,canvas!=null?canvas.scaleFactor:1f);
        float[] inset={0,.7f*pixel,1.6f*pixel,2.3f*pixel};
        const int steps=12,points=steps*4;
        for(int band=0;band<4;band++){
            var alpha=color;alpha.a*=band==0||band==3?0:1;
            var box=new Rect(r.x+inset[band],r.y+inset[band],Mathf.Max(0,r.width-2*inset[band]),Mathf.Max(0,r.height-2*inset[band]));
            float round=Mathf.Max(0,Mathf.Min(radius-inset[band],Mathf.Min(box.width,box.height)*.5f));
            for(int corner=0;corner<4;corner++){
                var center=new Vector2(corner==0||corner==3?box.xMax-round:box.xMin+round,corner<2?box.yMax-round:box.yMin+round);
                for(int i=0;i<steps;i++){float angle=(corner*90f+i*90f/(steps-1))*Mathf.Deg2Rad;vh.AddVert(center+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*round,alpha,Vector2.zero);}
            }
        }
        for(int band=0;band<3;band++)for(int i=0;i<points;i++){int j=(i+1)%points,a=band*points+i,b=band*points+j,c=(band+1)*points+j,d=(band+1)*points+i;vh.AddTriangle(a,b,c);vh.AddTriangle(a,c,d);}
    }
}
