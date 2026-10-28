using UnityEngine;
using System.Collections.Generic;

public interface IItemBox
{
    int  Capacity    { get; }
    int  TotalCount  { get; }
    bool IsUnlimited { get; }
    bool HasSpace    { get; }
    event System.Action OnStockChanged;
    IReadOnlyCollection<FarmingItemData> StockedItems { get; }
    int  GetCount(FarmingItemData item);
    bool Accepts(FarmingItemData item);
    int  AddItem(FarmingItemData item, int amount);
    int  RemoveItem(FarmingItemData item, int amount);
}

public class ItemStorage : TileItem, IItemBox
{
    [Header("창고 설정")]
    [Tooltip("총 보관 가능 개수")]
    [SerializeField, Min(0)] private int capacity = 200;
    [Tooltip("허용할 아이템 타입")]
    [SerializeField] private FarmingItemType[] acceptedTypes;

    [Header("실시간 상태")]
    [SerializeField] private int totalStored;

    private readonly Dictionary<FarmingItemData, int> stock = new();

    public int Capacity   => capacity;
    public int TotalCount => totalStored;

    public bool IsUnlimited => capacity <= 0;

    public bool HasSpace => IsUnlimited || totalStored < capacity;

    public override string AbilityText => (IsUnlimited ? "무엇이든 끝없이 담아요" : $"아이템을 {capacity}개까지 담아요") + AcceptedText;
    public override float  WorkRate    => IsUnlimited ? 0f : capacity;

    private string AcceptedText
    {
        get
        {
            if (acceptedTypes == null || acceptedTypes.Length == 0) return "";
            var names = new List<string>();
            foreach (var t in acceptedTypes)
                names.Add(t switch
                {
                    FarmingItemType.Resource  => "자원",
                    FarmingItemType.Seed      => "씨앗",
                    FarmingItemType.Tile      => "설치물",
                    FarmingItemType.Tool      => "도구",
                    FarmingItemType.Equipment => "장비",
                    _                         => t.ToString(),
                });
            return $" ({string.Join(" · ", names)}만)";
        }
    }

    public event System.Action OnStockChanged;

    public IReadOnlyCollection<FarmingItemData> StockedItems => stock.Keys;

    public bool Accepts(FarmingItemData item)
    {
        if (item == null) return false;
        if (acceptedTypes == null || acceptedTypes.Length == 0) return true;
        return System.Array.IndexOf(acceptedTypes, item.itemType) >= 0;
    }

    public int AddItem(FarmingItemData item, int amount)
    {
        if (!Accepts(item) || amount <= 0) return 0;

        int space = IsUnlimited ? int.MaxValue - totalStored : capacity - totalStored;
        int added = Mathf.Min(space, amount);
        if (added <= 0) return 0;

        stock.TryGetValue(item, out int cur);
        stock[item]  = cur + added;
        totalStored += added;
        OnStockChanged?.Invoke();
        NetOps.Tile(NetOpType.StorageAdd, this, item, added);
        return added;
    }

    public int RemoveItem(FarmingItemData item, int amount)
    {
        if (item == null || amount <= 0) return 0;
        if (!stock.TryGetValue(item, out int cur) || cur <= 0) return 0;

        int removed = Mathf.Min(cur, amount);
        cur -= removed;
        if (cur <= 0) stock.Remove(item);
        else stock[item] = cur;

        totalStored -= removed;
        OnStockChanged?.Invoke();
        NetOps.Tile(NetOpType.StorageRemove, this, item, removed);
        return removed;
    }

    public int GetCount(FarmingItemData item) => stock.TryGetValue(item, out int c) ? c : 0;

    protected internal override IEnumerable<(FarmingItemData item, int amount)> GetAttachedDrops()
    {
        foreach (var pair in new List<KeyValuePair<FarmingItemData, int>>(stock))
            if (pair.Key != null && pair.Value > 0)
                yield return (pair.Key, pair.Value);
    }
}
