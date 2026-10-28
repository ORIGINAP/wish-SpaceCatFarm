public class InventorySlot
{
    public FarmingItemData Item  { get; private set; }
    public int             Count { get; private set; }

    public bool IsEmpty => Item == null || Count <= 0;

    public void Set(FarmingItemData item, int count)
    {
        Item  = item;
        Count = UnityEngine.Mathf.Max(0, count);
    }

    public void Add(int amount)
    {
        Count += amount;
    }

    public void Consume(int amount = 1)
    {
        Count = UnityEngine.Mathf.Max(0, Count - amount);
        if (Count == 0) Item = null;
    }

    public void Clear()
    {
        Item  = null;
        Count = 0;
    }
}
