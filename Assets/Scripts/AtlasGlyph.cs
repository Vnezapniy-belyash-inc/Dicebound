using UnityEngine;
using UnityEngine.UI;

/// <summary>Small native line icons; independent of OS glyph/font fallback.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class AtlasGlyph : MaskableGraphic
{
    public string key;
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();var rect=rectTransform.rect;
        void Line(float ax,float ay,float bx,float by){var a=new Vector2(rect.xMin+ax/24*rect.width,rect.yMin+(24-ay)/24*rect.height);var b=new Vector2(rect.xMin+bx/24*rect.width,rect.yMin+(24-by)/24*rect.height);var n=(b-a).normalized;var offset=new Vector2(-n.y,n.x)*.8f;int start=vh.currentVertCount;vh.AddVert(a-offset,color,Vector2.zero);vh.AddVert(a+offset,color,Vector2.zero);vh.AddVert(b+offset,color,Vector2.zero);vh.AddVert(b-offset,color,Vector2.zero);vh.AddTriangle(start,start+1,start+2);vh.AddTriangle(start,start+2,start+3);}
        if(key=="ring"){for(int i=0;i<48;i++){float a=i*Mathf.PI/24,b=(i+1)*Mathf.PI/24;Line(12+10*Mathf.Cos(a),12+10*Mathf.Sin(a),12+10*Mathf.Cos(b),12+10*Mathf.Sin(b));}return;}
        if(key=="scenes"){foreach(int y in new[]{3,9,15}){Line(3,y+3,12,y);Line(12,y,21,y+3);Line(21,y+3,12,y+6);Line(12,y+6,3,y+3);}return;}
        if(key=="book"){Line(3,4,3,20);Line(3,4,10,4);Line(10,4,12,6);Line(12,6,14,4);Line(14,4,21,4);Line(21,4,21,20);Line(3,20,10,20);Line(10,20,12,22);Line(12,22,14,20);Line(14,20,21,20);Line(12,6,12,22);return;}
        if(key=="user"){Line(8,3,16,3);Line(16,3,18,7);Line(18,7,16,11);Line(16,11,8,11);Line(8,11,6,7);Line(6,7,8,3);Line(4,21,4,18);Line(4,18,8,14);Line(8,14,16,14);Line(16,14,20,18);Line(20,18,20,21);Line(20,21,4,21);return;}
        if(key=="exit"){Line(10,3,3,3);Line(3,3,3,21);Line(3,21,10,21);Line(8,12,22,12);Line(17,7,22,12);Line(22,12,17,17);return;}
        if(key=="die"){Line(12,2,22,7);Line(22,7,22,18);Line(22,18,12,23);Line(12,23,2,18);Line(2,18,2,7);Line(2,7,12,2);Line(12,2,7,10);Line(7,10,17,10);Line(17,10,12,2);Line(7,10,12,20);Line(12,20,17,10);Line(2,7,7,10);Line(22,7,17,10);Line(2,18,12,20);Line(22,18,12,20);return;}
        Line(3,4,21,4);Line(21,4,21,20);Line(21,20,3,20);Line(3,20,3,4);Line(9,4,9,20);Line(15,4,15,20);Line(3,10,21,10);Line(3,15,21,15);
    }
}
