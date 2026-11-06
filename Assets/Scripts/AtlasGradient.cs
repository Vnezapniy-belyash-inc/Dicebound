using UnityEngine;
using UnityEngine.UI;

/// <summary>Subtle paper shading from the Atlas reference; preserves Selectable tint.</summary>
public sealed class AtlasGradient : BaseMeshEffect
{
    public float bottom = .94f;
    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive()) return;
        var rect = ((RectTransform)transform).rect;
        var vertex = new UIVertex();
        for (int i = 0; i < mesh.currentVertCount; i++)
        {
            mesh.PopulateUIVertex(ref vertex, i);
            float shade = Mathf.Lerp(bottom, 1, Mathf.InverseLerp(rect.yMin, rect.yMax, vertex.position.y));
            var tint = (Color)vertex.color;
            vertex.color = new Color(tint.r * shade, tint.g * shade, tint.b * shade, tint.a);
            mesh.SetUIVertex(vertex, i);
        }
    }
}
