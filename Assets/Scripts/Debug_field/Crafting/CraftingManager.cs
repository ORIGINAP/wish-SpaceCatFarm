using UnityEngine;
using System.Collections.Generic;

public class CraftingManager : MonoBehaviour
{
    public static CraftingManager Instance => Services.Get<CraftingManager>();

    [Header("등록된 레시피")]
    [SerializeField] private List<CraftingRecipe> recipes = new();

    public IReadOnlyList<CraftingRecipe> Recipes => recipes;

    public event System.Action<FarmingItemData, int> OnItemCrafted;

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }

        recipes.RemoveAll(r => r == null);
        var known = new HashSet<CraftingRecipe>(recipes);
        foreach (var r in GameDataRegistry.FindAll<CraftingRecipe>())
            if (r != null && known.Add(r)) recipes.Add(r);
    }

    private void Start()  => CenterCore.OnAnyLevelUp += HandleCoreLevelUp;
    private void OnDestroy()
    {
        Services.Unregister(this);
        CenterCore.OnAnyLevelUp -= HandleCoreLevelUp;
    }

    private void HandleCoreLevelUp(CenterCore core, int level)
    {
        // 폰트에 이모지 없음
        if (level == IntermediateCoreLevel)
            NotificationUI.Instance?.Show("중급 제작이 열렸어요! (티어2 드릴 · 자동화 설비 · 창고)", NotificationUI.GoodNewsColor);
        else if (level == AdvancedCoreLevel)
            NotificationUI.Instance?.Show("고급 제작이 열렸어요! (티어3 드릴 · 고급 설비 · 강화 장비)", NotificationUI.GoodNewsColor);
    }

    public bool Craft(CraftingRecipe recipe)
    {
        if (recipe == null) return false;
        if (Inventory.Instance == null) return false;
        if (recipe.IsFurnaceRecipe)
        {
            NotificationUI.Instance?.Show("광석은 제련소에 넣으면 잠시 뒤 주괴가 돼요.");
            return false;
        }
        if (recipe.IsKitchenRecipe)
        {
            NotificationUI.Instance?.Show($"{ItemCellUI.Eun(recipe.outputItem.itemName)} 고양이 주방에서 메뉴로 골라 요리해요.");
            return false;
        }
        if (!IsTierUnlocked(recipe.tier)) return false;
        if (!IsCoreLevelReached(recipe))
        {
            NotificationUI.Instance?.Show($"센터코어가 문명 Lv.{recipe.requiredCoreLevel}에 올라야 만들 수 있어요.");
            return false;
        }
        var status = GetStationStatus(recipe.station);
        if (status != StationStatus.Ready)
        {
            NotificationUI.Instance?.Show(status == StationStatus.Unpowered
                ? $"{StationDisplayName(recipe.station)}에 전기가 들어와야 해요 (센터코어 · 전력 중계탑 범위 안으로 옮겨요)."
                : $"{StationDisplayName(recipe.station)} 근처에서만 만들 수 있어요.");
            return false;
        }

        bool success = recipe.Craft(Inventory.Instance);

        if (success)
        {
            OnItemCrafted?.Invoke(recipe.outputItem, recipe.outputAmount);
            if (recipe.station == CraftingStation.Smelter && stationBuffer.Count > 0 && stationBuffer[0] is Smelter smelter)
                smelter.Glow();
        }

        return success;
    }

    public bool CanCraftNow(CraftingRecipe recipe)
        => recipe != null && !recipe.IsMachineRecipe && Inventory.Instance != null && IsRecipeUnlocked(recipe)
           && IsStationAvailable(recipe.station) && recipe.CanCraft(Inventory.Instance);

    public bool IsRecipeUnlocked(CraftingRecipe recipe)
        => recipe != null && IsTierUnlocked(recipe.tier) && IsCoreLevelReached(recipe);

    public static bool IsCoreLevelReached(CraftingRecipe recipe)
        => recipe == null || recipe.requiredCoreLevel <= CenterCore.CurrentLevel;

    public List<CraftingRecipe> GetCraftableRecipes()
    {
        var result = new List<CraftingRecipe>();
        if (Inventory.Instance == null) return result;

        foreach (var r in recipes)
            if (CanCraftNow(r)) result.Add(r);

        return result;
    }

    public const int StationRangeCells = 5;

    private readonly List<TileItem> stationBuffer = new();

    public enum StationStatus { Ready, Missing, Unpowered }

    public bool IsStationAvailable(CraftingStation station) => GetStationStatus(station) == StationStatus.Ready;

    public StationStatus GetStationStatus(CraftingStation station)
    {
        if (station == CraftingStation.None) return StationStatus.Ready;
        var player = PlayerTileController.Instance;
        var grid   = GridManager.Instance;
        if (player == null || grid == null) return StationStatus.Missing;

        // 설비는 아직 제련소뿐
        grid.GetTilesInRadius(grid.WorldToGrid(player.transform.position), StationRangeCells, TileType.Smelter, stationBuffer);
        if (stationBuffer.Count == 0) return StationStatus.Missing;

        for (int i = 0; i < stationBuffer.Count; i++)
        {
            if (stationBuffer[i] is not Smelter s || !s.IsPowered) continue;
            (stationBuffer[0], stationBuffer[i]) = (stationBuffer[i], stationBuffer[0]);
            return StationStatus.Ready;
        }
        return StationStatus.Unpowered;
    }

    public static string StationDisplayName(CraftingStation station) => station switch
    {
        CraftingStation.Smelter => "제련소",
        CraftingStation.Kitchen => "고양이 주방",
        _ => "",
    };

    public void NotifyMachineCrafted(FarmingItemData item, int amount)
    {
        if (item != null && amount > 0) OnItemCrafted?.Invoke(item, amount);
    }

    public const int IntermediateCoreLevel = 2, AdvancedCoreLevel = 4;

    public bool IsTierUnlocked(CraftingTier tier) => CenterCore.CurrentLevel >= TierCoreLevel(tier);

    public static int TierCoreLevel(CraftingTier tier) => tier switch
    {
        CraftingTier.Intermediate => IntermediateCoreLevel,
        CraftingTier.Advanced     => AdvancedCoreLevel,
        _                         => 1,
    };

    public static string TierDisplayName(CraftingTier tier) => tier switch
    {
        CraftingTier.Beginner     => "초급",
        CraftingTier.Intermediate => "중급",
        CraftingTier.Advanced     => "고급",
        _ => tier.ToString()
    };
}
