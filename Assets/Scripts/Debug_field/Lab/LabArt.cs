using System.Collections.Generic;
using TMPro;
using UnityEngine;

public static class LabArt
{
    public const float PixelsPerUnit = 16f;

    private static readonly Dictionary<string, Sprite> cache = new();

    public static Sprite Pixel(string key, string[] rows, string palette, Vector2 pivot)
    {
        if (cache.TryGetValue(key, out var cached) && cached != null) return cached;
        var colors = ParsePalette(palette);
        int h = rows.Length, w = 0;
        foreach (var r in rows) w = Mathf.Max(w, r.Length);
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = $"Lab_{key}", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp,
        };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            string row = rows[h - 1 - y];
            for (int x = 0; x < w; x++)
            {
                char c = x < row.Length ? row[x] : '.';
                px[y * w + x] = colors.TryGetValue(c, out var col) ? col : new Color32(0, 0, 0, 0);
            }
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return cache[key] = Sprite.Create(tex, new Rect(0, 0, w, h), pivot, PixelsPerUnit, 0, SpriteMeshType.FullRect);
    }

    private static Dictionary<char, Color32> ParsePalette(string palette)
    {
        var d = new Dictionary<char, Color32>();
        foreach (var token in palette.Split(' '))
        {
            if (token.Length < 8 || token[1] != '#') continue;
            int rgb = System.Convert.ToInt32(token.Substring(2, 6), 16);
            byte a = token.Length >= 10 ? System.Convert.ToByte(token.Substring(8, 2), 16) : (byte)255;
            d[token[0]] = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, a);
        }
        return d;
    }

    public static Sprite Disc() => Procedural("disc", 64, (x, y) => Mathf.Clamp01(0.5f - (new Vector2(x - 32f, y - 32f).magnitude - 30f)));

    public static Sprite Ring() => Procedural("ring", 128, (x, y) =>
    {
        float d = Mathf.Abs(new Vector2(x - 64f, y - 64f).magnitude - 60f) - 3.5f;
        return Mathf.Clamp01(0.5f - d);
    });

    public static Sprite ThinRing() => Procedural("thinRing", 512, (x, y) =>
    {
        float d = Mathf.Abs(new Vector2(x - 256f, y - 256f).magnitude - 250f) - 2f;
        return Mathf.Clamp01(0.5f - d);
    });

    public static Sprite Glow() => Procedural("glow", 64, (x, y) =>
    {
        float r = new Vector2(x - 32f, y - 32f).magnitude / 32f;
        float a = Mathf.Clamp01(1f - r);
        return a * a;
    });

    public static Sprite Beam()
    {
        if (cache.TryGetValue("beam", out var s) && s != null) return s;
        const int w = 16, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Lab_beam", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float edge = 1f - Mathf.Abs((x + 0.5f) / w * 2f - 1f);
            float a = Mathf.SmoothStep(0f, 1f, edge) * Mathf.Pow(1f - (y + 0.5f) / h, 1.6f);
            px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return cache["beam"] = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), 16f, 0, SpriteMeshType.FullRect);
    }

    public static Sprite RoundedWorld() => CuteUIKit.Rounded(8);

    private static Sprite Procedural(string key, int size, System.Func<float, float, float> alpha)
    {
        if (cache.TryGetValue(key, out var s) && s != null) return s;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = $"Lab_{key}", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f)) * 255f));
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return cache[key] = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size, 0, SpriteMeshType.FullRect);
    }

    public const int OrderGround = -2, OrderObject = 1, OrderAir = 100;

    private static Material spriteMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => spriteMaterial = null;

    // 빌드에서 기본 재질이면 깨짐
    public static Material SpriteMaterial
    {
        get
        {
            if (spriteMaterial != null) return spriteMaterial;
            var me = PlayerTileController.Instance;
            if (me != null && me.TryGetComponent<SpriteRenderer>(out var sr) && sr.sharedMaterial != null) spriteMaterial = sr.sharedMaterial;
            return spriteMaterial;
        }
    }

    public static SpriteRenderer Make(string name, Sprite sprite, Vector3 pos, int order, Color color, Transform parent = null)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite       = sprite;
        sr.color        = color;
        sr.sortingOrder = order;
        if (SpriteMaterial != null) sr.sharedMaterial = SpriteMaterial;
        return sr;
    }

    public static TextMeshPro Label(Transform parent, string text, float size, Color color, Vector3 localPos, int order = 210)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshPro>();
        t.text             = text;
        t.fontSize         = size;
        t.color            = color;
        t.alignment        = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.fontStyle        = FontStyles.Bold;
        t.rectTransform.sizeDelta = new Vector2(6f, 1f);
        t.transform.localPosition = localPos;
        var layers = SortingLayer.layers;
        t.sortingLayerID = layers.Length > 0 ? layers[layers.Length - 1].id : 0;
        t.sortingOrder   = order;
        CuteUIKit.Outline(t, 0.22f, new Color32(90, 64, 56, 255));
        return t;
    }
}
