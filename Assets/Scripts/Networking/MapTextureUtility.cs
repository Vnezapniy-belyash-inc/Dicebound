using UnityEngine;

/// <summary>Bound map GPU/CPU storage before distributing it to the table.</summary>
public static class MapTextureUtility
{
    public const int MaxDimension = 4096;
    public static Texture2D Resize(Texture2D source) => ResizeTo(source, MaxDimension);
    public static Texture2D ResizeTo(Texture2D source, int maxDimension)
    {
        int sourceWidth = source.width, sourceHeight = source.height;
        float ratio = Mathf.Min(1f, (float)maxDimension / Mathf.Max(sourceWidth, sourceHeight));
        int width = Mathf.Max(1, Mathf.RoundToInt(sourceWidth * ratio));
        int height = Mathf.Max(1, Mathf.RoundToInt(sourceHeight * ratio));
        var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var input = source.GetRawTextureData<byte>();
        var output = result.GetRawTextureData<Color32>();
        int channels = source.format == TextureFormat.RGB24 ? 3 : 4;
        for (int y = 0; y < height; y++)
        {
            float sy = Mathf.Clamp((y + 0.5f) * sourceHeight / height - 0.5f, 0, sourceHeight - 1);
            int y0 = (int)sy, y1 = Mathf.Min(y0 + 1, sourceHeight - 1);
            float fy = sy - y0;
            for (int x = 0; x < width; x++)
            {
                float sx = Mathf.Clamp((x + 0.5f) * sourceWidth / width - 0.5f, 0, sourceWidth - 1);
                int x0 = (int)sx, x1 = Mathf.Min(x0 + 1, sourceWidth - 1);
                float fx = sx - x0;
                int a = (y0 * sourceWidth + x0) * channels, b = (y0 * sourceWidth + x1) * channels;
                int c = (y1 * sourceWidth + x0) * channels, d = (y1 * sourceWidth + x1) * channels;
                byte Blend(int channel) => (byte)Mathf.RoundToInt(Mathf.Lerp(Mathf.Lerp(input[a + channel], input[b + channel], fx), Mathf.Lerp(input[c + channel], input[d + channel], fx), fy));
                output[y * width + x] = new Color32(Blend(0), Blend(1), Blend(2), channels == 4 ? Blend(3) : (byte)255);
            }
        }
        result.Apply(false);
        return result;
    }
    public static byte[] TryJpegWithinBudget(Texture2D texture, int maxBytes)
    {
        if (texture.format == TextureFormat.RGBA32)
        {
            var pixels = texture.GetRawTextureData<byte>();
            for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] != 255) return null;
        }
        else if (texture.format != TextureFormat.RGB24) return null;
        foreach (int quality in new[] { 95, 90, 80, 65, 50, 35 })
        {
            var bytes = texture.EncodeToJPG(quality);
            if (bytes.Length <= maxBytes) return bytes;
        }
        return null;
    }
}
