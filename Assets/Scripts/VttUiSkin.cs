using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Shared uGUI appearance. Views keep their own existing gameplay callbacks.</summary>
public static class VttUiSkin
{
    public static readonly Color Panel = new(0.055f, 0.070f, 0.092f, 0.94f);
    public static readonly Color Raised = new(0.090f, 0.112f, 0.144f, 0.96f);
    public static readonly Color Button = new(0.120f, 0.150f, 0.190f, 0.98f);
    public static readonly Color Stroke = new(0.30f, 0.37f, 0.45f, 0.64f);
    public static readonly Color Text = new(0.94f, 0.96f, 0.99f, 1f);
    public static readonly Color Muted = new(0.67f, 0.72f, 0.79f, 1f);
    public static readonly Color Blue = new(0.25f, 0.51f, 0.96f, 1f);
    public static readonly Color Green = new(0.41f, 0.80f, 0.46f, 1f);
    public static readonly Color Red = new(0.94f, 0.30f, 0.31f, 1f);

    static readonly Dictionary<int, Sprite> Sprites = new();
    static readonly Dictionary<string, Sprite> Icons = new();
    static GUIStyle _imGuiPanel, _imGuiButton, _imGuiDangerButton;

    public static GUIStyle ImGuiPanel => LiveGuiStyle(ref _imGuiPanel, Panel, Panel, Panel, false);
    public static GUIStyle ImGuiButton => LiveGuiStyle(ref _imGuiButton, Button,
        new Color(0.16f, 0.25f, 0.36f), new Color(0.10f, 0.29f, 0.48f), true);
    public static GUIStyle ImGuiDangerButton => LiveGuiStyle(ref _imGuiDangerButton,
        new Color(0.28f, 0.11f, 0.14f), new Color(0.39f, 0.14f, 0.17f),
        new Color(0.48f, 0.17f, 0.20f), true);

    // GUIStyle is managed and survives scene reloads even when its Unity textures do not.
    static bool HasGuiBackgrounds(GUIStyle style) => style != null
        && style.normal.background != null && style.hover.background != null && style.active.background != null;

    static GUIStyle LiveGuiStyle(ref GUIStyle style, Color normal, Color hover, Color pressed, bool button)
    {
        if (!HasGuiBackgrounds(style)) style = MakeGuiStyle(normal, hover, pressed, button);
        return style;
    }

    static GUIStyle MakeGuiStyle(Color normal, Color hover, Color pressed, bool button)
    {
        var style = new GUIStyle(button ? GUI.skin.button : GUI.skin.box);
        style.normal.background = GuiTexture(normal);
        style.hover.background = GuiTexture(hover);
        style.active.background = GuiTexture(pressed);
        style.normal.textColor = Text;
        style.hover.textColor = Text;
        style.active.textColor = Text;
        style.fontSize = button ? 14 : 15;
        style.fontStyle = button ? FontStyle.Normal : FontStyle.Bold;
        style.alignment = TextAnchor.MiddleCenter;
        style.border = new RectOffset(10, 10, 10, 10);
        return style;
    }

    static Texture2D GuiTexture(Color color)
    {
        var source = Rounded(10).texture;
        var pixels = source.GetPixels32();
        Color32 tint = color;
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32(tint.r, tint.g, tint.b,
                (byte)(pixels[i].a * tint.a / 255));
        var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        texture.name = "VTT IMGUI background";
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    public static void Surface(Image image, Color color, int radius = 12, bool outline = true)
    {
        if (image == null) return;
        image.sprite = Rounded(radius);
        image.type = Image.Type.Sliced;
        image.color = color;
        if (outline && image.GetComponent<Outline>() == null)
        {
            var border = image.gameObject.AddComponent<Outline>();
            border.effectColor = Stroke;
            border.effectDistance = new Vector2(1, -1);
        }
    }

    public static void ButtonStyle(Image image, Color color, int radius = 8)
    {
        Surface(image, color, radius, true);
        var button = image.GetComponent<Button>();
        if (button == null) return;
        var palette = button.colors;
        palette.normalColor = Color.white;
        palette.highlightedColor = new Color(1.16f, 1.16f, 1.16f, 1f);
        palette.pressedColor = new Color(0.76f, 0.79f, 0.85f, 1f);
        palette.selectedColor = Color.white;
        palette.disabledColor = new Color(0.55f, 0.58f, 0.63f, 0.7f);
        button.colors = palette;
    }

    public static Sprite Icon(string key)
    {
        if (Icons.TryGetValue(key, out var icon) && icon != null && icon.texture != null) return icon;
        icon = Resources.Load<Sprite>("UI/Icons/" + key);
        if (icon == null)
        {
            var texture = Resources.Load<Texture2D>("UI/Icons/" + key);
            if (texture != null)
                icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
        }
        if (icon != null && icon.texture.isReadable)
        {
            var pixels = icon.texture.GetPixels32();
            bool hasBlack = false;
            bool hasOtherColor = false;
            foreach (var pixel in pixels)
            {
                if (pixel.a < 16) continue;
                if (pixel.r < 32 && pixel.g < 32 && pixel.b < 32) hasBlack = true;
                else hasOtherColor = true;
            }
            if (hasBlack && !hasOtherColor)
            {
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i].r = 255;
                    pixels[i].g = 255;
                    pixels[i].b = 255;
                }
                var whiteTexture = new Texture2D(icon.texture.width, icon.texture.height,
                    TextureFormat.RGBA32, false);
                whiteTexture.hideFlags = HideFlags.HideAndDontSave;
                whiteTexture.SetPixels32(pixels);
                whiteTexture.Apply();
                icon = Sprite.Create(whiteTexture,
                    new Rect(0, 0, whiteTexture.width, whiteTexture.height),
                    new Vector2(0.5f, 0.5f), icon.pixelsPerUnit);
                icon.hideFlags = HideFlags.HideAndDontSave;
            }
        }
        Icons[key] = icon;
        return icon;
    }

    static Sprite Rounded(int radius)
    {
        radius = Mathf.Clamp(radius, 0, 30);
        if (Sprites.TryGetValue(radius, out var sprite) && sprite != null && sprite.texture != null) return sprite;
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = "VTT UI rounded " + radius;
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.filterMode = FilterMode.Bilinear;
        var pixels = new Color32[size * size];
        float r = Mathf.Max(0.5f, radius);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Max(Mathf.Abs(x - 31.5f) - (32f - r), 0);
            float dy = Mathf.Max(Mathf.Abs(y - 31.5f) - (32f - r), 0);
            byte alpha = (byte)Mathf.RoundToInt(255 * Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f));
            pixels[y * size + x] = new Color32(255, 255, 255, alpha);
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100,
            0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        sprite.hideFlags = HideFlags.HideAndDontSave;
        Sprites[radius] = sprite;
        return sprite;
    }
}
