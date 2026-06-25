using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Places readable digits on each face of the generated d20 mesh using flat quads.
/// </summary>
[RequireComponent(typeof(DiceMeshGenerator))]
[DefaultExecutionOrder(100)]
public class DiceFaceLabels : MonoBehaviour
{
    [Header("Label Style")]
    [SerializeField] Color labelColor = new Color(0.98f, 0.95f, 0.88f, 1f);

    [Tooltip("How much of the face area a label may occupy.")]
    [SerializeField] float faceFill = 0.82f;

    [Tooltip("Extra scale reduction for two-digit values (10-20).")]
    [SerializeField] float twoDigitScale = 0.78f;

    [SerializeField] int textureFontSize = 72;

    const string LabelsRootName = "FaceLabels";

    void Start()
    {
        BuildLabels();
    }

    [ContextMenu("Rebuild Face Labels")]
    public void BuildLabels()
    {
        Transform existingRoot = transform.Find(LabelsRootName);
        if (existingRoot != null)
        {
            if (Application.isPlaying)
                Destroy(existingRoot.gameObject);
            else
                DestroyImmediate(existingRoot.gameObject);
        }

        DiceMeshGenerator meshGenerator = GetComponent<DiceMeshGenerator>();
        if (meshGenerator.FaceNormals == null || meshGenerator.FaceValues == null)
            return;

        var labelsRoot = new GameObject(LabelsRootName);
        labelsRoot.transform.SetParent(transform, false);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        for (int i = 0; i < meshGenerator.FaceNormals.Length; i++)
        {
            int value = meshGenerator.FaceValues[i];
            float maxSize = meshGenerator.FaceInradii[i] * 2f * faceFill;
            if (value >= 10)
                maxSize *= twoDigitScale;

            CreateFaceLabel(
                labelsRoot.transform,
                meshGenerator.FaceCenters[i],
                meshGenerator.FaceNormals[i],
                meshGenerator.FaceLabelUps[i],
                value,
                font,
                maxSize,
                textureFontSize,
                labelColor);
        }
    }

    static void CreateFaceLabel(
        Transform parent,
        Vector3 localPosition,
        Vector3 localNormal,
        Vector3 localUp,
        int value,
        Font font,
        float maxSize,
        int fontSize,
        Color color)
    {
        string text = value.ToString();
        Texture2D texture = RenderTextTexture(text, font, fontSize, color, out float aspect);

        float width;
        float height;
        if (aspect >= 1f)
        {
            width = maxSize;
            height = maxSize / aspect;
        }
        else
        {
            height = maxSize;
            width = maxSize * aspect;
        }

        var labelObject = new GameObject($"Face_{value}");
        labelObject.transform.SetParent(parent, false);
        labelObject.transform.localPosition = localPosition;
        labelObject.transform.localRotation = Quaternion.LookRotation(localNormal.normalized, localUp);

        MeshFilter meshFilter = labelObject.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = CreateQuadMesh(width, height);

        MeshRenderer renderer = labelObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = CreateLabelMaterial(texture);
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static Mesh CreateQuadMesh(float width, float height)
    {
        float halfWidth = width * 0.5f;
        float halfHeight = height * 0.5f;

        var mesh = new Mesh { name = "FaceLabelQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-halfWidth, -halfHeight, 0f),
            new Vector3(halfWidth, -halfHeight, 0f),
            new Vector3(-halfWidth, halfHeight, 0f),
            new Vector3(halfWidth, halfHeight, 0f)
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
        return mesh;
    }

    static Texture2D RenderTextTexture(string text, Font font, int fontSize, Color color, out float aspect)
    {
        font.RequestCharactersInTexture(text, fontSize, FontStyle.Bold);

        int width = 0;
        int height = fontSize;
        foreach (char character in text)
        {
            font.GetCharacterInfo(character, out CharacterInfo info, fontSize, FontStyle.Bold);
            width += info.advance;
        }

        width = Mathf.Max(width, 1);
        height = Mathf.Max(height, 1);

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Color[width * height];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = Color.clear;
        texture.SetPixels(pixels);

        var source = (Texture2D)font.material.mainTexture;
        int cursorX = 0;

        foreach (char character in text)
        {
            font.GetCharacterInfo(character, out CharacterInfo info, fontSize, FontStyle.Bold);
            BlitGlyph(source, texture, info, cursorX, color);
            cursorX += info.advance;
        }

        texture.Apply();
        aspect = (float)width / height;
        return texture;
    }

    static void BlitGlyph(Texture2D source, Texture2D target, CharacterInfo info, int cursorX, Color color)
    {
        int glyphWidth = info.glyphWidth;
        int glyphHeight = info.glyphHeight;
        if (glyphWidth <= 0 || glyphHeight <= 0)
            return;

        for (int y = 0; y < glyphHeight; y++)
        {
            for (int x = 0; x < glyphWidth; x++)
            {
                float u = (x + 0.5f) / glyphWidth;
                float v = (y + 0.5f) / glyphHeight;

                Vector2 bottom = Vector2.Lerp(info.uvBottomLeft, info.uvBottomRight, u);
                Vector2 top = Vector2.Lerp(info.uvTopLeft, info.uvTopRight, u);
                Vector2 uv = Vector2.Lerp(bottom, top, v);

                Color sample = source.GetPixelBilinear(uv.x, uv.y);
                Color final = sample * color;
                final.a = sample.a * color.a;

                int px = cursorX + info.minX + x;
                int py = info.minY + y;
                if (px < 0 || py < 0 || px >= target.width || py >= target.height)
                    continue;

                if (final.a <= 0f)
                    continue;

                Color existing = target.GetPixel(px, py);
                target.SetPixel(px, py, BlendOver(existing, final));
            }
        }
    }

    static Color BlendOver(Color under, Color over)
    {
        float alpha = over.a + under.a * (1f - over.a);
        if (alpha <= 0f)
            return Color.clear;

        return new Color(
            (over.r * over.a + under.r * under.a * (1f - over.a)) / alpha,
            (over.g * over.a + under.g * under.a * (1f - over.a)) / alpha,
            (over.b * over.a + under.b * under.a * (1f - over.a)) / alpha,
            alpha);
    }

    static Material CreateLabelMaterial(Texture2D texture)
    {
        Shader shader = Shader.Find("Unlit/Transparent");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");

        var material = new Material(shader);
        material.mainTexture = texture;
        material.color = Color.white;
        material.renderQueue = (int)RenderQueue.Transparent;
        return material;
    }
}
