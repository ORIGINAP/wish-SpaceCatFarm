using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

[RequireComponent(typeof(Image))]
public class EquipmentSlotUI : MonoBehaviour,
    IDropHandler, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    static readonly Color C_EQUIPPED = CuteUIKit.Hex(0xEDE5FF);
    private const   float HoverScale = 1.08f;

    private int              slotIndex;
    private Image            frame;
    private Image            fill;
    private Image            icon;
    private Image            emptyHint;
    private TextMeshProUGUI  bonusText;
    private Coroutine        popCo;

    private static string StatLabel(TechStatType t) => t switch
    {
        TechStatType.MoveSpeed      => "이속",
        TechStatType.PlacementSpeed => "설치",
        TechStatType.MiningSpeed    => "채굴",
        TechStatType.MiningPower    => "파워",
        TechStatType.PickupRange    => "자석",
        TechStatType.DismantleSpeed => "해체",
        _                           => t.ToString()
    };

    public void Setup(int index)
    {
        slotIndex = index;
        frame     = GetComponent<Image>();
        var fillTf = transform.Find("Fill");
        fill      = fillTf != null ? fillTf.GetComponent<Image>() : frame;

        var hint = CuteUIKit.Layer(transform, "EmptyHint", CuteUIKit.Paw(), CuteUIKit.Hex(0xCFC0F4, 0.8f)).rectTransform;
        hint.anchorMin = new Vector2(0.27f, 0.27f);
        hint.anchorMax = new Vector2(0.73f, 0.73f);
        hint.offsetMin = hint.offsetMax = Vector2.zero;
        emptyHint = hint.GetComponent<Image>();

        var iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(transform, false);
        icon = iconGO.AddComponent<Image>();
        icon.raycastTarget  = false;
        icon.preserveAspect = true;
        var iconRT = icon.rectTransform;
        iconRT.anchorMin = new Vector2(0.1f, 0.28f);
        iconRT.anchorMax = new Vector2(0.9f, 0.88f);
        iconRT.offsetMin = iconRT.offsetMax = Vector2.zero;
        icon.enabled = false;

        var bGO = new GameObject("Bonus");
        bGO.transform.SetParent(transform, false);
        bonusText = bGO.AddComponent<TextMeshProUGUI>();
        bonusText.raycastTarget = false;
        bonusText.fontSize  = 7f;
        bonusText.color     = CuteUIKit.LavenderText;
        bonusText.alignment = TextAlignmentOptions.Center;
        var bRT = bonusText.rectTransform;
        bRT.anchorMin = new Vector2(0f, 0f);
        bRT.anchorMax = new Vector2(1f, 0.30f);
        bRT.offsetMin = bRT.offsetMax = Vector2.zero;

        EquipmentManager.GetOrCreate().OnEquipmentChanged += Refresh;

        Refresh();
    }

    private void OnDestroy()
    {
        if (EquipmentManager.Instance != null)
            EquipmentManager.Instance.OnEquipmentChanged -= Refresh;
    }

    public void Refresh()
    {
        var item = EquipmentManager.Instance?.GetSlot(slotIndex);
        icon.sprite  = item?.icon;
        icon.enabled = item?.icon != null;
        emptyHint.enabled = item == null;
        SetColors(CuteUIKit.EquipBorder, item != null ? C_EQUIPPED : CuteUIKit.EquipFill);

        if (item?.equipBonuses != null && item.equipBonuses.Length > 0)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var b in item.equipBonuses)
                sb.Append($"{StatLabel(b.statType)}+{b.bonus * 100f:F0}% ");
            bonusText.text = sb.ToString().Trim();
        }
        else
        {
            bonusText.text = "";
        }
    }

    public void OnDrop(PointerEventData e)
    {
        if (InventoryUI.Instance == null || !InventoryUI.Instance.IsDragging) return;

        int srcIdx = InventoryUI.Instance.DragSourceIndex;
        var slot   = Inventory.Instance?.GetSlot(srcIdx);
        if (slot == null || slot.IsEmpty) return;

        if (slot.Item.itemType != FarmingItemType.Equipment)
        {
            InventoryUI.Instance.CancelDragIfActive();
            NotificationUI.Instance?.Show("장비 아이템만 장착할 수 있습니다.");
            Sfx.Play(SfxId.UiError);
            return;
        }

        var em = EquipmentManager.GetOrCreate();

        // 장착 성공 후에만 아이템 소비
        var existing    = em.GetSlot(slotIndex);
        var itemToEquip = slot.Item;
        if (existing != null) em.Unequip(slotIndex);

        if (!em.TryEquip(slotIndex, itemToEquip))
        {
            // 실패하면 기존 장비 복원
            if (existing != null) em.TryEquip(slotIndex, existing);
            InventoryUI.Instance.CancelDragIfActive();
            return;
        }
        Inventory.Instance?.ConsumeItem(itemToEquip, 1);
        Sfx.Play(SfxId.Equip);

        if (existing != null && Inventory.Instance != null)
        {
            Vector3 dropPos = PlayerTileController.Instance != null ? PlayerTileController.Instance.transform.position : Vector3.zero;
            if (!Inventory.Instance.AddItemOrDrop(existing, 1, dropPos))
                NotificationUI.Instance?.Show($"가방이 가득 차서 {ItemCellUI.Eul(existing.itemName)} 바닥에 내려놓았어요.");
        }

        InventoryUI.Instance.EndDragExternal();
        Refresh();
    }

    public void OnPointerEnter(PointerEventData e)
    {
        if (InventoryUI.Instance?.IsDragging == true) SetColors(CuteUIKit.Strawberry, CuteUIKit.PinkSoft);
        else frame.color = CuteUIKit.Strawberry;
        Pop(HoverScale);
    }

    public void OnPointerExit(PointerEventData e)
    {
        bool equipped = EquipmentManager.Instance?.GetSlot(slotIndex) != null;
        SetColors(CuteUIKit.EquipBorder, equipped ? C_EQUIPPED : CuteUIKit.EquipFill);
        Pop(1f);
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (InventoryUI.Instance?.IsDragging == true) return;

        var item = EquipmentManager.Instance?.GetSlot(slotIndex);
        if (item == null) return;

        if (Inventory.Instance?.AddItem(item, 1) == true)
        {
            EquipmentManager.Instance.Unequip(slotIndex);
            Sfx.Play(SfxId.Equip);
            Refresh();
        }
        else
        {
            NotificationUI.Instance?.Show("인벤토리가 가득 찼습니다.");
            Sfx.Play(SfxId.UiError);
        }
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
