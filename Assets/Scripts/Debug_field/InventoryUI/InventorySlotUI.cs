using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

[RequireComponent(typeof(Image))]
public class InventorySlotUI : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler,
    IDropHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    private const float HoverScale = 1.08f;

    public int SlotIndex { get; private set; }

    private InventoryUI  mgr;
    private bool         isHotbar;
    private Image        frame;
    private Image        fill;
    private Image        icon;
    private GameObject   countBadge;
    private TextMeshProUGUI countText;
    private Coroutine    popCo, wiggleCo;
    private int          tier = 1;

    private Color BorderColor => CuteUIKit.TierBorder(tier, isHotbar ? CuteUIKit.HotbarBorder : CuteUIKit.SlotBorder);
    private Color FillColor   => CuteUIKit.TierFill(tier, isHotbar ? CuteUIKit.HotbarFill : CuteUIKit.SlotFill);

    public void Setup(int index, InventoryUI manager, bool hotbar)
    {
        SlotIndex = index;
        mgr       = manager;
        isHotbar  = hotbar;

        frame = GetComponent<Image>();
        var fillTf = transform.Find("Fill");
        fill  = fillTf != null ? fillTf.GetComponent<Image>() : frame;
        SetColors(BorderColor, FillColor);

        var iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(transform, false);
        icon = iconGO.AddComponent<Image>();
        icon.raycastTarget = false;
        icon.preserveAspect = true;
        var iconRT = icon.rectTransform;
        iconRT.anchorMin = new Vector2(0.14f, 0.16f);
        iconRT.anchorMax = new Vector2(0.86f, 0.86f);
        iconRT.offsetMin = iconRT.offsetMax = Vector2.zero;

        var badge = CuteUIKit.Pill(transform, "", CuteUIKit.Cocoa, Color.white, 9f, 7,
                                   new RectOffset(4, 4, 0, 1), out countText, fitSelf: true);
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1f, 0f);
        badge.anchoredPosition = new Vector2(-3f, 3f);
        countBadge = badge.gameObject;

        if (isHotbar)
        {
            var dot = CuteUIKit.Layer(transform, "HotbarNum", CuteUIKit.Rounded(8), CuteUIKit.SkyText).rectTransform;
            dot.anchorMin = dot.anchorMax = dot.pivot = new Vector2(0f, 1f);
            dot.sizeDelta        = new Vector2(16f, 16f);
            dot.anchoredPosition = new Vector2(3f, -3f);
            var num = CuteUIKit.Text(dot, (index + 1).ToString(), 9f, Color.white, FontStyles.Bold);
            num.rectTransform.anchorMin = Vector2.zero;
            num.rectTransform.anchorMax = Vector2.one;
            num.rectTransform.offsetMin = num.rectTransform.offsetMax = Vector2.zero;
        }

        Refresh();
    }

    public void Refresh()
    {
        var slot = Inventory.Instance?.GetSlot(SlotIndex);
        bool empty = slot == null || slot.IsEmpty;

        icon.sprite  = empty ? null : slot.Item.icon;
        icon.enabled = !empty && slot.Item.icon != null;

        bool showCount = !empty && slot.Count > 1;
        countBadge.SetActive(showCount);
        if (showCount) countText.text = slot.Count.ToString();

        int t = empty ? 1 : slot.Item.Tier;
        if (t != tier)
        {
            tier = t;
            SetColors(BorderColor, FillColor);
        }
    }

    public void ResetVisual()
    {
        if (popCo != null) { StopCoroutine(popCo); popCo = null; }
        if (wiggleCo != null) { StopCoroutine(wiggleCo); wiggleCo = null; }
        transform.localScale = Vector3.one;
        icon.transform.localRotation = Quaternion.identity;
        SetColors(BorderColor, FillColor);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        var slot = Inventory.Instance?.GetSlot(SlotIndex);
        if (slot == null || slot.IsEmpty) { e.pointerDrag = null; return; }

        ItemTooltipUI.Instance?.Hide();
        mgr.BeginDrag(SlotIndex);
        icon.enabled = false;
        countBadge.SetActive(false);
        SetColors(BorderColor, CuteUIKit.EmptiedFill);
    }

    public void OnDrag(PointerEventData e) => mgr.MoveDragIcon(e.position);

    public void OnEndDrag(PointerEventData e)
    {
        // OnDrop 없이 끝나면 월드에 버림
        mgr.CancelDragIfActive(e.pointerCurrentRaycast.gameObject == null, e.position);
        SetColors(BorderColor, FillColor);
        Refresh();
    }

    public void OnDrop(PointerEventData e)
    {
        mgr.EndDrag(SlotIndex);
        SetColors(BorderColor, FillColor);
        Pop(1f);
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (!GameInput.GetKey(KeyCode.LeftControl) && !GameInput.GetKey(KeyCode.RightControl)) return;
        ItemTooltipUI.Instance?.Hide();
        mgr.TrashFrom(SlotIndex);
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (mgr.IsDragging)
        {
            if (mgr.DragSourceIndex == SlotIndex) return;
            SetColors(CuteUIKit.Strawberry, CuteUIKit.PinkSoft);
            Pop(HoverScale);
            return;
        }

        frame.color = CuteUIKit.Strawberry;
        Pop(HoverScale);

        var slot = Inventory.Instance?.GetSlot(SlotIndex);
        if (slot != null && !slot.IsEmpty)
        {
            ItemTooltipUI.Instance?.Show(slot.Item);
            if (wiggleCo != null) StopCoroutine(wiggleCo);
            wiggleCo = StartCoroutine(CuteUIKit.Wiggle(icon.transform, 9f, 0.35f));
        }
    }

    public void OnPointerExit(PointerEventData e)
    {
        ItemTooltipUI.Instance?.Hide();
        Pop(1f);

        if (mgr.IsDragging && mgr.DragSourceIndex == SlotIndex) return;
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
