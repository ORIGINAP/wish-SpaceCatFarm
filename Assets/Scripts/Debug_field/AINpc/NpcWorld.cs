using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

public static class NpcWorld
{
    public const float WorkRadius = 40f;

    private static readonly string[] CraftWords = { "제작", "만들", "조합", "크래프트" };
    private static readonly string[] ShopWords  = { "사", "구매", "상점", "팔", "판매", "가격", "돈" };
    private static readonly string[] PlaceWords = { "설치", "놓", "깔", "짓", "배치", "세워", "만들어 둬" };
    private static readonly string[] SeederWords = { "파종기", "등록", "씨앗통" };

    private static Dictionary<string, FarmingItemData> itemsByName;
    private static HashSet<FarmingItemData> minableItems;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        itemsByName  = null;
        minableItems = null;
    }

    public static FarmingItemData FindItem(string name, System.Func<FarmingItemData, bool> filter = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (itemsByName == null)
        {
            itemsByName = new Dictionary<string, FarmingItemData>();
            foreach (var item in GameDataRegistry.FindAll<FarmingItemData>())
                if (item != null && !string.IsNullOrEmpty(item.itemName))
                    itemsByName[Key(item.itemName)] = item;
        }

        string k = Key(name);
        if (itemsByName.TryGetValue(k, out var exact) && (filter == null || filter(exact))) return exact;

        FarmingItemData best = null;
        int bestScore = 0;
        foreach (var kv in itemsByName)
        {
            if (!(kv.Key.Contains(k) || k.Contains(kv.Key)) || (filter != null && !filter(kv.Value))) continue;
            int score = (kv.Key.StartsWith(k) ? 2 : 1) + (IsMinable(kv.Value) ? 1 : 0);
            if (score > bestScore) { best = kv.Value; bestScore = score; }
        }
        return best;
    }

    public static bool IsMinable(FarmingItemData item)
    {
        if (minableItems == null)
        {
            minableItems = new HashSet<FarmingItemData>();
            foreach (var rd in GameDataRegistry.FindAll<ResourceData>())
                if (rd != null && rd.dropItem != null) minableItems.Add(rd.dropItem);
        }
        return item != null && minableItems.Contains(item);
    }

    public static CraftingRecipe FindRecipe(string name)
    {
        var cm = CraftingManager.Instance;
        if (cm == null || string.IsNullOrWhiteSpace(name)) return null;
        string k = Key(name);
        CraftingRecipe partial = null;
        foreach (var r in cm.Recipes)
        {
            if (r == null) continue;
            string rk = Key(RecipeName(r));
            if (rk == k) return r;
            if (partial == null && (rk.Contains(k) || k.Contains(rk))) partial = r;
        }
        return partial;
    }

    public static string RecipeName(CraftingRecipe r)
        => !string.IsNullOrEmpty(r.recipeName) ? r.recipeName : (r.outputItem != null ? r.outputItem.itemName : "?");

    private static string Key(string s) => s.Replace(" ", "").ToLowerInvariant();

    // LLM에 보낼 상태 요약
    public static string BuildState(AINpc npc, Vector3 playerPos, string playerText)
    {
        var sb = new StringBuilder("[상태] ");
        long coins = CurrencyManager.Instance != null ? CurrencyManager.Instance.Currency : 0;
        sb.Append($"코인 {coins:N0} | 내 작업: {npc.CurrentTaskLabel} | 내 도구등급 {npc.ToolGrade}\n");

        var inv = Inventory.Instance;
        if (inv != null)
        {
            var counts = new Dictionary<string, int>();
            for (int i = 0; i < inv.SlotCount; i++)
            {
                var slot = inv.GetSlot(i);
                if (slot == null || slot.IsEmpty) continue;
                counts.TryGetValue(slot.Item.itemName, out int c);
                counts[slot.Item.itemName] = c + slot.Count;
            }
            sb.Append("인벤: ").Append(counts.Count == 0 ? "비어있음"
                : string.Join(", ", counts.OrderByDescending(kv => kv.Value).Take(12).Select(kv => kv.Key + kv.Value)));
            sb.Append('\n');
        }

        var res = new Dictionary<string, (int blocks, bool tooHard)>();
        foreach (var b in ResourceBlock.Active)
        {
            if (b.DropItem == null || b.RemainingResources <= 0) continue;
            if ((b.transform.position - playerPos).sqrMagnitude > WorkRadius * WorkRadius) continue;
            string n = b.DropItem.itemName;
            res.TryGetValue(n, out var e);
            res[n] = (e.blocks + 1, b.Data != null && b.Data.minToolGrade > npc.ToolGrade);
        }
        sb.Append("근처자원: ").Append(res.Count == 0 ? "없음"
            : string.Join(", ", res.OrderByDescending(kv => kv.Value.blocks).Take(10)
                .Select(kv => $"{kv.Key}{kv.Value.blocks}덩이{(kv.Value.tooHard ? "(등급부족)" : "")}")));
        sb.Append('\n');

        int empty = 0, growing = 0, ready = 0;
        foreach (var p in AllPlants())
        {
            if ((p.transform.position - playerPos).sqrMagnitude > WorkRadius * WorkRadius) continue;
            switch (p.CurrentState)
            {
                case PlantTile.PlantState.Empty:          empty++;   break;
                case PlantTile.PlantState.ReadyToHarvest: ready++;   break;
                default:                                  growing++; break;
            }
        }
        sb.Append($"밭: 빈칸{empty} 성장중{growing} 수확가능{ready}");
        var gens = EnergyManager.Instance != null ? EnergyManager.Instance.Generators : null;
        if (gens != null && gens.Count > 0)
            sb.Append($" | 발전소 연료{gens.Average(g => g.FuelRatio) * 100f:F0}%");
        var core = CenterCore.Current;
        if (core != null)
        {
            sb.Append($" | 문명Lv{core.Level}/{core.MaxLevel}({core.CurrentStage?.title})");
            var next = core.NextStage;
            if (next != null)
                sb.Append(" 다음조건: ").Append(string.Join(", ",
                    next.requirements.Select(r => CoreProgress.Label(r) + CoreProgress.ProgressText(r))));
        }
        sb.Append('\n');

        if (ContainsAny(playerText, CraftWords) && CraftingManager.Instance != null)
        {
            var can = new List<string>(); var cannot = new List<string>();
            foreach (var r in CraftingManager.Instance.Recipes)
            {
                if (r == null || !CraftingManager.Instance.IsRecipeUnlocked(r)) continue;
                (r.CanCraft(Inventory.Instance) ? can : cannot).Add(RecipeName(r));
            }
            sb.Append("제작가능: ").Append(can.Count == 0 ? "없음" : string.Join(", ", can.Take(12))).Append('\n');
            if (cannot.Count > 0) sb.Append("재료부족: ").Append(string.Join(", ", cannot.Take(12))).Append('\n');
        }
        if (ContainsAny(playerText, PlaceWords) && inv != null)
        {
            var tiles = new Dictionary<string, int>();
            for (int i = 0; i < inv.SlotCount; i++)
            {
                var slot = inv.GetSlot(i);
                if (slot == null || slot.IsEmpty || slot.Item.itemType != FarmingItemType.Tile || slot.Item.tilePrefab == null) continue;
                string key = $"{slot.Item.itemName}({slot.Item.tileWidth}x{slot.Item.tileHeight})";
                tiles.TryGetValue(key, out int c);
                tiles[key] = c + slot.Count;
            }
            sb.Append("설치가능: ").Append(tiles.Count == 0 ? "없음" : string.Join(", ", tiles.Select(kv => kv.Key + kv.Value))).Append('\n');
        }
        if (ContainsAny(playerText, SeederWords))
        {
            var bySeed = new Dictionary<string, int>();
            int seeders = 0, space = 0;
            foreach (var s in NearbySeeders(playerPos))
            {
                seeders++;
                space += s.Capacity - s.TotalCount;
                foreach (var item in s.StockedItems)
                {
                    bySeed.TryGetValue(item.itemName, out int c);
                    bySeed[item.itemName] = c + s.GetCount(item);
                }
            }
            sb.Append($"파종기: {seeders}대");
            if (seeders > 0)
                sb.Append($" (씨앗통: {(bySeed.Count == 0 ? "비었음" : string.Join(", ", bySeed.Select(kv => kv.Key + kv.Value)))}, 남은자리 {space})");
            sb.Append(" | 씨앗종류: ").Append(string.Join(", ", AllSeedItems().Select(i => i.itemName))).Append('\n');
        }
        if (ContainsAny(playerText, ShopWords) && ShopManager.Instance != null)
        {
            sb.Append("상점: ").Append(string.Join(", ",
                ShopManager.Instance.CurrentStock.Where(i => i != null && ShopManager.Instance.IsPurchasable(i)).Take(25)
                    .Select(i => $"{i.itemName}{ShopManager.Instance.BuyPrice(i)}G"))).Append('\n');
        }

        return sb.ToString().TrimEnd();
    }

    public static IEnumerable<FarmingItemData> AllSeedItems()
    {
        FindItem("_");
        return itemsByName.Values.Where(i => i.itemType == FarmingItemType.Seed && i.seedData != null);
    }

    private static readonly List<TileItem> seederBuffer = new();

    public static IEnumerable<AutoSeeder> NearbySeeders(Vector3 playerPos)
    {
        if (GridManager.Instance == null) yield break;
        GridManager.Instance.CollectTiles(TileType.AutoSeeder, seederBuffer);
        foreach (var t in seederBuffer)
            if (t is AutoSeeder s && s != null && (s.transform.position - playerPos).sqrMagnitude <= WorkRadius * WorkRadius)
                yield return s;
    }

    private static readonly List<TileItem> plantBuffer = new();

    public static IEnumerable<PlantTile> AllPlants()
    {
        if (GridManager.Instance == null) yield break;
        GridManager.Instance.CollectTiles(TileType.Plant, plantBuffer);
        foreach (var t in plantBuffer)
            if (t is PlantTile p && p != null) yield return p;
    }

    private static bool ContainsAny(string text, string[] words)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var w in words) if (text.Contains(w)) return true;
        return false;
    }
}
