using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Local presentation only: never disables the network root or its behaviours.</summary>
public sealed class TokenAppearance
{
    private sealed class Surface
    {
        public Renderer Renderer;
        public bool Enabled;
        public Material[] Normal;
        public Material[] Translucent;
    }

    private readonly List<Surface> _surfaces = new();
    private readonly List<(Collider Collider, bool Enabled)> _colliders = new();

    public TokenAppearance(Transform root)
    {
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            _surfaces.Add(new Surface { Renderer = renderer, Enabled = renderer.enabled,
                Normal = renderer.sharedMaterials });
        foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            _colliders.Add((collider, collider.enabled));
    }

    public void Apply(bool hidden, bool isGameMaster) => ApplyWithFog(hidden, isGameMaster, true);
    public void ApplyWithFog(bool hidden, bool isGameMaster, bool fogVisible)
    {
        bool visible = (!hidden || isGameMaster) && fogVisible;
        foreach (var surface in _surfaces)
        {
            if (surface.Renderer == null) continue;
            surface.Renderer.enabled = surface.Enabled && visible;
            if (hidden && isGameMaster)
            {
                if (surface.Translucent == null)
                    surface.Translucent = new Material[surface.Normal.Length];
                for (int i = 0; i < surface.Normal.Length; i++)
                {
                    var original = surface.Normal[i];
                    if (original == null) continue;
                    var material = surface.Translucent[i];
                    if (material == null) surface.Translucent[i] = material = new Material(original);
                    else material.CopyPropertiesFromMaterial(original);
                    MakeTranslucent(material);
                }
                surface.Renderer.sharedMaterials = surface.Translucent;
            }
            else surface.Renderer.sharedMaterials = surface.Normal;
        }
        foreach (var entry in _colliders)
            if (entry.Collider != null) entry.Collider.enabled = entry.Enabled && visible;
    }

    private static void MakeTranslucent(Material material)
    {
        foreach (string property in new[] { "_BaseColor", "_Color" })
            if (material.HasProperty(property))
            {
                Color color = material.GetColor(property);
                color.a *= 0.35f;
                material.SetColor(property, color);
            }
        SetFloat(material, "_Surface", 1); // URP
        SetFloat(material, "_Mode", 2); // Standard fade
        SetFloat(material, "_Blend", 0);
        SetFloat(material, "_AlphaClip", 0);
        SetFloat(material, "_AlphaToMask", 0);
        SetFloat(material, "_ZWrite", 0);
        SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
        SetFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        SetFloat(material, "_SrcBlendAlpha", (float)BlendMode.One);
        SetFloat(material, "_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.SetOverrideTag("RenderType", "Transparent");
        material.renderQueue = (int)RenderQueue.Transparent;
        material.SetShaderPassEnabled("ShadowCaster", false);
        material.SetShaderPassEnabled("DepthOnly", false);
    }

    private static void SetFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property)) material.SetFloat(property, value);
    }

    public void Dispose()
    {
        Apply(false, false);
        foreach (var surface in _surfaces)
            if (surface.Translucent != null)
                foreach (var material in surface.Translucent)
                    if (material != null) Object.Destroy(material);
    }
}
