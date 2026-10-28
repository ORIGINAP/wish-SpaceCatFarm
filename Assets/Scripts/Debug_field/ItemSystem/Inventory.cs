using UnityEngine;

public class Inventory : MonoBehaviour
{
    public static Inventory Instance => Services.Get<Inventory>();

    [Header("슬롯 설정")]
    [Tooltip("총 인벤토리 슬롯 수 (핫바 포함)")]
    [SerializeField] private int slotCount = MinSlots;

    // 씬 값이 작아도 최소 35칸
    public const int MinSlots = 35;

    public event System.Action OnInventoryChanged;

    public event System.Action<FarmingItemData, int> OnItemAdded;

    private InventorySlot[] slots;

    public int SlotCount => slotCount;

    public const int HotbarSize = 7;

    public static bool IsHandItem(FarmingItemData item)
        => item != null && (item.itemType == FarmingItemType.Seed || item.itemType == FarmingItemType.Tile ||
                            item.itemType == FarmingItemType.Tool || item.IsFertilizer);

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }

        slotCount = Mathf.Max(slotCount, MinSlots);
        slots = new InventorySlot[slotCount];
        for (int i = 0; i < slotCount; i++)
            slots[i] = new InventorySlot();
    }

    private void OnDestroy() => Services.Unregister(this);

    public InventorySlot GetSlot(int index)
        => (index >= 0 && index < slotCount) ? slots[index] : null;

    public bool AddItem(FarmingItemData item, int amount = 1, bool countAsNew = true)
    {
        if (item == null || amount <= 0) return false;

        foreach (var slot in slots)
        {
            if (slot.Item == item)
            {
                slot.Add(amount);
                if (countAsNew) OnItemAdded?.Invoke(item, amount);
                OnInventoryChanged?.Invoke();
                return true;
            }
        }

        // 손에 드는 건 핫바부터
        int start = IsHandItem(item) ? 0 : Mathf.Min(HotbarSize, slotCount);
        for (int k = 0; k < slotCount; k++)
        {
            var slot = slots[(start + k) % slotCount];
            if (slot.IsEmpty)
            {
                slot.Set(item, amount);
                if (countAsNew) OnItemAdded?.Invoke(item, amount);
                OnInventoryChanged?.Invoke();
                return true;
            }
        }

        Debug.LogWarning($"[Inventory] 인벤토리가 가득 찼습니다. '{item.itemName}' x{amount} 추가 실패.");
        return false;
    }

    public bool CanAddItem(FarmingItemData item)
    {
        if (item == null) return false;
        foreach (var slot in slots)
            if (slot.Item == item || slot.IsEmpty) return true;
        return false;
    }

    public bool AddItemOrDrop(FarmingItemData item, int amount, Vector3 dropPosition)
    {
        if (item == null || amount <= 0) return false;
        if (AddItem(item, amount)) return true;

        DroppedItem.Spawn(item, amount, dropPosition, PlayerTileController.Instance != null ? PlayerTileController.Instance.transform : null);
        return false;
    }

    public bool ConsumeItem(FarmingItemData item, int amount = 1)
    {
        if (item == null || amount <= 0) return false;
        if (GetCount(item) < amount) return false;

        int remaining = amount;
        foreach (var slot in slots)
        {
            if (remaining <= 0) break;
            if (slot.Item != item) continue;

            int take = Mathf.Min(slot.Count, remaining);
            slot.Consume(take);
            remaining -= take;
        }
        OnInventoryChanged?.Invoke();
        return true;
    }

    public int GetCount(FarmingItemData item)
    {
        int total = 0;
        foreach (var slot in slots)
            if (slot.Item == item) total += slot.Count;
        return total;
    }

    public bool HasItem(FarmingItemData item) => GetCount(item) > 0;

    public int Signature(System.Predicate<FarmingItemData> filter)
    {
        unchecked
        {
            int hash = 17;
            foreach (var slot in slots)
            {
                if (slot == null || slot.IsEmpty || !filter(slot.Item)) continue;
                hash = hash * 31 + slot.Item.GetInstanceID();
                hash = hash * 31 + slot.Count;
            }
            return hash;
        }
    }

    public void ClearAll()
    {
        foreach (var slot in slots) slot.Clear();
        ClearTrash();
        OnInventoryChanged?.Invoke();
    }

    public FarmingItemData TrashItem { get; private set; }

    public int TrashCount { get; private set; }

    public bool TakeSlot(int index, out FarmingItemData item, out int count)
    {
        var slot = GetSlot(index);
        item  = slot?.Item;
        count = slot?.Count ?? 0;
        if (slot == null || slot.IsEmpty) return false;
        slot.Clear();
        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool TrashSlot(int index)
    {
        var slot = GetSlot(index);
        if (slot == null || slot.IsEmpty) return false;
        TrashItem  = slot.Item;
        TrashCount = slot.Count;
        slot.Clear();
        OnInventoryChanged?.Invoke();
        return true;
    }

    public bool RestoreTrash(int index)
    {
        var slot = GetSlot(index);
        if (slot == null || TrashItem == null) return false;

        if (slot.IsEmpty)
        {
            slot.Set(TrashItem, TrashCount);
            ClearTrash();
        }
        else if (slot.Item == TrashItem)
        {
            slot.Add(TrashCount);
            ClearTrash();
        }
        else
        {
            var item  = slot.Item;
            int count = slot.Count;
            slot.Set(TrashItem, TrashCount);
            TrashItem  = item;
            TrashCount = count;
        }
        OnInventoryChanged?.Invoke();
        return true;
    }

    private void ClearTrash()
    {
        TrashItem  = null;
        TrashCount = 0;
    }

    public void SetSlotDirect(int index, FarmingItemData item, int count)
    {
        if (index < 0 || index >= slotCount) return;
        slots[index].Set(item, count);
        OnInventoryChanged?.Invoke();
    }

    public void SwapSlots(int a, int b)
    {
        if (a < 0 || a >= slotCount || b < 0 || b >= slotCount || a == b) return;

        var slotA = slots[a];
        var slotB = slots[b];

        var tempItem  = slotA.Item;
        var tempCount = slotA.Count;

        if (slotB.IsEmpty) slotA.Clear();
        else               slotA.Set(slotB.Item, slotB.Count);

        if (tempItem == null) slotB.Clear();
        else                  slotB.Set(tempItem, tempCount);

        OnInventoryChanged?.Invoke();
    }
}
