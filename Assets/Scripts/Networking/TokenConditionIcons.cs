using System.Collections.Generic;
using UnityEngine;

/// <summary>Small built-in symbols; only callers' permitted condition IDs are drawn.</summary>
public static class TokenConditionIcons
{
    private static readonly Dictionary<string, Texture2D> Textures = new();

    public static bool Draw(Rect rect, string id)
    {
        if (!Textures.TryGetValue(id, out var texture) || texture == null)
        {
            texture = Create(id);
            Textures[id] = texture;
        }
        GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
        GUI.Label(rect, new GUIContent("", TokenConditionCatalog.DisplayName(id)), GUIStyle.none);
        return rect.Contains(Event.current.mousePosition);
    }

    private static Texture2D Create(string id)
    {
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Condition-" + id, filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color[size * size];
        int index = System.Array.IndexOf(TokenConditionCatalog.Ids, id);
        Color background = Color.HSVToRGB(Mathf.Repeat(index * 0.137f, 1f), 0.65f, 0.48f);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f)) <= 15.5f
                    ? background : Color.clear;
        void Dot(int x, int y)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (x + dx >= 0 && x + dx < size && y + dy >= 0 && y + dy < size)
                        pixels[(y + dy) * size + x + dx] = Color.white;
        }
        void Line(int x1, int y1, int x2, int y2)
        {
            int steps = Mathf.Max(Mathf.Abs(x2 - x1), Mathf.Abs(y2 - y1));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0 : (float)i / steps;
                Dot(Mathf.RoundToInt(Mathf.Lerp(x1, x2, t)), Mathf.RoundToInt(Mathf.Lerp(y1, y2, t)));
            }
        }
        void Circle(int cx, int cy, int radius)
        {
            for (int i = 0; i < 64; i++)
            {
                float angle = i * Mathf.PI / 32f;
                Dot(cx + Mathf.RoundToInt(Mathf.Cos(angle) * radius), cy + Mathf.RoundToInt(Mathf.Sin(angle) * radius));
            }
        }
        void Eye()
        {
            Line(5, 16, 15, 23); Line(15, 23, 26, 16);
            Line(26, 16, 15, 9); Line(15, 9, 5, 16); Circle(16, 16, 3);
        }
        switch (id)
        {
            case "blinded": Eye(); Line(7, 6, 25, 26); break;
            case "charmed":
                Line(16, 6, 5, 18); Line(5, 18, 8, 25); Line(8, 25, 16, 21);
                Line(16, 21, 24, 25); Line(24, 25, 27, 18); Line(27, 18, 16, 6); break;
            case "deafened": Circle(16, 19, 7); Line(21, 14, 17, 7); Line(17, 7, 11, 7); Line(5, 6, 26, 26); break;
            case "exhaustion":
                Line(6, 8, 6, 23); Line(6, 23, 24, 23); Line(24, 23, 24, 8); Line(24, 8, 6, 8);
                Line(27, 13, 27, 18); Line(10, 12, 10, 19); break;
            case "frightened": Line(16, 26, 16, 13); Circle(16, 7, 1); break;
            case "grappled": Circle(11, 12, 6); Circle(21, 21, 6); Line(11, 12, 21, 21); break;
            case "incapacitated": Circle(16, 16, 10); Line(9, 9, 23, 23); break;
            case "invisible": Eye(); Line(5, 6, 9, 6); Line(23, 26, 27, 26); break;
            case "paralyzed":
                Circle(16, 24, 3); Line(16, 20, 16, 6); Line(8, 16, 24, 16); Line(16, 11, 10, 5); Line(16, 11, 22, 5); break;
            case "petrified":
                Line(7, 7, 5, 18); Line(5, 18, 13, 26); Line(13, 26, 24, 22); Line(24, 22, 27, 7); Line(27, 7, 7, 7);
                Line(13, 24, 16, 17); Line(16, 17, 12, 11); break;
            case "poisoned": Circle(16, 20, 7); Circle(12, 21, 1); Circle(20, 21, 1); Line(12, 11, 20, 11); Line(8, 5, 24, 9); Line(8, 9, 24, 5); break;
            case "prone": Line(5, 6, 27, 6); Line(16, 26, 16, 12); Line(16, 12, 10, 18); Line(16, 12, 22, 18); break;
            case "restrained":
                for (int n = 8; n <= 24; n += 8) { Line(n, 7, n, 25); Line(7, n, 25, n); } break;
            case "stunned": Line(18, 27, 9, 15); Line(9, 15, 21, 15); Line(21, 15, 13, 5); break;
            case "unconscious": Line(7, 24, 25, 24); Line(25, 24, 7, 8); Line(7, 8, 25, 8); break;
            default: Circle(16, 16, 9); break;
        }
        texture.SetPixels(pixels); texture.Apply(false, true);
        return texture;
    }
}
