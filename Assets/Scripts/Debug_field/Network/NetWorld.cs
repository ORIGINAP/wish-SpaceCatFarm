using System.Collections.Generic;
using UnityEngine;

public static class NetWorld
{
    private static Dictionary<string, FarmingItemData> items;
    private static Dictionary<string, SeedData>        seeds;
    private static Dictionary<string, ResourceData>    resources;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        items     = null;
        seeds     = null;
        resources = null;
    }

    public static FarmingItemData Item(string name)
        => !string.IsNullOrEmpty(name) && (items ??= Build<FarmingItemData>()).TryGetValue(name, out var v) ? v : null;

    public static SeedData Seed(string name)
        => !string.IsNullOrEmpty(name) && (seeds ??= Build<SeedData>()).TryGetValue(name, out var v) ? v : null;

    public static ResourceData Resource(string name)
        => !string.IsNullOrEmpty(name) && (resources ??= Build<ResourceData>()).TryGetValue(name, out var v) ? v : null;

    public static FarmingItemData ItemForSeed(SeedData seed)
    {
        if (seed == null) return null;
        foreach (var item in (items ??= Build<FarmingItemData>()).Values)
            if (item.seedData == seed) return item;
        return null;
    }

    private static Dictionary<string, T> Build<T>() where T : ScriptableObject
    {
        var d = new Dictionary<string, T>(System.StringComparer.Ordinal);
        foreach (var x in GameDataRegistry.FindAll<T>())
            if (x != null && !d.ContainsKey(x.name)) d[x.name] = x;
        return d;
    }

    // 1/64 단위로 호스트와 같은 키
    public static Vector2Int RockKey(ResourceBlock b) => RockKey(b.OriginPosition.x, b.OriginPosition.y);

    public static Vector2Int RockKey(float x, float y) => new(Mathf.RoundToInt(x * 64f), Mathf.RoundToInt(y * 64f));

    // 자주 변하는 값은 비교에서 제외
    public static string TileKey(TileSave ts)
    {
        float timer = ts.stageTimer, fuel = ts.generatorFuel, progress = ts.smelterProgress;
        ts.stageTimer = ts.generatorFuel = ts.smelterProgress = 0f;
        string key = JsonUtility.ToJson(ts);
        ts.stageTimer = timer;
        ts.generatorFuel = fuel;
        ts.smelterProgress = progress;
        return key;
    }

    public static NetGlobals CaptureGlobals()
    {
        var g = new NetGlobals { currency = CurrencyManager.Instance != null ? CurrencyManager.Instance.Currency : 0 };

        var clock = GameTimeManager.Instance;
        if (clock != null)
        {
            g.day    = clock.CurrentDay;
            g.hour   = clock.CurrentHour;
            g.minute = clock.CurrentMinute;
        }
        if (TechTreeManager.Instance != null) g.tech = TechTreeManager.Instance.GetSaveData();

        var qm = QuestManager.Instance;
        if (qm != null)
        {
            g.questIndex = qm.CurrentQuestIndex;
            g.questsDone = qm.AllQuestsDone;
            foreach (var obj in qm.ActiveObjectives) g.questAmounts.Add(obj.currentAmount);
        }
        g.harvested = HarvestLog.ToList();

        var core = CenterCore.Current;
        if (core != null)
        {
            g.coreLevel = core.Level;
            g.coreFuel  = core.CurrentFuel;
        }
        g.boost = GameEventManager.Instance != null && GameEventManager.Instance.BoostActive;

        var wm = WeatherManager.Instance;
        if (wm != null)
        {
            g.weather      = (int)WeatherManager.Current;
            g.weatherNext  = (int)wm.Next;
            g.weatherNext2 = (int)wm.Next2;
        }
        var svc = ShuttleService.Instance;
        if (svc != null)
        {
            foreach (var s in svc.Shuttles) g.shuttles.Add(s.Clone());
            g.population   = ShuttleService.Population;
            g.shuttleTrips = ShuttleService.Trips;
            g.residents    = new List<ResidentSave>(svc.Residents);
        }
        g.codexGolden     = Codex.GoldenList();
        g.codexDishes     = Codex.DishList();
        g.codexMilestones = Codex.MilestonesPaid;
        return g;
    }

    public static string GlobalsKey(NetGlobals g)
    {
        int day = g.day, hour = g.hour, minute = g.minute;
        float fuel = g.coreFuel;
        var shuttles = g.shuttles;
        g.day = g.hour = g.minute = 0;
        g.coreFuel = 0f;
        g.shuttles = null;
        string key = JsonUtility.ToJson(g) + ShuttleService.StateKey(shuttles);
        g.day = day; g.hour = hour; g.minute = minute;
        g.coreFuel = fuel;
        g.shuttles = shuttles;
        return key;
    }

    public static TileItem ApplyTile(TileSave ts, out bool placedNew)
    {
        placedNew = false;
        var gm   = GridManager.Instance;
        var item = Item(ts.itemName);
        if (gm == null || item == null || item.tilePrefab == null) return null;

        var anchor = new Vector2Int(ts.anchorX, ts.anchorY);
        var tile   = gm.GetTile(anchor);
        if (tile != null && (tile.GridPosition != anchor || tile.SourceItem != item || tile.Rotation != ts.rotation))
        {
            if (tile.IsPersistent) return null;
            gm.RemoveTile(tile.GridPosition);
            tile = null;
        }
        if (tile == null)
        {
            tile = Place(gm, anchor, item, ts.rotation);
            if (tile == null)
            {
                // 호스트 월드가 기준
                ClearFootprint(gm, anchor, item, ts.rotation);
                tile = Place(gm, anchor, item, ts.rotation);
            }
            if (tile == null) return null;
            placedNew = true;
        }

        switch (tile)
        {
            case PlantTile plant:
                plant.RestoreFromSave((PlantTile.PlantState)ts.plantState, Seed(ts.seedDataName), ts.growthStage, ts.stageTimer);
                plant.RestoreFertility(ts.plotFertility);
                break;
            case CatKitchen kitchen:
                kitchen.RestoreFromSave(CatKitchen.FindRecipe(ts.machineRecipe), ts.storageItems,
                                        Item(ts.smelterOutput?.itemName), ts.smelterOutput?.count ?? 0, ts.smelterProgress);
                break;
            case Composter composter:
                composter.RestoreFromSave(ts.compostPoints, ts.smelterOutput?.count ?? 0, ts.smelterProgress);
                break;
            case Generator gen:
                if (!Mathf.Approximately(gen.CurrentFuel, ts.generatorFuel) || placedNew) gen.RestoreFromSave(ts.generatorFuel);
                break;
            case AutoSeeder seeder:
                seeder.RestoreLastSeed(Item(ts.seederSeedItem));
                SetStock(seeder, ts.storageItems, inOrder: true);
                break;
            case ItemStorage storage:
                SetStock(storage, ts.storageItems);
                break;
            case ResourceExtractor extractor:
                var drill = Item(ts.extractorDrill);
                if (extractor.EquippedDrill != drill)
                {
                    if (drill == null) extractor.Unequip();
                    else extractor.TryEquip(drill);
                }
                break;
            case Smelter smelter:
                smelter.RestoreFromSave(Item(ts.smelterInput?.itemName), ts.smelterInput?.count ?? 0,
                                        Item(ts.smelterOutput?.itemName), ts.smelterOutput?.count ?? 0, ts.smelterProgress);
                break;
        }
        return tile;
    }

    private static TileItem Place(GridManager gm, Vector2Int anchor, FarmingItemData item, int rotation)
        => gm.PlaceTile(anchor, item.tilePrefab, item.tileWidth, item.tileHeight, rotation, item);

    private static void ClearFootprint(GridManager gm, Vector2Int anchor, FarmingItemData item, int rotation)
    {
        bool swap = rotation == 90 || rotation == 270;
        int w = swap ? item.tileHeight : item.tileWidth;
        int h = swap ? item.tileWidth  : item.tileHeight;
        foreach (var cell in gm.GetOccupiedCells(anchor, w, h))
        {
            var t = gm.GetTile(cell);
            if (t != null && !t.IsPersistent) gm.RemoveTile(t.GridPosition);
        }
    }

    private static void SetStock(IItemBox storage, List<ItemSlotSave> target, bool inOrder = false)
    {
        var want = new Dictionary<FarmingItemData, int>();
        if (target != null)
            foreach (var s in target)
            {
                var it = Item(s.itemName);
                if (it == null || s.count <= 0) continue;
                want.TryGetValue(it, out int c);
                want[it] = c + s.count;
            }

        if (inOrder && !System.Linq.Enumerable.SequenceEqual(storage.StockedItems, want.Keys))
            foreach (var it in new List<FarmingItemData>(storage.StockedItems))
                storage.RemoveItem(it, storage.GetCount(it));

        foreach (var it in new List<FarmingItemData>(storage.StockedItems))
        {
            want.TryGetValue(it, out int w);
            int have = storage.GetCount(it);
            if (have > w) storage.RemoveItem(it, have - w);
        }
        foreach (var pair in want)
        {
            int have = storage.GetCount(pair.Key);
            if (pair.Value > have) storage.AddItem(pair.Key, pair.Value - have);
        }
    }

    public static void SetLinks(TileItem tile, List<LinkSave> links)
    {
        var gm = GridManager.Instance;
        if (gm == null || tile == null) return;

        var want = new List<TileItem>();
        if (links != null)
            foreach (var l in links)
            {
                var other = gm.GetTile(new Vector2Int(l.x, l.y));
                if (other != null && other != tile && !want.Contains(other)) want.Add(other);
            }

        foreach (var other in new List<TileItem>(tile.Links))
            if (!want.Contains(other)) TileItem.Unlink(tile, other);
        foreach (var other in want)
            if (!tile.IsLinkedTo(other)) TileItem.Link(tile, other);
    }

    public static void RemoveTileAt(Vector2Int anchor)
    {
        var gm = GridManager.Instance;
        var t  = gm != null ? gm.GetTile(anchor) : null;
        if (t != null && t.GridPosition == anchor && !t.IsPersistent) gm.RemoveTile(anchor);
    }

    public static void ApplyRock(ResourceBlockSave r, Dictionary<Vector2Int, ResourceBlock> map)
    {
        var key = RockKey(r.posX, r.posY);
        map.TryGetValue(key, out var block);
        if (r.remaining <= 0)
        {
            if (block != null)
            {
                // Destroy는 프레임 끝이라 콜라이더 먼저 끔
                foreach (var col in block.GetComponents<Collider2D>()) col.enabled = false;
                Object.Destroy(block.gameObject);
            }
            map.Remove(key);
            return;
        }
        if (block != null)
        {
            if (block.RemainingResources != r.remaining) block.RestoreState(r.remaining);
            return;
        }
        var data = Resource(r.resourceDataName);
        if (data == null) return;
        var player = PlayerTileController.Instance != null ? PlayerTileController.Instance.transform : null;
        map[key] = ResourceBlock.CreateSaved(data, new Vector2(r.posX, r.posY), player, r.remaining);
        if (WeatherManager.Current == WeatherType.MeteorShower) MeteorFall.Play(new Vector3(r.posX, r.posY, 0f), null, 0.45f);
    }

    public static void ApplyGlobals(NetGlobals g, bool includePredicted)
    {
        if (includePredicted)
        {
            var cm = CurrencyManager.Instance;
            if (cm != null && cm.Currency != g.currency) cm.SetCurrency(g.currency);

            var tech = TechTreeManager.Instance;
            if (tech != null && !SameTech(tech.GetSaveData(), g.tech)) tech.LoadSaveData(g.tech);

            if (!SameList(HarvestLog.ToList(), g.harvested)) HarvestLog.Restore(g.harvested);
        }

        var clock = GameTimeManager.Instance;
        if (clock != null)
        {
            // 2분 이상 어긋날 때만 맞춤
            int mine = (clock.CurrentDay * 24 + clock.CurrentHour) * 60 + clock.CurrentMinute;
            int host = (g.day * 24 + g.hour) * 60 + g.minute;
            if (Mathf.Abs(mine - host) >= 2) clock.RestoreTime(g.day, g.hour, g.minute);
        }

        QuestManager.Instance?.SyncFromHost(g.questIndex, g.questAmounts, g.questsDone);
        if (CenterCore.Current != null) CenterCore.Current.SyncFromHost(g.coreLevel, g.coreFuel);
        GameEventManager.Instance?.SyncBoost(g.boost);
        WeatherManager.Instance?.SyncFromHost(g.weather, g.weatherNext, g.weatherNext2);
        if (includePredicted) ShuttleService.Instance?.SyncFromHost(g.shuttles, g.population, g.shuttleTrips, g.residents);
        Codex.SyncFromHost(g.codexGolden, g.codexDishes, g.codexMilestones);
    }

    private static bool SameTech(List<TechNodeSave> a, List<TechNodeSave> b)
    {
        if (a == null || b == null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i].id != b[i].id || a[i].level != b[i].level) return false;
        return true;
    }

    private static bool SameList<T>(List<T> a, List<T> b)
    {
        if (a == null || b == null || a.Count != b.Count) return false;
        var cmp = EqualityComparer<T>.Default;
        for (int i = 0; i < a.Count; i++)
            if (!cmp.Equals(a[i], b[i])) return false;
        return true;
    }
}
