using UnityEngine;

/// <summary>Map 3000, grid 3005, fog cover 3010, visible overlays 3015–3017, walls 3020, pings 3030.</summary>
static class WorldOverlayMaterial
{
    public const int EffectsQueue = 3015;
    public const int PreviewQueue = 3016;
    public const int MeasurementQueue = 3017;

    public static Material Create(int queue)
    {
        var material = new Material(Resources.Load<Shader>("DiceboundMarkup"));
        material.renderQueue = queue;
        return material;
    }

    public static void RefreshFog(Material material)
    {
        var fog = FogManager.Instance;
        if (fog != null) fog.ApplyMarkupFog(material, fog.ShowingPlayerView);
        else material.SetFloat("_FogEnabled", 1); // Missing state must not expose hidden effects.
    }
}
