using System.Collections.Generic;
using UnityEngine;

public readonly struct GrowthFactor
{
    public readonly string label;
    public readonly float  mult;
    public GrowthFactor(string label, float mult) { this.label = label; this.mult = mult; }
}

public static class FarmConditions
{
    public const float WaterMult      = 1.25f;
    public const float WaterLoverMult = 2f;
    public const float GreenhouseMult = 1.2f;
    public const float NightOwlMult   = 2f;
    public const float SunLoverMult   = 1.6f;
    public const float StarlightMult  = 1.6f;
    public const float RainLoverMult  = 2.5f;
    public const float StormLoverMult = 3f;
    public const float CatLoverMult   = 2.5f;
    public const int   CatRadiusCells = 4;

    public const float StarFriendChance = 0.4f;
    public const float ClusterChance    = 0.2f;
    public const float VarietyChance    = 0.3f;

    public const float BaseGoldenChance    = 0.015f;
    public const float FertileGoldenMult   = 2f;
    public const float AuroraGoldenMult    = 2f;
    public const float StarlightAuroraMult = 5f;

    private static readonly Vector2Int[] Neighbors =
    {
        new(-1, -1), new(0, -1), new(1, -1),
        new(-1,  0),             new(1,  0),
        new(-1,  1), new(0,  1), new(1,  1),
    };

    private static readonly List<SeedData> kinds = new();

    public static bool IsNight
    {
        get
        {
            var t = GameTimeManager.Instance;
            if (t == null) return false;
            int h = t.CurrentHour;
            return h >= 18 || h < 6;
        }
    }

    public static bool IsWatered(Vector2Int cell) => WeatherManager.IsRaining || CivFacility.Covers(CivEffect.Water, cell);

    public static bool InGreenhouse(Vector2Int cell) => CivFacility.Covers(CivEffect.Greenhouse, cell);

    public static float GrowthMultiplier(PlantTile plot, List<GrowthFactor> factors = null)
    {
        factors?.Clear();
        if (plot == null) return 1f;
        var cell  = plot.GridPosition;
        var trait = plot.CurrentSeed != null ? plot.CurrentSeed.trait : CropTrait.None;
        float m = 1f;

        void Add(string label, float mult)
        {
            if (Mathf.Approximately(mult, 1f)) return;
            m *= mult;
            factors?.Add(new GrowthFactor(label, mult));
        }

        if (IsWatered(cell))
            Add(WeatherManager.IsRaining ? "별비" : "급수탑 물", trait == CropTrait.WaterLover ? WaterLoverMult : WaterMult);
        if (InGreenhouse(cell)) Add("온실", GreenhouseMult);
        Add("성장 촉진기", GrowthBooster.MultiplierAt(cell));

        bool night = IsNight;
        switch (trait)
        {
            case CropTrait.NightOwl:   if (night)  Add("밤 작물 (밤)", NightOwlMult);  break;
            case CropTrait.SunLover:   if (!night) Add("해 바라기 (낮)", SunLoverMult); break;
            case CropTrait.Starlight:  if (night)  Add("별빛 (밤)", StarlightMult);    break;
            case CropTrait.RainLover:  if (WeatherManager.IsRaining) Add("비 좋아함 (별비)", RainLoverMult); break;
            case CropTrait.StormLover: if (WeatherManager.Current == WeatherType.Storm) Add("폭풍 작물 (폭풍)", StormLoverMult); break;
            case CropTrait.CatLover:
                float cs = GridManager.Instance != null ? GridManager.Instance.CellSize : 0.75f;
                if (CatPresence.AnyWithin(plot.transform.position, CatRadiusCells * cs)) Add("고양이 곁", CatLoverMult);
                break;
        }

        float tech = TechTreeManager.Instance != null ? TechTreeManager.Instance.CropGrowthMult : 1f;
        Add("성장 연구", tech);
        return m;
    }

    public static float TraitBonusChance(PlantTile plot)
    {
        var seed = plot != null ? plot.CurrentSeed : null;
        if (seed == null) return 0f;
        switch (seed.trait)
        {
            case CropTrait.StarFriends: return CountKinds(plot, s => s != seed && s.trait == CropTrait.StarFriends) * StarFriendChance;
            case CropTrait.Cluster:     return CountSame(plot) * ClusterChance;
            case CropTrait.Variety:     return CountKinds(plot, s => s != seed) * VarietyChance;
            default:                    return 0f;
        }
    }

    public static int RollExtraCrops(PlantTile plot, bool fertile)
    {
        var seed = plot != null ? plot.CurrentSeed : null;
        if (seed == null) return 0;
        int extra = fertile ? (seed.trait == CropTrait.Hearty ? 2 : 1) : 0;
        float chance = TraitBonusChance(plot) + TechTreeManager.ExtraYieldChance;
        // 정수부는 확정, 나머지는 확률
        extra += Mathf.FloorToInt(chance);
        if (Random.value < chance - Mathf.Floor(chance)) extra++;
        return extra;
    }

    public static float GoldenChance(PlantTile plot, bool fertile)
    {
        var seed = plot != null ? plot.CurrentSeed : null;
        float c = BaseGoldenChance;
        if (fertile) c *= FertileGoldenMult;
        if (WeatherManager.Current == WeatherType.Aurora)
            c *= seed != null && seed.trait == CropTrait.Starlight ? StarlightAuroraMult : AuroraGoldenMult;
        return Mathf.Clamp01(c);
    }

    private static int CountSame(PlantTile plot)
    {
        var gm = GridManager.Instance;
        if (gm == null) return 0;
        int n = 0;
        foreach (var d in Neighbors)
            if (gm.GetTile(plot.GridPosition + d) is PlantTile p && p != plot && p.CurrentSeed == plot.CurrentSeed) n++;
        return n;
    }

    private static int CountKinds(PlantTile plot, System.Predicate<SeedData> match)
    {
        var gm = GridManager.Instance;
        if (gm == null) return 0;
        kinds.Clear();
        foreach (var d in Neighbors)
            if (gm.GetTile(plot.GridPosition + d) is PlantTile p && p != plot && p.CurrentSeed != null
                && match(p.CurrentSeed) && !kinds.Contains(p.CurrentSeed))
                kinds.Add(p.CurrentSeed);
        return kinds.Count;
    }
}
