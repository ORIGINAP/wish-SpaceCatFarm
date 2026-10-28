using System.Collections.Generic;
using UnityEngine;

public static class ItemInfo
{
    private const string UpHex = "#3F8A66";

    private static Dictionary<FarmingItemData, CraftingRecipe> recipeOf;
    private static Dictionary<FarmingItemData, ResourceData>   oreOf;
    private static Dictionary<FarmingItemData, List<string>>   usedIn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        recipeOf = null;
        oreOf    = null;
        usedIn   = null;
    }

    public static string Describe(FarmingItemData item)
    {
        string abilities = Abilities(item);
        string compare   = Comparison(item);
        if (string.IsNullOrEmpty(compare)) return abilities;
        compare = $"<color={UpHex}>{compare}</color>";
        return string.IsNullOrEmpty(abilities) ? compare : compare + "\n" + abilities;
    }

    public static string Abilities(FarmingItemData item)
    {
        if (item == null) return "";
        return item.itemType switch
        {
            FarmingItemType.Tile      => TileAbilities(item),
            FarmingItemType.Tool      => ToolAbilities(item),
            FarmingItemType.Equipment => "장착하면 " + BonusText(item),
            FarmingItemType.Seed      => SeedAbilities(item),
            _                         => ResourceAbilities(item),
        };
    }

    public static string Comparison(FarmingItemData item)
    {
        var prev = PreviousStage(item);
        if (prev == null) return null;

        switch (item.itemType)
        {
            case FarmingItemType.Tool:
                return prev.IsDrill ? $"{prev.itemName}보다 초당 채굴량 {Times(DrillRate(item), DrillRate(prev))}" : null;

            case FarmingItemType.Equipment:
                return $"{prev.itemName} {PercentText(prev)} → {PercentText(item)}";

            case FarmingItemType.Tile:
                var now = PrefabTile(item);
                var old = PrefabTile(prev);
                if (now == null || old == null) return null;
                if (now.WorkRate <= 0f || old.WorkRate <= 0f) return null;
                return now.TileType == TileType.Storage
                    ? $"{prev.itemName}보다 {Times(now.WorkRate, old.WorkRate)} 더 담아요"
                    : $"{prev.itemName}보다 {Times(now.WorkRate, old.WorkRate)} 빨리 일해요";
        }
        return null;
    }

    public static FarmingItemData PreviousStage(FarmingItemData item)
    {
        if (item == null) return null;
        BuildCaches();
        if (!recipeOf.TryGetValue(item, out var recipe) || recipe.ingredients == null) return null;
        foreach (var ing in recipe.ingredients)
            if (ing.item != null && ing.item != item && SameKind(ing.item, item)) return ing.item;
        return null;
    }

    private static string TileAbilities(FarmingItemData item)
    {
        var tile = PrefabTile(item);
        if (tile == null) return "";
        if (tile.AbilityText != null) return tile.AbilityText;
        return tile.TileType == TileType.Decoration ? "기지를 예쁘게 꾸며요" : "";
    }

    private static string ToolAbilities(FarmingItemData item)
    {
        if (!item.IsDrill)
        {
            int n = item.harvestRadius * 2 + 1;
            return $"주변 {n}×{n}칸의 다 자란 작물을 한 번에 수확해요";
        }
        return $"초당 약 {DrillRate(item):0.#}개 캐요 (속도 {item.miningSpeed:0.#} × 채굴력 {item.miningPower:0.#})\n" +
               $"캘 수 있는 광석: {MinableOres(item.toolGrade)}";
    }

    private static string SeedAbilities(FarmingItemData item)
    {
        var seed = item.seedData;
        if (seed == null) return "";
        FarmingItemData crop = null;
        int seeds = 0, bonusSeeds = 0;
        float bonusChance = 0f;
        if (seed.drops != null)
            foreach (var d in seed.drops)
            {
                if (d.item == null) continue;
                if (d.item.isCropProduct && crop == null) crop = d.item;
                else if (d.item == item)
                {
                    if (d.dropChance >= 1f) seeds += d.amount;
                    else { bonusSeeds += d.amount; bonusChance = d.dropChance; }
                }
            }

        string seedText = seeds > 0 ? $"씨앗 {seeds}개" : "씨앗";
        if (bonusSeeds > 0) seedText += $" ({bonusChance * 100f:0}% 확률로 {bonusSeeds}개 더)";
        string power = seed.trait == CropTrait.Shade ? "전기 없이도 자라요" : "전기 필요";
        string s = $"심으면 {seed.TotalGrowthSeconds:0}초 만에 자라요 ({power})";
        if (crop != null) s += $"\n수확하면 {crop.itemName} (판매가 {crop.basePrice}코인) + {seedText}";
        if (seed.trait != CropTrait.None && seed.trait != CropTrait.Shade)
            s += $"\n<color=#6E57B5>성향 [{CropTraits.Name(seed.trait)}]</color> {CropTraits.Describe(seed.trait)}";
        return s;
    }

    private static bool AnyFuelGenerator()
    {
        var em = EnergyManager.Instance;
        if (em == null) return false;
        foreach (var g in em.Generators)
            if (g != null && g.UsesFuel) return true;
        return false;
    }

    private static string ResourceAbilities(FarmingItemData item)
    {
        var lines = new List<string>();
        if (item.isGolden)       lines.Add("<color=#A8741A>황금 작물</color> — 아주 드물게 열려요 (퇴비 · 오로라 때 더 잘 나와요)");
        BuildCaches();
        if (recipeOf.TryGetValue(item, out var made) && made.IsKitchenRecipe)
            lines.Add($"고양이 주방에서 만들어요 (문명 Lv.{Mathf.Max(1, made.requiredCoreLevel)}) — 셔틀 짐으로 인기가 많아요");
        if (item.isCropProduct)  lines.Add($"상점에서 {item.basePrice}코인에 팔려요 (에너지 풍족기간엔 2배)");
        if (item.IsFertilizer)   lines.Add($"퇴비를 들고 {(Pad.Active ? "밭 곁에서 RT를 누르면" : "밭을 클릭하면")} 수확 {item.fertilizerCharges}번 동안 작물이 1개 더 열리고 황금 작물 확률이 2배 (밭 하나에 최대 {PlantTile.MaxFertility}회)");
        if (OreOf(item) is { } ore) lines.Add($"드릴 등급 {ore.minToolGrade} 이상으로 캐요");
        if (item.canUseFuel && AnyFuelGenerator()) lines.Add($"발전소 연료 — 1개에 {item.fuelValue:0}초");

        BuildCaches();
        if (usedIn.TryGetValue(item, out var uses) && uses.Count > 0)
            lines.Add("재료로 쓰여요: " + string.Join(" · ", uses.GetRange(0, Mathf.Min(3, uses.Count)))
                      + (uses.Count > 3 ? $" 외 {uses.Count - 3}개" : ""));
        return string.Join("\n", lines);
    }

    private static float DrillRate(FarmingItemData drill) => drill.miningSpeed * drill.miningPower;

    private static string Times(float now, float old)
    {
        float r = now / old;
        return r >= 10f ? $"{r:0}배" : $"{r:0.#}배";
    }

    private static string BonusText(FarmingItemData item)
    {
        if (item.equipBonuses == null || item.equipBonuses.Length == 0) return "";
        var parts = new List<string>();
        foreach (var b in item.equipBonuses) parts.Add($"{StatName(b.statType)} +{b.bonus * 100f:0}%");
        return string.Join(" · ", parts);
    }

    private static string PercentText(FarmingItemData item)
    {
        if (item.equipBonuses == null || item.equipBonuses.Length == 0) return "";
        var parts = new List<string>();
        foreach (var b in item.equipBonuses) parts.Add($"+{b.bonus * 100f:0}%");
        return string.Join(" · ", parts);
    }

    public static string StatName(TechStatType type) => type switch
    {
        TechStatType.MoveSpeed      => "이동 속도",
        TechStatType.PlacementSpeed => "설치 속도",
        TechStatType.MiningSpeed    => "채굴 속도",
        TechStatType.MiningPower    => "채굴량",
        TechStatType.PickupRange    => "자석 범위",
        TechStatType.DismantleSpeed => "해체 속도",
        _                           => type.ToString(),
    };

    private static string MinableOres(int grade)
    {
        var all = GameDataRegistry.FindAll<ResourceData>();
        int top = 0;
        foreach (var r in all)
            if (r != null && r.minToolGrade <= grade) top = Mathf.Max(top, r.minToolGrade);

        var names = new List<string>();
        foreach (var r in all)
            if (r != null && r.minToolGrade == top) names.Add(r.resourceName);
        names.Sort(string.CompareOrdinal);
        if (names.Count == 0) return "없음";
        return top <= 1 ? string.Join(" · ", names) : string.Join(" · ", names) + "까지";
    }

    private static TileItem PrefabTile(FarmingItemData item)
    {
        if (item == null || item.tilePrefab == null) return null;
        TileItem first = null;
        foreach (var t in item.tilePrefab.GetComponents<TileItem>())
        {
            if (t.AbilityText != null) return t;
            first ??= t;
        }
        return first;
    }

    private static bool SameKind(FarmingItemData a, FarmingItemData b)
    {
        if (a.itemType != b.itemType) return false;
        switch (a.itemType)
        {
            case FarmingItemType.Tool:
                return a.IsDrill && b.IsDrill;
            case FarmingItemType.Equipment:
                return a.equipBonuses is { Length: > 0 } && b.equipBonuses is { Length: > 0 }
                       && a.equipBonuses[0].statType == b.equipBonuses[0].statType;
            case FarmingItemType.Tile:
                var ta = PrefabTile(a);
                var tb = PrefabTile(b);
                return ta != null && tb != null && ta.TileType == tb.TileType;
        }
        return false;
    }

    private static ResourceData OreOf(FarmingItemData item)
    {
        BuildCaches();
        return oreOf.TryGetValue(item, out var r) ? r : null;
    }

    private static void BuildCaches()
    {
        if (recipeOf != null) return;
        recipeOf = new Dictionary<FarmingItemData, CraftingRecipe>();
        usedIn   = new Dictionary<FarmingItemData, List<string>>();
        oreOf    = new Dictionary<FarmingItemData, ResourceData>();

        foreach (var r in GameDataRegistry.FindAll<CraftingRecipe>())
        {
            if (r == null || r.outputItem == null) continue;
            if (!recipeOf.ContainsKey(r.outputItem) || recipeOf[r.outputItem].IsFurnaceRecipe) recipeOf[r.outputItem] = r;
            if (r.ingredients == null) continue;
            foreach (var ing in r.ingredients)
            {
                if (ing.item == null) continue;
                if (!usedIn.TryGetValue(ing.item, out var list)) usedIn[ing.item] = list = new List<string>();
                if (!list.Contains(r.outputItem.itemName)) list.Add(r.outputItem.itemName);
            }
        }
        foreach (var d in GameDataRegistry.FindAll<ResourceData>())
            if (d != null && d.dropItem != null) oreOf[d.dropItem] = d;
    }
}
