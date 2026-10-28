using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class ItemCell
{
    public int             index;
    public bool            hotbar;
    public RectTransform   rt;
    public Image           frame, fill, icon;
    public GameObject      badge, tag;
    public TextMeshProUGUI count, tagText;
    public FarmingItemData item;
    public int             shownCount = -1;
    public string          shownTag;
    public bool            usable = true, hovered;
    public int             tier = 1;

    private static readonly Color DimFill = CuteUIKit.Hex(0xEFE6DC);

    public static ItemCell Build(Transform parent, float size, bool hotbar, TMP_FontAsset font,
                                 System.Action<ItemCell> onClick, System.Action<ItemCell, bool> onHover)
    {
        var c  = new ItemCell { hotbar = hotbar };
        var go = CuteUIKit.Slot(parent, size, c.Border, c.Fill);
        c.rt    = (RectTransform)go.transform;
        c.frame = go.GetComponent<Image>();
        c.fill  = go.transform.Find("Fill").GetComponent<Image>();

        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => onClick?.Invoke(c));
        go.AddComponent<CutePop>().hoverScale = 1.08f;
        go.AddComponent<HoverRelay>().on = on => { c.hovered = on; c.Paint(); onHover?.Invoke(c, on); };

        c.icon = ItemCellUI.Icon(go.transform, null);

        var badge = CuteUIKit.Pill(go.transform, "", CuteUIKit.Cocoa, Color.white, 9f, 7, new RectOffset(4, 4, 0, 1), out c.count, fitSelf: true);
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1f, 0f);
        badge.anchoredPosition = new Vector2(-3f, 3f);
        c.badge = badge.gameObject;

        var tag = ItemCellUI.CoinPill(go.transform, "", CuteUIKit.ButterSoft, 9f, 10f, font, out c.tagText);
        var fit = tag.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        tag.anchorMin = tag.anchorMax = tag.pivot = new Vector2(0f, 1f);
        tag.anchoredPosition = new Vector2(2f, -2f);
        tag.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(3, 5, 1, 1);
        c.tag = tag.gameObject;
        c.tag.SetActive(false);
        c.badge.SetActive(false);
        return c;
    }

    public void Set(FarmingItemData newItem, int newCount, bool newUsable, string newTag)
    {
        if (newItem == null) { newCount = 0; newTag = null; newUsable = true; }
        if (newItem == item && newCount == shownCount && newUsable == usable && newTag == shownTag) return;
        item       = newItem;
        shownCount = newCount;
        usable     = newUsable;
        shownTag   = newTag;
        tier       = item != null ? item.Tier : 1;

        icon.sprite  = item != null ? item.icon : null;
        icon.enabled = item != null && item.icon != null;
        icon.color   = item == null || usable ? Color.white : new Color(1f, 1f, 1f, 0.38f);
        badge.SetActive(newCount > 1);
        if (newCount > 1) count.text = newCount.ToString();
        tag.SetActive(newTag != null);
        if (newTag != null) tagText.text = newTag;
        Paint();
    }

    private Color Border
    {
        get
        {
            if (hovered) return item == null ? CuteUIKit.Latte : usable ? CuteUIKit.Strawberry : CuteUIKit.CocoaMuted;
            return CuteUIKit.TierBorder(tier, hotbar ? CuteUIKit.HotbarBorder : CuteUIKit.SlotBorder);
        }
    }

    private Color Fill
    {
        get
        {
            if (item != null && !usable) return DimFill;
            if (hovered && item != null) return CuteUIKit.PinkSoft;
            return CuteUIKit.TierFill(tier, hotbar ? CuteUIKit.HotbarFill : CuteUIKit.SlotFill);
        }
    }

    public void Paint()
    {
        if (frame == null) return;
        frame.color = Border;
        fill.color  = Fill;
    }

    private sealed class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action<bool> on;
        public void OnPointerEnter(PointerEventData e) => on?.Invoke(true);
        public void OnPointerExit(PointerEventData e)  => on?.Invoke(false);
    }
}

public sealed class BagView
{
    public const int Cols = Inventory.HotbarSize;

    public readonly List<ItemCell> Cells = new();
    public System.Func<FarmingItemData, bool>   Usable = _ => true;
    public System.Func<FarmingItemData, string> Tag    = _ => null;
    public System.Action<ItemCell>              Clicked;
    public System.Action<ItemCell, bool>        Hovered;
    public ScrollRect Scroll => scroll;

    private readonly Transform   hotbarRow, grid;
    private readonly ScrollRect  scroll;
    private readonly float       slot;
    private readonly TMP_FontAsset font;

    public static float Width(float slot, float gap) => Cols * slot + (Cols - 1) * gap;

    public BagView(Transform parent, float slot, float gap, int rows, TMP_FontAsset font)
    {
        this.slot = slot;
        this.font = font;
        float w = Width(slot, gap);

        hotbarRow = ItemCellUI.HRow("Hotbar", parent, gap, TextAnchor.MiddleLeft);
        var hle = hotbarRow.gameObject.AddComponent<LayoutElement>();
        hle.preferredWidth  = w + 12f;
        hle.preferredHeight = slot + 4f;
        hotbarRow.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(6, 6, 2, 2);

        float h = rows * slot + (rows - 1) * gap;
        grid = ItemCellUI.ScrollGrid(parent, "Bag", w, h, new Vector2(slot, slot), gap, Cols, out scroll);
    }

    public void Refresh()
    {
        var inv = Inventory.Instance;
        if (inv == null) return;
        if (Cells.Count != inv.SlotCount) Rebuild(inv.SlotCount);
        foreach (var c in Cells)
        {
            var s = inv.GetSlot(c.index);
            var item = s == null || s.IsEmpty ? null : s.Item;
            c.Set(item, item != null ? s.Count : 0, item == null || Usable(item), item != null ? Tag(item) : null);
        }
    }

    public void Repaint()
    {
        foreach (var c in Cells) c.shownTag = "\u0000";
        Refresh();
    }

    private void Rebuild(int total)
    {
        foreach (var c in Cells) if (c.rt != null) Object.DestroyImmediate(c.rt.gameObject);
        Cells.Clear();
        for (int i = 0; i < total; i++)
        {
            var c = ItemCell.Build(i < Cols ? hotbarRow : grid, slot, i < Cols, font, x => Clicked?.Invoke(x), (x, on) => Hovered?.Invoke(x, on));
            c.index   = i;
            c.rt.name = $"Bag_{i}";
            Cells.Add(c);
        }
        ItemCellUI.UpdateScrollable(scroll);
    }

    public ItemCell CellOf(FarmingItemData item)
    {
        if (item == null) return null;
        foreach (var c in Cells) if (c.item == item) return c;
        return null;
    }

    public ItemCell CellAt(int index) => index >= 0 && index < Cells.Count ? Cells[index] : null;
}

public static class ItemCellUI
{
    public static Image Icon(Transform slot, Sprite sprite)
    {
        var go = new GameObject("Icon", typeof(RectTransform));
        go.transform.SetParent(slot, false);
        var img = go.AddComponent<Image>();
        img.sprite         = sprite;
        img.enabled        = sprite != null;
        img.preserveAspect = true;
        img.raycastTarget  = false;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0.14f, 0.15f);
        rt.anchorMax = new Vector2(0.86f, 0.86f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return img;
    }

    public static RectTransform CoinPill(Transform parent, string text, Color fill, float fontSize, float iconSize, TMP_FontAsset font,
                                         out TextMeshProUGUI label)
    {
        var pill = CuteUIKit.Pill(parent, text, fill, CuteUIKit.Cocoa, fontSize, Mathf.CeilToInt(fontSize),
                                  new RectOffset(5, 8, 2, 2), out label);
        pill.GetComponent<HorizontalLayoutGroup>().spacing = 3f;
        if (font != null) label.font = font;

        var coinGO = new GameObject("Coin", typeof(RectTransform));
        coinGO.transform.SetParent(pill, false);
        coinGO.transform.SetAsFirstSibling();
        var coin = coinGO.AddComponent<Image>();
        coin.sprite         = CuteUIKit.Coin();
        coin.preserveAspect = true;
        coin.raycastTarget  = false;
        var le = coinGO.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = le.minWidth = le.minHeight = iconSize;
        return pill;
    }

    public static Transform HRow(string name, Transform parent, float spacing, TextAnchor align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = spacing;
        hlg.childAlignment         = align;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        return go.transform;
    }

    public static Transform Pane(string name, Transform parent)
    {
        var img = CuteUIKit.Card(parent, CuteUIKit.Hex(0xFBEEDD), 18, name);
        // 빈 곳 끌면 창 이동
        img.raycastTarget = false;
        var vlg = img.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(12, 12, 10, 12);
        vlg.spacing                = 8f;
        vlg.childAlignment         = TextAnchor.UpperCenter;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = false;
        vlg.childForceExpandHeight = false;
        return img.transform;
    }

    public static Transform PaneHeader(Transform pane, string title, Color tag, Color text, string note, float width, TMP_FontAsset font,
                                       out TextMeshProUGUI noteText)
    {
        var row = HRow("Header", pane, 6f, TextAnchor.MiddleLeft);
        var le = row.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth  = width;
        le.preferredHeight = le.minHeight = 26f;

        CuteUIKit.Pill(row, title, tag, text, 13f, 11, new RectOffset(11, 11, 3, 4), out var titleText);
        if (font != null) titleText.font = font;
        noteText = CuteUIKit.Text(row, note, 12f, CuteUIKit.CocoaSoft);
        if (font != null) noteText.font = font;
        var spacer = new GameObject("Spacer", typeof(RectTransform));
        spacer.transform.SetParent(row, false);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;
        return row;
    }

    public static Button[] AmountPicker(Transform row, int[] amounts, int chosen, TMP_FontAsset font, System.Action<int> pick)
    {
        var label = CuteUIKit.Text(row, "한 번에", 11f, CuteUIKit.CocoaSoft);
        if (font != null) label.font = font;
        var arr = new Button[amounts.Length];
        for (int i = 0; i < amounts.Length; i++)
        {
            int k = i;
            arr[i] = CuteUIKit.Button(row, amounts[i] <= 0 ? "전부" : $"{amounts[i]}개", CuteUIKit.ButtonLatte, () => pick(k), 46f, 24f, 11f);
            var t = arr[i].GetComponentInChildren<TextMeshProUGUI>(true);
            if (t != null && font != null) t.font = font;
        }
        PaintAmounts(arr, chosen);
        return arr;
    }

    public static void PaintAmounts(Button[] arr, int chosen)
    {
        if (arr == null) return;
        for (int i = 0; i < arr.Length; i++)
            CuteUIKit.ApplyButtonColors(arr[i], i == chosen ? CuteUIKit.Strawberry : CuteUIKit.ButtonLatte);
    }

    public static Transform ScrollGrid(Transform parent, string name, float width, float height, Vector2 cell, float gap, int cols,
                                       out ScrollRect scroll)
    {
        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var le = root.AddComponent<LayoutElement>();
        le.preferredWidth  = le.minWidth  = width + 12f;
        le.preferredHeight = le.minHeight = height + 12f;

        scroll = root.AddComponent<ScrollRect>();
        scroll.horizontal        = false;
        scroll.vertical          = true;
        scroll.movementType      = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 25f;

        var vp = new GameObject("Viewport", typeof(RectTransform));
        vp.transform.SetParent(root.transform, false);
        var vpRT = (RectTransform)vp.transform;
        vpRT.anchorMin = Vector2.zero;
        vpRT.anchorMax = Vector2.one;
        vpRT.offsetMin = vpRT.offsetMax = Vector2.zero;
        vp.AddComponent<RectMask2D>();

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(vp.transform, false);
        var cRT = (RectTransform)content.transform;
        cRT.anchorMin = new Vector2(0f, 1f);
        cRT.anchorMax = new Vector2(1f, 1f);
        cRT.pivot     = new Vector2(0.5f, 1f);
        cRT.offsetMin = cRT.offsetMax = Vector2.zero;
        var g = content.AddComponent<GridLayoutGroup>();
        g.cellSize        = cell;
        g.spacing         = new Vector2(gap, gap);
        g.padding         = new RectOffset(6, 6, 6, 6);
        g.startCorner     = GridLayoutGroup.Corner.UpperLeft;
        g.startAxis       = GridLayoutGroup.Axis.Horizontal;
        g.childAlignment  = TextAnchor.UpperLeft;
        g.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        g.constraintCount = cols;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = vpRT;
        scroll.content  = cRT;
        return content.transform;
    }

    public static void UpdateScrollable(ScrollRect scroll)
    {
        if (scroll == null || scroll.content == null) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        var vp = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
        // 스크롤 여부는 vertical에 기록
        scroll.vertical = scroll.content.rect.height > vp.rect.height + 1f;
        scroll.enabled  = scroll.vertical && !UIStyleKit.ScrollPaused(scroll);
    }

    public static void ResetScroll(ScrollRect scroll)
    {
        if (scroll == null || scroll.content == null) return;
        scroll.content.anchoredPosition = Vector2.zero;
        scroll.StopMovement();
    }

    private static Image FxImage(Canvas canvas, Sprite sprite, float size)
    {
        var go = new GameObject("ItemFx", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);
        go.transform.SetAsLastSibling();
        var img = go.AddComponent<Image>();
        img.sprite         = sprite;
        img.preserveAspect = true;
        img.raycastTarget  = false;
        img.rectTransform.sizeDelta = new Vector2(size, size);
        return img;
    }

    public static void FlyIcon(MonoBehaviour host, Sprite sprite, Vector3 from, RectTransform target)
    {
        var canvas = host != null ? host.GetComponentInParent<Canvas>() : null;
        if (canvas == null || sprite == null || target == null || !host.isActiveAndEnabled) return;
        host.StartCoroutine(FlyIconCo(host, canvas.rootCanvas, sprite, from, target));
    }

    private static IEnumerator FlyIconCo(MonoBehaviour host, Canvas canvas, Sprite sprite, Vector3 from, RectTransform target)
    {
        float s = canvas.scaleFactor;
        var img = FxImage(canvas, sprite, 44f);
        var rt  = img.rectTransform;
        rt.position = from;
        const float FlyTime = 0.45f;
        Vector3 ctrl = (from + target.position) * 0.5f + Vector3.up * 90f * s;
        for (float e = 0f; e < FlyTime; e += Time.unscaledDeltaTime)
        {
            if (img == null || target == null) break;
            float k = e / FlyTime;
            k = k * k * (3f - 2f * k);
            Vector3 to = target.position;
            rt.position      = Vector3.Lerp(Vector3.Lerp(from, ctrl, k), Vector3.Lerp(ctrl, to, k), k);
            rt.localScale    = Vector3.one * Mathf.Lerp(1.2f, 0.85f, k);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(k * Mathf.PI) * -14f);
            yield return null;
        }
        if (img != null) Object.Destroy(img.gameObject);
        if (target != null && host != null) host.StartCoroutine(Punch(target, 1.18f));
    }

    public static void CoinShower(MonoBehaviour host, Vector3 from, RectTransform to, long amount, System.Action<bool> onLand)
    {
        var canvas = host != null ? host.GetComponentInParent<Canvas>() : null;
        if (canvas == null || !host.isActiveAndEnabled) return;
        int n = Mathf.Clamp(3 + Mathf.FloorToInt(Mathf.Log10(Mathf.Max(1f, amount)) * 1.6f), 3, 10);
        for (int i = 0; i < n; i++) host.StartCoroutine(FlyCoin(canvas.rootCanvas, from, to, i * 0.04f, i == n - 1, onLand));
    }

    private static IEnumerator FlyCoin(Canvas canvas, Vector3 from, RectTransform toRT, float delay, bool last, System.Action<bool> onLand)
    {
        float s = canvas.scaleFactor;
        var img = FxImage(canvas, CuteUIKit.Coin(), 18f);
        var rt  = img.rectTransform;
        rt.position = from;
        img.enabled = false;
        for (float d = 0f; d < delay; d += Time.unscaledDeltaTime) yield return null;
        if (img == null) yield break;
        img.enabled = true;

        Vector3 pop = from + (Vector3)(Random.insideUnitCircle * 26f * s) + Vector3.up * 22f * s;
        const float PopTime = 0.16f, FlyTime = 0.42f;
        for (float e = 0f; e < PopTime; e += Time.unscaledDeltaTime)
        {
            if (img == null) yield break;
            float k = 1f - (1f - e / PopTime) * (1f - e / PopTime);
            rt.position   = Vector3.Lerp(from, pop, k);
            rt.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.15f, k);
            yield return null;
        }
        Vector3 to   = toRT != null ? toRT.position : pop;
        Vector3 ctrl = (pop + to) * 0.5f + Vector3.up * 60f * s;
        for (float e = 0f; e < FlyTime; e += Time.unscaledDeltaTime)
        {
            if (img == null) yield break;
            float k = e / FlyTime;
            k = k * k;
            rt.position   = Vector3.Lerp(Vector3.Lerp(pop, ctrl, k), Vector3.Lerp(ctrl, to, k), k);
            rt.localScale = Vector3.one * Mathf.Lerp(1.15f, 0.7f, k);
            yield return null;
        }
        if (img != null) Object.Destroy(img.gameObject);
        onLand?.Invoke(last);
    }

    public static void FloatText(MonoBehaviour host, Vector3 at, string text, Color color, float size, TMP_FontAsset font)
    {
        var canvas = host != null ? host.GetComponentInParent<Canvas>() : null;
        if (canvas == null || !host.isActiveAndEnabled) return;
        var t = CuteUIKit.Text(canvas.rootCanvas.transform, text, size, color, FontStyles.Bold);
        if (font != null) t.font = font;
        t.transform.SetAsLastSibling();
        t.rectTransform.sizeDelta = new Vector2(220f, 30f);
        t.rectTransform.position  = at;
        host.StartCoroutine(Rise(t, canvas.rootCanvas.scaleFactor));
    }

    private static IEnumerator Rise(TextMeshProUGUI t, float s)
    {
        const float Dur = 0.95f;
        var rt    = t.rectTransform;
        var start = rt.position;
        var color = t.color;
        for (float e = 0f; e < Dur; e += Time.unscaledDeltaTime)
        {
            if (t == null) yield break;
            float k = e / Dur;
            rt.position   = start + Vector3.up * (1f - (1f - k) * (1f - k)) * 34f * s;
            rt.localScale = Vector3.one * (k < 0.15f ? Mathf.Lerp(0.6f, 1.1f, k / 0.15f) : Mathf.Lerp(1.1f, 1f, (k - 0.15f) / 0.85f));
            t.color       = new Color(color.r, color.g, color.b, k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f);
            yield return null;
        }
        if (t != null) Object.Destroy(t.gameObject);
    }

    public static IEnumerator Punch(Transform t, float scale)
    {
        if (t == null) yield break;
        yield return CuteUIKit.ScaleTo(t, scale, 0.07f, 0f);
        if (t == null) yield break;
        yield return CuteUIKit.ScaleTo(t, 1f, 0.16f);
    }

    public static bool HasBatchim(string w)
    {
        if (string.IsNullOrEmpty(w)) return false;
        char c = w[w.Length - 1];
        return c >= 0xAC00 && c <= 0xD7A3 && (c - 0xAC00) % 28 != 0;
    }

    public static string Eun(string w) => w + (HasBatchim(w) ? "은" : "는");
    public static string Eul(string w) => w + (HasBatchim(w) ? "을" : "를");
    public static string Ga(string w)  => w + (HasBatchim(w) ? "이" : "가");
}
