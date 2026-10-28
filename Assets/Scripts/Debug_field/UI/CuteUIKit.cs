using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using TMPro;

// 공통 귀여운 UI 테마
public static class CuteUIKit
{
    public static readonly Color Cream      = Hex(0xFFF7EA);
    public static readonly Color Latte      = Hex(0xE9CBA7);
    public static readonly Color LatteLight = Hex(0xF6E7D4);
    public static readonly Color Cocoa      = Hex(0x7A5A4F);
    public static readonly Color CocoaSoft  = Hex(0xA88E82);
    public static readonly Color CocoaMuted = Hex(0xC4B0A5);
    public static readonly Color Strawberry = Hex(0xF7879F);
    public static readonly Color PinkSoft   = Hex(0xFFD6E0);
    public static readonly Color Butter     = Hex(0xFFD66B);
    public static readonly Color ButterSoft = Hex(0xFFF0C2);
    public static readonly Color Shadow     = new(0.36f, 0.22f, 0.16f, 0.30f);
    public static readonly Color Overlay    = new(0.24f, 0.15f, 0.11f, 0.45f);

    public static readonly Color SlotBorder   = Hex(0xEAD3B7), SlotFill   = Hex(0xFDF3E6);
    public static readonly Color HotbarBorder = Hex(0xAFD5F4), HotbarFill = Hex(0xEEF7FF);
    public static readonly Color EquipBorder  = Hex(0xCFC0F4), EquipFill  = Hex(0xF6F1FF);
    public static readonly Color EmptiedFill  = Hex(0xEFE3D3);

    private static readonly Color[] TierBorders = { default, default, Hex(0x6FC495), Hex(0xA487EA), Hex(0xE8B23A) };
    private static readonly Color[] TierFills   = { default, default, Hex(0xD7F3E3), Hex(0xE9DFFF), Hex(0xFFEBAE) };
    private static readonly Color[] TierTexts   = { default, default, Hex(0x3F8A66), Hex(0x6E57B5), Hex(0xA8741A) };

    public static Color TierBorder(int tier, Color normal) => tier > 1 && tier < TierBorders.Length ? TierBorders[tier] : normal;

    public static Color TierFill(int tier, Color normal) => tier > 1 && tier < TierFills.Length ? TierFills[tier] : normal;

    public static Color TierBorder(FarmingItemData item, Color normal) => TierBorder(item != null ? item.Tier : 1, normal);
    public static Color TierFill(FarmingItemData item, Color normal)   => TierFill(item != null ? item.Tier : 1, normal);

    public static Color TierText(int tier) => tier > 1 && tier < TierTexts.Length ? TierTexts[tier] : Cocoa;

    public static readonly Color MintTag     = Hex(0xD2F2E1), MintText     = Hex(0x3F8A66);
    public static readonly Color SkyTag      = Hex(0xD3EBFF), SkyText      = Hex(0x3F78AE);
    public static readonly Color LavenderTag = Hex(0xE6DCFF), LavenderText = Hex(0x6E57B5);

    public static readonly Color ButtonMint   = Hex(0xBDEBD3);
    public static readonly Color ButtonCoral  = Hex(0xFFD0D6);
    public static readonly Color ButtonLatte  = Hex(0xF1E2CF);
    public static readonly Color ButtonSky    = Hex(0xCBE6FF);
    public static readonly Color ButtonButter = Hex(0xFFE7A3);

    public static readonly Color CoralText = Hex(0xC9485B);
    public static readonly Color AmberText = Hex(0xA8741A);

    public static Color Hex(int rgb, float alpha = 1f)
        => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    public static Color TextOn(Color fill)
    {
        float luminance = 0.299f * fill.r + 0.587f * fill.g + 0.114f * fill.b;
        if (luminance < 0.72f) return Color.white;
        Color.RGBToHSV(fill, out float h, out float s, out _);
        bool warm = h >= 0.03f && h <= 0.18f;
        return s < 0.08f || warm ? Cocoa : Color.HSVToRGB(h, Mathf.Clamp01(s * 1.8f + 0.25f), 0.45f);
    }

    public static Color Darker(Color c, float k = 0.85f) => new(c.r * k, c.g * k, c.b * k, c.a);
    public static Color Lighter(Color c, float t = 0.3f) => Color.Lerp(c, Color.white, t);

    private static readonly Dictionary<int, Sprite>  roundedCache = new();
    private static readonly Dictionary<long, Sprite> shadowCache  = new();
    private static Sprite ear, paw, dots, coin, trash;

    // 도메인 리로드 없이 재시작 대비
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ear = paw = dots = coin = trash = null;

    public static Sprite Rounded(int radius)
    {
        radius = Mathf.Clamp(radius, 1, 64);
        if (roundedCache.TryGetValue(radius, out var cached) && cached != null) return cached;

        int   size = (radius + 3) * 2;
        float half = size * 0.5f, box = half - 1f;
        var tex = NewTexture(size, size, $"Cute_Rounded_{radius}");
        Paint(tex, (x, y) => Coverage(RoundedBox(x - half, y - half, box, box, radius)));
        return roundedCache[radius] = SlicedSprite(tex, radius + 2);
    }

    public static Sprite SoftShadow(int radius, int blur)
    {
        long key = ((long)radius << 16) | (uint)blur;
        if (shadowCache.TryGetValue(key, out var cached) && cached != null) return cached;

        int   pad  = radius + blur + 3;
        float half = pad, box = pad - blur - 1f;
        var tex = NewTexture(pad * 2, pad * 2, $"Cute_Shadow_{radius}_{blur}");
        Paint(tex, (x, y) =>
        {
            float d = RoundedBox(x - half, y - half, box, box, radius);
            return 1f - Mathf.SmoothStep(0f, 1f, (d + blur * 0.35f) / (blur * 1.35f));
        });
        return shadowCache[key] = SlicedSprite(tex, radius + blur + 2);
    }

    public static Sprite Ear()
    {
        if (ear != null) return ear;
        const int size = 64;
        var a = new Vector2(10f, 8f);
        var b = new Vector2(54f, 8f);
        var c = new Vector2(29f, 52f);
        var tex = NewTexture(size, size, "Cute_Ear");
        Paint(tex, (x, y) => Coverage(Triangle(new Vector2(x, y), a, b, c) - 6f));
        return ear = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.05f), 100f, 0, SpriteMeshType.FullRect);
    }

    public static Sprite Paw()
    {
        if (paw != null) return paw;
        const int size = 64;
        var tex = NewTexture(size, size, "Cute_Paw");
        Paint(tex, (x, y) =>
        {
            var p = new Vector2(x, y);
            float d = Ellipse(p - new Vector2(32f, 22f), new Vector2(15f, 12f));
            d = Mathf.Min(d, Vector2.Distance(p, new Vector2(14f, 38f)) - 6.5f);
            d = Mathf.Min(d, Vector2.Distance(p, new Vector2(25f, 50f)) - 7f);
            d = Mathf.Min(d, Vector2.Distance(p, new Vector2(39f, 50f)) - 7f);
            d = Mathf.Min(d, Vector2.Distance(p, new Vector2(50f, 38f)) - 6.5f);
            return Coverage(d);
        });
        return paw = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    public static Sprite Trash()
    {
        if (trash != null) return trash;
        const int size = 64;
        var tex = NewTexture(size, size, "Cute_Trash");
        Paint(tex, (x, y) =>
        {
            float d = RoundedBox(x - 32f, y - 46f, 23f, 3.6f, 3.6f);
            d = Mathf.Min(d, RoundedBox(x - 32f, y - 52.5f, 6.5f, 3.2f, 2.6f));
            const float bottom = 7.5f, top = 39.5f;
            float hw   = Mathf.Lerp(14.5f, 18f, Mathf.Clamp01((y - bottom) / (top - bottom)));
            float body = RoundedBox(x - 32f, y - (bottom + top) * 0.5f, hw, (top - bottom) * 0.5f, 4.5f);
            foreach (float gx in new[] { -8.5f, 0f, 8.5f })
            {
                var groove = new Vector2(x - (32f + gx * hw / 18f), y - Mathf.Clamp(y, 14f, 33f));
                body = Mathf.Max(body, 1.9f - groove.magnitude);
            }
            return Coverage(Mathf.Min(d, body));
        });
        return trash = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    public static Sprite Dots()
    {
        if (dots != null) return dots;
        var tex = NewTexture(10, 6, "Cute_Dots");
        tex.wrapMode = TextureWrapMode.Repeat;
        Paint(tex, (x, y) => Coverage(Vector2.Distance(new Vector2(x, y), new Vector2(5f, 3f)) - 2f));
        return dots = Sprite.Create(tex, new Rect(0, 0, 10, 6), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    public static Sprite Coin()
    {
        if (coin != null) return coin;
        const int size = 48;
        var tex = NewTexture(size, size, "Cute_Coin");
        var edge  = (Color32)Hex(0xE3A33B);
        var face  = (Color32)Butter;
        var inner = (Color32)Hex(0xFFE59A);
        var px = new Color32[size * size];
        var center = new Vector2(24f, 24f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            float r = Vector2.Distance(p, center);
            Color32 col = r > 19.5f ? edge : r > 14f ? face : inner;
            if (Vector2.Distance(p, new Vector2(16f, 32f)) < 4.2f) col = new Color32(255, 255, 255, 255);
            col.a = (byte)(Coverage(r - 22.5f) * 255f);
            px[y * size + x] = col;
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return coin = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    public static Image SetRounded(Image img, int radius, Color color)
    {
        img.sprite = Rounded(radius);
        img.type   = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 1f;
        img.color  = color;
        return img;
    }

    public static Image Layer(Transform parent, string name, Sprite sprite, Color color, float inset = 0f)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.AddComponent<LayoutElement>().ignoreLayout = true;

        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);

        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.type   = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
        img.color  = color;
        img.raycastTarget = false;
        return img;
    }

    public static TextMeshProUGUI Text(Transform parent, string text, float size, Color color,
                                       FontStyles style = FontStyles.Normal,
                                       TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text             = text;
        t.fontSize         = size;
        t.color            = color;
        t.fontStyle        = style;
        t.alignment        = align;
        t.raycastTarget    = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    private static readonly Dictionary<(Material, float, int), Material> outlineMats = new();

    // TMP 외곽선 머티리얼 누수 방지
    public static void Outline(TMP_Text t, float width, Color32 color)
    {
        var src = t != null ? t.fontSharedMaterial : null;
        if (src == null) return;
        var key = (src, width, color.r << 24 | color.g << 16 | color.b << 8 | color.a);
        if (!outlineMats.TryGetValue(key, out var mat) || mat == null)
        {
            ShaderUtilities.GetShaderPropertyIDs();
            mat = new Material(src) { name = src.name + " (Outline)" };
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, Mathf.Clamp01(width));
            mat.SetColor(ShaderUtilities.ID_OutlineColor, color);
            outlineMats[key] = mat;
        }
        t.fontSharedMaterial = mat;
    }

    public static RectTransform Pill(Transform parent, string text, Color fill, Color textColor, float fontSize,
                                     int radius, RectOffset padding, out TextMeshProUGUI label, bool fitSelf = false)
    {
        var go = new GameObject("Pill", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        SetRounded(go.AddComponent<Image>(), radius, fill).raycastTarget = false;

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = padding;
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        if (fitSelf)
        {
            var csf = go.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        }

        label = Text(go.transform, text, fontSize, textColor, FontStyles.Bold);
        return (RectTransform)go.transform;
    }

    public static Image Card(Transform parent, Color fill, int radius = 12, string name = "Card")
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return SetRounded(go.AddComponent<Image>(), radius, fill);
    }

    public const int   WindowRadius = 22;
    public const int   WindowBorder = 4;
    private const int  ShadowBlur   = 10;
    private const float EarSize     = 50f;
    private const float EarTilt     = 12f;

    public static void DecorateWindow(RectTransform window, bool catEars = true, float earInset = 72f,
                                      int radius = WindowRadius, int border = WindowBorder)
    {
        foreach (var fx in window.GetComponents<BaseMeshEffect>()) UnityEngine.Object.DestroyImmediate(fx);
        var hitArea = window.GetComponent<Image>();
        if (hitArea != null)
        {
            hitArea.sprite = null;
            hitArea.color  = Color.clear;
        }

        int order = 0;
        var shadow = Layer(window, "Cute_Shadow", SoftShadow(radius, ShadowBlur), Shadow, -(ShadowBlur + 1));
        shadow.rectTransform.anchoredPosition = new Vector2(0f, -6f);
        shadow.transform.SetSiblingIndex(order++);
        if (catEars)
        {
            BuildEar(window, left: true,  earInset).SetSiblingIndex(order++);
            BuildEar(window, left: false, earInset).SetSiblingIndex(order++);
        }
        Layer(window, "Cute_Border", Rounded(radius), Latte).transform.SetSiblingIndex(order++);
        Layer(window, "Cute_Fill", Rounded(Mathf.Max(1, radius - border)), Cream, border).transform.SetSiblingIndex(order);
    }

    private static Transform BuildEar(RectTransform window, bool left, float inset)
    {
        var ear = Layer(window, left ? "Cute_EarL" : "Cute_EarR", Ear(), Latte).rectTransform;
        ear.anchorMin = ear.anchorMax = new Vector2(left ? 0f : 1f, 1f);
        ear.pivot            = new Vector2(0.5f, 0.05f);
        ear.sizeDelta        = new Vector2(EarSize, EarSize);
        ear.anchoredPosition = new Vector2(left ? inset : -inset, -4f);
        ear.localRotation    = Quaternion.Euler(0f, 0f, left ? EarTilt : -EarTilt);
        if (!left) ear.localScale = new Vector3(-1f, 1f, 1f);

        var inner = Layer(ear, "Inner", Ear(), PinkSoft).rectTransform;
        inner.anchorMin = inner.anchorMax = inner.pivot = new Vector2(0.5f, 0.05f);
        inner.sizeDelta        = new Vector2(EarSize * 0.55f, EarSize * 0.55f);
        inner.anchoredPosition = new Vector2(-1f, EarSize * 0.16f);
        return ear;
    }

    public static void SparkleBurst(MonoBehaviour host, RectTransform at, Color color, int count = 12, float radius = 80f)
    {
        if (host == null || at == null || !host.isActiveAndEnabled) return;
        var canvas = at.GetComponentInParent<Canvas>();
        if (canvas == null) return;
        canvas = canvas.rootCanvas;

        for (int i = 0; i < count; i++)
        {
            var star = Text(canvas.transform, "+", UnityEngine.Random.Range(11f, 19f), color, FontStyles.Bold);
            star.raycastTarget = false;
            var rt = star.rectTransform;
            rt.sizeDelta = new Vector2(24f, 24f);
            rt.position  = at.position;
            float angle = (i + UnityEngine.Random.Range(-0.3f, 0.3f)) / count * Mathf.PI * 2f;
            var offset  = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * UnityEngine.Random.Range(0.55f, 1.15f);
            host.StartCoroutine(FlyStar(star, offset, UnityEngine.Random.Range(0.55f, 0.85f)));
        }
    }

    private static IEnumerator FlyStar(TextMeshProUGUI star, Vector2 offset, float duration)
    {
        var rt    = star.rectTransform;
        var start = rt.localPosition;
        var end   = start + (Vector3)offset;
        var color = star.color;
        for (float e = 0f; e < duration; e += Time.unscaledDeltaTime)
        {
            if (star == null) yield break;
            float t = e / duration;
            rt.localPosition = Vector3.Lerp(start, end, 1f - (1f - t) * (1f - t));
            rt.localScale    = Vector3.one * (0.6f + 0.7f * Mathf.Sin(t * Mathf.PI));
            rt.localRotation = Quaternion.Euler(0f, 0f, t * 90f);
            star.color       = new Color(color.r, color.g, color.b, 1f - t * t);
            yield return null;
        }
        if (star != null) UnityEngine.Object.Destroy(star.gameObject);
    }

    public static void WiggleEars(MonoBehaviour host, Transform window)
    {
        if (host == null || !host.isActiveAndEnabled) return;
        var l = window.Find("Cute_EarL");
        var r = window.Find("Cute_EarR");
        if (l != null) host.StartCoroutine(Wiggle(l, 14f, 0.5f, 1.5f, EarTilt));
        if (r != null) host.StartCoroutine(Wiggle(r, -12f, 0.55f, 1.5f, -EarTilt));
    }

    public static IEnumerator PopIn(RectTransform window, CanvasGroup group = null, float duration = 0.32f)
    {
        for (float e = 0f; e < duration; e += Time.unscaledDeltaTime)
        {
            float t = e / duration;
            float k = EaseOutBack(t, 2.2f);
            float s = Mathf.LerpUnclamped(0.7f, 1f, k) * FitScale(window);
            window.localScale    = new Vector3(s, s, 1f);
            window.localRotation = Quaternion.Euler(0f, 0f, Mathf.LerpUnclamped(4f, 0f, k));
            if (group != null) group.alpha = Mathf.Clamp01(t * 3f);
            yield return null;
        }
        float fit = FitScale(window);
        window.localScale    = new Vector3(fit, fit, 1f);
        window.localRotation = Quaternion.identity;
        if (group != null) group.alpha = 1f;
    }

    // 숨김 상태는 alpha 0
    public static IEnumerator PopOut(RectTransform window, CanvasGroup group = null, float duration = 0.16f, bool hideAtEnd = true)
    {
        float fit = FitScale(window);
        for (float e = 0f; e < duration; e += Time.unscaledDeltaTime)
        {
            float k = (e / duration) * (e / duration);
            float s = Mathf.Lerp(1f, 0.82f, k) * fit;
            window.localScale = new Vector3(s, s, 1f);
            if (group != null) group.alpha = 1f - k;
            yield return null;
        }
        window.localScale    = hideAtEnd ? Vector3.zero : new Vector3(fit, fit, 1f);
        window.localRotation = Quaternion.identity;
        if (group != null) group.alpha = 0f;
    }

    public static float FitScale(RectTransform window)
    {
        var canvas = window.GetComponentInParent<Canvas>();
        if (canvas == null) return 1f;
        const float margin = 8f;
        Vector2 room = ((RectTransform)canvas.rootCanvas.transform).rect.size - new Vector2(margin * 2f, margin * 2f);
        Vector2 size = window.rect.size;
        float fit = 1f;
        if (size.x > 0f && Mathf.Approximately(window.anchorMin.x, window.anchorMax.x)) fit = Mathf.Min(fit, room.x / size.x);
        if (size.y > 0f && Mathf.Approximately(window.anchorMin.y, window.anchorMax.y)) fit = Mathf.Min(fit, room.y / size.y);
        return fit;
    }

    public static RectTransform TitleRibbon(Transform parent, string title, string keyHint = null, float fontSize = 16f)
    {
        if (MobileMode.Active) keyHint = null;
        var go = new GameObject("TitleRibbon", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        SetRounded(go.AddComponent<Image>(), 15, Strawberry).raycastTarget = false;

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(14, keyHint != null ? 10 : 16, 5, 5);
        hlg.spacing                = 6f;
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        var paw = new GameObject("Paw", typeof(RectTransform));
        paw.transform.SetParent(go.transform, false);
        var pawImg = paw.AddComponent<Image>();
        pawImg.sprite        = Paw();
        pawImg.raycastTarget = false;
        var pawLE = paw.AddComponent<LayoutElement>();
        pawLE.preferredWidth = pawLE.preferredHeight = fontSize - 1f;

        Text(go.transform, title, fontSize, Color.white, FontStyles.Bold);
        if (!string.IsNullOrEmpty(keyHint))
            Pill(go.transform, keyHint, Hex(0xFFFFFF, 0.9f), Strawberry, 9f, 7, new RectOffset(5, 5, 1, 1), out _);
        return (RectTransform)go.transform;
    }

    public static RectTransform SectionHeader(Transform parent, string label, Color tagColor, Color textColor,
                                              string note = null, float width = -1f)
    {
        var go = new GameObject($"Header_{label}", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = 6f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        if (width > 0f) go.AddComponent<LayoutElement>().preferredWidth = width;

        Pill(go.transform, label, tagColor, textColor, 10f, 9, new RectOffset(9, 9, 2, 2), out _);
        if (!string.IsNullOrEmpty(note))
            Text(go.transform, note, 9f, CocoaSoft, align: TextAlignmentOptions.MidlineLeft);
        return (RectTransform)go.transform;
    }

    public static Image DottedDivider(Transform parent, float width = -1f)
    {
        var go = new GameObject("Divider", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite        = Dots();
        img.type          = Image.Type.Tiled;
        img.color         = Latte;
        img.raycastTarget = false;

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 6f;
        if (width > 0f) le.preferredWidth = width; else le.flexibleWidth = 1f;
        return img;
    }

    public static Button Button(Transform parent, string label, Color fill, UnityAction onClick,
                                float width = -1f, float height = 30f, float fontSize = 13f)
    {
        var go = new GameObject($"Btn_{label}", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        int radius = Mathf.Clamp(Mathf.RoundToInt(height * 0.5f), 4, 32);

        go.AddComponent<Image>().color = Color.clear;

        var lip = Layer(go.transform, "Lip", Rounded(radius), Darker(fill, 0.8f));
        lip.rectTransform.offsetMin = new Vector2(0f, -3f);
        lip.rectTransform.offsetMax = new Vector2(0f, -3f);
        var face = Layer(go.transform, "Face", Rounded(radius), Color.white);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = face;
        ApplyButtonColors(btn, fill);
        if (onClick != null) btn.onClick.AddListener(onClick);
        go.AddComponent<UiButtonSfx>().hover = true;
        go.AddComponent<CutePop>();

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight       = height;
        if (width > 0f) le.preferredWidth = width; else le.flexibleWidth = 1f;

        var txt = Text(go.transform, label, fontSize, TextOn(fill), FontStyles.Bold);
        txt.rectTransform.anchorMin = Vector2.zero;
        txt.rectTransform.anchorMax = Vector2.one;
        txt.rectTransform.offsetMin = new Vector2(6f, 0f);
        txt.rectTransform.offsetMax = new Vector2(-6f, 0f);
        return btn;
    }

    public static void ApplyButtonColors(Selectable selectable, Color fill)
    {
        if (selectable.targetGraphic != null) selectable.targetGraphic.color = Color.white;
        var colors = selectable.colors;
        colors.normalColor      = fill;
        colors.highlightedColor = Lighter(fill, 0.35f);
        colors.selectedColor    = Lighter(fill, 0.35f);
        colors.pressedColor     = Darker(fill, 0.9f);
        colors.disabledColor    = Color.Lerp(fill, Hex(0xE8E0D8), 0.65f);
        colors.colorMultiplier  = 1f;
        colors.fadeDuration     = 0.08f;
        selectable.colors = colors;

        var t = selectable.transform;
        if (t.Find("Face") == null) return;
        var lip = t.Find("Lip");
        if (lip != null && lip.TryGetComponent<Image>(out var lipImg)) lipImg.color = Darker(fill, 0.8f);
        var label = t.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null) label.color = TextOn(fill);
    }

    public static Button CloseButton(Transform parent, UnityAction onClick, float size = 28f)
    {
        var btn = Button(parent, "×", ButtonCoral, onClick, size, size, size * 0.62f);
        btn.name = "Btn_Close";
        return btn;
    }

    public static GameObject Slot(Transform parent, float size, Color border, Color fill, int radius = 12)
    {
        var go = new GameObject("Slot", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        ((RectTransform)go.transform).sizeDelta = new Vector2(size, size);
        SetRounded(go.AddComponent<Image>(), radius, border);

        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth  = size;
        le.preferredHeight = size;
        le.minWidth        = size;
        le.minHeight       = size;

        Layer(go.transform, "Fill", Rounded(Mathf.Max(1, radius - 2)), fill, 2.5f);

        var gloss = Layer(go.transform, "Gloss", Rounded(4), new Color(1f, 1f, 1f, 0.6f)).rectTransform;
        gloss.anchorMin = gloss.anchorMax = gloss.pivot = new Vector2(0f, 1f);
        gloss.sizeDelta        = new Vector2(size * 0.32f, Mathf.Clamp(size * 0.11f, 4f, 8f));
        gloss.anchoredPosition = new Vector2(Mathf.Clamp(size * 0.11f, 4f, 8f), -Mathf.Clamp(size * 0.1f, 4f, 7f));
        return go;
    }

    public static RectTransform Bar(Transform parent, float height, Color track, Color fill, float width = -1f)
    {
        var go = new GameObject("Bar", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        int radius = Mathf.Max(2, Mathf.RoundToInt(height * 0.5f));
        SetRounded(go.AddComponent<Image>(), radius, track).raycastTarget = false;

        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.minHeight       = height;
        if (width > 0f) le.preferredWidth = width; else le.flexibleWidth = 1f;

        var area = new GameObject("FillArea", typeof(RectTransform));
        area.transform.SetParent(go.transform, false);
        area.AddComponent<LayoutElement>().ignoreLayout = true;
        var areaRT = (RectTransform)area.transform;
        areaRT.anchorMin = Vector2.zero;
        areaRT.anchorMax = Vector2.one;
        areaRT.offsetMin = new Vector2(2f, 2f);
        areaRT.offsetMax = new Vector2(-2f, -2f);

        var fillImg = Layer(areaRT, "Fill", Rounded(Mathf.Max(1, radius - 2)), fill);
        var rt = fillImg.rectTransform;
        rt.anchorMax = new Vector2(0f, 1f);
        return rt;
    }

    public static void SetBar(RectTransform fill, float value)
    {
        value = Mathf.Clamp01(value);
        fill.anchorMax = new Vector2(value, 1f);
        fill.gameObject.SetActive(value > 0.001f);
    }

    public static void StyleScrollbar(Scrollbar scrollbar, float thickness = 8f)
    {
        if (scrollbar == null) return;
        int radius = Mathf.Max(2, Mathf.RoundToInt(thickness * 0.5f));
        if (scrollbar.TryGetComponent<Image>(out var track)) SetRounded(track, radius, LatteLight);
        if (scrollbar.handleRect != null && scrollbar.handleRect.TryGetComponent<Image>(out var handle))
        {
            SetRounded(handle, radius, Latte);
            scrollbar.targetGraphic = handle;
            ApplyButtonColors(scrollbar, Latte);
        }
    }

    public static void StyleInputField(TMP_InputField input, Color? background = null)
    {
        if (input == null) return;
        if (input.TryGetComponent<Image>(out var bg)) SetRounded(bg, 12, background ?? Color.white);
        if (input.textComponent != null) input.textComponent.color = Cocoa;
        if (input.placeholder is TMP_Text ph) ph.color = CocoaMuted;
        input.caretColor     = Strawberry;
        input.selectionColor = new Color(Strawberry.r, Strawberry.g, Strawberry.b, 0.35f);
    }

    public static float EaseOutBack(float t, float overshoot = 1.7f)
    {
        t = Mathf.Clamp01(t) - 1f;
        return 1f + t * t * ((overshoot + 1f) * t + overshoot);
    }

    public static IEnumerator ScaleTo(Transform t, float to, float duration, float overshoot = 2.2f)
    {
        if (t == null) yield break;
        float from = t.localScale.x;
        for (float e = 0f; e < duration; e += Time.unscaledDeltaTime)
        {
            if (t == null) yield break;
            t.localScale = Vector3.one * Mathf.LerpUnclamped(from, to, EaseOutBack(e / duration, overshoot));
            yield return null;
        }
        if (t != null) t.localScale = Vector3.one * to;
    }

    public static IEnumerator Wiggle(Transform t, float amplitude, float duration, float cycles = 1.5f, float baseAngle = 0f)
    {
        for (float e = 0f; e < duration; e += Time.unscaledDeltaTime)
        {
            if (t == null) yield break;
            float k = e / duration;
            float angle = baseAngle + amplitude * Mathf.Sin(k * Mathf.PI * 2f * cycles) * (1f - k);
            t.localRotation = Quaternion.Euler(0f, 0f, angle);
            yield return null;
        }
        if (t != null) t.localRotation = Quaternion.Euler(0f, 0f, baseAngle);
    }

    private static Texture2D NewTexture(int w, int h, string name) => new(w, h, TextureFormat.RGBA32, false)
    {
        name       = name,
        wrapMode   = TextureWrapMode.Clamp,
        filterMode = FilterMode.Bilinear,
    };

    private static void Paint(Texture2D tex, Func<float, float, float> alpha)
    {
        int w = tex.width, h = tex.height;
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f)) * 255f));
        tex.SetPixels32(px);
        tex.Apply(false, true);
    }

    private static Sprite SlicedSprite(Texture2D tex, int border)
        => Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0,
                         SpriteMeshType.FullRect, new Vector4(border, border, border, border));

    private static float Coverage(float distance) => Mathf.Clamp01(0.5f - distance);

    private static float RoundedBox(float px, float py, float hx, float hy, float r)
    {
        float qx = Mathf.Abs(px) - (hx - r), qy = Mathf.Abs(py) - (hy - r);
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    private static float Ellipse(Vector2 p, Vector2 r)
        => (new Vector2(p.x / r.x, p.y / r.y).magnitude - 1f) * Mathf.Min(r.x, r.y);

    private static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 e0 = b - a, e1 = c - b, e2 = a - c;
        Vector2 v0 = p - a, v1 = p - b, v2 = p - c;
        Vector2 pq0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
        Vector2 pq1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
        Vector2 pq2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
        float s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
        float d0 = pq0.sqrMagnitude, d1 = pq1.sqrMagnitude, d2 = pq2.sqrMagnitude;
        float s0 = s * (v0.x * e0.y - v0.y * e0.x);
        float s1 = s * (v1.x * e1.y - v1.y * e1.x);
        float s2 = s * (v2.x * e2.y - v2.y * e2.x);
        float dist = Mathf.Min(d0, Mathf.Min(d1, d2));
        float side = Mathf.Min(s0, Mathf.Min(s1, s2));
        return -Mathf.Sqrt(dist) * Mathf.Sign(side);
    }
}
