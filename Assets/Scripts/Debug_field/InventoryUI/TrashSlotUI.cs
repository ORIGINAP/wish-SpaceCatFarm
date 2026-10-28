using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

[RequireComponent(typeof(Image))]
public class TrashSlotUI : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler,
    IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    public static readonly Color BorderColor = CuteUIKit.Hex(0xE8A2AC);
    public static readonly Color FillColor   = CuteUIKit.Hex(0xFFF1F2);

    private InventoryUI     mgr;
    private Image           frame, fill, bin, icon;
    private GameObject      countBadge;
    private TextMeshProUGUI countText;
    private Coroutine       popCo;

    public void Setup(InventoryUI manager)
    {
        mgr   = manager;
        frame = GetComponent<Image>();
        var fillTf = transform.Find("Fill");
        fill  = fillTf != null ? fillTf.GetComponent<Image>() : frame;

        bin  = MakeImage("Bin", CuteUIKit.Trash(), new Vector2(0.21f, 0.21f), new Vector2(0.79f, 0.79f));
        icon = MakeImage("Icon", null, new Vector2(0.14f, 0.16f), new Vector2(0.86f, 0.86f));

        var badge = CuteUIKit.Pill(transform, "", CuteUIKit.Cocoa, Color.white, 9f, 7,
                                   new RectOffset(4, 4, 0, 1), out countText, fitSelf: true);
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1f, 0f);
        badge.anchoredPosition = new Vector2(-3f, 3f);
        countBadge = badge.gameObject;

        Refresh();
    }

    private Image MakeImage(string name, Sprite sprite, Vector2 min, Vector2 max)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var img = go.AddComponent<Image>();
        img.sprite         = sprite;
        img.preserveAspect = true;
        img.raycastTarget  = false;
        var rt = img.rectTransform;
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return img;
    }

    public void Refresh()
    {
        var inv  = Inventory.Instance;
        var item = inv != null ? inv.TrashItem : null;
        bool has = item != null;

        icon.sprite  = has ? item.icon : null;
        icon.enabled = has && item.icon != null;
        icon.color   = new Color(1f, 1f, 1f, 0.85f);
        bin.color    = new Color(BorderColor.r, BorderColor.g, BorderColor.b, has ? 0.35f : 1f);

        bool showCount = has && inv.TrashCount > 1;
        countBadge.SetActive(showCount);
        if (showCount) countText.text = inv.TrashCount.ToString();
    }

    public void OnDrop(PointerEventData e)
    {
        if (mgr == null || !mgr.IsDragging || mgr.DragSourceIndex < 0) return;
        mgr.EndDrag(InventoryUI.TrashIndex);
        SetColors(BorderColor, FillColor);
        Pop(1f);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (Inventory.Instance == null || Inventory.Instance.TrashItem == null) { e.pointerDrag = null; return; }
        ItemTooltipUI.Instance?.Hide();
        mgr.BeginDrag(InventoryUI.TrashIndex);
        icon.enabled = false;
        countBadge.SetActive(false);
    }

    public void OnDrag(PointerEventData e) => mgr.MoveDragIcon(e.position);

    public void OnEndDrag(PointerEventData e)
    {
        mgr.CancelDragIfActive();
        Refresh();
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (mgr != null && mgr.IsDragging)
        {
            if (mgr.DragSourceIndex == InventoryUI.TrashIndex) return;
            SetColors(CuteUIKit.CoralText, CuteUIKit.Hex(0xFFD6DC));
            Pop(1.08f);
            return;
        }
        frame.color = CuteUIKit.Strawberry;
        Pop(1.08f);
        var item = Inventory.Instance != null ? Inventory.Instance.TrashItem : null;
        if (item != null) ItemTooltipUI.Instance?.Show(item);
    }

    public void OnPointerExit(PointerEventData e)
    {
        ItemTooltipUI.Instance?.Hide();
        Pop(1f);
        SetColors(BorderColor, FillColor);
    }

    private void SetColors(Color border, Color background)
    {
        frame.color = border;
        if (fill != frame) fill.color = background;
    }

    private void Pop(float scale)
    {
        if (!isActiveAndEnabled) return;
        if (popCo != null) StopCoroutine(popCo);
        popCo = StartCoroutine(CuteUIKit.ScaleTo(transform, scale, scale > 1f ? 0.16f : 0.12f));
    }
}
