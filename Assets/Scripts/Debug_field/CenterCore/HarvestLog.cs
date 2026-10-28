using System.Collections.Generic;
using UnityEngine;

public static class HarvestLog
{
    private static readonly HashSet<string> crops = new();

    public static int Count => crops.Count;

    public static event System.Action OnChanged;

    public static bool Contains(FarmingItemData product) => product != null && crops.Contains(product.itemName);

    public static List<string> ToList() => new(crops);

    public static void Clear()
    {
        crops.Clear();
        OnChanged?.Invoke();
    }

    public static void Restore(IEnumerable<string> names)
    {
        crops.Clear();
        if (names != null)
            foreach (var n in names)
                if (!string.IsNullOrEmpty(n)) crops.Add(n);
        OnChanged?.Invoke();
    }

    private static void Record(PlantTile plant, HarvestResult[] results)
    {
        if (results == null) return;
        bool added = false;
        foreach (var r in results)
            if (r?.item != null && r.item.isCropProduct && !r.item.isGolden && crops.Add(r.item.itemName)) added = true;
        if (added) OnChanged?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        crops.Clear();
        OnChanged = null;
    }

    // static 이벤트라 한 번만 구독
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Hook()
    {
        PlantTile.OnAnyPlantHarvested -= Record;
        PlantTile.OnAnyPlantHarvested += Record;
    }
}
