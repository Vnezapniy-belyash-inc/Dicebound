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
        void Line(float ax,float ay,float bx,float by){
            var a=new Vector2(rect.xMin+ax/24*rect.width,rect.yMin+(24-ay)/24*rect.height);var b=new Vector2(rect.xMin+bx/24*rect.width,rect.yMin+(24-by)/24*rect.height);
            var n=(b-a).normalized;var normal=new Vector2(-n.y,n.x);float unit=1f/Mathf.Max(.1f,canvas!=null?canvas.scaleFactor:1f);float half=.8f*unit,feather=.7f*unit;
            float[] offsets={-half-feather,-half,half,half+feather};int start=vh.currentVertCount;
            for(int i=0;i<4;i++){var tint=color;if(i==0||i==3)tint.a=0;vh.AddVert(a+normal*offsets[i],tint,Vector2.zero);vh.AddVert(b+normal*offsets[i],tint,Vector2.zero);}
            for(int i=0;i<3;i++){int p=start+i*2;vh.AddTriangle(p,p+1,p+3);vh.AddTriangle(p,p+3,p+2);}
        }
        void Arc(float x,float y,float radius,float start,float end,int segments=24){for(int i=0;i<segments;i++){float a=Mathf.Lerp(start,end,(float)i/segments)*Mathf.Deg2Rad,b=Mathf.Lerp(start,end,(float)(i+1)/segments)*Mathf.Deg2Rad;Line(x+Mathf.Cos(a)*radius,y+Mathf.Sin(a)*radius,x+Mathf.Cos(b)*radius,y+Mathf.Sin(b)*radius);}}

        if(key=="ring"){for(int i=0;i<48;i++){float a=i*Mathf.PI/24,b=(i+1)*Mathf.PI/24;Line(12+10*Mathf.Cos(a),12+10*Mathf.Sin(a),12+10*Mathf.Cos(b),12+10*Mathf.Sin(b));}return;}
        if(key=="scenes"){foreach(int y in new[]{3,9,15}){Line(3,y+3,12,y);Line(12,y,21,y+3);Line(21,y+3,12,y+6);Line(12,y+6,3,y+3);}return;}
        if(key=="book"){Line(3,4,3,20);Line(3,4,10,4);Line(10,4,12,6);Line(12,6,14,4);Line(14,4,21,4);Line(21,4,21,20);Line(3,20,10,20);Line(10,20,12,22);Line(12,22,14,20);Line(14,20,21,20);Line(12,6,12,22);return;}
        if(key=="user"||key=="users"){Arc(12,7,4,0,360);Arc(12,21,8,180,360);if(key=="users"){Arc(20,8,3,-85,85);Arc(18,20,6,-80,0);}return;}

        if(key=="exit"){Line(10,3,3,3);Line(3,3,3,21);Line(3,21,10,21);Line(8,12,22,12);Line(17,7,22,12);Line(22,12,17,17);return;}
        if(key=="arrow-left"){Line(5,12,20,12);Line(5,12,11,6);Line(5,12,11,18);return;}
        if(key=="arrow-right"){Line(4,12,19,12);Line(19,12,13,6);Line(19,12,13,18);return;}
        if(key=="arrow-up"){Line(12,5,12,20);Line(12,5,6,11);Line(12,5,18,11);return;}
        if (key == "pin")
        {
            Line(8, 4, 16, 4);
            Line(9, 4, 9, 10); Line(15, 4, 15, 10);
            Line(9, 10, 6, 14); Line(15, 10, 18, 14);
            Line(6, 14, 18, 14); Line(12, 14, 12, 21);
            return;
        }
        if (key == "paperclip")
        {
            Line(21.44f, 11.05f, 12.24f, 20.24f);
            Arc(8, 16, 6, 45, 225, 12);
            Line(3.76f, 11.76f, 12.95f, 2.57f);
            Arc(15.78f, 5.4f, 4, 225, 405, 12);
            Line(18.61f, 8.23f, 9.41f, 17.41f);
            Arc(8, 16, 2, 45, 225, 10);
            Line(6.59f, 14.59f, 15.07f, 6.10f);
            return;
        }
        if(key=="folder"){Line(3,6,10,6);Line(10,6,12,8);Line(12,8,21,8);Line(21,8,21,19);Line(21,19,3,19);Line(3,19,3,6);return;}
        if(key=="file"){Line(6,3,14,3);Line(14,3,19,8);Line(19,8,19,21);Line(19,21,6,21);Line(6,21,6,3);Line(14,3,14,8);Line(14,8,19,8);return;}
        if(key=="die"){Line(12,2,22,7);Line(22,7,22,18);Line(22,18,12,23);Line(12,23,2,18);Line(2,18,2,7);Line(2,7,12,2);Line(12,2,7,10);Line(7,10,17,10);Line(17,10,12,2);Line(7,10,12,20);Line(12,20,17,10);Line(2,7,7,10);Line(22,7,17,10);Line(2,18,12,20);Line(22,18,12,20);return;}
        Line(3,4,21,4);Line(21,4,21,20);Line(21,20,3,20);Line(3,20,3,4);Line(9,4,9,20);Line(15,4,15,20);Line(3,10,21,10);Line(3,15,21,15);
    }
}
