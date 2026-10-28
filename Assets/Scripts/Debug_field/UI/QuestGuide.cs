using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 퀘스트 길잡이: 다음 할 일 안내
public class QuestGuide : MonoBehaviour
{
    public struct Target
    {
        public bool          hasWorld;
        public Vector3       world;
        public float         worldLift;
        public string        worldLabel;
        public string        edgeLabel;
        public RectTransform ui;
        public string        uiLabel;
        public string        action;
        public string        why;
        public string        shortcutLabel;
        public Action        shortcut;
        public CraftingRecipe recipe;
        public CraftingRecipe goal;
    }

    public static QuestGuide Instance => Services.Get<QuestGuide>();

    public static Target Current => Instance != null ? Instance.current : default;

    public static event Action OnChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnChanged = null;
        oreCache.Clear();
        roundedOutline = null;
    }

    private const float ResolveInterval = 0.25f;
    private const int   MaxDepth        = 9;

    private Target current;
    private string lastActionKey;
    private float  nextResolve;

    public static void Create()
    {
        if (Instance != null) return;
        var go = new GameObject("QuestGuide");
        go.AddComponent<QuestGuide>();
    }

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }
        BuildWorldMarker();
        BuildOverlay();
    }

    private void OnDestroy()
    {
        Services.Unregister(this);
        if (markerRoot != null) Destroy(markerRoot.gameObject);
    }

    private void Update()
    {
        if (Time.unscaledTime >= nextResolve)
        {
            nextResolve = Time.unscaledTime + ResolveInterval;
            current = Resolve();
            string key = current.action + "|" + current.why + "|" + current.shortcutLabel;
            if (key != lastActionKey)
            {
                lastActionKey = key;
                OnChanged?.Invoke();
            }
        }
    }

    private void LateUpdate()
    {
        bool hide = PlayerTileController.Instance == null || PlayerTileController.IsPauseMenuOpen() || TutorialUI.IsShowing || JourneyUI.IsOpen;
        UpdateWorldMarker(!hide && current.hasWorld);
        UpdateWorldBubble(!hide && current.hasWorld && !PlayerTileController.IsAnyUIOpen());
        UpdateEdgeArrow(!hide && current.hasWorld && !PlayerTileController.IsAnyUIOpen());
        UpdateUiHighlight(hide || (IsHotbarSlot(current.ui) && PlayerTileController.IsAnyUIOpen()) ? null : current.ui);
    }

    private static bool IsHotbarSlot(RectTransform rt)
    {
        var bar = TilePlacementUI.Instance;
        if (rt == null || bar == null) return false;
        for (int i = 0; i < Inventory.HotbarSize; i++)
            if (bar.SlotRect(i) == rt) return true;
        return false;
    }

    private readonly HashSet<CraftingRecipe> cookChain = new();

    private Target Resolve()
    {
        var t  = new Target();
        cookChain.Clear();
        var qm = QuestManager.Instance;
        if (qm == null || qm.CurrentQuest == null || qm.ActiveObjectives == null) return t;

        foreach (var obj in qm.ActiveObjectives)
        {
            if (obj.IsComplete) continue;
            ResolveObjective(obj, ref t);
            break;
        }
        return t;
    }

    private void ResolveObjective(QuestObjective obj, ref Target t)
    {
        switch (obj.type)
        {
            case QuestObjectiveType.PlantSeed:     ResolvePlant(obj.targetItem, ref t, 0); break;
            case QuestObjectiveType.HarvestPlant:  ResolveHarvest(obj.targetItem, ref t, 0); break;
            case QuestObjectiveType.CollectItem:   ResolveObtain(obj.targetItem, obj.targetAmount, ref t, 0); break;
            case QuestObjectiveType.CraftItem:     ResolveCraft(obj.targetItem, ref t, 0); break;
            case QuestObjectiveType.PlaceTile:     ResolvePlace(obj.targetItem, ref t, 0); break;
            case QuestObjectiveType.SellItem:      ResolveSell(obj.targetItem, obj.targetAmount, ref t, 0); break;
            case QuestObjectiveType.UpgradeTech:   ResolveResearch(ref t); break;
            case QuestObjectiveType.CompleteOrder:
            case QuestObjectiveType.OrdersTotal:
            case QuestObjectiveType.Residents:     ResolveShuttle(ref t, 0); break;
            case QuestObjectiveType.CropVarieties: ResolveVarieties(ref t, 0); break;
            case QuestObjectiveType.CoreLevel:     ResolveCore(ref t, 0); break;
            case QuestObjectiveType.AutoHarvest:   ResolveAutoHarvest(ref t, 0); break;
        }
    }

    private static readonly List<TileItem> harvesterBuffer = new();
    private static readonly List<TileItem> plotsNear       = new();

    private void ResolveAutoHarvest(ref Target t, int depth)
    {
        var gm = GridManager.Instance;
        if (gm == null) return;
        gm.CollectTiles(TileType.AutoHarvester, harvesterBuffer);
        AutoHarvester best = null;
        int bestPlots = 0, bestGrowing = 0;
        PlantTile empty = null;
        foreach (var tile in harvesterBuffer)
        {
            if (tile is not AutoHarvester h || h.IsGhost) continue;
            gm.GetTilesNearArea(h.FootprintMin, h.FootprintMax, h.CoverageRadiusCells, TileType.Plant, plotsNear);
            int plots = 0, growing = 0;
            PlantTile firstEmpty = null;
            foreach (var p in plotsNear)
            {
                if (p is not PlantTile plot || plot.IsGhost) continue;
                plots++;
                if (plot.CurrentState != PlantTile.PlantState.Empty) growing++;
                else if (firstEmpty == null && plot.IsPowered) firstEmpty = plot;
            }
            if (best == null || growing > bestGrowing || (growing == bestGrowing && plots > bestPlots))
            {
                best = h; bestPlots = plots; bestGrowing = growing; empty = firstEmpty;
            }
        }

        if (best == null)
        {
            ResolvePlace(NetWorld.Item("AutoHarvester"), ref t, depth + 1);
            Why(ref t, "자동수확기로 거두기");
            return;
        }
        if (bestPlots == 0)
        {
            World(ref t, best, "곁에 밭!", "자동수확기");
            t.action = $"자동수확기 둘레 {best.CoverageRadiusCells}칸 안에 밭이 없어요 — 수확기 곁에 플랜트를 깔아요 (수확기에 마우스를 올리면 범위가 보여요)";
            return;
        }
        if (bestGrowing == 0 && empty != null)
        {
            var selected = PlayerTileController.Instance != null ? PlayerTileController.Instance.SelectedItem : null;
            var seed = selected != null && selected.itemType == FarmingItemType.Seed && Count(selected) > 0 ? selected : FirstSeedInInventory();
            if (seed == null)
            {
                ResolvePlant(null, ref t, depth + 1);
                Why(ref t, "자동수확기 곁 밭에 심기");
                return;
            }
            bool needSelect = SelectHint(seed, ref t);
            World(ref t, empty, "심기", "수확기 곁 빈 밭");
            if (needSelect) return;
            t.action = Pad.Active ? "수확기 곁 빈 밭 앞에서 RT로 심기 — 다 자라면 수확기가 거둬요"
                                  : "수확기 곁 빈 밭에 씨앗을 심어요 — 다 자라면 수확기가 알아서 거둬요";
            return;
        }
        World(ref t, best, "수확 중", "자동수확기");
        t.action = "자동수확기가 다 자란 작물을 알아서 거둬요 — 그동안 다른 일을 해도 돼요 (수확물은 이어 준 창고 · 가방으로)";
    }

    private void ResolvePlant(FarmingItemData seedItem, ref Target t, int depth)
    {
        if (depth > MaxDepth) return;
        var selected = PlayerTileController.Instance != null ? PlayerTileController.Instance.SelectedItem : null;

        var seed = seedItem;
        if (seed == null && selected != null && selected.itemType == FarmingItemType.Seed && Count(selected) > 0) seed = selected;
        if (seed == null) seed = FirstSeedInInventory();

        if (seed == null || Count(seed) <= 0)
        {
            if (CanBuy(seedItem))
            {
                Shop(ref t, $"{Ga(seedItem.itemName)} 없어요 — [상점](B)에서 사요", seedItem);
                return;
            }
            ResolveHarvest(null, ref t, depth + 1);
            if (string.IsNullOrEmpty(t.action)) t.action = "씨앗이 없어요 — 수확하면 씨앗이 나와요";
            return;
        }

        var plot = NearestPlot(p => p.CurrentState == PlantTile.PlantState.Empty && p.IsPowered)
                ?? NearestPlot(p => p.CurrentState == PlantTile.PlantState.Empty);
        if (plot == null)
        {
            var ready = NearestPlot(p => p.CurrentState == PlantTile.PlantState.ReadyToHarvest);
            if (ready != null) { World(ref t, ready, "수확!", "다 자란 작물"); t.action = "다 자란 작물을 먼저 거둬 밭을 비워요"; return; }
            var plotItem = NetWorld.Item("PlantTile");
            bool refilled = seedItem != null && NearestTile<AutoSeeder>(TileType.AutoSeeder, null) != null;
            if (Count(plotItem) > 0 || refilled)
            {
                ResolvePlace(plotItem, ref t, depth + 1);
                Why(ref t, refilled ? $"{seedItem.itemName} 심을 빈 밭 만들기" : "빈 밭 만들기");
                return;
            }
            var soon = SoonestPlot();
            if (soon == null)
            {
                ResolvePlace(plotItem, ref t, depth + 1);
                Why(ref t, "밭 만들기");
                return;
            }
            float left = soon.RemainingSeconds;
            World(ref t, soon, left >= 0f ? $"{Mathf.CeilToInt(left)}초" : "자라는 중", "자라는 밭");
            t.action = "밭이 다 찼어요 — 작물이 자라면 거두고 다시 심어요 (플랜트를 더 만들어 밭을 넓혀도 돼요)";
            return;
        }

        bool needSelect = SelectHint(seed, ref t);
        World(ref t, plot, plot.IsPowered ? "심기" : "전기 없음", "빈 밭");
        if (needSelect) return;
        t.action = !plot.IsPowered ? "빈 밭이 전기 범위 밖이에요 — 센터코어 둘레에 밭을 깔아요"
                 : Pad.Active ? "빈 밭 앞에서 RT로 심기" : "빈 밭을 클릭해 심어요 (누른 채 쓸면 여러 칸)";
    }

    private void ResolveHarvest(FarmingItemData crop, ref Target t, int depth)
    {
        if (depth > MaxDepth) return;
        var ready = NearestPlot(p => p.CurrentState == PlantTile.PlantState.ReadyToHarvest && Grows(p, crop));
        if (ready != null)
        {
            World(ref t, ready, "수확!", crop != null ? crop.itemName : "다 자란 작물");
            t.action = Pad.Active ? "다 자란 밭 앞에서 RT로 수확" : "다 자란 작물을 클릭해 거둬요";
            return;
        }

        var growing = NearestPlot(p => (p.CurrentState == PlantTile.PlantState.Growing || p.CurrentState == PlantTile.PlantState.Seeded) && Grows(p, crop));
        if (growing != null)
        {
            float left = growing.RemainingSeconds;
            if (left < 0f)
            {
                World(ref t, growing, "전기 없음", "멈춘 밭");
                t.action = "이 밭은 전기가 없어 자라지 않아요 — 센터코어 · 전력 중계탑 범위 안으로";
                return;
            }
            World(ref t, growing, $"{Mathf.CeilToInt(left)}초", "자라는 밭");
            t.action = $"자라는 중이에요 ({Mathf.CeilToInt(left)}초) — 그동안 다른 일을 해도 돼요";
            return;
        }

        var seed = crop != null ? SeedFor(crop) : null;
        ResolvePlant(seed, ref t, depth + 1);
        if (crop != null) Why(ref t, $"{crop.itemName} 키우기");
    }

    private void ResolveObtain(FarmingItemData item, int need, ref Target t, int depth)
    {
        if (item == null || depth > MaxDepth) return;

        var cook  = KitchenRecipeFor(item);
        if (cook != null) { ResolveCook(cook, ref t, depth + 1); return; }
        if (item.isCropProduct) { ResolveHarvest(item, ref t, depth + 1); return; }
        if (OreData(item) != null) { ResolveMine(item, ref t, depth + 1); return; }
        var smelt = FurnaceRecipeFor(item);
        if (smelt != null) { ResolveSmelt(smelt, ref t, depth + 1); return; }
        if (item.itemType == FarmingItemType.Seed)
        {
            if (CanBuy(item)) Shop(ref t, $"[상점](B)에서 {item.itemName} 사기", item);
            else ResolveHarvest(CropOf(item), ref t, depth + 1);
            return;
        }
        if (IsCompost(item)) { ResolveCompost(ref t, depth + 1); return; }
        if (RecipeFor(item) != null) { ResolveCraft(item, ref t, depth + 1); return; }
        t.action = $"{item.itemName} 모으기";
    }

    private static bool IsCompost(FarmingItemData item) => item != null && item.name == "Item_Compost";

    private void ResolveCompost(ref Target t, int depth)
    {
        var bin = NearestTile<Composter>(TileType.Composter, c => c.OutputCount > 0)
               ?? NearestTile<Composter>(TileType.Composter, c => c.IsWorking)
               ?? NearestTile<Composter>(TileType.Composter, null);
        if (bin == null)
        {
            ResolvePlace(NetWorld.Item("Item_Composter"), ref t, depth + 1);
            Why(ref t, "퇴비통에 씨앗을 넣어 퇴비 만들기");
            return;
        }

        string open  = Pad.Active ? "퇴비통 앞에서 A로 열기" : "퇴비통 클릭으로 열기";
        string click = Pad.Active ? "퇴비통 앞에서 A로" : "퇴비통을 클릭해";
        if (bin.OutputCount > 0)
        {
            World(ref t, bin, "받기", "퇴비통");
            t.action = $"{click} 퇴비 받기";
            return;
        }
        if (bin.IsWorking)
        {
            World(ref t, bin, "만드는 중", "퇴비통");
            t.action = $"퇴비가 되는 중이에요 ({Mathf.RoundToInt(bin.Progress01 * 100f)}%) — 씨앗을 더 넣어 두면 이어서 만들어요";
            return;
        }

        bool hasFeed = false;
        var inv = Inventory.Instance;
        if (inv != null)
            for (int i = 0; i < inv.SlotCount && !hasFeed; i++)
            {
                var s = inv.GetSlot(i);
                hasFeed = s != null && !s.IsEmpty && Composter.PointsOf(s.Item) > 0;
            }
        if (!hasFeed)
        {
            ResolveHarvest(null, ref t, depth + 1);
            Why(ref t, "거둔 씨앗을 퇴비통에 넣기");
            return;
        }

        World(ref t, bin, "씨앗 넣기", "퇴비통");
        var held = PlayerTileController.Instance != null ? PlayerTileController.Instance.SelectedItem : null;
        t.action = ComposterUI.IsOpen
            ? $"남는 씨앗 옆 [전부]를 눌러 넣어요 (씨앗 {bin.PointsPerCompost}개 = 퇴비 1개)"
            : held != null && Count(held) > 0 && Composter.PointsOf(held) > 0
            ? $"{click} 든 {held.itemName} 넣기 (씨앗 {bin.PointsPerCompost}개 = 퇴비 1개)"
            : $"{open} → 남는 씨앗 넣기 (씨앗 {bin.PointsPerCompost}개 = 퇴비 1개)";
    }

    private void ResolveMine(FarmingItemData ore, ref Target t, int depth)
    {
        var data  = OreData(ore);
        int grade = data != null ? data.minToolGrade : 1;
        var drill = BestDrill();
        if (drill == null || drill.toolGrade < grade)
        {
            var next = DrillOfGrade(grade);
            if (next != null && depth <= MaxDepth)
            {
                ResolveCraft(next, ref t, depth + 1);
                Why(ref t, $"티어{grade} 드릴로 {ore.itemName} 캐기");
            }
            else t.action = $"{Eul(ore.itemName)} 캘 드릴이 필요해요";
            return;
        }

        var block = NearestBlock(ore);
        if (block == null)
        {
            t.action = $"{ore.itemName} 광석을 다 캤어요 — 밤의 유성우가 새 광석을 떨어뜨려요";
            return;
        }
        t.hasWorld   = true;
        t.world      = block.transform.position + Vector3.up * 0.55f;
        t.worldLabel = "캐기";
        t.edgeLabel  = ore.itemName;

        var ptc = PlayerTileController.Instance;
        bool holding = ptc != null && ptc.SelectedItem != null && ptc.SelectedItem.IsDrill && ptc.CurrentToolGrade >= grade;
        if (!holding && SelectHint(drill, ref t)) return;
        t.action = Pad.Active ? "광석 앞에서 RT를 꾹 눌러 캐요" : "광석을 꾹 클릭해 캐요 (가까이 가야 해요)";
    }

    private void ResolveSmelt(CraftingRecipe r, ref Target t, int depth)
    {
        var ore = r.ingredients[0].item;
        int per = r.ingredients[0].amount;
        var smelter = NearestTile<Smelter>(TileType.Smelter, s => s.OutputItem == r.outputItem && s.OutputCount > 0)
                   ?? NearestTile<Smelter>(TileType.Smelter, s => s.InputItem == ore && s.InputCount > 0)
                   ?? NearestTile<Smelter>(TileType.Smelter, null);
        if (smelter == null)
        {
            ResolvePlace(NetWorld.Item("Item_Smelter"), ref t, depth + 1);
            Why(ref t, $"제련소에서 {r.outputItem.itemName} 녹이기");
            return;
        }

        string open  = Pad.Active ? "제련소 앞에서 A → 창에서" : "제련소 클릭 → 창에서";
        string click = Pad.Active ? "제련소 앞에서 A로" : "제련소를 클릭해";
        if (smelter.OutputItem == r.outputItem && smelter.OutputCount > 0)
        {
            World(ref t, smelter, "받기", "제련소");
            t.action = $"{click} {r.outputItem.itemName} 받기";
            return;
        }
        if (smelter.InputItem == ore && smelter.InputCount >= per)
        {
            World(ref t, smelter, "녹이는 중", "제련소");
            t.action = $"{Eul(ore.itemName)} 녹이는 중이에요 — 다 되면 제련소를 눌러 받아요";
            return;
        }
        if (Count(ore) < per)
        {
            ResolveMine(ore, ref t, depth + 1);
            Why(ref t, $"{r.outputItem.itemName} 녹이기 ({ore.itemName} {Count(ore)}/{per})");
            return;
        }
        World(ref t, smelter, "광석 넣기", "제련소");
        var other = smelter.InputItem;
        if (other != null && other != ore && smelter.InputCount > 0)
        {
            if (smelter.HasLeftover)
            {
                if (HoldHint(ore, ref t)) return;
                t.action = $"{click} {ore.itemName} 넣기 (남은 {other.itemName} {smelter.InputCount}개는 가방으로)";
                return;
            }
            World(ref t, smelter, smelter.IsPowered ? "녹이는 중" : "전기 없음", "제련소");
            t.action = smelter.IsPowered
                ? $"제련소가 {Eul(other.itemName)} 녹이는 중이에요 — 끝나면 {Eul(ore.itemName)} 넣어요"
                : "제련소에 전기가 안 들어와요 — 센터코어 · 전력 중계탑 범위 안으로 옮겨요";
            return;
        }
        if (PlayerTileController.QuickOreFor(smelter) == ore) { t.action = $"{click} {ore.itemName} 넣기"; return; }
        if (HoldHint(ore, ref t)) return;
        t.action = SmelterUI.IsOpen ? $"{ore.itemName} 옆 [전부]를 눌러 넣어요" : $"{open} {ore.itemName} [전부] 넣기";
    }

    private static bool HoldHint(FarmingItemData item, ref Target t)
    {
        int slot = SlotOf(item);
        if (item == null || slot < 0 || slot >= Inventory.HotbarSize) return false;
        if (!SelectHint(item, ref t)) return false;
        Why(ref t, $"든 채 {(Pad.Active ? "A" : "클릭")}하면 바로 넣어요");
        return true;
    }

    private void ResolveCook(CraftingRecipe r, ref Target t, int depth)
    {
        cookChain.Add(r);
        var dish = r.outputItem;
        var kitchen = NearestTile<CatKitchen>(TileType.Kitchen, k => k.OutputItem == dish && k.OutputCount > 0)
                   ?? NearestTile<CatKitchen>(TileType.Kitchen, k => k.Recipe == r && k.IsWorking)
                   ?? NearestTile<CatKitchen>(TileType.Kitchen, null);
        if (kitchen == null)
        {
            ResolvePlace(NetWorld.Item("Item_CatKitchen"), ref t, depth + 1);
            Why(ref t, $"주방에서 {dish.itemName} 요리");
            return;
        }

        string open = Pad.Active ? "주방 앞에서 A" : "주방 클릭";
        if (kitchen.OutputItem == dish && kitchen.OutputCount > 0)
        {
            World(ref t, kitchen, "받기", "고양이 주방");
            t.action = Pad.Active ? $"주방 앞에서 A로 {dish.itemName} 받기" : $"주방을 클릭해 {dish.itemName} 받기";
            return;
        }
        if (kitchen.Recipe == r && kitchen.IsWorking)
        {
            World(ref t, kitchen, "요리 중", "고양이 주방");
            t.action = $"{Eul(dish.itemName)} 요리하는 중이에요";
            return;
        }
        if (!kitchen.IsPowered)
        {
            World(ref t, kitchen, "전기 없음", "고양이 주방");
            t.action = "주방에 전기가 안 들어와요 — 센터코어 · 전력 중계탑 범위 안으로 옮겨요";
            return;
        }
        if (kitchen.Recipe != r && !kitchen.CanChangeRecipe)
        {
            var free = NearestTile<CatKitchen>(TileType.Kitchen, k => k.Recipe == r || k.CanChangeRecipe);
            if (free != null) kitchen = free;
            else
            {
                var other = NearestTile<CatKitchen>(TileType.Kitchen, k => !cookChain.Contains(k.Recipe));
                if (other == null)
                {
                    ResolvePlace(NetWorld.Item("Item_CatKitchen"), ref t, depth + 1);
                    Why(ref t, $"{dish.itemName} 요리할 주방 하나 더 (다른 주방은 {kitchen.Recipe.outputItem.itemName} 준비 중)");
                    return;
                }
                World(ref t, other, "메뉴 바꾸기", "고양이 주방");
                t.action = $"{open} → 메뉴판에서 {dish.itemName} 고르기 (넣어 둔 다른 재료는 가방으로 돌아와요)";
                return;
            }
        }
        foreach (var ing in r.ingredients)
        {
            if (ing.item == null || Count(ing.item) + kitchen.GetInput(ing.item) >= ing.amount) continue;
            ResolveObtain(ing.item, ing.amount, ref t, depth + 1);
            Why(ref t, $"{dish.itemName} 요리 ({ing.item.itemName} {Count(ing.item)}/{ing.amount})");
            return;
        }
        World(ref t, kitchen, "요리", "고양이 주방");
        t.action = $"{open} → {dish.itemName} → [재료 넣기]";
    }

    private void ResolveCraft(FarmingItemData item, ref Target t, int depth)
    {
        if (item == null || depth > MaxDepth) return;
        if (IsCompost(item)) { ResolveCompost(ref t, depth + 1); return; }
        var r = RecipeFor(item);
        if (r == null) { t.action = $"{Ga(item.itemName)} 필요해요"; return; }
        if (r.IsKitchenRecipe) { ResolveCook(r, ref t, depth + 1); return; }
        if (r.IsFurnaceRecipe) { ResolveSmelt(r, ref t, depth + 1); return; }
        if (t.goal == null) t.goal = r;

        var cm = CraftingManager.Instance;
        if (cm != null && !cm.IsRecipeUnlocked(r))
        {
            int need = Mathf.Max(r.requiredCoreLevel, CraftingManager.TierCoreLevel(r.tier));
            t.action = $"{Eun(item.itemName)} 문명 Lv.{need}에 열려요";
            return;
        }

        foreach (var ing in r.ingredients)
        {
            if (ing.item == null || Count(ing.item) >= ing.amount) continue;
            ResolveObtain(ing.item, ing.amount, ref t, depth + 1);
            Why(ref t, $"{item.itemName} 만들기 ({ing.item.itemName} {Count(ing.item)}/{ing.amount})");
            return;
        }

        if (r.station == CraftingStation.Smelter && cm != null && !cm.IsStationAvailable(CraftingStation.Smelter))
        {
            var smelter = NearestTile<Smelter>(TileType.Smelter, null);
            if (smelter == null)
            {
                ResolvePlace(NetWorld.Item("Item_Smelter"), ref t, depth + 1);
                Why(ref t, $"제련소 곁에서 {item.itemName} 만들기");
                return;
            }
            World(ref t, smelter, "여기 곁에서", "제련소");
            t.action = $"{Eun(item.itemName)} 제련소 곁(5칸)에서 만들어요 — 제련소로 가요";
            return;
        }

        t.recipe = r;
        string tab = CraftingTableUI.TabName(r);
        var ui = CraftingTableUI.Instance;
        if (ui == null || !ui.IsOpen)
        {
            t.ui            = HudDock.ButtonRect(HudDock.Slot.Craft);
            t.uiLabel       = "열기!";
            t.action        = Pad.Active ? $"십자키 ↑ 제작 → A로 {Eul(item.itemName)} 만들어요" : $"[제작](C)을 열어 {Eul(item.itemName)} 만들어요";
            t.shortcutLabel = "제작창 열기";
            t.shortcut      = () => CraftingTableUI.Instance?.Focus(r);
            return;
        }
        if (ui.SelectedRecipe == r && ui.CraftButtonRect != null)
        {
            t.ui     = ui.CraftButtonRect;
            t.uiLabel = "만들기!";
            t.action = $"[만들기]를 눌러 {Eul(item.itemName)} 만들어요";
            return;
        }
        if (ui.IsInCurrentTab(r) && ui.CardRect(r) != null)
        {
            t.ui     = ui.CardRect(r);
            t.uiLabel = "고르기!";
            t.action = $"{item.itemName} 칸을 눌러 골라요";
            return;
        }
        t.ui            = ui.TabRect(r);
        t.uiLabel       = $"{tab} 탭!";
        t.action        = $"{tab} 탭을 눌러요";
        t.shortcutLabel = "레시피 찾기";
        t.shortcut      = () => CraftingTableUI.Instance?.Focus(r);
    }

    private void ResolvePlace(FarmingItemData item, ref Target t, int depth)
    {
        if (item == null || depth > MaxDepth) return;
        if (Count(item) <= 0)
        {
            ResolveCraft(item, ref t, depth + 1);
            if (!string.IsNullOrEmpty(t.action) && string.IsNullOrEmpty(t.why)) Why(ref t, $"{item.itemName} 설치하기");
            return;
        }
        if (SelectHint(item, ref t)) return;

        string many = Count(item) > 1 ? (Pad.Active ? " (누른 채 걸으면 여러 개)" : " (누른 채 끌면 여러 개)") : "";
        t.action = Pad.Active ? $"RT로 {item.itemName} 설치{many}" : $"원하는 곳을 클릭해 {item.itemName} 설치{many}";
        if (NeedsPower(item) && !PlayerNearPower() && CenterCore.Current != null)
        {
            World(ref t, CenterCore.Current, "전기 범위", "센터코어");
            t.action = $"{Eun(item.itemName)} 전기가 있어야 일해요 — 센터코어 곁에 설치해요";
        }
    }

    private void ResolveSell(FarmingItemData item, int need, ref Target t, int depth)
    {
        if (item != null && Count(item) <= 0) { ResolveObtain(item, need, ref t, depth + 1); return; }
        Shop(ref t, $"[상점](B)을 열고 가방의 {item?.itemName} 누르기", item, sell: true);
    }

    private void ResolveResearch(ref Target t)
    {
        t.ui            = HudDock.ButtonRect(HudDock.Slot.Research);
        t.uiLabel       = "열기!";
        t.action        = Pad.Active ? "십자키 ← 연구 → 고른 연구를 A로 연구하기" : "[연구](T)에서 연구를 골라 [연구하기]";
        t.shortcutLabel = "연구 열기";
        t.shortcut      = () => { if (TechTreeUI.Instance != null && !TechTreeUI.Instance.IsOpen) TechTreeUI.Instance.Toggle(); };
    }

    private void ResolveShuttle(ref Target t, int depth)
    {
        var svc = ShuttleService.Instance;
        if (svc == null || depth > MaxDepth) return;
        string click = Pad.Active ? "셔틀 앞에서 A로" : "셔틀을 클릭해";

        if (svc.Shuttles.Count == 0)
        {
            var pad0 = NearestTile<ShuttlePad>(TileType.ShuttlePad, null);
            if (pad0 != null)
            {
                World(ref t, pad0, "셔틀 오는 중", "셔틀 착륙장");
                t.action = "셔틀이 착륙장으로 오고 있어요 — 잠깐만 기다려요";
                return;
            }
            if (ShuttleService.PadItem != null)
            {
                ResolvePlace(ShuttleService.PadItem, ref t, depth + 1);
                Why(ref t, "셔틀이 내려앉을 착륙장");
            }
            return;
        }

        // 1. 짐 다 채울 수 있는 셔틀
        ShuttleSave ready = null;
        foreach (var s in svc.Shuttles)
            if (!s.away && ShuttleService.PadOf(s) != null && ShuttleService.CanComplete(s)) { ready = s; break; }
        if (ready != null)
        {
            if (ShuttleUI.IsOpen && ShuttleUI.Instance.LoadButtonRect != null)
            {
                t.ui      = ShuttleUI.Instance.LoadButtonRect;
                t.uiLabel = "출발!";
                t.action  = "[싣고 출발!]을 눌러요";
                return;
            }
            World(ref t, ShuttleService.PadOf(ready), "싣고 출발!", "이주 셔틀");
            t.action = $"{click} 짐을 싣고 고향별로 보내요";
            t.shortcutLabel = "셔틀 창 열기";
            t.shortcut      = () => ShuttleUI.Instance?.Open();
            return;
        }

        // 2. 모자란 짐 구하기
        CargoLine bestLine = null;
        float bestScore = float.MaxValue;
        foreach (var s in svc.Shuttles)
        {
            if (s.away) continue;
            float missing = 0f;
            CargoLine first = null;
            foreach (var line in s.cargo)
            {
                if (line.Missing <= 0) continue;
                var it = NetWorld.Item(line.item);
                if (it == null || !Obtainable(it, 0)) { missing = float.MaxValue; break; }
                int lack = line.Missing - Count(it);
                if (lack <= 0) continue;
                missing += lack * Mathf.Max(1, it.basePrice);
                if (first == null) first = line;
            }
            if (first != null && missing < bestScore) { bestScore = missing; bestLine = first; }
        }
        if (bestLine != null)
        {
            var item = NetWorld.Item(bestLine.item);
            ResolveObtain(item, bestLine.Missing, ref t, depth + 1);
            Why(ref t, $"셔틀 짐 ({item.itemName} {Count(item)}/{bestLine.Missing})");
            if (t.ui == null && !t.hasWorld)
            {
                t.shortcutLabel = "셔틀 창 열기";
                t.shortcut      = () => ShuttleUI.Instance?.Open();
            }
            return;
        }

        // 3. 셔틀 귀환 대기
        ShuttleSave soonest = null;
        bool allAway = true;
        foreach (var s in svc.Shuttles)
        {
            if (!s.away) { allAway = false; continue; }
            if (soonest == null || s.timer < soonest.timer) soonest = s;
        }
        if (allAway && soonest != null)
        {
            int sec = Mathf.CeilToInt(soonest.timer);
            t.action = soonest.cats.Count > 0
                ? $"셔틀이 {soonest.family} 가족을 태우고 오는 중이에요 ({sec}초) — 그동안 작물을 길러요"
                : $"셔틀이 착륙장으로 오는 중이에요 ({sec}초)";
            return;
        }

        t.ui            = ShuttleUI.IsOpen ? null : HudDock.ButtonRect(HudDock.Slot.Shuttle);
        t.uiLabel       = "열기!";
        t.action        = "지금 셔틀 짐은 아직 구하기 어려워요 — 셔틀 창에서 [다른 짐 받기]";
        t.shortcutLabel = "셔틀 창 열기";
        t.shortcut      = () => ShuttleUI.Instance?.Open();
    }

    private void ResolveVarieties(ref Target t, int depth)
    {
        var growing = NearestPlot(p => p.CurrentSeed != null && !HarvestLog.Contains(CropOf(p.CurrentSeed)));
        if (growing != null) { ResolveHarvest(CropOf(growing.CurrentSeed), ref t, depth + 1); return; }

        var inv = Inventory.Instance;
        if (inv != null)
            for (int i = 0; i < inv.SlotCount; i++)
            {
                var s = inv.GetSlot(i);
                if (s == null || s.IsEmpty || s.Item.itemType != FarmingItemType.Seed) continue;
                var crop = CropOf(s.Item.seedData);
                if (crop == null || HarvestLog.Contains(crop)) continue;
                ResolvePlant(s.Item, ref t, depth + 1);
                Why(ref t, $"새 작물 {crop.itemName} 길러 보기");
                return;
            }
        Shop(ref t, "아직 안 길러 본 작물의 씨앗을 [상점](B)에서 사서 길러요 (\"처음!\" 딱지)", ShopUI.FirstNewSeed());
    }

    private void ResolveCore(ref Target t, int depth)
    {
        var core = CenterCore.Current;
        var next = core != null ? core.NextStage : null;
        if (next == null) return;

        foreach (var r in next.requirements)
        {
            if (CoreProgress.IsMet(r)) continue;
            switch (r.type)
            {
                case CoreRequirementType.Facility:
                    var off = UnpoweredPlaced(r.item);
                    if (off != null)
                    {
                        World(ref t, off, "전기 없음", r.item.itemName);
                        t.action = $"{r.item.itemName}에 전기가 안 들어와요 — 센터코어 · 전력 중계탑 범위 안으로 옮겨요";
                        return;
                    }
                    ResolvePlace(r.item, ref t, depth + 1);
                    Why(ref t, $"문명 조건: {CoreProgress.Label(r)} {CoreProgress.ProgressText(r)}");
                    return;
                case CoreRequirementType.CropVarieties:
                    ResolveVarieties(ref t, depth + 1);
                    Why(ref t, $"문명 조건: 작물 {CoreProgress.ProgressText(r)}종");
                    return;
                case CoreRequirementType.Orders:
                case CoreRequirementType.Residents:
                    ResolveShuttle(ref t, depth + 1);
                    Why(ref t, r.type == CoreRequirementType.Residents ? $"문명 조건: 주민 {CoreProgress.ProgressText(r)}마리"
                                                                        : $"문명 조건: 셔틀 {CoreProgress.ProgressText(r)}번");
                    return;
                case CoreRequirementType.Coins:
                    if (ShuttleService.Instance != null && ShuttleService.Instance.CompletableCount() > 0)
                    {
                        ResolveShuttle(ref t, depth + 1);
                        Why(ref t, $"발전 자금 {CoreProgress.ProgressText(r)}코인");
                        return;
                    }
                    t.ui      = CoreProgressHUD.ChipRect(r) ?? HudDock.ButtonRect(HudDock.Slot.Shop);
                    t.uiLabel = "모으는 중";
                    t.action  = $"발전 자금 {CoreProgress.ProgressText(r)}코인 — 셔틀 짐값 · 작물 판매로 모아요";
                    t.shortcutLabel = "상점 열기";
                    t.shortcut      = () => { if (ShopUI.Instance != null && !ShopUI.Instance.IsOpen) ShopUI.Instance.Toggle(); };
                    return;
                case CoreRequirementType.Research:
                    ResolveResearch(ref t);
                    return;
            }
        }
        World(ref t, core, "곧 발전!", "센터코어");
        t.action = "조건을 모두 채웠어요 — 센터코어가 곧 발전해요!";
    }

    private static void Why(ref Target t, string goal)
    {
        if (string.IsNullOrEmpty(goal)) return;
        t.why = string.IsNullOrEmpty(t.why) ? goal : t.why + " → " + goal;
    }

    private static bool SelectHint(FarmingItemData item, ref Target t)
    {
        var ptc = PlayerTileController.Instance;
        if (item == null || ptc == null || ptc.SelectedItem == item) return false;
        int slot = SlotOf(item);
        var hotbar = TilePlacementUI.Instance;
        if (slot >= 0 && hotbar != null && hotbar.SlotRect(slot) != null)
        {
            t.ui     = hotbar.SlotRect(slot);
            t.uiLabel = Pad.Active ? "LB/RB" : $"{slot + 1}번 키";
            t.action = Pad.Active ? $"LB/RB로 {item.itemName} 고르기" : $"{slot + 1}번 칸 {item.itemName} 고르기 (숫자 {slot + 1} 키)";
            return true;
        }
        t.ui     = InventoryUI.Instance != null && InventoryUI.Instance.IsOpen ? null : HudDock.ButtonRect(HudDock.Slot.Bag);
        t.uiLabel = "가방 열기";
        t.action = Pad.Active ? $"Y로 가방을 열어 {Eul(item.itemName)} 핫바로 옮겨요"
                              : $"[가방](Tab)에서 {Eul(item.itemName)} 아래 핫바 칸으로 끌어 놓아요";
        return true;
    }

    private static void Shop(ref Target t, string action, FarmingItemData item = null, bool sell = false)
    {
        var shop = ShopUI.Instance;
        if (shop != null && shop.IsOpen && item != null)
        {
            var rect = sell ? shop.BagSlotRect(item) : shop.TileRect(item);
            if (rect != null)
            {
                string press = Pad.Active ? "A로" : "눌러";
                t.ui      = rect;
                t.uiLabel = sell ? "팔기!" : "사기!";
                t.action  = sell ? $"오른쪽 가방의 {Eul(item.itemName)} {press} 팔아요" : $"왼쪽 가게에서 {Eul(item.itemName)} {press} 사요";
                return;
            }
        }
        t.ui            = HudDock.ButtonRect(HudDock.Slot.Shop);
        t.uiLabel       = "열기!";
        t.action        = Pad.Active ? action.Replace("[상점](B)", "상점(십자키 →)") : action;
        t.shortcutLabel = "상점 열기";
        t.shortcut      = () => { if (ShopUI.Instance != null && !ShopUI.Instance.IsOpen) ShopUI.Instance.Toggle(); };
    }

    private static void World(ref Target t, TileItem tile, string label, string edge)
    {
        if (tile == null) return;
        var gm = GridManager.Instance;
        t.hasWorld   = true;
        t.worldLabel = label;
        t.edgeLabel  = edge;
        if (gm == null) { t.world = tile.transform.position; return; }
        var min = gm.GridToWorld(tile.FootprintMin);
        var max = gm.GridToWorld(tile.FootprintMax);
        t.world = new Vector3((min.x + max.x) * 0.5f, max.y + gm.CellSize * 0.45f, 0f);
        if (tile is ShuttlePad pad) t.worldLift = Mathf.Max(0f, pad.HintAnchor.y - t.world.y);
    }

    private static bool Obtainable(FarmingItemData item, int depth)
    {
        if (item == null || depth > 3) return false;
        if (Count(item) > 0) return true;
        if (item.isGolden) return false;
        var cook = KitchenRecipeFor(item);
        if (cook != null)
        {
            if (!CatKitchen.IsUnlocked(cook)) return false;
            foreach (var ing in cook.ingredients) if (!Obtainable(ing.item, depth + 1)) return false;
            return true;
        }
        if (item.isCropProduct)
        {
            var seed = SeedFor(item);
            return seed != null && (Count(seed) > 0 || CanBuy(seed) || NearestPlot(p => Grows(p, item) && p.CurrentState != PlantTile.PlantState.Empty) != null);
        }
        if (OreData(item) != null) return true;
        var r = RecipeFor(item);
        return r == null || CraftingManager.Instance == null || CraftingManager.Instance.IsRecipeUnlocked(r) || r.IsFurnaceRecipe;
    }

    public static string HowToGet(FarmingItemData item, bool brief = false)
    {
        if (item == null) return "";
        if (KitchenRecipeFor(item) != null) return brief ? "주방 요리" : "고양이 주방에서 요리해요";
        if (item.isCropProduct)
        {
            if (item.isGolden) return brief ? "황금 작물" : "아주 가끔 열리는 황금 작물이에요";
            if (brief) return $"{item.itemName} 기르기";
            var seed = SeedFor(item);
            return seed != null ? $"{Eul(seed.itemName)} 심어 거둬요" : "길러서 거둬요";
        }
        var ore = OreData(item);
        if (ore != null)
        {
            if (brief) return ore.minToolGrade > 1 ? $"광석 캐기 · 티어{ore.minToolGrade} 드릴" : "광석 캐기";
            return ore.minToolGrade > 1 ? $"티어{ore.minToolGrade} 드릴로 광석을 캐요" : "광석을 캐요";
        }
        var smelt = FurnaceRecipeFor(item);
        if (smelt != null && smelt.ingredients.Count > 0 && smelt.ingredients[0].item != null)
            return brief ? $"제련소에서 {smelt.ingredients[0].item.itemName} 녹이기" : $"제련소에 {Eul(smelt.ingredients[0].item.itemName)} 넣어 녹여요";
        if (item.itemType == FarmingItemType.Seed)
            return CanBuy(item) ? (brief ? "상점" : "상점(B)에서 사요") : (brief ? "수확하면 나와요" : "작물을 거두면 나와요");
        if (IsCompost(item)) return brief ? "퇴비통" : "퇴비통에 씨앗을 넣어 만들어요";
        if (RecipeFor(item) != null) return brief ? "제작" : "제작창에서 만들어요";
        return "";
    }

    private static bool CanBuy(FarmingItemData item)
        => item != null && ShopManager.IsBuyable(item) && item.unlockCoreLevel <= CenterCore.CurrentLevel;

    private static int Count(FarmingItemData item) => item != null && Inventory.Instance != null ? Inventory.Instance.GetCount(item) : 0;

    private static int SlotOf(FarmingItemData item)
    {
        var inv = Inventory.Instance;
        if (inv == null || item == null) return -1;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            var s = inv.GetSlot(i);
            if (s != null && !s.IsEmpty && s.Item == item) return i;
        }
        return -1;
    }

    private static FarmingItemData FirstSeedInInventory()
    {
        var inv = Inventory.Instance;
        if (inv == null) return null;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            var s = inv.GetSlot(i);
            if (s != null && !s.IsEmpty && s.Item.itemType == FarmingItemType.Seed) return s.Item;
        }
        return null;
    }

    private static FarmingItemData BestDrill()
    {
        var inv = Inventory.Instance;
        FarmingItemData best = null;
        if (inv == null) return null;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            var s = inv.GetSlot(i);
            if (s == null || s.IsEmpty || !s.Item.IsDrill) continue;
            if (best == null || s.Item.toolGrade > best.toolGrade) best = s.Item;
        }
        return best;
    }

    private static FarmingItemData DrillOfGrade(int grade)
    {
        FarmingItemData best = null;
        foreach (var it in GameDataRegistry.FindAll<FarmingItemData>())
            if (it != null && it.IsDrill && it.toolGrade >= grade && (best == null || it.toolGrade < best.toolGrade || (it.toolGrade == best.toolGrade && it.Tier < best.Tier)))
                best = it;
        return best;
    }

    private static FarmingItemData CropOf(SeedData seed)
    {
        if (seed?.drops == null) return null;
        foreach (var d in seed.drops)
            if (d.item != null && d.item.isCropProduct) return d.item;
        return null;
    }

    private static FarmingItemData CropOf(FarmingItemData seedItem) => seedItem != null ? CropOf(seedItem.seedData) : null;

    private static FarmingItemData SeedFor(FarmingItemData crop)
    {
        foreach (var it in GameDataRegistry.FindAll<FarmingItemData>())
            if (it != null && it.itemType == FarmingItemType.Seed && it.seedData != null && CropOf(it.seedData) == crop) return it;
        return null;
    }

    private static bool Grows(PlantTile p, FarmingItemData crop) => crop == null || (p.CurrentSeed != null && CropOf(p.CurrentSeed) == crop);

    private static CraftingRecipe RecipeFor(FarmingItemData item)
    {
        var cm = CraftingManager.Instance;
        if (cm == null || item == null) return null;
        CraftingRecipe found = null;
        foreach (var r in cm.Recipes)
        {
            if (r == null || r.outputItem != item) continue;
            if (found == null || (cm.IsRecipeUnlocked(r) && !cm.IsRecipeUnlocked(found))) found = r;
        }
        return found;
    }

    private static CraftingRecipe KitchenRecipeFor(FarmingItemData item)
    {
        var r = RecipeFor(item);
        return r != null && r.IsKitchenRecipe ? r : null;
    }

    private static CraftingRecipe FurnaceRecipeFor(FarmingItemData item)
    {
        var cm = CraftingManager.Instance;
        if (cm == null) return null;
        foreach (var r in cm.Recipes)
            if (r != null && r.IsFurnaceRecipe && r.outputItem == item) return r;
        return null;
    }

    private static readonly Dictionary<FarmingItemData, ResourceData> oreCache = new();

    private static ResourceData OreData(FarmingItemData item)
    {
        if (item == null) return null;
        if (oreCache.TryGetValue(item, out var cached)) return cached;
        ResourceData found = null;
        foreach (var d in GameDataRegistry.FindAll<ResourceData>())
            if (d != null && d.dropItem == item) { found = d; break; }
        oreCache[item] = found;
        return found;
    }

    private static ResourceBlock NearestBlock(FarmingItemData ore)
    {
        Vector2 me = Me;
        ResourceBlock best = null;
        float bestD = float.MaxValue;
        foreach (var b in ResourceBlock.Active)
        {
            if (b == null || b.DropItem != ore || b.RemainingResources <= 0) continue;
            float d = ((Vector2)b.transform.position - me).sqrMagnitude;
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    private static readonly List<TileItem> tileBuffer = new();

    private static PlantTile NearestPlot(Func<PlantTile, bool> pred)
    {
        var gm = GridManager.Instance;
        if (gm == null) return null;
        gm.CollectTiles(TileType.Plant, tileBuffer);
        Vector2 me = Me;
        PlantTile best = null;
        float bestD = float.MaxValue;
        foreach (var tile in tileBuffer)
        {
            if (tile is not PlantTile p || p.IsGhost || !pred(p)) continue;
            float d = ((Vector2)p.transform.position - me).sqrMagnitude;
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    private static PlantTile SoonestPlot()
    {
        var gm = GridManager.Instance;
        if (gm == null) return null;
        gm.CollectTiles(TileType.Plant, tileBuffer);
        PlantTile best = null;
        float bestLeft = float.MaxValue;
        foreach (var tile in tileBuffer)
        {
            if (tile is not PlantTile p || p.IsGhost || p.CurrentState == PlantTile.PlantState.Empty) continue;
            float left = p.RemainingSeconds;
            if (left < 0f) left = 9999f;
            if (left < bestLeft) { bestLeft = left; best = p; }
        }
        return best;
    }

    private static T NearestTile<T>(TileType type, Func<T, bool> pred) where T : TileItem
    {
        var gm = GridManager.Instance;
        if (gm == null) return null;
        gm.CollectTiles(type, tileBuffer);
        Vector2 me = Me;
        T best = null;
        float bestD = float.MaxValue;
        foreach (var tile in tileBuffer)
        {
            if (tile is not T x || x.IsGhost || (pred != null && !pred(x))) continue;
            float d = x.DistanceFrom(me);
            if (d < bestD) { bestD = d; best = x; }
        }
        return best;
    }

    private static TileItem UnpoweredPlaced(FarmingItemData item)
    {
        var gm = GridManager.Instance;
        var em = EnergyManager.Instance;
        if (gm == null || em == null || item == null) return null;
        foreach (var t in gm.GetDynamicTiles())
            if (t != null && !t.IsGhost && t.SourceItem == item && !em.HasEnergy(t.FootprintMin, t.FootprintMax)) return t;
        return null;
    }

    private static bool NeedsPower(FarmingItemData item)
    {
        if (item == null || item.tilePrefab == null) return false;
        var tile = item.tilePrefab.GetComponent<TileItem>();
        return tile != null && tile.TileType is TileType.Plant or TileType.Smelter or TileType.Kitchen or TileType.Civilization or TileType.GrowthBooster;
    }

    private static bool PlayerNearPower()
    {
        var gm = GridManager.Instance;
        var em = EnergyManager.Instance;
        if (gm == null || em == null) return true;
        var c = gm.WorldToGrid(Me);
        return em.HasEnergy(c);
    }

    private static bool HasBatchim(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        char c = word[word.Length - 1];
        if (c >= 0xAC00 && c <= 0xD7A3) return (c - 0xAC00) % 28 != 0;
        // 숫자는 읽는 소리 기준
        return "013678".IndexOf(c) >= 0;
    }

    private static string Eul(string w) => w + (HasBatchim(w) ? "을" : "를");
    private static string Ga(string w)  => w + (HasBatchim(w) ? "이" : "가");
    private static string Eun(string w) => w + (HasBatchim(w) ? "은" : "는");

    private static Vector2 Me => PlayerTileController.Instance != null ? (Vector2)PlayerTileController.Instance.transform.position : Vector2.zero;

    private static readonly Color Accent = CuteUIKit.Strawberry;

    private Transform      markerRoot;
    private SpriteRenderer arrowSr, ringSr;
    private TextMeshPro    markerLabel;
    private Vector3        shownWorld;
    private bool           markerShown;

    private static readonly string[] ArrowRows =
    {
        "..#######..",
        "..#wwwww#..",
        "..#wwwww#..",
        "..#wwwww#..",
        "###wwwww###",
        "#wwwwwwwww#",
        ".#wwwwwww#.",
        "..#wwwww#..",
        "...#www#...",
        "....#w#....",
        ".....#.....",
    };

    private void BuildWorldMarker()
    {
        markerRoot = new GameObject("QuestGuideMarker").transform;
        arrowSr = LabArt.Make("Arrow", LabArt.Pixel("guideArrow", ArrowRows, "##7A5A4F w#F7879F", new Vector2(0.5f, 0f)),
                              Vector3.zero, 520, Color.white, markerRoot);
        arrowSr.transform.localScale = Vector3.one * 1.2f;
        ringSr = LabArt.Make("Ring", LabArt.Ring(), Vector3.zero, 519, new Color(Accent.r, Accent.g, Accent.b, 0.8f), markerRoot);
        markerLabel = LabArt.Label(markerRoot, "", 3f, Color.white, Vector3.zero, 521);
        markerLabel.gameObject.SetActive(false);
        var layers = SortingLayer.layers;
        int top = layers.Length > 0 ? layers[layers.Length - 1].id : 0;
        arrowSr.sortingLayerID = ringSr.sortingLayerID = top;
        markerRoot.gameObject.SetActive(false);
    }

    private void UpdateWorldMarker(bool show)
    {
        if (markerRoot == null) return;
        if (markerRoot.gameObject.activeSelf != show) markerRoot.gameObject.SetActive(show);
        if (!show) { markerShown = false; return; }

        if (!markerShown || (shownWorld - current.world).sqrMagnitude > 4f) shownWorld = current.world;
        else shownWorld = Vector3.Lerp(shownWorld, current.world, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
        markerShown = true;

        float t   = Time.unscaledTime;
        float bob = Mathf.Abs(Mathf.Sin(t * 4.2f)) * 0.22f;
        arrowSr.transform.position = shownWorld + Vector3.up * (current.worldLift + 0.12f + bob);
        bubbleAnchor = shownWorld + Vector3.up * (current.worldLift + 1.0f + bob);

        float k = (t * 0.9f) % 1f;
        ringSr.transform.position   = shownWorld + Vector3.down * 0.55f;
        ringSr.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.4f, k);
        ringSr.color = new Color(Accent.r, Accent.g, Accent.b, (1f - k) * 0.85f);
    }

    private RectTransform overlay;
    private RectTransform edgeRT;
    private RectTransform edgeTip;
    private TextMeshProUGUI edgeText;
    private RectTransform hlRT;
    private Image         hlFrame;
    private RectTransform hlPointer;

    private Vector3         bubbleAnchor;
    private RectTransform   worldBubble, uiBubble;
    private TextMeshProUGUI worldBubbleText, uiBubbleText;

    private RectTransform MakeBubble(string name, float fontSize, out TextMeshProUGUI label)
    {
        var pill = CuteUIKit.Pill(overlay, "", CuteUIKit.Cream, Color.white, fontSize, 14, new RectOffset(14, 14, 6, 8), out label, fitSelf: true);
        pill.name = name;
        pill.anchorMin = pill.anchorMax = new Vector2(0.5f, 0.5f);
        pill.pivot = new Vector2(0.5f, 0f);
        var fill = CuteUIKit.Layer(pill, "Fill", CuteUIKit.Rounded(12), Accent, 3f);
        fill.transform.SetAsFirstSibling();
        label.outlineWidth = 0.18f;
        label.outlineColor = new Color32(170, 70, 95, 255);
        foreach (var g in pill.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
        pill.gameObject.SetActive(false);
        return pill;
    }

    private void UpdateWorldBubble(bool show)
    {
        var cam = Camera.main;
        string text = current.worldLabel;
        show &= cam != null && !string.IsNullOrEmpty(text);
        Vector3 sp = show ? cam.WorldToScreenPoint(bubbleAnchor) : Vector3.zero;
        show &= sp.z > 0f && sp.x > 0f && sp.x < Screen.width && sp.y > 0f && sp.y < Screen.height;
        if (worldBubble.gameObject.activeSelf != show) worldBubble.gameObject.SetActive(show);
        if (!show) return;
        if (worldBubbleText.text != text) worldBubbleText.text = text;
        sp = AvoidHud(sp, worldBubble);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlay, sp, null, out var local);
        worldBubble.anchoredPosition = local;
    }

    private Vector3 AvoidHud(Vector3 sp, RectTransform bubble)
    {
        float scale = overlay.lossyScale.x > 0f ? overlay.lossyScale.x : 1f;
        float halfW = bubble.rect.width * 0.5f * scale;
        float h     = bubble.rect.height * scale;
        if (ScreenRect(QuestUI.Instance != null ? QuestUI.Instance.transform as RectTransform : null, out var quest)
            && new Rect(sp.x - halfW, sp.y, halfW * 2f, h).Overlaps(quest))
            sp.x = quest.xMax + halfW + 8f * scale;
        if (ScreenRect(CoreProgressHUD.CardRect, out var civ)
            && new Rect(sp.x - halfW, sp.y, halfW * 2f, h).Overlaps(civ))
            sp.y = civ.yMin - h - 6f * scale;
        return sp;
    }

    private bool ScreenRect(RectTransform rt, out Rect rect)
    {
        rect = default;
        if (rt == null || !rt.gameObject.activeInHierarchy) return false;
        rt.GetWorldCorners(corners);
        rect = Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        return true;
    }
    private RectTransform hlTarget;

    private const float InsetSide = 54f, InsetTop = 100f, InsetBottom = 150f;

    private void BuildOverlay()
    {
        var go = new GameObject("QuestGuideOverlay", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        // 창 위, 퀘스트 카드 아래
        UIStyleKit.CreateOverlayCanvas(go, 430).GetComponent<GraphicRaycaster>().enabled = false;
        overlay = (RectTransform)go.transform;

        var e = new GameObject("Edge", typeof(RectTransform));
        e.transform.SetParent(overlay, false);
        edgeRT = (RectTransform)e.transform;
        edgeRT.anchorMin = edgeRT.anchorMax = new Vector2(0.5f, 0.5f);
        edgeRT.sizeDelta = new Vector2(34f, 34f);
        var bg = CuteUIKit.Layer(edgeRT, "Bg", CuteUIKit.Rounded(17), Accent);
        bg.rectTransform.offsetMin = bg.rectTransform.offsetMax = Vector2.zero;
        bg.raycastTarget = false;
        var tipGO = new GameObject("Tip", typeof(RectTransform));
        tipGO.transform.SetParent(edgeRT, false);
        var tipImg = tipGO.AddComponent<Image>();
        tipImg.sprite = LabArt.Pixel("guideEdge", new[]
        {
            "....##....",
            "...####...",
            "..######..",
            ".########.",
            "##########",
            "...####...",
            "...####...",
        }, "##FFFFFF", new Vector2(0.5f, 0.5f));
        tipImg.raycastTarget = false;
        edgeTip = tipImg.rectTransform;
        edgeTip.sizeDelta = new Vector2(20f, 14f);
        edgeText = CuteUIKit.Text(edgeRT, "", 12f, CuteUIKit.Cocoa, FontStyles.Bold);
        edgeText.outlineWidth = 0.25f;
        edgeText.outlineColor = new Color32(255, 247, 234, 255);
        edgeText.raycastTarget = false;
        var trt = edgeText.rectTransform;
        trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0f);
        trt.pivot     = new Vector2(0.5f, 1f);
        trt.sizeDelta = new Vector2(140f, 32f);
        edgeRT.gameObject.SetActive(false);

        var h = new GameObject("Highlight", typeof(RectTransform));
        h.transform.SetParent(overlay, false);
        hlRT = (RectTransform)h.transform;
        hlRT.anchorMin = hlRT.anchorMax = new Vector2(0.5f, 0.5f);
        hlFrame = h.AddComponent<Image>();
        hlFrame.sprite = RoundedOutline();
        hlFrame.type   = Image.Type.Sliced;
        hlFrame.color  = Accent;
        hlFrame.raycastTarget = false;

        var p = new GameObject("Pointer", typeof(RectTransform));
        p.transform.SetParent(hlRT, false);
        var pImg = p.AddComponent<Image>();
        pImg.sprite = LabArt.Pixel("guideArrow", ArrowRows, "##7A5A4F w#F7879F", new Vector2(0.5f, 0f));
        pImg.raycastTarget = false;
        hlPointer = pImg.rectTransform;
        hlPointer.sizeDelta = new Vector2(36f, 36f);
        hlRT.gameObject.SetActive(false);

        worldBubble = MakeBubble("WorldBubble", 17f, out worldBubbleText);
        uiBubble    = MakeBubble("UiBubble", 14f, out uiBubbleText);
    }

    private void UpdateEdgeArrow(bool want)
    {
        var cam = Camera.main;
        bool show = want && cam != null;
        Vector3 sp = Vector3.zero;
        if (show)
        {
            sp = cam.WorldToScreenPoint(current.world);
            show = !(sp.x > 0f && sp.x < Screen.width && sp.y > 0f && sp.y < Screen.height);
        }
        if (edgeRT.gameObject.activeSelf != show) edgeRT.gameObject.SetActive(show);
        if (!show) return;

        float scale = overlay.lossyScale.x > 0f ? overlay.lossyScale.x : 1f;
        Vector2 half = new Vector2(Screen.width, Screen.height) * 0.5f / scale;
        Vector2 dir  = new Vector2(sp.x - Screen.width * 0.5f, sp.y - Screen.height * 0.5f) / scale;
        if (sp.z < 0f) dir = -dir;
        if (dir.sqrMagnitude < 0.01f) dir = Vector2.up;
        float sx = (half.x - InsetSide) / Mathf.Max(0.001f, Mathf.Abs(dir.x));
        float sy = (half.y - (dir.y > 0f ? InsetTop : InsetBottom)) / Mathf.Max(0.001f, Mathf.Abs(dir.y));
        Vector2 at = dir * Mathf.Min(sx, sy);
        float pulse = 1f + Mathf.Sin(Time.unscaledTime * 6f) * 0.06f;
        edgeRT.anchoredPosition = at;
        edgeRT.localScale = Vector3.one * pulse;
        edgeTip.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f);
        edgeText.rectTransform.anchoredPosition = new Vector2(0f, at.y < 0f ? 64f : -3f);
        float dist = Vector2.Distance(Me, current.world);
        string label = string.IsNullOrEmpty(current.edgeLabel) ? $"{dist:0}m" : $"{current.edgeLabel}\n<size=85%>{dist:0}m</size>";
        if (edgeText.text != label) edgeText.text = label;
    }

    private readonly Vector3[] corners = new Vector3[4];

    private void UpdateUiHighlight(RectTransform target)
    {
        bool show = target != null && target.gameObject.activeInHierarchy;
        Rect screenRect = default;
        if (show)
        {
            var canvas = target.GetComponentInParent<Canvas>();
            show = canvas != null && canvas.enabled;
            if (show)
            {
                var root = canvas.rootCanvas;
                Camera cam = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
                target.GetWorldCorners(corners);
                Vector2 a = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
                Vector2 b = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
                screenRect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                show = screenRect.width > 6f && screenRect.height > 6f &&
                       screenRect.xMax > 0f && screenRect.xMin < Screen.width && screenRect.yMax > 0f && screenRect.yMin < Screen.height;
            }
        }
        if (hlRT.gameObject.activeSelf != show) hlRT.gameObject.SetActive(show);
        if (uiBubble.gameObject.activeSelf != show) uiBubble.gameObject.SetActive(show);
        if (!show) { hlTarget = null; return; }

        if (hlTarget != target) hlTarget = target;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlay, screenRect.min, null, out var lmin);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(overlay, screenRect.max, null, out var lmax);
        const float Pad = 7f;
        Vector2 size = lmax - lmin + Vector2.one * Pad * 2f;
        float t = Time.unscaledTime;
        float pulse = (Mathf.Sin(t * 6f) + 1f) * 0.5f;
        hlRT.anchoredPosition = (lmin + lmax) * 0.5f;
        hlRT.sizeDelta = size;
        hlRT.localScale = Vector3.one * (1f + pulse * 0.05f);
        hlFrame.color = new Color(Accent.r, Accent.g, Accent.b, 0.65f + pulse * 0.35f);

        bool fromBelow = screenRect.yMax > Screen.height - 90f;
        float bob = Mathf.Abs(Mathf.Sin(t * 4.2f)) * 8f;
        hlPointer.localRotation = Quaternion.Euler(0f, 0f, fromBelow ? 180f : 0f);
        hlPointer.anchoredPosition = new Vector2(0f, fromBelow ? -(size.y * 0.5f + 2f + bob) : size.y * 0.5f + 2f + bob);

        string label = string.IsNullOrEmpty(current.uiLabel) ? "클릭!" : current.uiLabel;
        if (uiBubbleText.text != label) uiBubbleText.text = label;
        float reach = size.y * 0.5f + 2f + bob + hlPointer.sizeDelta.y + 2f;
        uiBubble.pivot = new Vector2(0.5f, fromBelow ? 1f : 0f);
        uiBubble.anchoredPosition = hlRT.anchoredPosition + new Vector2(0f, fromBelow ? -reach : reach);
    }

    private static Sprite roundedOutline;

    private static Sprite RoundedOutline()
    {
        if (roundedOutline != null) return roundedOutline;
        const int S = 64, R = 18, W = 4;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { name = "GuideOutline", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float qx = Mathf.Max(Mathf.Abs(x + 0.5f - S * 0.5f) - (S * 0.5f - R), 0f);
            float qy = Mathf.Max(Mathf.Abs(y + 0.5f - S * 0.5f) - (S * 0.5f - R), 0f);
            float d  = Mathf.Sqrt(qx * qx + qy * qy) - R;
            float a  = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(d + W + 0.5f);
            px[y * S + x] = new Color32(255, 255, 255, (byte)(a * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return roundedOutline = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0,
                                              SpriteMeshType.FullRect, new Vector4(R + 2, R + 2, R + 2, R + 2));
    }
}
