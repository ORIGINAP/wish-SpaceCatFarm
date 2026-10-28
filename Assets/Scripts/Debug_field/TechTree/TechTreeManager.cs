using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

public enum TechStatType
{
    MoveSpeed,
    PlacementSpeed,
    MiningSpeed,
    MiningPower,
    PickupRange,
    DismantleSpeed,
    CraftIntermediateUnlock,
    CraftAdvancedUnlock,
    CropGrowth,
    OrderReward,
    ExtraYield,
    MachineSpeed,
    ProcessSpeed,
    ShuttleSpeed,
}

public enum TechCategory { Farming, Automation, Shuttle, Pioneer }

public class TechNodeData
{
    public string       id;
    public string       displayName;
    public string       description;
    public TechCategory category;
    public string       iconItem;
    public TechStatType statType;
    public float        statPerLevel;
    public int          maxLevel;
    public int[]        coinCosts;
    public int[]        coreLevels;
    public string[]     itemCosts;
    public int[]        itemAmounts;

    [System.NonSerialized] public int currentLevel;

    public bool IsMaxed => currentLevel >= maxLevel;

    public int NextCoreLevel => IsMaxed ? 0 : coreLevels[currentLevel];

    public int FirstCoreLevel => coreLevels.Length > 0 ? coreLevels[0] : 1;

    public float EffectAt(int level) => statPerLevel * level;
}

// 연구 11갈래, 네 분야
public class TechTreeManager : MonoBehaviour
{
    public static TechTreeManager Instance => Services.Get<TechTreeManager>();

    public readonly TechNodeData[] Nodes = BuildNodes();

    private static TechNodeData[] BuildNodes()
    {
        var list = new List<TechNodeData>
        {
            Track("crop_growth", "작물 성장", TechCategory.Farming, "BlueStar",
                  "모든 작물이 단계마다 10% 더 빨리 자라요", TechStatType.CropGrowth, 0.10f,
                  new[] { 2, 3, 4, 5 }, new[] { 200, 700, 2000, 6000 },
                  new[] { "BlueStar", "ChuruCorn", "CrystalBerry", "GalaxyMelon" }, new[] { 20, 20, 15, 10 }),
            Track("bountiful", "풍성한 수확", TechCategory.Farming, "Item_Golden_BlueStar",
                  "수확할 때 작물이 하나 더 열릴 확률이 단계마다 10%씩 올라요", TechStatType.ExtraYield, 0.10f,
                  new[] { 3, 4, 5 }, new[] { 600, 2500, 7000 },
                  new[] { "Catnip", "MilkShroom", "SunFlower" }, new[] { 30, 25, 12 }),

            Track("machine_speed", "기계 속도", TechCategory.Automation, "AutoHarvester",
                  "자동파종기 · 자동수확기 · 자동채굴기가 단계마다 15% 더 빨리 일해요", TechStatType.MachineSpeed, 0.15f,
                  new[] { 3, 4, 5 }, new[] { 800, 2500, 8000 },
                  new[] { "Item_CircuitBoard", "Item_LightAlloy", "Item_EnergyCell" }, new[] { 4, 6, 6 }),
            Track("process_speed", "가공 속도", TechCategory.Automation, "Item_Smelter",
                  "제련소 · 고양이 주방 · 퇴비통이 단계마다 15% 더 빨리 일해요", TechStatType.ProcessSpeed, 0.15f,
                  new[] { 2, 3, 5 }, new[] { 400, 1500, 6000 },
                  new[] { "Item_IronIngot", "item_Alloy", "Item_TitaniumAlloy" }, new[] { 10, 8, 4 }),

            Track("order_bonus", "짐값 협상", TechCategory.Shuttle, "",
                  "셔틀에 짐을 실어 보낼 때 받는 짐값이 단계마다 10% 늘어요", TechStatType.OrderReward, 0.10f,
                  new[] { 2, 3, 4, 5 }, new[] { 300, 1000, 3000, 8000 },
                  new[] { "Item_StarJam", "Item_Churu", "Item_BerryTart", "Item_MelonJuice" }, new[] { 3, 6, 2, 6 }),
            Track("shuttle_speed", "빠른 셔틀", TechCategory.Shuttle, "",
                  "셔틀이 고향별에 다녀오는 시간이 단계마다 10%씩 짧아져요", TechStatType.ShuttleSpeed, 0.10f,
                  new[] { 3, 4, 6 }, new[] { 1000, 3000, 10000 },
                  new[] { "Item_CopperIngot", "Item_LightAlloy", "Item_ReinforcedAlloy" }, new[] { 20, 8, 6 }),

            Track("move_speed", "이동 속도", TechCategory.Pioneer, "Item_JellyBoots",
                  "대원이 단계마다 15% 더 빨리 걸어요", TechStatType.MoveSpeed, 0.15f,
                  new[] { 1, 1, 2, 3, 4 }, new[] { 50, 150, 500, 1500, 4000 },
                  new[] { "", "", "Item_CopperIngot", "item_Alloy", "Item_LightAlloy" }, new[] { 0, 0, 6, 4, 3 }),
            Track("place_speed", "설치 속도", TechCategory.Pioneer, "Item_ToolBelt",
                  "밭 · 설비 · 씨앗을 단계마다 20% 더 빨리 놓아요 (누른 채 끌 때)", TechStatType.PlacementSpeed, 0.20f,
                  new[] { 1, 1, 2, 3, 4 }, new[] { 50, 150, 500, 1500, 4000 },
                  new[] { "", "", "Item_IronIngot", "item_Alloy", "Item_LightAlloy" }, new[] { 0, 0, 6, 4, 3 }),
            Track("mining_speed", "채굴 속도", TechCategory.Pioneer, "Item_Tier1_Drill",
                  "광석을 단계마다 20% 더 빨리 캐요", TechStatType.MiningSpeed, 0.20f,
                  new[] { 1, 1, 2, 3, 4 }, new[] { 80, 250, 800, 2000, 5000 },
                  new[] { "", "", "Item_CopperIngot", "item_Alloy", "Item_TitaniumIngot" }, new[] { 0, 0, 8, 5, 4 }),
            Track("dismantle_speed", "해체 속도", TechCategory.Pioneer, "Item_PowerGloves",
                  "설치물을 단계마다 25% 더 빨리 해체해요", TechStatType.DismantleSpeed, 0.25f,
                  new[] { 1, 2, 2, 3, 4 }, new[] { 100, 300, 1000, 2500, 6000 },
                  new[] { "", "", "Item_IronIngot", "item_Alloy", "Item_AluminumIngot" }, new[] { 0, 0, 6, 4, 6 }),
            Track("pickup_range", "자석 범위", TechCategory.Pioneer, "Item_MagnetBell",
                  "떨어진 아이템을 끌어오는 범위가 단계마다 40% 넓어져요", TechStatType.PickupRange, 0.40f,
                  new[] { 1, 2, 3 }, new[] { 100, 600, 2500 },
                  new[] { "", "Item_IronIngot", "item_Alloy" }, new[] { 0, 5, 4 }),
        };
        return list.ToArray();
    }

    private static TechNodeData Track(string id, string name, TechCategory category, string icon, string desc,
        TechStatType stat, float perLevel, int[] coreLevels, int[] coins, string[] items, int[] amounts)
        => new()
        {
            id = id, displayName = name, category = category, iconItem = icon, description = desc,
            statType = stat, statPerLevel = perLevel, maxLevel = coins.Length,
            coreLevels = coreLevels, coinCosts = coins, itemCosts = items, itemAmounts = amounts,
        };

    public static string CategoryName(TechCategory c) => c switch
    {
        TechCategory.Farming    => "농사",
        TechCategory.Automation => "자동화",
        TechCategory.Shuttle    => "셔틀",
        _                       => "대원",
    };

    public event System.Action OnTechChanged;

    public event System.Action<TechNodeData> OnNodeUpgraded;

    public float MoveSpeedMult      => 1f + GetTotalStat(TechStatType.MoveSpeed);

    public float PlacementSpeedMult => 1f + GetTotalStat(TechStatType.PlacementSpeed);

    public float MiningSpeedMult    => 1f + GetTotalStat(TechStatType.MiningSpeed);

    public float PickupRangeMult    => 1f + GetTotalStat(TechStatType.PickupRange);

    public float DismantleSpeedMult => 1f + GetTotalStat(TechStatType.DismantleSpeed);

    public float CropGrowthMult     => 1f + GetTotalStat(TechStatType.CropGrowth);

    public float OrderRewardMult    => 1f + GetTotalStat(TechStatType.OrderReward);

    public static float ExtraYieldChance => Instance != null ? Instance.GetTotalStat(TechStatType.ExtraYield) : 0f;

    public static float MachineSpeed => 1f + (Instance != null ? Instance.GetTotalStat(TechStatType.MachineSpeed) : 0f);

    public static float ProcessSpeed => 1f + (Instance != null ? Instance.GetTotalStat(TechStatType.ProcessSpeed) : 0f);

    public static float ShuttleSpeed => 1f + (Instance != null ? Instance.GetTotalStat(TechStatType.ShuttleSpeed) : 0f);

    // 기초 연구소 있으면 10% 할인
    public const float LabDiscount = 0.1f;

    public int CostOf(TechNodeData node)
    {
        if (node == null || node.IsMaxed) return 0;
        int cost = node.coinCosts[node.currentLevel];
        if (CivFacility.CountPowered(CivEffect.Research) > 0) cost = Mathf.Max(1, Mathf.RoundToInt(cost * (1f - LabDiscount)));
        return cost;
    }

    public static (FarmingItemData item, int amount) ItemCostOf(TechNodeData node)
    {
        if (node == null || node.IsMaxed || node.itemCosts == null || node.currentLevel >= node.itemCosts.Length) return (null, 0);
        string name = node.itemCosts[node.currentLevel];
        int amount  = node.itemAmounts != null && node.currentLevel < node.itemAmounts.Length ? node.itemAmounts[node.currentLevel] : 0;
        if (string.IsNullOrEmpty(name) || amount <= 0) return (null, 0);
        var item = NetWorld.Item(name);
        return item != null ? (item, amount) : (null, 0);
    }

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }

        foreach (var node in Nodes)
            node.currentLevel = 0;
    }

    private void OnDestroy()
    {
        Services.Unregister(this);
    }

    public int GetLevel(string nodeId)
    {
        foreach (var n in Nodes)
            if (n.id == nodeId) return n.currentLevel;
        return 0;
    }

    public TechNodeData GetNode(string nodeId)
    {
        foreach (var n in Nodes)
            if (n.id == nodeId) return n;
        return null;
    }

    public bool ArePrerequisitesMet(TechNodeData node)
        => node != null && !node.IsMaxed && node.NextCoreLevel <= CenterCore.CurrentLevel;

    public static bool HasItems(TechNodeData node)
    {
        var (item, amount) = ItemCostOf(node);
        return item == null || (Inventory.Instance != null && Inventory.Instance.GetCount(item) >= amount);
    }

    public bool CanUpgrade(TechNodeData node)
    {
        if (!ArePrerequisitesMet(node)) return false;
        return CurrencyManager.Instance != null && CurrencyManager.Instance.CanAfford(CostOf(node)) && HasItems(node);
    }

    public bool TryUpgrade(TechNodeData node)
    {
        if (!CanUpgrade(node)) return false;

        var (item, amount) = ItemCostOf(node);
        if (item != null && !Inventory.Instance.ConsumeItem(item, amount)) return false;
        if (!CurrencyManager.Instance.TrySpend(CostOf(node)))
        {
            if (item != null) Inventory.Instance.AddItem(item, amount, countAsNew: false);
            return false;
        }

        node.currentLevel++;
        OnTechChanged?.Invoke();
        OnNodeUpgraded?.Invoke(node);
        NetOps.Global(NetOpType.Research, node.id, 1);
        return true;
    }

    public bool ApplyRemoteUpgrade(TechNodeData node)
    {
        if (!ArePrerequisitesMet(node)) return false;
        if (CurrencyManager.Instance == null || !CurrencyManager.Instance.TrySpend(CostOf(node))) return false;
        node.currentLevel++;
        OnTechChanged?.Invoke();
        OnNodeUpgraded?.Invoke(node);
        return true;
    }

    private float GetTotalStat(TechStatType type)
    {
        float total = 0f;
        foreach (var node in Nodes)
            if (node.statType == type)
                total += node.statPerLevel * node.currentLevel;
        return total;
    }

    // 구버전 사슬 노드 ID 변환
    private static readonly Regex ChainStepId = new(@"^(.+)_(\d+)$");

    private void ApplySavedLevel(string nodeId, int level)
    {
        if (string.IsNullOrEmpty(nodeId) || level <= 0) return;
        var node = GetNode(nodeId);
        if (node == null)
        {
            var m = ChainStepId.Match(nodeId);
            if (!m.Success) return;
            node = GetNode(m.Groups[1].Value);
            if (node == null) return;
            level = int.Parse(m.Groups[2].Value);
        }
        node.currentLevel = Mathf.Max(node.currentLevel, Mathf.Min(level, node.maxLevel));
    }

    public List<TechNodeSave> GetSaveData()
    {
        var list = new List<TechNodeSave>();
        foreach (var node in Nodes)
            list.Add(new TechNodeSave { id = node.id, level = node.currentLevel });
        return list;
    }

    public void LoadSaveData(List<TechNodeSave> data)
    {
        if (data == null) return;
        foreach (var node in Nodes)
            node.currentLevel = 0;
        foreach (var save in data)
            ApplySavedLevel(save.id, save.level);
        OnTechChanged?.Invoke();
    }

    public void ResetAllNodes()
    {
        foreach (var node in Nodes)
            node.currentLevel = 0;
        OnTechChanged?.Invoke();
    }

    public void CompleteAllNodes()
    {
        var upgraded = new List<TechNodeData>();
        foreach (var node in Nodes)
        {
            if (node.IsMaxed) continue;
            node.currentLevel = node.maxLevel;
            upgraded.Add(node);
        }
        OnTechChanged?.Invoke();
        foreach (var node in upgraded)
            OnNodeUpgraded?.Invoke(node);
    }

#if UNITY_EDITOR
    [ContextMenu("연구 초기화 (디버그)")]
    private void DebugReset() => ResetAllNodes();
#endif
}
