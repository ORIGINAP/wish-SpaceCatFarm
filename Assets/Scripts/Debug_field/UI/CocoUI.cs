using TMPro;
using UnityEngine;
using UnityEngine.UI;

public static class CocoUI
{
    public static RectTransform Row(Transform parent, string line, float portrait, float fontSize, bool stretch = true, TMP_FontAsset font = null)
    {
        var rowGO = new GameObject("CocoRow", typeof(RectTransform));
        rowGO.transform.SetParent(parent, false);
        var hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = 10f;
        hlg.childAlignment         = stretch ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        Portrait(rowGO.transform, portrait, font);

        var bubble = CuteUIKit.Card(rowGO.transform, CuteUIKit.Latte, 14, "Bubble");
        bubble.raycastTarget = false;
        CuteUIKit.Layer(bubble.transform, "Fill", CuteUIKit.Rounded(12), Color.white, 2f);
        var bl = bubble.gameObject.AddComponent<HorizontalLayoutGroup>();
        bl.padding                = new RectOffset(13, 13, 9, 10);
        bl.childAlignment         = TextAnchor.MiddleLeft;
        bl.childControlWidth      = true;
        bl.childControlHeight     = true;
        bl.childForceExpandWidth  = false;
        bl.childForceExpandHeight = false;

        var text = CuteUIKit.Text(bubble.transform, line, fontSize, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        if (font != null) text.font = font;
        text.lineSpacing = 6f;
        if (stretch)
        {
            text.textWrappingMode = TextWrappingModes.Normal;
            bubble.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            text.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }
        return (RectTransform)rowGO.transform;
    }

    public static RectTransform Portrait(Transform parent, float size, TMP_FontAsset font = null)
    {
        var badge = CuteUIKit.Card(parent, CuteUIKit.SkyTag, Mathf.RoundToInt(size * 0.5f), "Coco");
        badge.raycastTarget = false;
        var le = badge.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth  = le.minWidth  = size;
        le.preferredHeight = le.minHeight = size;
        badge.rectTransform.sizeDelta = new Vector2(size, size);

        int scale = size >= 60f ? 3 : 2;
        var cat = CatPortrait.Create(badge.transform, new Vector2(size, size), scale, animate: true, new Vector2(0f, -size * 0.06f));
        var crt = (RectTransform)cat.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        var catImg = cat.GetComponentInChildren<Image>(true);
        if (catImg != null) catImg.color = AINpc.Instance != null ? AINpc.Instance.Tint : AINpc.DefaultTint;

        var tag = CuteUIKit.Pill(badge.transform, AINpc.NpcName, CuteUIKit.Strawberry, Color.white, 10f, 7,
                                 new RectOffset(5, 5, 0, 1), out var tagText, fitSelf: true);
        if (font != null) tagText.font = font;
        tag.anchorMin = tag.anchorMax = tag.pivot = new Vector2(0.5f, 0f);
        tag.anchoredPosition = new Vector2(0f, -6f);
        tag.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        return badge.rectTransform;
    }
}
