using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// 세이브 슬롯 3개, JSON 파일
public class SaveManager : MonoBehaviour
{
    public static SaveManager Instance => Services.Get<SaveManager>();

    public const int SlotCount = 3;

    public static bool IsLoading { get; private set; }

    public static int LastLoadFrame { get; private set; } = -1000;

    // 도메인 리로드 없이 재시작 대비
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsLoading = false;
        LastLoadFrame = -1000;
    }

    public static bool RecentlyLoaded => Time.frameCount - LastLoadFrame <= 10;

    [Header("자동 저장")]
    [SerializeField] private float autoSaveInterval = 60f;

    // 멀티 참가자는 저장 안 함
    public static bool CanSave => GameLaunchContext.LaunchedFromMenu && !NetSession.IsReplica;

    public static int CurrentSlot => GameLaunchContext.SelectedSlot;

    private float  _autoSaveTimer;
    private float  _playStartTime;

    private int    _playTimeSlot = -1;
    private float  _playTimeBase;

    public static string GetPath(int slot)
        => Path.Combine(Application.persistentDataPath, $"save_slot_{slot}.json");

    private static string GetBackupPath(int slot) => GetPath(slot) + ".bak";
    private static string GetTempPath(int slot)   => GetPath(slot) + ".tmp";

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }
        _playStartTime = Time.time;
    }

    private void OnDestroy()
    {
        Services.Unregister(this);
    }

    private void Update()
    {
        if (!CanSave) return;

        _autoSaveTimer += Time.deltaTime;
        if (_autoSaveTimer >= autoSaveInterval)
        {
            _autoSaveTimer = 0f;
            if (Save(CurrentSlot))
                NotificationUI.Instance?.Show("자동 저장 완료");
        }
    }

    private void OnApplicationQuit()
    {
        if (CanSave) Save(CurrentSlot);
    }

    public enum SlotState { Empty, Ok, Corrupt }

    public bool SlotExists(int slot) => File.Exists(GetPath(slot)) || File.Exists(GetBackupPath(slot));

    public SaveData ReadSlot(int slot) => ReadSlotStatic(slot);

    public static SaveData ReadSlotStatic(int slot)
    {
        return TryRead(GetPath(slot)) ?? TryRead(GetBackupPath(slot));
    }

    public static SlotState GetSlotState(int slot)
    {
        bool anyFile = File.Exists(GetPath(slot)) || File.Exists(GetBackupPath(slot));
        if (!anyFile) return SlotState.Empty;
        return ReadSlotStatic(slot) != null ? SlotState.Ok : SlotState.Corrupt;
    }

    private static SaveData TryRead(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
            return data;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] 세이브 파일을 읽지 못했습니다: {path}\n{e.Message}");
            return null;
        }
    }

    public void DeleteSlot(int slot) => DeleteSlotStatic(slot);

    public static void DeleteSlotStatic(int slot)
    {
        foreach (var path in new[] { GetPath(slot), GetBackupPath(slot), GetTempPath(slot) })
            if (File.Exists(path)) File.Delete(path);
    }

    public bool Save(int slot)
    {
        if (!CanSave)
        {
            Debug.Log("[SaveManager] 메인메뉴를 거치지 않은 테스트 세션이라 저장하지 않습니다.");
            return false;
        }

        var data = CaptureWorld();
        data.slotIndex       = slot;
        data.timestamp       = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        if (_playTimeSlot != slot)
        {
            _playTimeBase = ReadSlot(slot)?.playTimeSeconds ?? 0f;
            _playTimeSlot = slot;
        }
        data.playTimeSeconds = _playTimeBase + (Time.time - _playStartTime);
        _playTimeBase        = data.playTimeSeconds;
        _playStartTime       = Time.time;

        var ptc = PlayerTileController.Instance;
        if (ptc != null)
        {
            data.playerX = ptc.transform.position.x;
            data.playerY = ptc.transform.position.y;
        }
        data.character = PlayerCharacter.Current.Copy();

        var inv = Inventory.Instance;
        if (inv != null)
        {
            for (int i = 0; i < inv.SlotCount; i++)
            {
                var slot2 = inv.GetSlot(i);
                data.inventorySlots.Add(new ItemSlotSave
                {
                    itemName = (slot2 == null || slot2.IsEmpty) ? "" : slot2.Item.name,
                    count    = (slot2 == null || slot2.IsEmpty) ? 0  : slot2.Count
                });
            }
        }

        var em = EquipmentManager.Instance;
        data.equipmentSlots = new string[EquipmentManager.SlotCount];
        for (int i = 0; i < EquipmentManager.SlotCount; i++)
            data.equipmentSlots[i] = em?.GetSlot(i)?.name ?? "";

        data.guests = NetGuests.ToList();

        try
        {
            string path = GetPath(slot);
            string dir  = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

            string tmp = GetTempPath(slot);
            File.WriteAllText(tmp, JsonUtility.ToJson(data, true));

            if (File.Exists(path))
                File.Replace(tmp, path, GetBackupPath(slot));
            else
                File.Move(tmp, path);

            Debug.Log($"[SaveManager] 슬롯 {slot} 저장 완료: {path}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] 저장 실패: {e.Message}");
            NotificationUI.Instance?.Show("저장에 실패했습니다. (콘솔 로그 확인)");
            return false;
        }
    }

    public SaveData CaptureWorld()
    {
        var data = new SaveData();
        data.version    = SaveData.CurrentVersion;
        data.worldSize  = (int)GameLaunchContext.PendingWorldSize;
        data.worldTheme = (int)GameLaunchContext.PendingWorldTheme;
        data.worldSeed  = GameLaunchContext.PendingWorldSeed;
        var grid = GridManager.Instance;
        if (grid != null)
        {
            data.gridWidth  = grid.GridWidth;
            data.gridHeight = grid.GridHeight;
        }

        data.currency = CurrencyManager.Instance?.Currency ?? 0;

        var clock = GameTimeManager.Instance;
        if (clock != null)
        {
            data.gameDay    = clock.CurrentDay;
            data.gameHour   = clock.CurrentHour;
            data.gameMinute = clock.CurrentMinute;
        }

        data.techNodes = TechTreeManager.Instance?.GetSaveData() ?? new List<TechNodeSave>();

        var gm = GridManager.Instance;
        if (gm != null)
        {
            foreach (var tile in gm.GetDynamicTiles())
            {
                var ts = CaptureTile(tile);
                if (ts != null) data.placedTiles.Add(ts);
            }
        }

        var plant = CenterCore.Current;
        if (plant != null)
        {
            data.centralPlant = new CentralPlantSave
            {
                exists  = true,
                anchorX = plant.GridPosition.x,
                anchorY = plant.GridPosition.y,
                fuel    = plant.CurrentFuel,
                level   = plant.Level,
            };
        }
        data.harvestedCrops = HarvestLog.ToList();
        data.exploredCells  = ExploredMap.ToSave();

        var weather = WeatherManager.Instance;
        if (weather != null)
        {
            data.weather           = (int)WeatherManager.Current;
            data.weatherNext       = (int)weather.Next;
            data.weatherNext2      = (int)weather.Next2;
            data.weatherPeriod     = weather.Period;
            data.stormStruckPeriod = weather.StormStruckPeriod;
        }
        ShuttleService.Instance?.CaptureTo(data);
        data.codexGolden     = Codex.GoldenList();
        data.codexDishes     = Codex.DishList();
        data.codexMilestones = Codex.MilestonesPaid;

        // 부유 연출 말고 원래 위치 저장
        foreach (var block in ResourceBlock.Active)
        {
            if (block == null || block.Data == null) continue;
            data.resourceBlocks.Add(new ResourceBlockSave
            {
                posX             = block.OriginPosition.x,
                posY             = block.OriginPosition.y,
                resourceDataName = block.Data.name,
                remaining        = block.RemainingResources
            });
        }

        var qm = QuestManager.Instance;
        if (qm != null)
        {
            data.allQuestsDone     = qm.AllQuestsDone;
            data.currentQuestIndex = qm.CurrentQuestIndex;
            data.currentQuestId    = qm.CurrentQuestId;
            data.questObjectiveAmounts.Clear();
            foreach (var obj in qm.ActiveObjectives)
                data.questObjectiveAmounts.Add(obj.currentAmount);
        }

        return data;
    }

    public static TileSave CaptureTile(TileItem tile)
    {
        if (tile == null || tile.SourceItem == null) return null;

        var ts = new TileSave
        {
            itemName = tile.SourceItem.name,
            anchorX  = tile.GridPosition.x,
            anchorY  = tile.GridPosition.y,
            rotation = tile.Rotation,
            subType  = tile.GetType().Name
        };

        if (tile is PlantTile pt)
        {
            ts.plantState = (int)pt.CurrentState;
            ts.seedDataName  = pt.CurrentSeed?.name ?? "";
            ts.growthStage   = pt.GrowthStage;
            ts.stageTimer    = pt.StageTimer;
            ts.plotFertility = pt.Fertility;
        }
        else if (tile is CatKitchen kitchen)
        {
            ts.machineRecipe = kitchen.Recipe != null ? kitchen.Recipe.name : "";
            foreach (var item in kitchen.InputItems)
                ts.storageItems.Add(new ItemSlotSave { itemName = item.name, count = kitchen.GetInput(item) });
            ts.smelterOutput   = new ItemSlotSave { itemName = kitchen.OutputItem != null ? kitchen.OutputItem.name : "", count = kitchen.OutputCount };
            ts.smelterProgress = kitchen.Progress;
        }
        else if (tile is Composter composter)
        {
            ts.compostPoints   = composter.Points;
            ts.smelterOutput   = new ItemSlotSave { itemName = composter.CompostItem != null ? composter.CompostItem.name : "", count = composter.OutputCount };
            ts.smelterProgress = composter.Progress;
        }
        else if (tile is Generator gen)
        {
            ts.generatorFuel = gen.CurrentFuel;
        }
        else if (tile is AutoSeeder seeder)
        {
            ts.seederSeedItem = seeder.LastSeedItem?.name ?? "";
            foreach (var item in seeder.StockedItems)
                ts.storageItems.Add(new ItemSlotSave { itemName = item.name, count = seeder.GetCount(item) });
        }
        else if (tile is ItemStorage storage)
        {
            foreach (var item in storage.StockedItems)
                ts.storageItems.Add(new ItemSlotSave { itemName = item.name, count = storage.GetCount(item) });
        }
        else if (tile is ResourceExtractor extractor)
        {
            ts.extractorDrill = extractor.EquippedDrill?.name ?? "";
        }
        else if (tile is Smelter smelter)
        {
            ts.smelterInput    = new ItemSlotSave { itemName = smelter.InputItem  != null ? smelter.InputItem.name  : "", count = smelter.InputCount };
            ts.smelterOutput   = new ItemSlotSave { itemName = smelter.OutputItem != null ? smelter.OutputItem.name : "", count = smelter.OutputCount };
            ts.smelterProgress = smelter.Progress;
        }

        foreach (var other in tile.Links)
            if (other != null) ts.links.Add(new LinkSave { x = other.GridPosition.x, y = other.GridPosition.y });

        return ts;
    }

    public void Load(int slot)
    {
        var data = ReadSlot(slot);
        if (data == null)
        {
            NotificationUI.Instance?.Show("세이브 파일이 없거나 손상되었습니다.");
            return;
        }

        IsLoading = true;
        try   { ApplyLoad(data); }
        catch (Exception e) { Debug.LogError($"[SaveManager] 로드 실패: {e.Message}\n{e.StackTrace}"); }
        finally { IsLoading = false; }

        TerrainMap.Current?.CarveAroundObjects();
        ResourceBlock.RelocateOverlapping();

        LastLoadFrame  = Time.frameCount;
        _playStartTime = Time.time;
        _playTimeSlot  = slot;
        _playTimeBase  = data.playTimeSeconds;
        NotificationUI.Instance?.Show("불러오기 완료");
    }

    public void ApplyRemoteWorld(SaveData data)
    {
        IsLoading = true;
        try   { ApplyLoad(data, worldOnly: true); }
        catch (Exception e) { Debug.LogError($"[SaveManager] 호스트 월드 적용 실패: {e.Message}\n{e.StackTrace}"); }
        finally { IsLoading = false; }

        LastLoadFrame = Time.frameCount;
    }

    public static void ApplyRemote(Func<bool> apply)
    {
        bool placed;
        IsLoading = true;
        try   { placed = apply(); }
        finally { IsLoading = false; }
        if (placed) LastLoadFrame = Time.frameCount;
    }

    private void ApplyLoad(SaveData data, bool worldOnly = false)
    {
        var lookup = BuildItemLookup();
        var seedLookup = BuildSeedLookup();
        var resLookup  = BuildResourceLookup();

        CurrencyManager.Instance?.SetCurrency(data.currency);

        GameTimeManager.Instance?.RestoreTime(data.gameDay, data.gameHour, data.gameMinute);

        TechTreeManager.Instance?.LoadSaveData(data.techNodes);

        // 구버전 세이브 변환
        var legacy = data.version < SaveData.FirstCentralPlantVersion ? new LegacyMigration() : null;
        var removedBatteries = new RemovedBatteryRefund();

        var inv = worldOnly ? null : Inventory.Instance;
        if (inv != null)
        {
            inv.ClearAll();
            for (int i = 0; i < data.inventorySlots.Count && i < inv.SlotCount; i++)
            {
                var s = data.inventorySlots[i];
                if (string.IsNullOrEmpty(s.itemName) || s.count <= 0) continue;
                if (legacy != null && legacy.TryRefundItem(s.itemName, s.count)) continue;
                if (removedBatteries.TryTake(s.itemName, s.count)) continue;
                if (lookup.TryGetValue(s.itemName, out var item))
                    inv.SetSlotDirect(i, item, s.count);
            }
        }

        if (!worldOnly)
        {
            var em = EquipmentManager.GetOrCreate();
            for (int i = 0; i < EquipmentManager.SlotCount; i++)
                em.Unequip(i);
            for (int i = 0; i < data.equipmentSlots?.Length && i < EquipmentManager.SlotCount; i++)
            {
                var name = data.equipmentSlots[i];
                if (string.IsNullOrEmpty(name)) continue;
                if (lookup.TryGetValue(name, out var item))
                    em.TryEquip(i, item);
            }

            NetGuests.Load(data.guests);
        }

        // 타일 복원 전에 먼저 치움
        foreach (var rb in ResourceBlock.Active)
        {
            foreach (var col in rb.GetComponents<Collider2D>()) col.enabled = false;
            Destroy(rb.gameObject);
        }

        var gm = GridManager.Instance;
        gm?.ClearDynamicTiles();

        var plantSave = data.centralPlant;
        HarvestLog.Restore(data.harvestedCrops);
        Codex.Restore(data.codexGolden, data.codexDishes, data.codexMilestones);
        WeatherManager.Instance?.Restore(data.weather, data.weatherNext, data.weatherNext2, data.weatherPeriod, data.stormStruckPeriod);
        ShuttleService.Instance?.Restore(data);
        if (legacy == null && plantSave != null && plantSave.exists)
            CenterCore.Spawn(new Vector2Int(plantSave.anchorX, plantSave.anchorY))
                      ?.RestoreState(data.version >= SaveData.FirstCivilizationVersion ? plantSave.level : 1, plantSave.fuel);

        var restored = new List<(TileItem tile, TileSave save)>();
        foreach (var ts in data.placedTiles)
        {
            if (legacy != null && legacy.TryTakePowerTile(ts)) continue;
            if (removedBatteries.TryTake(ts.itemName, 1)) continue;
            if (!lookup.TryGetValue(ts.itemName, out var itemData)) continue;
            if (itemData.tilePrefab == null) continue;

            int w = itemData.tileWidth,  h = itemData.tileHeight;
            var anchor = new Vector2Int(ts.anchorX, ts.anchorY);
            var tile   = gm.PlaceTile(anchor, itemData.tilePrefab, w, h, ts.rotation, itemData);

            if (tile == null) continue;
            restored.Add((tile, ts));

            if (tile is PlantTile pt)
            {
                SeedData seed = null;
                if (!string.IsNullOrEmpty(ts.seedDataName))
                    seedLookup.TryGetValue(ts.seedDataName, out seed);
                pt.RestoreFromSave((PlantTile.PlantState)ts.plantState, seed, ts.growthStage, ts.stageTimer);
                pt.RestoreFertility(ts.plotFertility);
            }
            else if (tile is CatKitchen kitchen)
            {
                kitchen.RestoreFromSave(CatKitchen.FindRecipe(ts.machineRecipe), ts.storageItems,
                                        Find(lookup, ts.smelterOutput?.itemName), ts.smelterOutput?.count ?? 0, ts.smelterProgress);
            }
            else if (tile is Composter composter)
            {
                composter.RestoreFromSave(ts.compostPoints, ts.smelterOutput?.count ?? 0, ts.smelterProgress);
            }
            else if (tile is Generator gen)
            {
                gen.RestoreFromSave(ts.generatorFuel);
            }
            else if (tile is AutoSeeder seeder)
            {
                if (!string.IsNullOrEmpty(ts.seederSeedItem) && lookup.TryGetValue(ts.seederSeedItem, out var seedItem))
                    seeder.RestoreLastSeed(seedItem);
                if (ts.storageItems != null)
                    foreach (var s in ts.storageItems)
                        if (s.count > 0 && !string.IsNullOrEmpty(s.itemName) && lookup.TryGetValue(s.itemName, out var boxSeed))
                            seeder.AddItem(boxSeed, s.count);
            }
            else if (tile is ItemStorage storage && ts.storageItems != null)
            {
                foreach (var s in ts.storageItems)
                {
                    if (s.count <= 0 || string.IsNullOrEmpty(s.itemName)) continue;
                    if (legacy != null && legacy.TryRefundItem(s.itemName, s.count)) continue;
                    if (removedBatteries.TryTake(s.itemName, s.count)) continue;
                    if (lookup.TryGetValue(s.itemName, out var stored))
                        storage.AddItem(stored, s.count);
                }
            }
            else if (tile is ResourceExtractor extractor)
            {
                if (!string.IsNullOrEmpty(ts.extractorDrill) && lookup.TryGetValue(ts.extractorDrill, out var drill))
                    extractor.TryEquip(drill);
            }
            else if (tile is Smelter smelter)
            {
                smelter.RestoreFromSave(Find(lookup, ts.smelterInput?.itemName),  ts.smelterInput?.count  ?? 0,
                                        Find(lookup, ts.smelterOutput?.itemName), ts.smelterOutput?.count ?? 0,
                                        ts.smelterProgress);
            }
        }

        Transform playerTr = PlayerTileController.Instance != null ? PlayerTileController.Instance.transform : null;
        foreach (var rbs in data.resourceBlocks)
        {
            if (!resLookup.TryGetValue(rbs.resourceDataName, out var resData)) continue;
            ResourceBlock.CreateSaved(resData, new Vector2(rbs.posX, rbs.posY), playerTr, rbs.remaining);
        }

        if (CenterCore.Current == null)
        {
            var plant = CenterCore.Spawn(CenterCore.FindFreeAnchorNearCenter());
            if (plant != null && legacy != null)
            {
                plant.RestoreState(1, 0f);
                plant.RestoreFromSave(plant.MaxFuel);
            }
        }

        if (legacy == null)
        {
            foreach (var (tile, ts) in restored)
            {
                if (ts.links == null) continue;
                foreach (var l in ts.links)
                {
                    var other = gm.GetTile(new Vector2Int(l.x, l.y));
                    if (other != null && LinkRules.CanLink(tile, other, out _, checkDistance: false))
                        TileItem.Link(tile, other);
                }
            }
        }
        else
        {
            LegacyMigration.AutoLink(gm, restored);
        }

        var quests = QuestManager.Instance;
        quests?.RestoreFromSave(quests.ResolveSavedIndex(data.currentQuestId, data.currentQuestIndex),
                                data.questObjectiveAmounts,
                                data.allQuestsDone);

        if (worldOnly) return;
        var ptc = PlayerTileController.Instance;
        if (ptc != null)
        {
            ptc.transform.position = new Vector3(data.playerX, data.playerY, 0f);
            if (CenterCore.Current != null) CenterCore.Current.MoveOutside(ptc.transform);
        }

        if (!ExploredMap.Restore(data.exploredCells)) ExploredMap.RevealAroundWorld(8);

        legacy?.GiveRefundsAndNotify(lookup, ptc != null ? ptc.transform.position : Vector3.zero);
        removedBatteries.GiveRefundsAndNotify(lookup, ptc != null ? ptc.transform.position : Vector3.zero);
    }

    private sealed class RemovedBatteryRefund
    {
        private const string BatteryItem      = "Battery";
        private const string LargeBatteryItem = "Item_LargeBattery";

        private int batteries;
        private int largeBatteries;

        public bool TryTake(string itemName, int count)
        {
            if (itemName == BatteryItem)      { batteries      += count; return true; }
            if (itemName == LargeBatteryItem) { largeBatteries += count; return true; }
            return false;
        }

        public void GiveRefundsAndNotify(Dictionary<string, FarmingItemData> lookup, Vector3 dropPos)
        {
            if (batteries + largeBatteries == 0) return;
            int ingots = batteries + largeBatteries * 2;
            Give(lookup, "Item_CopperIngot",  ingots,             dropPos);
            Give(lookup, "Item_IronIngot",    ingots,             dropPos);
            Give(lookup, "Item_CircuitBoard", largeBatteries,     dropPos);
            Give(lookup, "Item_LithiumIngot", largeBatteries * 2, dropPos);

            NotificationUI.Instance?.Show("업데이트: 배터리가 없어졌어요. 이제 전기는 '전력 중계탑'으로 멀리 넓혀요 (제작창 · 도움말 참고).");
            NotificationUI.Instance?.Show($"배터리 {batteries}개 · 대형 배터리 {largeBatteries}개는 만들 때 든 재료로 돌려드렸어요.");
        }

        private static void Give(Dictionary<string, FarmingItemData> lookup, string name, int amount, Vector3 dropPos)
        {
            if (amount <= 0 || Inventory.Instance == null || !lookup.TryGetValue(name, out var item)) return;
            Inventory.Instance.AddItemOrDrop(item, amount, dropPos);
        }
    }

    private static FarmingItemData Find(Dictionary<string, FarmingItemData> lookup, string name)
        => !string.IsNullOrEmpty(name) && lookup.TryGetValue(name, out var item) ? item : null;

    private sealed class LegacyMigration
    {
        private const string GeneratorItem = "Ganerator";
        private const string CoreItem      = "CenterCOre";
        private const int    CoreItemCoins = 10;
        private const int    LegacyLinkRadiusCells = 4;

        private int generators;
        private int coreItems;
        private int placedCores;

        public bool TryRefundItem(string itemName, int count)
        {
            if (itemName == GeneratorItem) { generators += count; return true; }
            if (itemName == CoreItem)      { coreItems  += count; return true; }
            return false;
        }

        public bool TryTakePowerTile(TileSave ts)
        {
            if (ts.subType == nameof(Generator) || ts.itemName == GeneratorItem)
            {
                generators++;
                return true;
            }
            if (ts.subType == nameof(CenterCore) || ts.itemName == CoreItem)
            {
                placedCores++;
                return true;
            }
            return false;
        }

        public static void AutoLink(GridManager gm, List<(TileItem tile, TileSave save)> restored)
        {
            if (gm == null) return;
            var buffer = new List<TileItem>();
            foreach (var (tile, _) in restored)
            {
                int r = tile switch
                {
                    ResourceExtractor e => e.SearchRadiusCells,
                    AutoHarvester h     => h.CoverageRadiusCells,
                    AutoSeeder sd       => sd.CoverageRadiusCells,
                    AutoSeller or Smelter or AutoFuelPipe => LegacyLinkRadiusCells,
                    _ => 0,
                };
                if (r <= 0) continue;

                gm.GetTilesNearArea(tile.FootprintMin, tile.FootprintMax, r, TileType.Storage, buffer);
                foreach (var t in buffer) TileItem.Link(tile, t);

                if (tile is not AutoFuelPipe) continue;
                gm.GetTilesNearArea(tile.FootprintMin, tile.FootprintMax, r, TileType.AutoFuelPipe, buffer);
                foreach (var t in buffer) TileItem.Link(tile, t);
                gm.GetTilesNearArea(tile.FootprintMin, tile.FootprintMax, r, TileType.CenterCore, buffer);
                foreach (var t in buffer) TileItem.Link(tile, t);
            }
        }

        public void GiveRefundsAndNotify(Dictionary<string, FarmingItemData> lookup, Vector3 dropPos)
        {
            var inv = Inventory.Instance;
            if (generators > 0 && inv != null)
            {
                if (lookup.TryGetValue("Item_IronIngot", out var iron))     inv.AddItemOrDrop(iron,   generators * 3, dropPos);
                if (lookup.TryGetValue("Item_CopperIngot", out var copper)) inv.AddItemOrDrop(copper, generators * 3, dropPos);
            }
            if (coreItems > 0) CurrencyManager.Instance?.Add((long)coreItems * CoreItemCoins);

            NotificationUI.Instance?.Show("업데이트: 발전기 · 센터코어가 맵 한가운데의 '중앙 발전소' 하나로 합쳐졌어요!");
            if (generators > 0)
                NotificationUI.Instance?.Show($"발전기 {generators}대는 철 주괴 · 구리 주괴 {generators * 3}개씩으로 돌려드렸어요.");
            if (placedCores > 0)
                NotificationUI.Instance?.Show("센터코어는 중앙 발전소로 합쳐졌어요 (문명 시설을 세워 발전시켜요). 연료도 가득 채워 뒀어요!");
            NotificationUI.Instance?.Show("이제 설비와 창고는 클릭 → [연결하기]로 직접 이어요 (기존 연결은 옮겨 뒀어요).");
        }
    }

    private static Dictionary<string, FarmingItemData> BuildItemLookup()
    {
        var d = new Dictionary<string, FarmingItemData>(StringComparer.Ordinal);
        foreach (var item in GameDataRegistry.FindAll<FarmingItemData>())
            if (!d.ContainsKey(item.name)) d[item.name] = item;
        return d;
    }

    private static Dictionary<string, SeedData> BuildSeedLookup()
    {
        var d = new Dictionary<string, SeedData>(StringComparer.Ordinal);
        foreach (var s in GameDataRegistry.FindAll<SeedData>())
            if (!d.ContainsKey(s.name)) d[s.name] = s;
        return d;
    }

    private static Dictionary<string, ResourceData> BuildResourceLookup()
    {
        var d = new Dictionary<string, ResourceData>(StringComparer.Ordinal);
        foreach (var r in GameDataRegistry.FindAll<ResourceData>())
            if (!d.ContainsKey(r.name)) d[r.name] = r;
        return d;
    }
}
