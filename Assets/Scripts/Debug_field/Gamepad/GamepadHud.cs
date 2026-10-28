using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum PadGlyph { A, B, X, Y, LB, RB, LT, RT, Start, Back, L3, R3, Up, Down, Left, Right, DPad, LStick, RStick }

public readonly struct PadHint
{
    public readonly PadGlyph glyph;
    public readonly PadGlyph? glyph2;
    public readonly string   label;
    public PadHint(PadGlyph glyph, string label)                  { this.glyph = glyph; glyph2 = null;   this.label = label; }
    public PadHint(PadGlyph glyph, PadGlyph glyph2, string label) { this.glyph = glyph; this.glyph2 = glyph2; this.label = label; }
}

public static class GamepadHud
{
    private static readonly Color ColA     = CuteUIKit.Hex(0x5DB866);
    private static readonly Color ColB     = CuteUIKit.Hex(0xE8605F);
    private static readonly Color ColX     = CuteUIKit.Hex(0x4F8FE8);
    private static readonly Color ColY     = CuteUIKit.Hex(0xF2BE45);
    private static readonly Color ColPill  = CuteUIKit.Cocoa;
    private static readonly Color ColStick = CuteUIKit.Hex(0x8C8190);

    private const float HintFont  = 14f;
    private const float GlyphSize = 24f;

    private static Canvas        canvas;
    private static RectTransform canvasRT;

    private static RectTransform hintBar;
    private static readonly List<PadHint> hints = new();
    private static int  shownHintKey;
    private static bool hintBuilt;

    private static RectTransform promptRT;
    private static TextMeshProUGUI promptLabel;
    private static string shownPrompt = "";
    private static bool promptWanted;
    private static Vector2 promptScreen;

    private static RectTransform frameRT;
    private static Image frameImage, pawImage;
    private static bool frameWanted, frameCarry;
    private static Rect frameTarget;
    private static Rect frameShown;
    private static bool frameVisible;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        canvas = null;
        canvasRT = null;
        hintBar = null;
        hints.Clear();
        shownHintKey = 0;
        hintBuilt = false;
        promptRT = null;
        promptLabel = null;
        shownPrompt = "";
        promptWanted = false;
        promptScreen = default;
        frameRT = null;
        frameImage = pawImage = null;
        frameWanted = frameCarry = false;
        frameTarget = frameShown = default;
        frameVisible = false;
        ring = null;
    }

    public static List<PadHint> Hints => hints;

    public static void ShowPrompt(Vector2 screenPos, string label)
    {
        promptWanted = true;
        promptScreen = screenPos;
        if (label != shownPrompt && promptLabel != null)
        {
            shownPrompt = label;
            promptLabel.text = label;
        }
    }

    public static void ShowSelection(Rect screenRect, bool carry)
    {
        frameWanted = true;
        frameTarget = screenRect;
        frameCarry  = carry;
    }

    internal static void BeginFrame()
    {
        hints.Clear();
        promptWanted = false;
        frameWanted  = false;
    }

    internal static void EndFrame()
    {
        EnsureCanvas();
        bool show = Pad.Active;
        canvas.enabled = show;
        if (!show) { frameVisible = false; return; }

        UpdateHintBar();
        UpdatePrompt();
        UpdateFrame();
    }

    private static void EnsureCanvas()
    {
        if (canvas != null) return;
        var go = new GameObject("GamepadHudCanvas");
        Object.DontDestroyOnLoad(go);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1001;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = UiScale.Reference;
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.Expand;
        UiScale.Track(scaler);
        canvasRT = (RectTransform)go.transform;

        BuildFrame();
        BuildPrompt();
        BuildHintBar();
    }

    private static void BuildHintBar()
    {
        var go = new GameObject("PadHints", typeof(RectTransform));
        go.transform.SetParent(canvasRT, false);
        hintBar = (RectTransform)go.transform;
        hintBar.anchorMin = hintBar.anchorMax = hintBar.pivot = new Vector2(1f, 0f);
        hintBar.anchoredPosition = new Vector2(-14f, 12f);

        CuteUIKit.Layer(hintBar, "Border", CuteUIKit.Rounded(14), CuteUIKit.Hex(0xE9CBA7, 0.9f), -2f);
        CuteUIKit.Layer(hintBar, "Fill",   CuteUIKit.Rounded(14), CuteUIKit.Hex(0xFFF7EA, 0.94f));

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(14, 14, 6, 6);
        hlg.spacing                = 14f;
        hlg.childAlignment         = TextAnchor.MiddleRight;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        var csf = go.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
    }

    private static void UpdateHintBar()
    {
        // 바뀔 때만 다시 만들려고 해시 비교
        int key = 17;
        foreach (var h in hints)
            key = key * 31 + ((int)h.glyph * 64 + (h.glyph2.HasValue ? (int)h.glyph2.Value + 1 : 0)) * 7919 + (h.label?.GetHashCode() ?? 0);

        hintBar.gameObject.SetActive(hints.Count > 0);
        float k = canvasRT.rect.width / Mathf.Max(1f, Screen.width);
        hintBar.anchoredPosition = new Vector2(-14f - RightPanelInset() * k, 12f);
        if (key == shownHintKey && hintBuilt) return;
        shownHintKey = key;
        hintBuilt    = true;

        for (int i = hintBar.childCount - 1; i >= 0; i--)
        {
            var child = hintBar.GetChild(i);
            if (child.name == "Item") Object.Destroy(child.gameObject);
        }
        foreach (var h in hints)
        {
            var item = new GameObject("Item", typeof(RectTransform));
            item.transform.SetParent(hintBar, false);
            var row = item.AddComponent<HorizontalLayoutGroup>();
            row.spacing                = 4f;
            row.childAlignment         = TextAnchor.MiddleLeft;
            row.childControlWidth      = true;
            row.childControlHeight     = true;
            row.childForceExpandWidth  = false;
            row.childForceExpandHeight = false;

            Glyph(item.transform, h.glyph);
            if (h.glyph2.HasValue) Glyph(item.transform, h.glyph2.Value);
            var label = CuteUIKit.Text(item.transform, h.label, HintFont, CuteUIKit.Cocoa, FontStyles.Bold);
            label.gameObject.AddComponent<LayoutElement>();
        }
    }

    private static readonly Vector3[] corners = new Vector3[4];

    private static float RightPanelInset()
    {
        float inset = 0f;
        if (HelpUI.AnyOpen)     inset = Mathf.Max(inset, InsetOf(HelpUI.PanelRoot));
        if (DevPanelUI.AnyOpen) inset = Mathf.Max(inset, InsetOf(DevPanelUI.PanelRoot));
        return inset;
    }

    private static float InsetOf(Transform panel)
    {
        if (panel is not RectTransform rt) return 0f;
        rt.GetWorldCorners(corners);
        float left = Mathf.Min(corners[0].x, corners[1].x), bottom = Mathf.Min(corners[0].y, corners[3].y);
        return bottom > 120f || left > Screen.width ? 0f : Mathf.Max(0f, Screen.width - left + 8f);
    }

    public static RectTransform Glyph(Transform parent, PadGlyph g, float size = GlyphSize)
    {
        var (text, fill, round) = Look(g);
        var go = new GameObject("Glyph_" + g, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;

        var img = go.AddComponent<Image>();
        CuteUIKit.SetRounded(img, round ? Mathf.RoundToInt(size * 0.5f) : Mathf.RoundToInt(size * 0.32f), fill);
        img.raycastTarget = false;

        float width = round ? size : Mathf.Max(size, 8f + text.Length * size * 0.36f);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth  = le.minWidth  = width;
        le.preferredHeight = le.minHeight = size;
        rt.sizeDelta = new Vector2(width, size);

        var label = CuteUIKit.Text(rt, text, size * (text.Length > 2 ? 0.42f : text.Length == 2 ? 0.5f : 0.62f),
                                   g == PadGlyph.Y ? CuteUIKit.Cocoa : Color.white, FontStyles.Bold);
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        return rt;
    }

    private static (string text, Color fill, bool round) Look(PadGlyph g) => g switch
    {
        PadGlyph.A      => ("A", ColA, true),
        PadGlyph.B      => ("B", ColB, true),
        PadGlyph.X      => ("X", ColX, true),
        PadGlyph.Y      => ("Y", ColY, true),
        PadGlyph.LB     => ("LB", ColPill, false),
        PadGlyph.RB     => ("RB", ColPill, false),
        PadGlyph.LT     => ("LT", ColPill, false),
        PadGlyph.RT     => ("RT", ColPill, false),
        PadGlyph.Start  => ("START", ColPill, false),
        PadGlyph.Back   => ("BACK", ColPill, false),
        PadGlyph.L3     => ("L3", ColStick, false),
        PadGlyph.R3     => ("R3", ColStick, false),
        PadGlyph.Up     => ("↑", ColPill, false),
        PadGlyph.Down   => ("↓", ColPill, false),
        PadGlyph.Left   => ("←", ColPill, false),
        PadGlyph.Right  => ("→", ColPill, false),
        PadGlyph.DPad   => ("+", ColPill, false),
        PadGlyph.LStick => ("L", ColStick, true),
        _               => ("R", ColStick, true),
    };

    private static void BuildPrompt()
    {
        var go = new GameObject("PadPrompt", typeof(RectTransform));
        go.transform.SetParent(canvasRT, false);
        promptRT = (RectTransform)go.transform;
        promptRT.anchorMin = promptRT.anchorMax = Vector2.zero;
        promptRT.pivot = new Vector2(0.5f, 0f);

        CuteUIKit.Layer(promptRT, "Border", CuteUIKit.Rounded(12), CuteUIKit.Strawberry, -2f);
        CuteUIKit.Layer(promptRT, "Fill",   CuteUIKit.Rounded(12), CuteUIKit.Hex(0xFFF7EA, 0.96f));

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(5, 9, 3, 3);
        hlg.spacing                = 5f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        var csf = go.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        Glyph(promptRT, PadGlyph.A, 22f);
        promptLabel = CuteUIKit.Text(promptRT, "", 14f, CuteUIKit.Cocoa, FontStyles.Bold);
        promptLabel.gameObject.AddComponent<LayoutElement>();
        go.SetActive(false);
    }

    private static void UpdatePrompt()
    {
        promptRT.gameObject.SetActive(promptWanted);
        if (!promptWanted) return;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, promptScreen, null, out var local))
            promptRT.anchoredPosition = local + canvasRT.rect.size * 0.5f;
    }

    private static void BuildFrame()
    {
        var go = new GameObject("PadSelection", typeof(RectTransform));
        go.transform.SetParent(canvasRT, false);
        frameRT = (RectTransform)go.transform;
        frameRT.anchorMin = frameRT.anchorMax = Vector2.zero;
        frameRT.pivot = new Vector2(0.5f, 0.5f);

        var glow = CuteUIKit.Layer(frameRT, "Glow", RingSprite(), CuteUIKit.Hex(0xFFFFFF, 0.9f), -2.5f);
        glow.type = Image.Type.Sliced;
        frameImage = CuteUIKit.Layer(frameRT, "Ring", RingSprite(), CuteUIKit.Strawberry);
        frameImage.type = Image.Type.Sliced;

        var paw = new GameObject("Paw", typeof(RectTransform));
        paw.transform.SetParent(frameRT, false);
        var prt = (RectTransform)paw.transform;
        prt.anchorMin = prt.anchorMax = new Vector2(1f, 0f);
        prt.pivot     = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(24f, 24f);
        prt.anchoredPosition = new Vector2(0f, 0f);
        prt.localRotation = Quaternion.Euler(0f, 0f, 20f);
        pawImage = paw.AddComponent<Image>();
        pawImage.sprite = CuteUIKit.Paw();
        pawImage.raycastTarget = false;
        go.SetActive(false);
    }

    private static void UpdateFrame()
    {
        if (!frameWanted)
        {
            frameVisible = false;
            frameRT.gameObject.SetActive(false);
            return;
        }

        float k = canvasRT.rect.width / Mathf.Max(1f, Screen.width);
        var target = new Rect(frameTarget.x * k, frameTarget.y * k, frameTarget.width * k, frameTarget.height * k);
        const float Pad = 5f;
        target.xMin -= Pad; target.yMin -= Pad; target.xMax += Pad; target.yMax += Pad;

        if (!frameVisible) frameShown = target;
        else
        {
            float t = 1f - Mathf.Exp(-22f * Time.unscaledDeltaTime);
            frameShown = new Rect(Vector2.Lerp(frameShown.position, target.position, t), Vector2.Lerp(frameShown.size, target.size, t));
        }
        frameVisible = true;
        frameRT.gameObject.SetActive(true);

        float pulse = 1f + 0.035f * Mathf.Sin(Time.unscaledTime * 6f);
        frameRT.anchoredPosition = frameShown.center;
        frameRT.sizeDelta        = frameShown.size * pulse;

        Color c = frameCarry ? CuteUIKit.Butter : CuteUIKit.Strawberry;
        frameImage.color = c;
        pawImage.color   = frameCarry ? CuteUIKit.Hex(0xE3A33B) : CuteUIKit.Hex(0xF06A89);
        pawImage.rectTransform.anchoredPosition = new Vector2(0f, 1.5f * Mathf.Sin(Time.unscaledTime * 5f));
    }

    private static Sprite ring;

    private static Sprite RingSprite()
    {
        if (ring != null) return ring;
        const int   size   = 48;
        const float radius = 12f, width = 3.5f;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px  = new Color32[size * size];
        float half = size * 0.5f, box = half - 2f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var q = new Vector2(Mathf.Abs(x + 0.5f - half) - (box - radius), Mathf.Abs(y + 0.5f - half) - (box - radius));
            float d    = Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
            float band = Mathf.Abs(d + width * 0.5f) - width * 0.5f;
            px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - band) * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        int border = (int)radius + 3;
        ring = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
                             new Vector4(border, border, border, border));
        return ring;
    }
}
