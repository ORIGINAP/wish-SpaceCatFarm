using UnityEngine;
using System.Collections;

public class AutoHarvester : TileItem, ICoverageArea
{
    public static event System.Action<AutoHarvester, PlantTile> OnAnyMachineHarvest;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => OnAnyMachineHarvest = null;

    [Header("수확기 설정")]
    [SerializeField] private float harvestInterval = 5f;
    [SerializeField] private float searchRadius    = 3f;
    [SerializeField] private int   targetsPerCycle = 1;

    [Header("작업 범위 표시 (마우스 호버 시)")]
    [SerializeField] private Color coverageColor = new Color(1.00f, 0.70f, 0.20f, 0.12f);

    [Header("실시간 상태")]
    [SerializeField] private float nextHarvestAt;

    public int        CoverageRadiusCells => SearchRadiusInCells();
    public Color      CoverageColor       => coverageColor;

    public override string AbilityText => $"{Interval:0.#}초마다 다 자란 밭 {targetsPerCycle}칸을 수확해요 (범위 {CoverageRadiusCells}칸)";

    private float Interval => harvestInterval / TechTreeManager.MachineSpeed;
    public override float  WorkRate    => harvestInterval > 0f ? targetsPerCycle / harvestInterval : 0f;

    private Coroutine harvestCoroutine;
    private FlashBeam flashBeam;

    private readonly NearbyTiles nearbyPlants = new(TileType.Plant);

    private void Start()
    {
        if (IsGhost) return;
        // 멀티: 수확은 호스트만
        if (!NetSession.IsReplica) harvestCoroutine = StartCoroutine(HarvestLoop());
        flashBeam = FlashBeam.Attach(transform, FlashBeam.Kind.Harvesting);
        LinkRules.HintOnPlace(this);
    }

    private void OnDestroy()
    {
        if (harvestCoroutine != null) StopCoroutine(harvestCoroutine);
    }

    private IEnumerator HarvestLoop()
    {
        while (true)
        {
            float interval = Interval;
            nextHarvestAt = Time.time + interval;
            yield return new WaitForSeconds(interval);

            for (int i = 0; i < targetsPerCycle; i++)
                TryHarvestOnce();
        }
    }

    private void TryHarvestOnce()
    {
        var target = FindReadyPlantTile();
        if (target == null) return;

        Vector3 targetPos = target.transform.position;
        var results = target.Harvest();
        if (results == null) return;

        flashBeam?.Fire(transform.position, targetPos, FlashBeam.Kind.Harvesting);

        foreach (var result in results)
        {
            if (result.item == null || result.amount <= 0) continue;
            SupplyResult(result.item, result.amount, targetPos);
        }
        OnAnyMachineHarvest?.Invoke(this, target);
    }

    private int SearchRadiusInCells()
    {
        float cellSize = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
        return Mathf.Max(1, Mathf.CeilToInt(searchRadius / cellSize));
    }

    private PlantTile FindReadyPlantTile()
    {
        foreach (var tile in nearbyPlants.Get(this, SearchRadiusInCells()))
        {
            if (tile is PlantTile plant && plant.CurrentState == PlantTile.PlantState.ReadyToHarvest
                && HasRoomForHarvest(plant))
                return plant;
        }
        return null;
    }

    private bool HasRoomForHarvest(PlantTile plant)
    {
        var drops = plant.CurrentSeed != null ? plant.CurrentSeed.drops : null;
        if (drops == null) return true;

        foreach (var drop in drops)
        {
            if (drop.item == null) continue;
            if (!HasRoomFor(drop.item)) return false;
        }
        return true;
    }

    private bool HasRoomFor(FarmingItemData item)
    {
        for (int i = 0; i < Links.Count; i++)
            if (Links[i] is ItemStorage storage && storage.Accepts(item) && storage.HasSpace)
                return true;
        return Inventory.Instance != null && Inventory.Instance.CanAddItem(item);
    }

    private void SupplyResult(FarmingItemData item, int amount, Vector3 fallbackDropPos)
    {
        for (int i = 0; i < Links.Count && amount > 0; i++)
            if (Links[i] is ItemStorage storage && storage.Accepts(item))
                amount -= storage.AddItem(item, amount);

        if (amount > 0)
            Inventory.Instance?.AddItemOrDrop(item, amount, fallbackDropPos);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        UnityEditor.Handles.color = Color.yellow;
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, searchRadius);
    }
#endif
}
