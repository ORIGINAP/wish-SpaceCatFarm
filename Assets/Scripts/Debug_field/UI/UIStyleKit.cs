using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

public static class UIStyleKit
{
    public static readonly Color PanelBg      = CuteUIKit.Cream;
    public static readonly Color PanelOutline = CuteUIKit.Latte;
    public static readonly Color HeaderBg     = CuteUIKit.Strawberry;
    public static readonly Color RowBg        = CuteUIKit.LatteLight;
    public static readonly Color Divider      = CuteUIKit.Latte;

    public static readonly Color TextPrimary   = CuteUIKit.Cocoa;
    public static readonly Color TextSecondary = CuteUIKit.CocoaSoft;
    public static readonly Color TextMuted     = CuteUIKit.CocoaMuted;
    public static readonly Color Accent        = CuteUIKit.Strawberry;

    public static readonly Color ButtonConfirm = CuteUIKit.ButtonMint;
    public static readonly Color ButtonDanger  = CuteUIKit.ButtonCoral;
    public static readonly Color ButtonNeutral = CuteUIKit.ButtonLatte;

    public static CanvasGroup CreatePanelRoot(GameObject go, float width, RectOffset padding = null, float spacing = 10f)
    {
        var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, 0f);

        if (go.GetComponent<Image>() == null) go.AddComponent<Image>();
        CuteUIKit.DecorateWindow(rt, catEars: width >= 260f);

        var cg = go.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        cg.interactable   = false;

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding = padding ?? new RectOffset(20, 20, 18, 20);
        vlg.spacing = spacing;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;

        var csf = go.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return cg;
    }

    public static TextMeshProUGUI CreateText(Transform parent, string text, float size, Color color,
        FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
    {
        var go  = new GameObject("Txt", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text          = text;
        tmp.fontSize      = size;
        tmp.color         = color;
        tmp.fontStyle     = style;
        tmp.alignment     = align;
        tmp.raycastTarget = false;
        var csf = go.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        go.AddComponent<LayoutElement>();
        return tmp;
    }

    public static Button CreateButton(Transform parent, string label, Color bgColor, UnityAction onClick,
        float preferredWidth = -1f, float preferredHeight = 30f)
        => CuteUIKit.Button(parent, label, bgColor, onClick, preferredWidth, preferredHeight);

    public static void ApplyButtonColors(Selectable selectable, Color bgColor)
        => CuteUIKit.ApplyButtonColors(selectable, bgColor);

    public static void KeepNavigable(GameObject defaultTarget, Transform scope)
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null || defaultTarget == null || !defaultTarget.activeInHierarchy || Pad.Active) return;

        var cur = es.currentSelectedGameObject;
        if (cur != null && cur.activeInHierarchy && cur.transform.IsChildOf(scope)) return;

        bool navInput = GameInput.GetKey(KeyCode.UpArrow) || GameInput.GetKey(KeyCode.DownArrow) || GameInput.GetKey(KeyCode.LeftArrow) || GameInput.GetKey(KeyCode.RightArrow)
                     || GameInput.GetKey(KeyCode.W) || GameInput.GetKey(KeyCode.S) || GameInput.GetKey(KeyCode.A) || GameInput.GetKey(KeyCode.D);
        if (navInput) es.SetSelectedGameObject(defaultTarget);
    }

    private static readonly System.Collections.Generic.List<ScrollRect> scrollBuf = new();
    private static readonly System.Collections.Generic.HashSet<Transform> scrollPaused = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => scrollPaused.Clear();

    // 크기 0 창의 ScrollRect 떨림 방지
    public static void SetScrolling(Transform root, bool on)
    {
        if (root == null) return;
        if (on) scrollPaused.Remove(root); else scrollPaused.Add(root);
        root.GetComponentsInChildren(true, scrollBuf);
        foreach (var s in scrollBuf) if (s != null) s.enabled = on && (s.vertical || s.horizontal);
        scrollBuf.Clear();
    }

    public static bool ScrollPaused(ScrollRect scroll)
    {
        scrollPaused.RemoveWhere(t => t == null);
        foreach (var root in scrollPaused)
            if (scroll.transform.IsChildOf(root)) return true;
        return false;
    }

    public static void ClearSelectionIn(Transform scope)
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null || es.currentSelectedGameObject == null) return;
        if (es.currentSelectedGameObject.transform.IsChildOf(scope)) es.SetSelectedGameObject(null);
    }

    public static Canvas CreateOverlayCanvas(GameObject go, int sortingOrder)
    {
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = UiScale.Reference;
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.Expand;
        // UI 크기 옵션 따라가게
        UiScale.Track(scaler);

        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    public static void CreateDivider(Transform parent, float heightPx = 1f)
        => CuteUIKit.DottedDivider(parent);

    public static Image CreateIcon(Transform parent, Sprite sprite, float size)
    {
        var go = new GameObject("Icon", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = size; le.preferredHeight = size;
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.enabled = sprite != null;
        return img;
    }
}
