using System.Collections.Generic;
using UnityEngine;

public static class FunEffectSprites
{
    private sealed class Asset { public Texture2D Texture; public Rect Uv; }
    private static readonly Dictionary<string, Asset> Assets = new();

    private static Asset Load(string name)
    {
        if (Assets.TryGetValue(name, out var cached)) return cached;
        var texture = Resources.Load<Texture2D>("Fun/" + name);
        if (texture == null) return null;
        Rect uv = new Rect(0, 0, 1, 1);
        if (texture.isReadable)
        {
            var pixels = texture.GetPixels32();
            int minX = texture.width, minY = texture.height, maxX = -1, maxY = -1;
            for (int y = 0; y < texture.height; y++) for (int x = 0; x < texture.width; x++)
                if (pixels[y * texture.width + x].a > 32)
                { minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x); minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
            if (maxX >= minX) uv = new Rect((float)minX / texture.width, (float)minY / texture.height,
                (float)(maxX - minX + 1) / texture.width, (float)(maxY - minY + 1) / texture.height);
        }
        cached = new Asset { Texture = texture, Uv = uv };
        Assets[name] = cached;
        return cached;
    }

    public static void DrawAsset(string name, Rect area)
    {
        var asset = Load(name);
        if (asset == null) return;
        float aspect = asset.Texture.width * asset.Uv.width / (asset.Texture.height * asset.Uv.height);
        float width = Mathf.Min(area.width, area.height * aspect), height = width / aspect;
        GUI.DrawTextureWithTexCoords(new Rect(area.center.x - width / 2, area.center.y - height / 2, width, height), asset.Texture, asset.Uv);
    }

}
