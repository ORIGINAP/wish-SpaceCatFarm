using UnityEngine;

public class EquipmentManager : MonoBehaviour
{
    public static EquipmentManager Instance => Services.Get<EquipmentManager>();

    public const int SlotCount = 5;
    private readonly FarmingItemData[] _slots = new FarmingItemData[SlotCount];

    public event System.Action OnEquipmentChanged;

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }
    }

    private void OnDestroy() => Services.Unregister(this);

    public static EquipmentManager GetOrCreate()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("EquipmentManager");
        return go.AddComponent<EquipmentManager>();
    }

    public FarmingItemData GetSlot(int i)
        => (i >= 0 && i < SlotCount) ? _slots[i] : null;

    public bool TryEquip(int slotIndex, FarmingItemData item)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) return false;
        if (item != null && item.itemType != FarmingItemType.Equipment) return false;
        _slots[slotIndex] = item;
        OnEquipmentChanged?.Invoke();
        return true;
    }

    public FarmingItemData Unequip(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SlotCount) return null;
        var prev = _slots[slotIndex];
        _slots[slotIndex] = null;
        if (prev != null) OnEquipmentChanged?.Invoke();
        return prev;
    }

    public float GetBonus(TechStatType type)
    {
        float best = 0f;
        foreach (var item in _slots)
        {
            if (item?.equipBonuses == null) continue;
            foreach (var b in item.equipBonuses)
                if (b.statType == type && b.bonus > best) best = b.bonus;
        }
        return best;
    }
}
