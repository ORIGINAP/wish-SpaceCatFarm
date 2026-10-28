using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class AutoSeeder : TileItem, ICoverageArea, IItemBox
{
    [Header("파종기 설정")]
    [SerializeField] private float plantInterval  = 5f;
    [SerializeField] private float searchRadius   = 3f;
    [SerializeField] private int   targetsPerCycle = 1;
    [Tooltip("씨앗통에 넣을 수 있는 씨앗 수 (모든 종류 합산)")]
    [SerializeField, Min(1)] private int seedCapacity = 50;

    [Header("작업 범위 표시 (마우스 호버 시)")]
    [SerializeField] private Color coverageColor = new Color(0.35f, 0.90f, 0.40f, 0.12f);

    [Header("실시간 상태")]
    [SerializeField] private int             storedSeeds;
    [SerializeField] private FarmingItemData lastSeedItem;
    [SerializeField] private float           nextPlantAt;

    public FarmingItemData LastSeedItem => lastSeedItem;

    public int        CoverageRadiusCells => SearchRadiusInCells();
    public Color      CoverageColor       => coverageColor;

    public override string AbilityText =>
        $"{Interval:0.#}초마다 빈 밭 {targetsPerCycle}칸에 씨앗을 심어요 (범위 {CoverageRadiusCells}칸)\n씨앗통 {seedCapacity}개 · 비면 이어 준 창고에서 꺼내 써요";

    private float Interval => plantInterval / TechTreeManager.MachineSpeed;
    public override float WorkRate => plantInterval > 0f ? targetsPerCycle / plantInterval : 0f;

    private Coroutine plantCoroutine;
    private FlashBeam flashBeam;

    private readonly NearbyTiles nearbyPlants = new(TileType.Plant);

    private readonly List<FarmingItemData>            seedOrder  = new();
    private readonly Dictionary<FarmingItemData, int> seedCounts = new();

    private void Start()
    {
        if (IsGhost) return;
        // 멀티: 파종은 호스트만
        if (!NetSession.IsReplica) plantCoroutine = StartCoroutine(PlantLoop());
        flashBeam = FlashBeam.Attach(transform, FlashBeam.Kind.Seeding);
        LinkRules.HintOnPlace(this);
    }

    private void OnDestroy()
    {
        if (plantCoroutine != null) StopCoroutine(plantCoroutine);
    }

    public int  Capacity    => seedCapacity;
    public int  TotalCount  => storedSeeds;
    public bool IsUnlimited => false;
    public bool HasSpace    => storedSeeds < seedCapacity;

    public event System.Action OnStockChanged;

    public IReadOnlyCollection<FarmingItemData> StockedItems => seedOrder;

    public int GetCount(FarmingItemData item) => item != null && seedCounts.TryGetValue(item, out int c) ? c : 0;

    public bool Accepts(FarmingItemData item) => IsPlantable(item);

    public int AddItem(FarmingItemData item, int amount)
    {
        if (!Accepts(item) || amount <= 0) return 0;

        int added = Mathf.Min(seedCapacity - storedSeeds, amount);
        if (added <= 0) return 0;

        if (seedCounts.TryGetValue(item, out int cur)) seedCounts[item] = cur + added;
        else { seedCounts[item] = added; seedOrder.Add(item); }
        storedSeeds += added;
        OnStockChanged?.Invoke();
        NetOps.Tile(NetOpType.StorageAdd, this, item, added);
        return added;
    }

    public int RemoveItem(FarmingItemData item, int amount)
    {
        if (item == null || amount <= 0 || !seedCounts.TryGetValue(item, out int cur)) return 0;

        int removed = Mathf.Min(cur, amount);
        if (cur > removed) seedCounts[item] = cur - removed;
        else { seedCounts.Remove(item); seedOrder.Remove(item); }
        storedSeeds -= removed;
        OnStockChanged?.Invoke();
        NetOps.Tile(NetOpType.StorageRemove, this, item, removed);
        return removed;
    }

    public int InsertFromInventory(FarmingItemData seed, int max = int.MaxValue)
    {
        var inv = Inventory.Instance;
        if (inv == null || !Accepts(seed)) return 0;

        int added = AddItem(seed, Mathf.Min(inv.GetCount(seed), max));
        if (added > 0 && !inv.ConsumeItem(seed, added)) { RemoveItem(seed, added); return 0; }
        return added;
    }

    public void RestoreLastSeed(FarmingItemData seed) => lastSeedItem = IsPlantable(seed) ? seed : null;

    protected internal override IEnumerable<(FarmingItemData item, int amount)> GetAttachedDrops()
    {
        foreach (var item in new List<FarmingItemData>(seedOrder))
        {
            int count = GetCount(item);
            if (count > 0) yield return (item, count);
        }
    }

    private IEnumerator PlantLoop()
    {
        while (true)
        {
            float interval = Interval;
            nextPlantAt = Time.time + interval;
            yield return new WaitForSeconds(interval);

            for (int i = 0; i < targetsPerCycle; i++)
                TryPlantOnce();
        }
    }

    private void TryPlantOnce()
    {
        var target = FindEmptyPlantTile();
        if (target == null) return;

        var seed = NextSeed(out var storage);
        if (seed == null || !target.PlantSeed(seed.seedData)) return;

        if (storage != null) storage.RemoveItem(seed, 1);
        else RemoveItem(seed, 1);
        lastSeedItem = seed;
        flashBeam?.Fire(transform.position, target.transform.position, FlashBeam.Kind.Seeding);
        TryFertilize(target);
    }

    private void TryFertilize(PlantTile plot)
    {
        if (plot == null || plot.IsFertile) return;
        for (int i = 0; i < Links.Count; i++)
        {
            if (Links[i] is not ItemStorage st) continue;
            foreach (var item in st.StockedItems)
            {
                if (item == null || !item.IsFertilizer) continue;
                if (st.RemoveItem(item, 1) > 0 && !plot.Fertilize(item)) st.AddItem(item, 1);
                return;
            }
        }
    }

    private FarmingItemData NextSeed(out ItemStorage storage)
    {
        storage = null;
        if (seedOrder.Count > 0) return seedOrder[0];

        if (lastSeedItem != null)
            for (int i = 0; i < Links.Count; i++)
                if (Links[i] is ItemStorage s && s.GetCount(lastSeedItem) > 0) { storage = s; return lastSeedItem; }

        for (int i = 0; i < Links.Count; i++)
            if (Links[i] is ItemStorage st)
                foreach (var item in st.StockedItems)
                    if (IsPlantable(item)) { storage = st; return item; }
        return null;
    }

    private static bool IsPlantable(FarmingItemData item)
        => item != null && item.itemType == FarmingItemType.Seed && item.seedData != null;

    private int SearchRadiusInCells()
    {
        float cellSize = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
        return Mathf.Max(1, Mathf.CeilToInt(searchRadius / cellSize));
    }

    private PlantTile FindEmptyPlantTile()
    {
        foreach (var tile in nearbyPlants.Get(this, SearchRadiusInCells()))
        {
            if (tile is PlantTile plant && plant.CurrentState == PlantTile.PlantState.Empty)
                return plant;
        }
        return null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        UnityEditor.Handles.color = Color.green;
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, searchRadius);
    }
#endif
}
