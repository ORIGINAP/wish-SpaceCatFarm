using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

// 씨앗만 파는 상점
public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance => Services.Get<ShopManager>();

    [Header("상점에 등록할 아이템 풀")]
    [SerializeField] private FarmingItemData[] itemPool;

    [Header("구매가 배율 (구매가 = basePrice × buyMultiplier)")]
    [SerializeField] private float buyMultiplier = 2f;

    public IReadOnlyList<FarmingItemData> CurrentStock => _stock;
    private readonly List<FarmingItemData> _stock = new();

    public event System.Action OnStockChanged;

    public event System.Action<FarmingItemData, int> OnItemSold;

    public int BuyPrice(FarmingItemData item)  => Mathf.Max(1, Mathf.RoundToInt(item.basePrice * buyMultiplier));

    public int SellPrice(FarmingItemData item)
    {
        float multiplier = item.isCropProduct ? GameEventManager.CropSellMultiplier : 1f;
        return Mathf.RoundToInt(item.basePrice * multiplier);
    }

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }
        LoadStock();
    }

    private void OnDestroy() => Services.Unregister(this);

    private void LoadStock()
    {
        _stock.Clear();
        var seen = new HashSet<FarmingItemData>();
        if (itemPool != null)
            foreach (var item in itemPool)
                if (IsBuyable(item) && seen.Add(item)) _stock.Add(item);
        foreach (var item in GameDataRegistry.FindAll<FarmingItemData>())
            if (IsBuyable(item) && seen.Add(item)) _stock.Add(item);
        OnStockChanged?.Invoke();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        var guids = AssetDatabase.FindAssets("t:FarmingItemData");
        var found = new List<FarmingItemData>();
        foreach (var guid in guids)
        {
            var asset = AssetDatabase.LoadAssetAtPath<FarmingItemData>(AssetDatabase.GUIDToAssetPath(guid));
            if (asset != null && !asset.hideFromShop) found.Add(asset);
        }
        // 개수만 비교하면 교체를 놓침
        if (itemPool == null || itemPool.Length != found.Count
            || !new HashSet<FarmingItemData>(itemPool).SetEquals(found))
        {
            itemPool = found.ToArray();
            EditorUtility.SetDirty(this);
        }
    }
#endif

    private Dictionary<FarmingItemData, List<CraftingRecipe>> _recipesByOutput;

    public bool IsPurchasable(FarmingItemData item) => item != null && LockReason(item) == null;

    public string LockReason(FarmingItemData item)
    {
        if (item == null) return null;
        int coreLevel = RequiredCoreLevel(item);
        if (coreLevel > CenterCore.CurrentLevel) return $"문명 Lv.{coreLevel} 필요";
        var tier = LockedTier(item);
        return tier.HasValue ? $"{CraftingManager.TierDisplayName(tier.Value)} 제작 필요" : null;
    }

    private int RequiredCoreLevel(FarmingItemData item)
    {
        int level = item.unlockCoreLevel;
        var recipes = RecipesFor(item);
        if (recipes != null)
        {
            int lowest = int.MaxValue;
            foreach (var r in recipes) lowest = Mathf.Min(lowest, r.requiredCoreLevel);
            level = Mathf.Max(level, lowest);
        }
        return level;
    }

    private List<CraftingRecipe> RecipesFor(FarmingItemData item)
    {
        var cm = CraftingManager.Instance;
        if (item == null || cm == null) return null;

        if (_recipesByOutput == null)
        {
            _recipesByOutput = new Dictionary<FarmingItemData, List<CraftingRecipe>>();
            foreach (var r in cm.Recipes)
            {
                if (r == null || r.outputItem == null) continue;
                if (!_recipesByOutput.TryGetValue(r.outputItem, out var list))
                {
                    list = new List<CraftingRecipe>();
                    _recipesByOutput.Add(r.outputItem, list);
                }
                list.Add(r);
            }
        }
        return _recipesByOutput.TryGetValue(item, out var recipes) ? recipes : null;
    }

    private CraftingTier? LockedTier(FarmingItemData item)
    {
        var cm = CraftingManager.Instance;
        var recipes = RecipesFor(item);
        if (recipes == null || recipes.Count == 0) return null;

        var lowest = recipes[0].tier;
        foreach (var r in recipes)
        {
            if (cm.IsTierUnlocked(r.tier)) return null;
            if (r.tier < lowest) lowest = r.tier;
        }
        return lowest;
    }

    public static bool IsBuyable(FarmingItemData item)
        => item != null && !item.hideFromShop && item.itemType == FarmingItemType.Seed;

    public static bool IsSellable(FarmingItemData item)
        => item != null && !item.hideFromShop && item.basePrice > 0
        && (item.itemType == FarmingItemType.Seed || item.isCropProduct);

    public bool TryBuy(FarmingItemData item, int amount = 1)
    {
        if (item == null || amount <= 0) return false;
        var inventory = Inventory.Instance;
        var currency  = CurrencyManager.Instance;
        if (inventory == null || currency == null) return false;
        if (!IsBuyable(item) || !IsPurchasable(item)) return false;

        // int 오버플로 방지
        long total = (long)BuyPrice(item) * amount;

        if (!inventory.CanAddItem(item)) return false;
        if (!currency.TrySpend(total)) return false;
        if (inventory.AddItem(item, amount))
        {
            NetOps.Global(NetOpType.ShopBuy, item.name, amount);
            return true;
        }

        currency.Add(total);
        return false;
    }

    public bool TrySell(FarmingItemData item, int amount = 1)
    {
        if (!IsSellable(item) || amount <= 0) return false;
        var inventory = Inventory.Instance;
        var currency  = CurrencyManager.Instance;
        if (inventory == null || currency == null) return false;
        if (!inventory.ConsumeItem(item, amount)) return false;
        currency.Add((long)SellPrice(item) * amount);
        OnItemSold?.Invoke(item, amount);
        NetOps.Global(NetOpType.ShopSell, item.name, amount);
        return true;
    }
}
