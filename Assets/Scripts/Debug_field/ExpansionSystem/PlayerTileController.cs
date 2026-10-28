using UnityEngine;

public class PlayerTileController : MonoBehaviour
{
    public static PlayerTileController Instance => Services.Get<PlayerTileController>();
    [Header("Settings")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float holdInterval = 0.1f;

    public FarmingItemData SelectedItem { get; private set; }

    private Camera        mainCam;
    private float         placeCooldown   = 0f;
    private ResourceBlock currentMining   = null;

    private readonly System.Collections.Generic.List<Vector2Int> dragTrail = new();
    private Vector2Int? dragFrom;
    private const int   MaxDragTrail = 64;

    private Coroutine     dismantleCoroutine  = null;
    private TileItem      dismantlingTile     = null;
    private GameObject    dismantleProgressBar = null;
    private RectTransform dismantleFillRT      = null;

    private bool blockRemoveUntilRelease = false;

    private bool blockActionUntilRelease = false;

    private Player        player;
    private float         baseMoveSpeed = 5f;

    public const float MiningRange = 3.5f;

    public static float InteractRange => 5f * (GridManager.Instance != null ? GridManager.Instance.CellSize : 1f);

    public int   CurrentToolGrade       => (SelectedItem?.itemType == FarmingItemType.Tool) ? SelectedItem.toolGrade   : 0;
    public float CurrentToolMiningPower => (SelectedItem?.itemType == FarmingItemType.Tool) ? SelectedItem.miningPower : 1f;

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }
        mainCam  = Camera.main;
    }

    private void Start()
    {
        player = GetComponentInParent<Player>();
        if (player != null)
        {
            baseMoveSpeed = player.moveSpeed;
            if (TechTreeManager.Instance != null)
            {
                TechTreeManager.Instance.OnTechChanged += ApplyMoveSpeedMultiplier;
                ApplyMoveSpeedMultiplier();
            }
            if (EquipmentManager.Instance != null)
                EquipmentManager.Instance.OnEquipmentChanged += ApplyMoveSpeedMultiplier;
        }
    }

    private void OnDestroy()
    {
        if (TechTreeManager.Instance != null)
            TechTreeManager.Instance.OnTechChanged -= ApplyMoveSpeedMultiplier;
        if (EquipmentManager.Instance != null)
            EquipmentManager.Instance.OnEquipmentChanged -= ApplyMoveSpeedMultiplier;
        Services.Unregister(this);
    }

    private void ApplyMoveSpeedMultiplier()
    {
        if (player == null) return;
        float techMult  = TechTreeManager.Instance?.MoveSpeedMult ?? 1f;
        float equipBonus = EquipmentManager.Instance?.GetBonus(TechStatType.MoveSpeed) ?? 0f;
        player.moveSpeed = baseMoveSpeed * (techMult + equipBonus);
    }

    private void Update()
    {
        HandlePlacementInput();
        HandleRemoveInput();
        HandleInteractionInput();
        UpdateResidentHover();
    }

    public void SelectItem(FarmingItemData item)
    {
        SelectedItem = item;
    }

    private void HandlePlacementInput()
    {
        var  im         = InputManager.Instance;
        bool actionDown = im != null ? im.ActionDown : GameInput.GetMouseButtonDown(0);
        bool actionHeld = im != null ? im.ActionHeld : GameInput.GetMouseButton(0);
        bool padWorld   = im != null && im.PadWorld;

        if (LinkModeController.BlocksWorldInput) blockActionUntilRelease = true;
        if (blockActionUntilRelease)
        {
            if (!actionHeld) blockActionUntilRelease = false;
            StopMining();
            return;
        }

        if (actionDown)
        {
            placeCooldown = 0f;
            dragTrail.Clear();
            dragFrom = null;

            bool overUI = GameInput.PointerOverUI;
            if (overUI && !padWorld)
            {
                blockActionUntilRelease = true;
                return;
            }
            if (!padWorld && !IsPauseMenuOpen())
            {
                Vector2 worldPos = GetWorldMousePos();

                var cat = ResidentCat.At(worldPos);
                if (cat != null && ResidentBond.TakesClick(SelectedItem))
                {
                    InteractResident(cat);
                    blockActionUntilRelease = true;
                    return;
                }

                foreach (var hit in Physics2D.OverlapPointAll(worldPos))
                {
                    var core = hit.GetComponentInParent<CenterCore>();
                    if (core != null)
                    {
                        OpenCentralPlant(core, InteractRange);
                        return;
                    }
                    var pad = hit.GetComponentInParent<ShuttlePad>();
                    if (pad != null && !pad.IsGhost && OpenFacility(pad)) return;
                }

                if (TryGetGridPositionSilent(out Vector2Int clickPos) && OpenFacility(GridManager.Instance.GetTile(clickPos)))
                    return;
            }
        }

        if (!actionHeld)
        {
            StopMining();
            dragFrom = null;
            if (dragTrail.Count > 0) ProcessDragTrail(tryCursorCell: false);
            return;
        }

        if (!padWorld && GameInput.PointerOverUI)
        {
            dragFrom = null;
            return;
        }

        if (IsPauseMenuOpen())
        {
            StopMining();
            dragTrail.Clear();
            return;
        }

        RecordDragCell(fillGaps: !padWorld);

        var block = GetResourceBlockAtCursor();
        if (block != null)
        {
            float dist = Vector2.Distance(transform.position, block.transform.position);
            if (dist <= MiningRange)
            {
                if (currentMining != block)
                {
                    StopMining();
                    currentMining = block;
                    block.StartMining();
                }
                return;
            }
            else
            {
                StopMining();
            }
        }
        else
        {
            StopMining();
        }

        ProcessDragTrail(tryCursorCell: true);
    }

    private void RecordDragCell(bool fillGaps)
    {
        if (!TryGetGridPositionSilent(out var cell)) return;
        if (dragFrom is Vector2Int from)
        {
            if (from == cell) return;
            int dx = Mathf.Abs(cell.x - from.x), dy = Mathf.Abs(cell.y - from.y);
            // 빠르게 드래그해도 칸 안 빠지게
            if (fillGaps && Mathf.Max(dx, dy) <= 16)
            {
                int sx = from.x < cell.x ? 1 : -1, sy = from.y < cell.y ? 1 : -1, err = dx - dy;
                var c = from;
                while (c != cell)
                {
                    int e2 = 2 * err;
                    if (e2 > -dy) { err -= dy; c.x += sx; }
                    if (e2 < dx)  { err += dx; c.y += sy; }
                    PushDragCell(c);
                }
            }
            else PushDragCell(cell);
        }
        else PushDragCell(cell);
        dragFrom = cell;
    }

    private void PushDragCell(Vector2Int c)
    {
        if (dragTrail.Count >= MaxDragTrail || (dragTrail.Count > 0 && dragTrail[^1] == c)) return;
        dragTrail.Add(c);
    }

    private void ProcessDragTrail(bool tryCursorCell)
    {
        placeCooldown -= Time.deltaTime;
        if (placeCooldown > 0f) return;

        if (BlocksPlacement()) { dragTrail.Clear(); return; }

        float placeMult = (TechTreeManager.Instance?.PlacementSpeedMult ?? 1f)
                        + (EquipmentManager.Instance?.GetBonus(TechStatType.PlacementSpeed) ?? 0f);
        float effectivePlaceInterval = holdInterval / placeMult;

        while (dragTrail.Count > 0)
        {
            var cell = dragTrail[0];
            dragTrail.RemoveAt(0);
            if (ActAt(cell)) { placeCooldown = effectivePlaceInterval; return; }
        }
        if (tryCursorCell && TryGetGridPositionSilent(out var cursor) && ActAt(cursor))
            placeCooldown = effectivePlaceInterval;
    }

    private bool ActAt(Vector2Int gridPos)
    {
        if (TryHarvest(gridPos)) return true;
        if (SelectedItem == null) return false;
        if (Inventory.Instance == null || !Inventory.Instance.HasItem(SelectedItem)) return false;
        if (SelectedItem.itemType == FarmingItemType.Tile) return TryPlaceTile(gridPos);
        if (SelectedItem.itemType == FarmingItemType.Seed) return TryPlantSeed(gridPos);
        return SelectedItem.IsFertilizer && TryFertilize(gridPos);
    }

    private bool TryFertilize(Vector2Int gridPos)
    {
        if (GridManager.Instance.GetTile(gridPos) is not PlantTile plant) return false;
        if (plant.Fertility >= PlantTile.MaxFertility)
        {
            if (fertilizeFullNoticeCell != gridPos) NotificationUI.Instance?.Show("이 밭은 퇴비가 가득해요 (최대 9회)");
            fertilizeFullNoticeCell = gridPos;
            return false;
        }
        if (!plant.Fertilize(SelectedItem)) return false;
        Inventory.Instance.ConsumeItem(SelectedItem);
        Sfx.PlayAt(SfxId.Plant, plant.transform.position);
        GamepadRumble.Play(Haptic.Plant);
        return true;
    }

    private Vector2Int fertilizeFullNoticeCell = new(int.MinValue, int.MinValue);

    private bool OpenFacility(TileItem tile)
    {
        if (tile is CenterCore plant)
        {
            OpenCentralPlant(plant, InteractRange);
            return true;
        }
        if (tile is SolarPanel solar)
        {
            NotificationUI.Instance?.Show(solar.IsDaytime
                ? "태양광 패널: 햇빛으로 발전 중이에요 (연료가 필요 없어요)"
                : "태양광 패널: 밤이라 쉬는 중이에요 (06:00에 다시 발전)");
            return true;
        }
        if (tile is Generator gen)
        {
            if (tile.DistanceFrom(transform.position) <= InteractRange)
                GeneratorFuelUI.Instance?.Open(gen);
            else
                TooFar(tile);
            return true;
        }

        if (tile is ShuttlePad pad && !pad.IsGhost)
        {
            if (tile.DistanceFrom(transform.position) > InteractRange)
                TooFar(tile);
            else if (!QuickLoad(pad))
                ShuttleUI.Instance?.Open(pad);
            return true;
        }

        if (tile != null && LinkRules.IsLinkable(tile))
        {
            if (tile.DistanceFrom(transform.position) > InteractRange)
                TooFar(tile);
            else if (!QuickUse(tile))
                OpenWindow(tile);
            return true;
        }
        return false;
    }

    private static void TooFar(TileItem tile)
    {
        NotificationUI.Instance?.Show($"{LinkRules.NameOf(tile)}에서 너무 멀어요 — 조금 더 가까이 가서 눌러요");
        Sfx.Play(SfxId.UiError);
    }

    private static void OpenWindow(TileItem tile)
    {
        switch (tile)
        {
            case IItemBox _:           ItemStorageUI.Instance?.Open(tile);     break;
            case ResourceExtractor ex: ResourceExtractorUI.Instance?.Open(ex); break;
            case Smelter smelter:      SmelterUI.Open(smelter);                break;
            case CatKitchen kitchen:   CatKitchenUI.Open(kitchen);             break;
            case Composter composter:  ComposterUI.Open(composter);            break;
            default:                   FacilityPopup.Open(tile);               break;
        }
    }

    private bool QuickUse(TileItem tile)
    {
        var inv = Inventory.Instance;
        if (inv == null) return false;
        if (tile is AutoSeeder seeder)
        {
            if (SelectedItem == null || SelectedItem.itemType != FarmingItemType.Seed) return false;
            HandleSeedInsertInput(seeder);
            return true;
        }
        if (!(tile is Smelter || tile is CatKitchen || tile is Composter)) return false;
        bool did = false;

        FarmingItemData outItem = null;
        int outCount = 0;
        switch (tile)
        {
            case Smelter s:    outItem = s.OutputItem;  outCount = s.OutputCount; break;
            case CatKitchen k: outItem = k.OutputItem;  outCount = k.OutputCount; break;
            case Composter c:  outItem = c.CompostItem; outCount = c.OutputCount; break;
        }
        if (outItem != null && outCount > 0)
        {
            if (!inv.CanAddItem(outItem))
            {
                NotificationUI.Instance?.Show($"가방이 꽉 차서 {outItem.itemName}{(ItemCellUI.HasBatchim(outItem.itemName) ? "을" : "를")} 받을 수 없어요 — 자리를 만들고 다시 눌러요");
                Sfx.Play(SfxId.UiError);
                return true;
            }
            int n = 0;
            FarmingItemData got = outItem;
            switch (tile)
            {
                case Smelter s:    n = s.TakeOutput(outCount, out got); break;
                case CatKitchen k: n = k.TakeOutput(outCount, out got); break;
                case Composter c:  n = c.TakeOutput(outCount); break;
            }
            if (n > 0 && got != null)
            {
                inv.AddItemOrDrop(got, n, transform.position);
                Sfx.PlayAt(SfxId.Pickup, tile.transform.position);
                did = true;
            }
        }

        if (tile is Smelter left && left.HasLeftover)
        {
            var leftOre = left.InputItem;
            var held    = SelectedItem;
            if (held != null && held != leftOre && Batchable(held) && inv.CanAddItem(leftOre))
            {
                int n = left.TakeInput(left.InputCount, out var got);
                if (n > 0 && got != null)
                {
                    inv.AddItem(got, n, countAsNew: false);
                    NotificationUI.Instance?.Show($"제련소에 남아 있던 {got.itemName} {n}개를 가방으로 꺼냈어요 (한 번에 {left.BatchSize}개씩 녹아요)");
                    did = true;
                }
            }
        }

        var put = tile is Smelter sm ? QuickOreFor(sm) : SelectedItem;
        int have = put != null ? inv.GetCount(put) : 0;
        if (have > 0)
        {
            int room = tile switch
            {
                Smelter s    => s.CanInsert(put) ? s.InputCapacity - s.InputCount : 0,
                CatKitchen k => k.RoomFor(put),
                Composter c  => c.RoomFor(put),
                _            => 0,
            };
            int want = Mathf.Min(have, room);
            if (want > 0 && inv.ConsumeItem(put, want))
            {
                int inserted = tile switch
                {
                    Smelter s    => s.InsertOre(put, want),
                    CatKitchen k => k.InsertIngredient(put, want),
                    Composter c  => c.Insert(put, want),
                    _            => 0,
                };
                if (inserted < want) inv.AddItem(put, want - inserted, countAsNew: false);
                if (inserted > 0)
                {
                    NotificationUI.Instance?.Show($"{LinkRules.NameOf(tile)}에 {put.itemName} ×{inserted} 넣었어요");
                    Sfx.PlayAt(SfxId.StoreItem, tile.transform.position);
                    did = true;
                }
            }
        }
        else if (!did && tile is Smelter busy && SelectedItem != null && Smelter.RecipeFor(SelectedItem) != null
                 && busy.InputItem != null && busy.InputItem != SelectedItem)
            NotificationUI.Instance?.Show($"제련소가 {busy.InputItem.itemName}{(ItemCellUI.HasBatchim(busy.InputItem.itemName) ? "을" : "를")} 녹이는 중이에요 — 다 녹으면 넣어요");

        if (did) GamepadRumble.Play(Haptic.Store);
        return did;
    }

    private bool QuickLoad(ShuttlePad pad)
    {
        var svc = ShuttleService.Instance;
        var s   = pad != null ? pad.Shuttle : null;
        if (svc == null || s == null || s.away) return false;
        var got = new System.Collections.Generic.List<(FarmingItemData item, int amount)>();
        if (svc.LoadFromBag(s, got) <= 0) return false;

        var parts = new System.Collections.Generic.List<string>();
        foreach (var (item, amount) in got) parts.Add($"{item.itemName} ×{amount}");
        if (!s.away)
            NotificationUI.Instance?.Show($"셔틀에 {string.Join(" · ", parts)} 실었어요 — 남은 짐: {ShuttleUI.MissingText(s)}");
        Sfx.PlayAt(SfxId.StoreItem, pad.transform.position);
        GamepadRumble.Play(Haptic.Store);
        return true;
    }

    public static FarmingItemData QuickOreFor(Smelter sm)
    {
        var inv  = Inventory.Instance;
        var held = Instance != null ? Instance.SelectedItem : null;
        if (sm == null || inv == null || held == null) return null;
        return inv.GetCount(held) > 0 && sm.CanInsert(held) ? held : null;
    }

    private static bool Batchable(FarmingItemData ore)
    {
        var r = Smelter.RecipeFor(ore);
        return r != null && Inventory.Instance != null && Inventory.Instance.GetCount(ore) >= Mathf.Max(1, r.ingredients[0].amount);
    }

    private void OpenCentralPlant(CenterCore core, float interactRange)
    {
        if (core.DistanceFrom(transform.position) <= interactRange)
            CenterCoreUI.Instance?.Open(core);
        else
            TooFar(core);
    }

    private void StopMining()
    {
        if (currentMining == null) return;
        currentMining.StopMining();
        currentMining = null;
    }

    private ResourceBlock GetResourceBlockAtCursor()
    {
        Vector2 worldPos = GetWorldMousePos();
        var hits = Physics2D.OverlapPointAll(worldPos);
        foreach (var hit in hits)
        {
            var block = hit.GetComponent<ResourceBlock>();
            if (block != null) return block;
        }
        return null;
    }

    private void HandleRemoveInput()
    {
        var  im         = InputManager.Instance;
        bool removeHeld = im != null ? im.RemoveHeld : GameInput.GetMouseButton(1);
        if (!removeHeld)
        {
            blockRemoveUntilRelease = false;
            StopDismantleTile();
            return;
        }

        if (blockRemoveUntilRelease || IsAnyUIOpen() || EscapeRouter.ConsumedThisFrame)
        {
            blockRemoveUntilRelease = true;
            StopDismantleTile();
            return;
        }

        TileItem   tile    = null;
        Vector2Int gridPos = default;
        if (im != null && im.PadWorld)
        {
            tile = im.PadDismantleTarget;
            if (tile != null) gridPos = tile.GridPosition;
        }
        else
        {
            if (GameInput.PointerOverUI) return;
            if (TryGetGridPositionSilent(out gridPos)) tile = GridManager.Instance.GetTile(gridPos);
        }

        if (tile != null && tile.CanDismantle)
        {
            if (dismantlingTile != tile)
            {
                StopDismantleTile();
                dismantlingTile    = tile;
                dismantleCoroutine = StartCoroutine(DismantleTileCoroutine(tile, gridPos, () =>
                    InputManager.Instance != null ? InputManager.Instance.RemoveHeld : GameInput.GetMouseButton(1)));
            }
            return;
        }

        StopDismantleTile();
    }

    private Vector3 dismantleOriginalPos;

    private void StopDismantleTile()
    {
        if (dismantleCoroutine != null)
        {
            StopCoroutine(dismantleCoroutine);
            dismantleCoroutine = null;
            if (dismantlingTile != null)
                dismantlingTile.transform.position = dismantleOriginalPos;
        }
        dismantlingTile = null;

        if (dismantleProgressBar != null) { Destroy(dismantleProgressBar); dismantleProgressBar = null; }
        dismantleFillRT = null;
    }

    private System.Collections.IEnumerator DismantleTileCoroutine(TileItem tile, Vector2Int gridPos, System.Func<bool> removeHeldGetter = null)
    {
        float speedMult = (TechTreeManager.Instance?.DismantleSpeedMult ?? 1f)
                        + (EquipmentManager.Instance?.GetBonus(TechStatType.DismantleSpeed) ?? 0f);
        float totalTime = tile.DismantleTime / speedMult;

        dismantleOriginalPos  = tile.transform.position;
        Vector3 originalPos   = dismantleOriginalPos;
        float   shakeInterval = 0.08f;
        float   nextShakeTime = Time.time;
        float   nextTickTime  = Time.time;
        float   elapsed       = 0f;

        BuildDismantleProgressBar(tile.transform.position);

        bool completed = false;
        while (elapsed < totalTime)
        {
            bool holding = removeHeldGetter != null ? removeHeldGetter() : GameInput.GetMouseButton(1);
            if (tile == null || !holding) break;

            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / totalTime);

            if (Time.time >= nextShakeTime)
            {
                tile.transform.position = originalPos + (Vector3)(UnityEngine.Random.insideUnitCircle * 0.03f);
                nextShakeTime = Time.time + shakeInterval;
            }

            if (Time.time >= nextTickTime)
            {
                Sfx.PlayAt(SfxId.MineHit, originalPos, 0.5f);
                GamepadRumble.Play(Haptic.MineHit, 0.5f + 0.5f * progress);
                nextTickTime = Time.time + 0.22f;
            }
            GamepadRumble.Hold(0.06f + 0.3f * progress, 0.12f + 0.25f * progress);

            if (dismantleFillRT != null)
                dismantleFillRT.anchorMax = new Vector2(progress, 1f);

            if (dismantleProgressBar != null)
                dismantleProgressBar.transform.position = tile.transform.position + Vector3.up * 0.7f;

            yield return null;
        }

        if (tile != null)
            tile.transform.position = originalPos;

        completed = (tile != null && elapsed >= totalTime);

        if (completed)
        {
            if (tile.DropItemData != null)
                DroppedItem.Spawn(tile.DropItemData, 1, originalPos + Vector3.up * 0.3f, transform, homing: true);

            var attached = tile.GetAttachedDrops();
            if (attached != null)
            {
                foreach (var (extraItem, extraAmount) in attached)
                {
                    if (extraItem == null || extraAmount <= 0) continue;
                    Vector3 offset = (Vector3)(UnityEngine.Random.insideUnitCircle * 0.3f) + Vector3.up * 0.3f;
                    DroppedItem.Spawn(extraItem, extraAmount, originalPos + offset, transform, homing: true);
                }
            }

            GridManager.Instance.RemoveTile(gridPos);
            Sfx.PlayAt(SfxId.Dismantle, originalPos);
            GamepadRumble.Play(Haptic.DismantleDone);
        }

        if (dismantleProgressBar != null) { Destroy(dismantleProgressBar); dismantleProgressBar = null; }
        dismantleFillRT  = null;
        dismantlingTile  = null;
        dismantleCoroutine = null;
    }

    private void BuildDismantleProgressBar(Vector3 worldPos)
    {
        var root = new GameObject("DismantleBar");
        root.transform.position   = worldPos + Vector3.up * 0.7f;
        root.transform.localScale = Vector3.one * 0.008f;
        dismantleProgressBar      = root;

        var canvas = root.AddComponent<UnityEngine.Canvas>();
        canvas.renderMode  = UnityEngine.RenderMode.WorldSpace;
        canvas.sortingOrder = 150;

        var rt = root.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(100f, 14f);

        CuteUIKit.Layer(root.transform, "Border", CuteUIKit.Rounded(7), CuteUIKit.Latte);
        CuteUIKit.Layer(root.transform, "Track",  CuteUIKit.Rounded(6), CuteUIKit.LatteLight, 1.5f);

        var area = new GameObject("FillArea", typeof(RectTransform));
        area.transform.SetParent(root.transform, false);
        var areaRT = (RectTransform)area.transform;
        areaRT.anchorMin = Vector2.zero; areaRT.anchorMax = Vector2.one;
        areaRT.offsetMin = new Vector2(3f, 3f);
        areaRT.offsetMax = new Vector2(-3f, -3f);

        dismantleFillRT            = CuteUIKit.Layer(areaRT, "Fill", CuteUIKit.Rounded(4), CuteUIKit.Strawberry).rectTransform;
        dismantleFillRT.anchorMin  = Vector2.zero;
        dismantleFillRT.anchorMax  = new Vector2(0f, 1f);
        dismantleFillRT.offsetMin  = dismantleFillRT.offsetMax = Vector2.zero;
    }

    private void HandleInteractionInput()
    {
        var im = InputManager.Instance;
        if (im != null && im.PadInteractDown)
        {
            if (!IsAnyUIOpen())
            {
                if (im.PadResidentTarget != null) InteractResident(im.PadResidentTarget);
                else InteractWith(im.PadInteractTarget);
            }
            return;
        }

        bool interact = im != null ? im.InteractDown : GameInput.GetKeyDown(KeyCode.F);
        if (!interact) return;
        if (IsAnyUIOpen()) return;

        var cat = ResidentCat.At(GetWorldMousePos());
        if (cat != null)
        {
            InteractResident(cat);
            return;
        }

        if (!TryGetGridPosition(out Vector2Int gridPos)) return;

        var tile = GridManager.Instance.GetTile(gridPos);

        if (tile is Generator g && g.UsesFuel)
        {
            InsertFuel(g);
            return;
        }

        if (tile is AutoSeeder seeder)
        {
            HandleSeedInsertInput(seeder);
            return;
        }

        TryHarvest(gridPos);
    }

    private void InteractResident(ResidentCat cat)
    {
        if (cat == null || !cat.Interactable) return;
        if (Vector2.Distance(transform.position, cat.transform.position) > InteractRange)
        {
            NotificationUI.Instance?.Show($"{cat.CatName}에게 조금 더 가까이 가서 말을 걸어요");
            Sfx.Play(SfxId.UiError);
            return;
        }
        ResidentBond.Interact(cat, SelectedItem, transform.position);
    }

    private void UpdateResidentHover()
    {
        if (ResidentCat.All.Count == 0 || MobileMode.Active) return;
        var im = InputManager.Instance;
        if (im != null && im.PadWorld) return;
        if (GameInput.PointerOverUI || IsAnyUIOpen() || IsPauseMenuOpen() || LinkModeController.IsActive) return;
        ResidentCat.At(GetWorldMousePos())?.ShowHover(ResidentBond.HoverVerb(SelectedItem));
    }

    private void InteractWith(TileItem tile)
    {
        if (tile == null) return;
        if (tile is PlantTile)
        {
            TryHarvest(tile.GridPosition);
            return;
        }
        if (tile is Generator g && g.UsesFuel && SelectedItem != null && SelectedItem.canUseFuel)
        {
            InsertFuel(g);
            return;
        }
        if (tile is AutoSeeder seeder && SelectedItem != null && SelectedItem.itemType == FarmingItemType.Seed)
        {
            HandleSeedInsertInput(seeder);
            return;
        }
        OpenFacility(tile);
    }

    private void HandleSeedInsertInput(AutoSeeder seeder)
    {
        if (SelectedItem == null || SelectedItem.itemType != FarmingItemType.Seed) return;

        var seed  = SelectedItem;
        int added = seeder.InsertFromInventory(seed);
        if (added > 0)
        {
            NotificationUI.Instance?.Show($"{LinkRules.NameOf(seeder)}: {seed.itemName} ×{added} 넣음 ({seeder.TotalCount} / {seeder.Capacity})");
            Sfx.PlayAt(SfxId.StoreItem, seeder.transform.position);
            GamepadRumble.Play(Haptic.Store);
        }
        else
        {
            NotificationUI.Instance?.Show(seeder.HasSpace ? $"{LinkRules.NameOf(seeder)}에 넣을 수 없는 씨앗이에요."
                                                          : $"{LinkRules.NameOf(seeder)} 씨앗통이 가득 찼어요.");
            Sfx.Play(SfxId.UiError);
        }
    }

    private bool TryHarvest(Vector2Int gridPos)
    {
        int radius = SelectedItem != null && SelectedItem.itemType == FarmingItemType.Tool ? SelectedItem.harvestRadius : 0;
        if (radius <= 0) return HarvestAt(gridPos, playSound: true);

        bool any = false;
        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
            if (HarvestAt(gridPos + new Vector2Int(dx, dy), playSound: !any))
                any = true;
        return any;
    }

    private bool HarvestAt(Vector2Int gridPos, bool playSound)
    {
        var tile = GridManager.Instance.GetTile(gridPos);
        if (tile == null)
        {
            return false;
        }
        if (tile is not PlantTile plant)
        {
            return false;
        }
        if (plant.CurrentState != PlantTile.PlantState.ReadyToHarvest)
        {
            return false;
        }

        var results = plant.Harvest();
        if (results == null) return false;

        for (int i = 0; i < results.Length; i++)
        {
            Vector3 offset   = i == 0 ? Vector3.up * 0.5f : (Vector3)(UnityEngine.Random.insideUnitCircle * 0.4f) + Vector3.up * 0.5f;
            Vector3 spawnPos = tile.transform.position + offset;
            DroppedItem.Spawn(results[i].item, results[i].amount, spawnPos, transform, homing: true);
        }
        if (playSound)
        {
            Sfx.PlayAt(SfxId.Harvest, tile.transform.position);
            GamepadRumble.Play(Haptic.Harvest);
        }
        return true;
    }

    private void InsertFuel(Generator generator)
    {
        if (SelectedItem == null || !SelectedItem.canUseFuel)
        {
            return;
        }
        if (!Inventory.Instance.HasItem(SelectedItem))
        {
            return;
        }

        int stock = Inventory.Instance.GetCount(SelectedItem);
        int used  = generator.AddFuel(SelectedItem, stock);
        if (used > 0)
        {
            // 한 번에 차감
            Inventory.Instance.ConsumeItem(SelectedItem, used);
            Sfx.PlayAt(SfxId.FuelInsert, generator.transform.position);
            GamepadRumble.Play(Haptic.Store);
        }
    }

    private bool TryPlaceTile(Vector2Int gridPos)
    {
        if (SelectedItem.tilePrefab == null)
        {
            Debug.LogWarning($"[TryPlaceTile] '{SelectedItem.itemName}'의 tilePrefab이 NULL입니다.");
            return false;
        }

        var placed = GridManager.Instance.PlaceTile(
            gridPos,
            SelectedItem.tilePrefab,
            SelectedItem.tileWidth,
            SelectedItem.tileHeight,
            sourceItem: SelectedItem);

        if (placed != null)
        {
            Inventory.Instance.ConsumeItem(SelectedItem);
            Sfx.PlayAt(SfxId.Place, placed.transform.position);
            GamepadRumble.Play(Haptic.Place);
        }
        else
        {
        }
        return placed != null;
    }

    private bool TryPlantSeed(Vector2Int gridPos)
    {
        if (SelectedItem.seedData == null)
        {
            Debug.LogWarning($"[TryPlantSeed] '{SelectedItem.itemName}'의 seedData가 비어 있습니다.");
            return false;
        }

        var tile = GridManager.Instance.GetTile(gridPos);
        if (tile is not PlantTile plant) return false;
        bool planted = plant.PlantSeed(SelectedItem.seedData);
        if (planted)
        {
            Inventory.Instance.ConsumeItem(SelectedItem);
            Sfx.PlayAt(SfxId.Plant, plant.transform.position);
            GamepadRumble.Play(Haptic.Plant);
        }
        return planted;
    }

    public static bool IsAnyUIOpen()
    {
        return (TechTreeUI.Instance      != null && TechTreeUI.Instance.IsOpen)      ||
               (ShopUI.Instance          != null && ShopUI.Instance.IsOpen)          ||
               (CraftingTableUI.Instance != null && CraftingTableUI.Instance.IsOpen) ||
               (InventoryUI.Instance     != null && InventoryUI.Instance.IsOpen)     ||
               (CenterCoreUI.Instance    != null && CenterCoreUI.Instance.IsOpen)    ||
               (GeneratorFuelUI.Instance != null && GeneratorFuelUI.Instance.IsOpen) ||
               (ItemStorageUI.Instance   != null && ItemStorageUI.Instance.IsOpen)   ||
               (ResourceExtractorUI.Instance != null && ResourceExtractorUI.Instance.IsOpen) ||
               SmelterUI.IsOpen ||
               CatKitchenUI.IsOpen ||
               ComposterUI.IsOpen ||
               ShuttleUI.IsOpen ||
               JourneyUI.IsOpen ||
               CodexUI.IsOpen ||
               FacilityPopup.BlocksWorldInput ||
               LinkModeController.BlocksWorldInput ||
               HelpUI.AnyOpen ||
               IsPauseMenuOpen();
    }

    public static bool BlocksPlacement()
        => FacilityPopup.BlocksWorldInput || LinkModeController.BlocksWorldInput || HelpUI.AnyOpen || IsPauseMenuOpen();

    public static bool IsPauseMenuOpen()
        => (SaveMenuUI.Instance != null && SaveMenuUI.Instance.IsOpen) || OptionsUI.IsOpen || CharacterEditorUI.IsOpen;

    private Vector2 GetWorldMousePos()
    {
        Vector3 mp = InputManager.Instance != null ? InputManager.Instance.CursorScreenPos : (Vector3)GameInput.MousePosition;
        mp.z = -mainCam.transform.position.z;
        Vector3 wp = mainCam.ScreenToWorldPoint(mp);
        wp.z = 0f;
        return wp;
    }

    private bool TryGetGridPosition(out Vector2Int gridPos)
    {
        Vector2 worldPos = GetWorldMousePos();
        gridPos = GridManager.Instance.WorldToGrid(worldPos);
        bool inBounds = GridManager.Instance.IsInBounds(gridPos);
        return inBounds;
    }

    private bool TryGetGridPositionSilent(out Vector2Int gridPos)
    {
        Vector2 worldPos = GetWorldMousePos();
        gridPos  = GridManager.Instance.WorldToGrid(worldPos);
        return GridManager.Instance.IsInBounds(gridPos);
    }
}
