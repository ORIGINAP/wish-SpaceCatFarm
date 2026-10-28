using System.Collections.Generic;
using UnityEngine;

public static class CatSkin
{
    private sealed class Fur
    {
        public string  name;
        public Color32 body, outline, mark;
        public Color32? eye, cheek;
    }

    private static readonly Fur[] Furs =
    {
        F("하양",   0xFFFFFF, 0x6B2E38),
        F("크림",   0xFFEFD6, 0xA0705A),
        F("치즈",   0xFFCB8E, 0xB25E34),
        F("딸기",   0xFFD0DD, 0xB84D6E, cheek: 0xF58AA6),
        F("민트",   0xC9F0DC, 0x3F8A66),
        F("하늘",   0xCDE5FF, 0x3F78AE),
        F("라벤더", 0xE2D7FF, 0x6E57B5),
        F("회색",   0xC9C8D3, 0x5B5768),
        F("까망",   0x4B4655, 0x221E29, mark: 0x3A3543, eye: 0xFFE07A),
    };

    private static Fur F(string name, int body, int outline, int mark = -1, int eye = -1, int cheek = -1) => new()
    {
        name    = name,
        body    = C(body),
        outline = C(outline),
        mark    = C(mark >= 0 ? mark : outline),
        eye     = eye   >= 0 ? C(eye)   : null,
        cheek   = cheek >= 0 ? C(cheek) : null,
    };

    private enum Anchor
    {
        HeadTop,
        AboveEars,
        LeftEar,
        RightEar,
        Eyes,
    }

    private sealed class Accessory
    {
        public string   name;
        public Anchor   anchor;
        public string[] rows;
        public Dictionary<char, Color32> palette;
    }

    private static readonly Accessory[] Accessories =
    {
        A("없음",     Anchor.HeadTop,   null),
        A("리본",     Anchor.RightEar,  new[] { "RR.RR", "RRKRR", "R...R" },          ('R', 0xF7879F), ('K', 0xC9485B)),
        A("꽃",       Anchor.LeftEar,   new[] { ".P.", "PYP", ".P." },                ('P', 0xFFD34D), ('Y', 0x9A6A3C)),
        A("새싹",     Anchor.HeadTop,   new[] { "GG.GG", ".GSG.", "..S.." },          ('G', 0x7CCB6B), ('S', 0x4E9A45)),
        A("왕관",     Anchor.HeadTop,   new[] { "Y.Y.Y", "YYYYY", "YRYBY" },          ('Y', 0xFFD34D), ('R', 0xF2607A), ('B', 0x6FB8F0)),
        A("고깔모자", Anchor.HeadTop,   new[] { "..W..", "..P..", ".PYP.", "PYPYP" }, ('W', 0xFFFFFF), ('P', 0xF7879F), ('Y', 0xFFD66B)),
        A("안경",     Anchor.Eyes,      null,                                         ('G', 0xE0A040)),
        A("천사 링",  Anchor.AboveEars, new[] { ".YYY.", "Y...Y", ".YYY." },          ('Y', 0xFFD34D)),
    };

    private static Accessory A(string name, Anchor anchor, string[] rows, params (char key, int rgb)[] colors)
    {
        var palette = new Dictionary<char, Color32>();
        foreach (var (key, rgb) in colors) palette[key] = C(rgb);
        return new Accessory { name = name, anchor = anchor, rows = rows, palette = palette };
    }

    private static Color32 C(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

    public static int ColorCount     => Furs.Length;
    public static int AccessoryCount => Accessories.Length;

    public static string ColorName(int color)         => Furs[ValidColor(color)].name;
    public static string AccessoryName(int accessory) => Accessories[ValidAccessory(accessory)].name;

    public static Color Swatch(int color)        => Furs[ValidColor(color)].body;
    public static Color SwatchOutline(int color) => Furs[ValidColor(color)].outline;

    public static int ValidColor(int color)         => (uint)color < (uint)Furs.Length ? color : 0;
    public static int ValidAccessory(int accessory) => (uint)accessory < (uint)Accessories.Length ? accessory : 0;

    private static readonly Dictionary<(Sprite, int, int), Sprite> cache = new();
    private static readonly Dictionary<Sprite, Sprite> originals = new();
    private static readonly Dictionary<Texture2D, Color32[]> pixels = new();

    public static Sprite Get(Sprite frame, int color, int accessory)
    {
        if (frame == null) return null;
        frame     = Original(frame);
        color     = ValidColor(color);
        accessory = ValidAccessory(accessory);
        if (color == 0 && accessory == 0) return frame;

        var key = (frame, color, accessory);
        if (cache.TryGetValue(key, out var skinned) && skinned != null) return skinned;

        skinned = Build(frame, color == 0 ? null : Furs[color], Accessories[accessory]) ?? frame;
        cache[key] = skinned;
        if (skinned != frame) originals[skinned] = frame;
        return skinned;
    }

    public static Sprite Original(Sprite sprite)
        => sprite != null && originals.TryGetValue(sprite, out var original) ? original : sprite;

    private const int Pad = 4;

    private enum Kind : byte { Empty, Body, Cheek, Ink, Line }

    private struct Shape
    {
        public int centerX;
        public int headTop;
        public int top;
        public int leftTip, rightTip;
        public List<Vector2Int> eyes;
    }

    private static Sprite Build(Sprite frame, Fur fur, Accessory acc)
    {
        // Tight 패킹은 칸을 몰라서 원본 그대로
        if (frame.packed && frame.packingMode == SpritePackingMode.Tight) return null;
        var src = frame.texture;
        var srcPx = src != null ? ReadPixels(src) : null;
        if (srcPx == null) return null;

        var r = frame.packed ? frame.textureRect : frame.rect;
        var rect = new RectInt(Mathf.RoundToInt(r.x), Mathf.RoundToInt(r.y), Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height));
        int sw = src.width, sh = src.height;
        int w = sw, h = sh + Pad;

        var px = new Color32[w * h];
        for (int y = 0; y < sh; y++) System.Array.Copy(srcPx, y * sw, px, y * w, sw);

        var kinds = Classify(srcPx, sw, rect);
        var shape = Analyze(kinds, rect);
        if (fur != null) Recolor(px, w, kinds, rect, shape, fur);

        int x0 = rect.xMin, y0 = rect.yMin, x1 = rect.xMax - 1, y1 = rect.yMax - 1;
        if (acc.anchor == Anchor.Eyes) DrawGlasses(px, w, h, shape, acc.palette['G'], ref x0, ref y0, ref x1, ref y1);
        else if (acc.rows != null) DrawPattern(px, w, h, shape, acc, ref x0, ref y0, ref x1, ref y1);

        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name       = $"{frame.name}_{fur?.name ?? "하양"}_{acc.name}",
            filterMode = FilterMode.Point,
            wrapMode   = TextureWrapMode.Clamp,
        };
        tex.SetPixels32(px);
        tex.Apply(false, true);

        var bounds = new Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
        var pivot  = frame.pivot + new Vector2(rect.xMin - x0, rect.yMin - y0);
        var sprite = Sprite.Create(tex, bounds, new Vector2(pivot.x / bounds.width, pivot.y / bounds.height),
                                   frame.pixelsPerUnit, 0, SpriteMeshType.FullRect);
        sprite.name = tex.name;
        return sprite;
    }

    private static Kind KindOf(Color32 c)
    {
        if (c.a < 8) return Kind.Empty;
        if (c.r > 235 && c.g > 235 && c.b > 235) return Kind.Body;
        if (c.r > 235 && c.g > 185 && c.b > 165) return Kind.Cheek;
        if (c.r + c.g + c.b < 60) return Kind.Ink;
        return Kind.Line;
    }

    private static Kind[] Classify(Color32[] srcPx, int sw, RectInt rect)
    {
        var kinds = new Kind[rect.width * rect.height];
        for (int y = 0; y < rect.height; y++)
        for (int x = 0; x < rect.width; x++)
            kinds[y * rect.width + x] = KindOf(srcPx[(rect.yMin + y) * sw + rect.xMin + x]);
        return kinds;
    }

    private static Kind KindAt(Kind[] kinds, RectInt rect, int x, int y)
    {
        x -= rect.xMin;
        y -= rect.yMin;
        return x < 0 || y < 0 || x >= rect.width || y >= rect.height ? Kind.Empty : kinds[y * rect.width + x];
    }

    private static bool IsEdge(Kind[] kinds, RectInt rect, int x, int y)
        => KindAt(kinds, rect, x + 1, y) == Kind.Empty || KindAt(kinds, rect, x - 1, y) == Kind.Empty
        || KindAt(kinds, rect, x, y + 1) == Kind.Empty || KindAt(kinds, rect, x, y - 1) == Kind.Empty;

    private static Shape Analyze(Kind[] kinds, RectInt rect)
    {
        var s = new Shape { centerX = rect.xMin + (rect.width - 1) / 2, headTop = rect.yMax - 1, eyes = new List<Vector2Int>() };
        int top = int.MinValue, bottom = int.MaxValue, headTop = int.MinValue;
        for (int y = rect.yMin; y < rect.yMax; y++)
        for (int x = rect.xMin; x < rect.xMax; x++)
        {
            if (KindAt(kinds, rect, x, y) == Kind.Empty) continue;
            top    = Mathf.Max(top, y);
            bottom = Mathf.Min(bottom, y);
            if (x == s.centerX) headTop = Mathf.Max(headTop, y);
        }
        if (top == int.MinValue) return s;
        s.top     = top;
        s.headTop = headTop != int.MinValue ? headTop : top;

        s.leftTip = int.MaxValue;
        s.rightTip = int.MinValue;
        for (int x = rect.xMin; x < rect.xMax; x++)
        {
            if (KindAt(kinds, rect, x, top) == Kind.Empty) continue;
            s.leftTip  = Mathf.Min(s.leftTip, x);
            s.rightTip = Mathf.Max(s.rightTip, x);
        }

        float mid = (top + bottom) * 0.5f;
        for (int y = rect.yMin; y < rect.yMax; y++)
        for (int x = rect.xMin; x < rect.xMax; x++)
        {
            var k = KindAt(kinds, rect, x, y);
            if ((k == Kind.Ink || k == Kind.Line) && y < mid && !IsEdge(kinds, rect, x, y)) s.eyes.Add(new Vector2Int(x, y));
        }
        return s;
    }

    private static void Recolor(Color32[] px, int w, Kind[] kinds, RectInt rect, Shape shape, Fur fur)
    {
        var eyes = new HashSet<Vector2Int>(shape.eyes);
        for (int y = rect.yMin; y < rect.yMax; y++)
        for (int x = rect.xMin; x < rect.xMax; x++)
        {
            int i = y * w + x;
            switch (KindAt(kinds, rect, x, y))
            {
                case Kind.Body:
                    px[i] = fur.body;
                    break;
                case Kind.Cheek:
                    if (fur.cheek.HasValue) px[i] = fur.cheek.Value;
                    break;
                case Kind.Ink:
                case Kind.Line:
                    if (eyes.Contains(new Vector2Int(x, y)))  { if (fur.eye.HasValue) px[i] = fur.eye.Value; }
                    else if (!IsEdge(kinds, rect, x, y))      px[i] = fur.mark;
                    else if (KindAt(kinds, rect, x, y) == Kind.Line) px[i] = fur.outline;
                    break;
            }
        }
    }

    private static void DrawPattern(Color32[] px, int w, int h, Shape s, Accessory acc, ref int x0, ref int y0, ref int x1, ref int y1)
    {
        int pw = acc.rows[0].Length, ph = acc.rows.Length;
        int left, top;
        switch (acc.anchor)
        {
            case Anchor.HeadTop:   left = s.centerX - pw / 2; top = s.headTop + ph; break;
            case Anchor.AboveEars: left = s.centerX - pw / 2; top = s.top + ph;     break;
            case Anchor.RightEar:  left = s.rightTip - 2;     top = s.top - 1;      break;
            default:               left = s.leftTip - 1;      top = s.top - 1;      break;
        }
        for (int row = 0; row < ph; row++)
        for (int col = 0; col < pw; col++)
        {
            char ch = acc.rows[row][col];
            if (ch != '.') Put(px, w, h, left + col, top - row, acc.palette[ch], ref x0, ref y0, ref x1, ref y1);
        }
    }

    private static void DrawGlasses(Color32[] px, int w, int h, Shape s, Color32 frameColor, ref int x0, ref int y0, ref int x1, ref int y1)
    {
        var columns = new SortedDictionary<int, Vector2Int>();
        foreach (var e in s.eyes)
            columns[e.x] = columns.TryGetValue(e.x, out var span) ? new Vector2Int(Mathf.Min(span.x, e.y), Mathf.Max(span.y, e.y)) : new Vector2Int(e.y, e.y);

        foreach (var kv in columns)
        {
            int x = kv.Key, lo = kv.Value.x, hi = kv.Value.y;
            for (int y = lo - 1; y <= hi + 1; y++)
            {
                Put(px, w, h, x - 1, y, frameColor, ref x0, ref y0, ref x1, ref y1);
                Put(px, w, h, x + 1, y, frameColor, ref x0, ref y0, ref x1, ref y1);
            }
            Put(px, w, h, x, lo - 1, frameColor, ref x0, ref y0, ref x1, ref y1);
            Put(px, w, h, x, hi + 1, frameColor, ref x0, ref y0, ref x1, ref y1);
        }

        var xs = new List<int>(columns.Keys);
        if (xs.Count < 2) return;
        int bridgeY = columns[xs[0]].y + 1;
        for (int x = xs[0] + 2; x <= xs[1] - 2; x++)
            Put(px, w, h, x, bridgeY, frameColor, ref x0, ref y0, ref x1, ref y1);
    }

    private static void Put(Color32[] px, int w, int h, int x, int y, Color32 c, ref int x0, ref int y0, ref int x1, ref int y1)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        px[y * w + x] = c;
        x0 = Mathf.Min(x0, x);
        y0 = Mathf.Min(y0, y);
        x1 = Mathf.Max(x1, x);
        y1 = Mathf.Max(y1, y);
    }

    // 읽기 불가 텍스처는 GPU 복사 후 읽음
    private static Color32[] ReadPixels(Texture2D tex)
    {
        if (pixels.TryGetValue(tex, out var cached)) return cached;
        Color32[] result;
        if (tex.isReadable) result = tex.GetPixels32();
        else
        {
            var rt   = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0, false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            result = copy.GetPixels32();
            Object.Destroy(copy);
        }
        return pixels[tex] = result;
    }
}
