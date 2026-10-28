using System.Collections.Generic;
using UnityEngine;

public static class CoreProgress
{
    private static readonly Dictionary<FarmingItemData, int> placed  = new();
    private static readonly Dictionary<FarmingItemData, int> powered = new();
    private static GridManager countedGrid;
    private static int         countedVersion = -1;
    private static float       countedTime    = -10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        placed.Clear();
        powered.Clear();
        countedGrid    = null;
        countedVersion = -1;
        countedTime    = -10f;
    }

    public static int Current(CoreRequirement r)
    {
        if (r == null) return 0;
        switch (r.type)
        {
            case CoreRequirementType.Facility:      return CountFacility(r.item, r.needsPower);
            case CoreRequirementType.CropVarieties: return HarvestLog.Count;
            case CoreRequirementType.Research:      return (TechTreeManager.Instance?.GetLevel(r.techNodeId) ?? 0) >= 1 ? 1 : 0;
            case CoreRequirementType.Coins:         return (int)System.Math.Min(CurrencyManager.Instance?.Currency ?? 0L, int.MaxValue);
            case CoreRequirementType.Orders:        return ShuttleService.Trips;
            case CoreRequirementType.Residents:     return ShuttleService.Population;
            default:                                return 0;
        }
    }

    public static int Target(CoreRequirement r)
        => r == null ? 1 : r.type == CoreRequirementType.Research ? 1 : Mathf.Max(1, r.amount);

    public static bool IsMet(CoreRequirement r) => r != null && Current(r) >= Target(r);

    public static bool AllMet(CoreStage stage)
    {
        if (stage == null || stage.requirements == null || stage.requirements.Count == 0) return false;
        foreach (var r in stage.requirements)
            if (!IsMet(r)) return false;
        return true;
    }

    public static long Funds(CoreStage stage)
    {
        long sum = 0;
        if (stage?.requirements != null)
            foreach (var r in stage.requirements)
                if (r != null && r.type == CoreRequirementType.Coins) sum += Target(r);
        return sum;
    }

    public static int MetCount(CoreStage stage)
    {
        int n = 0;
        if (stage?.requirements != null)
            foreach (var r in stage.requirements)
                if (IsMet(r)) n++;
        return n;
    }

    public static string Label(CoreRequirement r)
    {
        if (r == null) return "?";
        switch (r.type)
        {
            case CoreRequirementType.Facility:
                string name = r.item != null ? r.item.itemName : "?";
                return r.needsPower ? $"{name} (전기 연결)" : name;
            case CoreRequirementType.CropVarieties:
                return $"작물 {Target(r)}종 수확";
            case CoreRequirementType.Research:
                return $"연구: {TechTreeManager.Instance?.GetNode(r.techNodeId)?.displayName ?? r.techNodeId}";
            case CoreRequirementType.Coins:
                return "발전 자금 (오를 때 내요)";
            case CoreRequirementType.Orders:
                return "셔틀 왕복";
            case CoreRequirementType.Residents:
                return "주민 (셔틀로 데려오기)";
            default:
                return "?";
        }
    }

    public static string ProgressText(CoreRequirement r)
    {
        if (r != null && r.type == CoreRequirementType.Research) return IsMet(r) ? "완료" : "0/1";
        if (r != null && r.type == CoreRequirementType.Coins)
            return $"{CurrencyManager.Format(Mathf.Min(Current(r), Target(r)))}/{CurrencyManager.Format(Target(r))}";
        return $"{Mathf.Min(Current(r), Target(r))}/{Target(r)}";
    }

    private static int CountFacility(FarmingItemData item, bool needsPower)
    {
        if (item == null) return 0;
        Recount();
        return (needsPower ? powered : placed).TryGetValue(item, out int n) ? n : 0;
    }

    private static void Recount()
    {
        var gm = GridManager.Instance;
        if (gm == null) { placed.Clear(); powered.Clear(); countedGrid = null; return; }
        // 전기 상태 때문에 1초마다 다시 셈
        if (gm == countedGrid && gm.Version == countedVersion && Time.unscaledTime - countedTime < 1f) return;

        countedGrid    = gm;
        countedVersion = gm.Version;
        countedTime    = Time.unscaledTime;
        placed.Clear();
        powered.Clear();
        foreach (var tile in gm.GetDynamicTiles())
        {
            if (tile == null || tile.IsGhost) continue;
            var item = tile.SourceItem != null ? tile.SourceItem : tile.DropItemData;
            if (item == null) continue;
            placed[item] = (placed.TryGetValue(item, out int a) ? a : 0) + 1;
            if (IsPowered(tile)) powered[item] = (powered.TryGetValue(item, out int b) ? b : 0) + 1;
        }
    }

    private static bool IsPowered(TileItem tile)
    {
        if (tile is CivFacility facility) return facility.IsPowered;
        if (tile is PowerRelay relay)     return relay.IsEnergized;
        if (tile is PlantTile plot)       return plot.IsPowered;
        var link = tile.GetComponent<PowerLink>();
        return link != null && link.IsPowered;
    }
}
