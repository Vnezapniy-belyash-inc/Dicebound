using UnityEngine;

/// <summary>
/// Shared label material setup for local and network dice.
/// Uses Resources.Load so the FontFaceUnlit shader is included in builds.
/// </summary>
public static class DiceLabelSetup
{
    private const string FontMaterialPath = "DiceFontMaterial";
    private const float AlphaCutoff = 0.1f;

    private static Material _fontTemplate;

    public static void ApplyLabelMaterial(MeshRenderer renderer, Color textColor)
    {
        if (renderer == null) return;

        if (_fontTemplate == null)
        {
            _fontTemplate = Resources.Load<Material>(FontMaterialPath);
            if (_fontTemplate == null)
                Debug.LogError($"[DiceLabelSetup] Material '{FontMaterialPath}' not found in Resources.");
        }

        Texture fontTexture = renderer.material != null ? renderer.material.mainTexture : null;

        if (_fontTemplate != null)
        {
            var labelMat = new Material(_fontTemplate);
            if (fontTexture != null)
                labelMat.mainTexture = fontTexture;
            labelMat.color = textColor;
            labelMat.SetFloat("_Cutoff", AlphaCutoff);
            renderer.material = labelMat;
            return;
        }

        // Fallback: built-in cutout shader (included in Always Included Shaders)
        Shader cutout = Shader.Find("Unlit/Transparent Cutout");
        if (cutout == null) return;

        var fallback = new Material(cutout);
        if (fontTexture != null)
            fallback.mainTexture = fontTexture;
        fallback.color = textColor;
        fallback.SetFloat("_Cutoff", AlphaCutoff);
        fallback.renderQueue = 2450;
        renderer.material = fallback;
    }

    public static void UpdateBackFaceVisibility(
        MeshRenderer[] faceRenderers,
        DieFaceData[] faces,
        Transform dieTransform,
        Camera camera)
    {
        if (faceRenderers == null || faces == null || dieTransform == null || camera == null)
            return;

        Vector3 camPos = camera.transform.position;
        for (int i = 0; i < faceRenderers.Length; i++)
        {
            if (faceRenderers[i] == null) continue;
            Vector3 worldNormal = dieTransform.TransformDirection(faces[i].normal);
            Vector3 toCamera = (camPos - dieTransform.TransformPoint(faces[i].center)).normalized;
            faceRenderers[i].enabled = Vector3.Dot(worldNormal, toCamera) > 0f;
        }
    }
}
