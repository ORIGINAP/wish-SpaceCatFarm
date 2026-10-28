using UnityEngine;

public class StarterKit : MonoBehaviour
{
    [Header("지급 아이템 (미사용)")]
    [SerializeField] private FarmingItemData basicExtractorItem;
    [SerializeField] private int             extractorCount = 4;

    [Header("추가 지급 (선택)")]
    [SerializeField] private FarmingItemData[] bonusItems;
    [SerializeField] private int[]             bonusCounts;

    private static readonly (string item, int amount)[] Kit =
    {
        ("Item_Seed_BlueStar", 6),
        ("Item_Tier1_Drill",   1),
        ("PlantTile",          4),
    };

    private static readonly Vector2Int FarmOffset = new(-5, -3);
    private const int FarmW = 3, FarmH = 2;

    private void Start()
    {
        if (!GameLaunchContext.IsNewGame) return;
        if (Inventory.Instance == null) return;

        foreach (var (name, amount) in Kit)
        {
            var item = NetWorld.Item(name);
            if (item != null) Inventory.Instance.AddItem(item, amount);
        }

        if (bonusItems != null)
        {
            for (int i = 0; i < bonusItems.Length; i++)
            {
                if (bonusItems[i] == null) continue;
                int count = (bonusCounts != null && i < bonusCounts.Length) ? bonusCounts[i] : 1;
                Inventory.Instance.AddItem(bonusItems[i], count);
            }
        }

        TilePlacementUI.Instance?.SelectSlot(0);

        // 멀티: 호스트만 배치
        if (!NetSession.IsReplica)
        {
            PlaceStarterFarm();
            ShuttleService.PlaceStarterPad();
        }
    }

    private static void PlaceStarterFarm()
    {
        var gm   = GridManager.Instance;
        var plot = NetWorld.Item("PlantTile");
        var core = CenterCore.Current;
        if (gm == null || plot == null || plot.tilePrefab == null || core == null) return;

        var origin = new Vector2Int(core.GridPosition.x, core.GridPosition.y) + FarmOffset;
        for (int y = 0; y < FarmH; y++)
        for (int x = 0; x < FarmW; x++)
        {
            var cell = origin + new Vector2Int(x, -y);
            if (gm.CanPlaceAt(cell, 1, 1)) gm.PlaceTile(cell, plot.tilePrefab, 1, 1, 0, plot);
        }
    }
}
