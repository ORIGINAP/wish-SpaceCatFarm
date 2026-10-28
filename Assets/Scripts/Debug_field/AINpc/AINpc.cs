using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

// LLM 조수 NPC 코코
public class AINpc : MonoBehaviour
{
    public static AINpc Instance => Services.Get<AINpc>();
    public const string NpcName = "코코";

    [Header("이동")]
    [SerializeField] private float moveSpeed      = 4.5f;
    [SerializeField] private float followDistance = 2.2f;

    [Header("LLM")]
    [SerializeField] private string model = "claude-haiku-4-5-20251001";

    public static readonly Color DefaultTint = new Color(0.65f, 0.9f, 1f, 1f);

    [Header("표시")]
    [SerializeField] private Color tint      = DefaultTint;
    [SerializeField] private Color chatColor = new Color(0.55f, 0.95f, 1f, 1f);

    private enum Mode { Follow, Stay, Wander }
    private Mode mode = Mode.Follow;

    private Transform      player;
    private SpriteRenderer body;
    private TextMeshPro    bubble;
    private SpriteRenderer bubbleBg;
    private SpriteRenderer bubbleFill;
    private Transform      bubbleTail;
    private int            topSortingLayer;
    private float          bubbleUntil;

    private LlmClient llm;
    private NpcBrain  brain;
    private NpcPersonaType persona;

    public NpcPersonaType Persona => persona;

    public Color Tint => tint;

    private Coroutine taskCo;
    private bool      taskRunning;
    private int       taskRunId;
    private string    taskLabel = "";

    private Vector3 wanderAnchor;
    private Vector3 wanderTarget;
    private float   nextWanderTime;
    private Vector3 lastPlayerPos;
    private float   playerIdleSince;
    private float   nextIdleChatTime;

    public bool IsThinking => llm != null && llm.Busy;

    public string CurrentTaskLabel => taskRunning ? taskLabel
        : mode == Mode.Follow ? "따라다니는 중" : mode == Mode.Stay ? "제자리 대기" : "주변 산책";

    public int ToolGrade
    {
        get
        {
            int best = 1;
            var inv = Inventory.Instance;
            if (inv == null) return best;
            for (int i = 0; i < inv.SlotCount; i++)
            {
                var s = inv.GetSlot(i);
                if (s != null && !s.IsEmpty && s.Item.itemType == FarmingItemType.Tool) best = Mathf.Max(best, s.Item.toolGrade);
            }
            return best;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        TrySpawn();
        // 중복 구독 방지
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        GameSettings.AiNpcEnabledChanged -= OnAiNpcSettingChanged;
        GameSettings.AiNpcEnabledChanged += OnAiNpcSettingChanged;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TrySpawn();

    private static void OnAiNpcSettingChanged(bool enabled)
    {
        if (enabled) { TrySpawn(); return; }

        NpcChatUI.DestroyInstance();
        var npc = Instance;
        if (npc == null) return;
        Services.Unregister(npc);
        Destroy(npc.gameObject);
    }

    private static void TrySpawn()
    {
        if (Instance != null || !GameSettings.AiNpcEnabled || NetSession.IsReplica) return;
        var p = Player.Local;
        if (p == null || GridManager.Instance == null) return;

        var go = new GameObject($"AINpc_{NpcName}");
        go.transform.position = p.transform.position + new Vector3(1.5f, 0f, 0f);
        go.AddComponent<AINpc>().Init(p);
    }

    private void Init(Player p)
    {
        Services.Replace(this);
        player   = p.transform;
        llm      = new LlmClient { Model = model };
        persona  = NpcPersona.Load();
        brain    = new NpcBrain(NpcName, persona);
        lastPlayerPos = player.position;

        BuildVisual(p);
        NpcChatUI.Ensure(this);
        CatPresence.Register(transform);

        if (llm.HasKey) Say(L("greet"), 6f);
    }

    public void SetPersona(NpcPersonaType p)
    {
        persona = p;
        NpcPersona.Save(p);
        brain.SetPersona(p);
        Report(L("persona_on"));
    }

    private void OnDestroy()
    {
        Services.Unregister(this);
    }

    public void HandlePlayerMessage(string text)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return;
        NotificationUI.Instance?.Show($"나: {NoParse(text)}", new Color(0.85f, 0.85f, 0.85f));

        if (NpcPersona.TryParseSwitch(text, out var newPersona)) { SetPersona(newPersona); return; }

        if (llm.Busy) { Say(L("busy"), 2f); return; }

        Say("...", 30f);
        var messages = brain.BuildMessages(NpcWorld.BuildState(this, player.position, text), text);
        StartCoroutine(llm.Send(messages,
            reply => OnReply(text, reply),
            error => { Say(error, 4f); Log(error); }));
    }

    private void OnReply(string playerText, string reply)
    {
        brain.Remember(playerText, reply);
        var (say, commands) = NpcBrain.Parse(reply);
        say = NoParse(FontSafe(say));

        if (!string.IsNullOrEmpty(say)) { Say(say); Log(say); Sfx.PlayAt(SfxId.Notify, transform.position, 0.7f); }
        else HideBubble();

        if (commands.Count > 0) RunCommands(commands);
    }

    private void RunCommands(List<NpcCommand> commands)
    {
        if (taskCo != null) StopCoroutine(taskCo);
        int id = ++taskRunId;
        taskRunning = true;
        taskCo = StartCoroutine(RunQueue(commands, id));
    }

    private IEnumerator RunQueue(List<NpcCommand> commands, int id)
    {
        foreach (var cmd in commands)
            yield return Execute(cmd);
        if (id != taskRunId) yield break;
        taskRunning = false;
        taskLabel   = "";
    }

    private IEnumerator Execute(NpcCommand c)
    {
        switch (c.verb)
        {
            case "follow": mode = Mode.Follow; break;
            case "stay":   mode = Mode.Stay;   break;
            case "wander": mode = Mode.Wander; wanderAnchor = transform.position; break;
            case "come":
                taskLabel = "플레이어에게 가는 중";
                yield return MoveTo(player.position, 1.2f);
                mode = Mode.Follow;
                break;
            case "mine":    yield return MineTask(c.arg, c.count > 0 ? c.count : 5); break;
            case "harvest": yield return HarvestTask(); break;
            case "plant":   yield return PlantTask(c.arg, c.count); break;
            case "register": yield return RegisterSeedTask(c.arg, c.count); break;
            case "craft":   yield return CraftTask(c.arg, c.count > 0 ? c.count : 1); break;
            case "place":   yield return PlaceTask(c.arg, c.count > 0 ? c.count : 1, c.near); break;
            case "fuel":    yield return FuelTask(); break;
            case "sell":    SellTask(c.arg, c.count); break;
            case "buy":     BuyTask(c.arg, c.count > 0 ? c.count : 1); break;
        }
    }

    private IEnumerator MineTask(string name, int count)
    {
        var item = NpcWorld.FindItem(name, NpcWorld.IsMinable)
                ?? NpcWorld.FindItem(name, i => i.itemType == FarmingItemType.Resource) ?? NpcWorld.FindItem(name);
        if (item == null) { Report(L("unknown_item", name)); yield break; }

        int got = 0;
        bool tooHardSeen = false;
        while (got < count)
        {
            taskLabel = $"{item.itemName} 채굴 {got}/{count}";
            var block = NearestBlock(item, out bool tooHard);
            tooHardSeen |= tooHard;
            if (block == null) break;

            yield return MoveTo(block.transform.position, 1.1f);
            while (block != null && got < count && block.RemainingResources > 0)
            {
                yield return new WaitForSeconds(0.5f);
                if (block == null) break;
                var (dropped, amount) = block.ExtractResources(1);
                if (dropped == null) break;
                Give(dropped, amount);
                got += amount;
                taskLabel = $"{item.itemName} 채굴 {got}/{count}";
            }
        }

        if (got >= count)      Report(L("mine_done",     item.itemName, got));
        else if (got > 0)      Report(L("mine_partial",  item.itemName, got));
        else if (tooHardSeen)  Report(L("mine_too_hard", item.itemName));
        else                   Report(L("mine_none",     item.itemName));
    }

    private ResourceBlock NearestBlock(FarmingItemData item, out bool tooHard)
    {
        tooHard = false;
        ResourceBlock best = null;
        float bestD = float.MaxValue;
        foreach (var b in ResourceBlock.Active)
        {
            if (b.DropItem != item || b.RemainingResources <= 0) continue;
            if ((b.transform.position - player.position).sqrMagnitude > NpcWorld.WorkRadius * NpcWorld.WorkRadius) continue;
            if (b.Data != null && b.Data.minToolGrade > ToolGrade) { tooHard = true; continue; }
            float d = (b.transform.position - transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    private IEnumerator HarvestTask()
    {
        int done = 0;
        while (true)
        {
            taskLabel = $"수확 중 ({done}칸)";
            var plant = NearestPlant(PlantTile.PlantState.ReadyToHarvest);
            if (plant == null) break;

            yield return MoveTo(plant.transform.position, 0.9f);
            if (plant == null || plant.CurrentState != PlantTile.PlantState.ReadyToHarvest) continue;

            var results = plant.Harvest();
            if (results == null) break;
            foreach (var r in results) Give(r.item, r.amount);
            done++;
            yield return new WaitForSeconds(0.25f);
        }
        Report(done > 0 ? L("harvest_done", done) : L("harvest_none"));
    }

    private IEnumerator PlantTask(string seedName, int count)
    {
        var seed = NpcWorld.FindItem(seedName, i => i.itemType == FarmingItemType.Seed && i.seedData != null);
        if (seed == null)                                           { Report(L("seed_unknown", seedName)); yield break; }
        if (Inventory.Instance == null || !Inventory.Instance.HasItem(seed)) { Report(L("not_in_inventory", seed.itemName)); yield break; }

        int planted = 0;
        while (count <= 0 || planted < count)
        {
            taskLabel = $"{seed.itemName} 심는 중 ({planted})";
            if (!Inventory.Instance.HasItem(seed)) break;
            var plant = NearestPlant(PlantTile.PlantState.Empty);
            if (plant == null) break;

            yield return MoveTo(plant.transform.position, 0.9f);
            if (plant == null || plant.CurrentState != PlantTile.PlantState.Empty) continue;
            if (!Inventory.Instance.ConsumeItem(seed, 1)) break;
            if (!plant.PlantSeed(seed.seedData)) { Inventory.Instance.AddItem(seed, 1); break; }
            planted++;
            yield return new WaitForSeconds(0.2f);
        }
        Report(planted > 0 ? L("plant_done", seed.itemName, planted) : L("plant_none"));
    }

    private IEnumerator RegisterSeedTask(string seedName, int count)
    {
        var seed = NpcWorld.FindItem(seedName, i => i.itemType == FarmingItemType.Seed && i.seedData != null);
        if (seed == null) { Report(L("seed_unknown", seedName)); yield break; }
        if (Inventory.Instance == null || !Inventory.Instance.HasItem(seed)) { Report(L("not_in_inventory", seed.itemName)); yield break; }

        var seeders = new List<AutoSeeder>(NpcWorld.NearbySeeders(player.position));
        if (seeders.Count == 0) { Report(L("no_seeder")); yield break; }
        seeders.Sort((a, b) => (a.transform.position - transform.position).sqrMagnitude
                       .CompareTo((b.transform.position - transform.position).sqrMagnitude));

        int left = count <= 0 ? int.MaxValue : count;
        int put = 0, used = 0;
        foreach (var s in seeders)
        {
            if (left <= 0 || !Inventory.Instance.HasItem(seed)) break;
            if (s == null || !s.HasSpace) continue;

            taskLabel = $"파종기에 {seed.itemName} 넣는 중 ({put})";
            yield return MoveTo(s.transform.position, 1.2f);
            if (s == null) continue;
            int n = s.InsertFromInventory(seed, left);
            if (n <= 0) continue;
            put += n;
            left -= n;
            used++;
            yield return new WaitForSeconds(0.2f);
        }
        Report(put > 0 ? L("register_done", used, seed.itemName, put) : L("seeder_full"));
    }

    private PlantTile NearestPlant(PlantTile.PlantState state)
    {
        PlantTile best = null;
        float bestD = float.MaxValue;
        foreach (var p in NpcWorld.AllPlants())
        {
            if (p.CurrentState != state) continue;
            if ((p.transform.position - player.position).sqrMagnitude > NpcWorld.WorkRadius * NpcWorld.WorkRadius) continue;
            float d = (p.transform.position - transform.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = p; }
        }
        return best;
    }

    private IEnumerator CraftTask(string name, int count)
    {
        var recipe = NpcWorld.FindRecipe(name);
        var cm = CraftingManager.Instance;
        if (recipe == null || cm == null)      { Report(L("craft_unknown", name)); yield break; }
        if (!cm.IsRecipeUnlocked(recipe))     { Report(L("craft_locked", NpcWorld.RecipeName(recipe))); yield break; }

        int made = 0;
        for (int i = 0; i < count; i++)
        {
            taskLabel = $"{NpcWorld.RecipeName(recipe)} 제작 {made}/{count}";
            if (!cm.Craft(recipe)) break;
            made++;
            yield return new WaitForSeconds(0.3f);
        }
        Report(made >= count ? L("craft_done", NpcWorld.RecipeName(recipe), made)
             : made > 0     ? L("craft_partial", made)
                            : L("craft_none"));
    }

    private IEnumerator PlaceTask(string name, int count, string near)
    {
        var inv = Inventory.Instance;
        var gm  = GridManager.Instance;
        if (inv == null || gm == null) yield break;

        var item = NpcWorld.FindItem(name, i => i.itemType == FarmingItemType.Tile && i.tilePrefab != null && inv.GetCount(i) > 0)
                ?? NpcWorld.FindItem(name, i => i.itemType == FarmingItemType.Tile);
        if (item == null || item.tilePrefab == null) { Report(L("place_not_placeable", name)); yield break; }
        if (inv.GetCount(item) <= 0)                 { Report(L("not_in_inventory", item.itemName)); yield break; }

        Vector2Int origin = gm.WorldToGrid(player.position);
        if (!string.IsNullOrEmpty(near))
        {
            var anchorTile = NearestPlacedTile(near);
            if (anchorTile == null) { Report(L("place_no_near", near)); yield break; }
            origin = anchorTile.GridPosition;
        }

        int w = item.tileWidth, h = item.tileHeight;
        Vector2Int playerCell = gm.WorldToGrid(player.position);
        int placed = 0, want = Mathf.Min(count, inv.GetCount(item));

        foreach (var anchor in CellsNearestFirst(origin, 8))
        {
            if (placed >= want || inv.GetCount(item) <= 0) break;
            if (!FootprintFree(gm, anchor, w, h, playerCell)) continue;

            taskLabel = $"{item.itemName} 설치 {placed}/{want}";
            yield return MoveTo(gm.GridToWorldCenter(anchor, w, h), 1.2f);
            if (!FootprintFree(gm, anchor, w, h, playerCell)) continue;

            var tile = gm.PlaceTile(anchor, item.tilePrefab, w, h, 0, item);
            if (tile == null) continue;
            inv.ConsumeItem(item, 1);
            placed++;
            yield return new WaitForSeconds(0.25f);
        }

        Report(placed >= want ? L("place_done",    item.itemName, placed)
             : placed > 0     ? L("place_partial", item.itemName, placed)
                              : L("place_none"));
    }

    private TileItem NearestPlacedTile(string name)
    {
        var target = NpcWorld.FindItem(name, i => i.itemType == FarmingItemType.Tile);
        if (target == null || GridManager.Instance == null) return null;
        TileItem best = null;
        float bestD = float.MaxValue;
        foreach (var t in GridManager.Instance.GetDynamicTiles())
        {
            if (t == null || t.SourceItem != target) continue;
            float d = (t.transform.position - player.position).sqrMagnitude;
            if (d < bestD) { bestD = d; best = t; }
        }
        return best;
    }

    private static bool FootprintFree(GridManager gm, Vector2Int anchor, int w, int h, Vector2Int playerCell)
    {
        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
        {
            var c = new Vector2Int(anchor.x + x, anchor.y + y);
            if (c == playerCell || !gm.IsInBounds(c) || gm.GetTile(c) != null) return false;
        }
        return true;
    }

    private static List<Vector2Int> CellsNearestFirst(Vector2Int origin, int radius)
    {
        var cells = new List<Vector2Int>((radius * 2 + 1) * (radius * 2 + 1));
        for (int x = -radius; x <= radius; x++)
        for (int y = -radius; y <= radius; y++)
            cells.Add(new Vector2Int(origin.x + x, origin.y + y));
        cells.Sort((a, b) => (a - origin).sqrMagnitude.CompareTo((b - origin).sqrMagnitude));
        return cells;
    }

    private IEnumerator FuelTask()
    {
        var gens = EnergyManager.Instance != null ? new List<Generator>(EnergyManager.Instance.Generators) : new List<Generator>();
        int used = 0;
        foreach (var gen in gens)
        {
            if (gen == null || !gen.UsesFuel || gen.FuelRatio >= 0.99f) continue;
            if ((gen.transform.position - player.position).sqrMagnitude > NpcWorld.WorkRadius * NpcWorld.WorkRadius) continue;
            var fuel = FindFuelInInventory();
            if (fuel == null) break;

            taskLabel = "발전소에 연료 넣는 중";
            float cellSize = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
            int   extra    = Mathf.Max(0, gen.FootprintMax.x - gen.FootprintMin.x);
            yield return MoveTo(gen.transform.position, 1.3f + extra * cellSize * 0.5f);
            while (gen != null && gen.FuelRatio < 0.99f && (fuel = FindFuelInInventory()) != null)
            {
                int n = gen.AddFuel(fuel, Inventory.Instance.GetCount(fuel));
                if (n <= 0) break;
                Inventory.Instance.ConsumeItem(fuel, n);
                used += n;
            }
        }
        Report(used > 0 ? L("fuel_done", used) : FindFuelInInventory() == null ? L("fuel_no_fuel") : L("fuel_full"));
    }

    private static FarmingItemData FindFuelInInventory()
    {
        var inv = Inventory.Instance;
        if (inv == null) return null;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            var s = inv.GetSlot(i);
            if (s != null && !s.IsEmpty && s.Item.canUseFuel) return s.Item;
        }
        return null;
    }

    private void SellTask(string name, int count)
    {
        var inv = Inventory.Instance;
        var item = NpcWorld.FindItem(name, i => inv != null && inv.GetCount(i) > 0);
        if (item == null || ShopManager.Instance == null) { Report(L("sell_none", name)); return; }

        // 개수 없으면 기본 1개만 판매
        int owned = inv.GetCount(item);
        int n = Mathf.Min(count > 0 ? count : 1, owned);
        Report(ShopManager.Instance.TrySell(item, n) ? L("sell_done", item.itemName, n) : L("sell_fail"));
    }

    private void BuyTask(string name, int count)
    {
        var shop = ShopManager.Instance;
        if (shop == null) return;
        var wanted = NpcWorld.FindItem(name);
        FarmingItemData item = null;
        foreach (var s in shop.CurrentStock)
            if (s != null && s == wanted) { item = s; break; }
        if (item == null) { Report(L("buy_not_in_shop", name)); return; }

        Report(shop.TryBuy(item, count) ? L("buy_done", item.itemName, count) : L("buy_fail"));
    }

    private void Give(FarmingItemData item, int amount)
    {
        if (item == null || amount <= 0) return;
        if (Inventory.Instance == null || !Inventory.Instance.AddItem(item, amount))
            DroppedItem.Spawn(item, amount, player.position, player);
    }

    private IEnumerator MoveTo(Vector3 target, float stopDistance)
    {
        float giveUp = Time.time + 25f;
        while (Time.time < giveUp)
        {
            Vector3 to = target - transform.position;
            to.z = 0f;
            if (to.magnitude <= stopDistance) yield break;
            Step(target);
            yield return null;
        }
    }

    private void Step(Vector3 target)
    {
        Vector3 next = Vector3.MoveTowards(transform.position, new Vector3(target.x, target.y, transform.position.z),
                                           moveSpeed * Time.deltaTime);
        if (GridManager.Instance != null)
        {
            Vector2 clamped = GridManager.Instance.ClampToWorldBounds(next);
            next = new Vector3(clamped.x, clamped.y, next.z);
        }
        if (body != null && Mathf.Abs(next.x - transform.position.x) > 0.001f)
            body.flipX = next.x < transform.position.x;
        transform.position = next;
    }

    private void Update()
    {
        if (player == null) return;
        TrackPlayerIdle();

        if (!taskRunning)
        {
            switch (mode)
            {
                case Mode.Follow: UpdateFollow(); break;
                case Mode.Wander: UpdateWander(wanderAnchor, 4f); break;
            }
            MaybeIdleChat();
        }

        if (bubble != null && bubble.gameObject.activeSelf && Time.time > bubbleUntil) HideBubble();
    }

    private void UpdateFollow()
    {
        float d = Vector2.Distance(transform.position, player.position);
        if (d > followDistance + 0.4f)
        {
            Vector3 dir = (transform.position - player.position).normalized;
            Step(player.position + dir * followDistance);
            nextWanderTime = Time.time + 2f;
        }
        else if (Time.time - playerIdleSince > 6f)
        {
            UpdateWander(player.position, 2.5f);
        }
    }

    private void UpdateWander(Vector3 anchor, float radius)
    {
        if (Time.time >= nextWanderTime)
        {
            wanderTarget   = anchor + (Vector3)(Random.insideUnitCircle * radius);
            nextWanderTime = Time.time + Random.Range(3f, 6f);
        }
        if (Vector2.Distance(transform.position, wanderTarget) > 0.2f) Step(wanderTarget);
    }

    private void TrackPlayerIdle()
    {
        if ((player.position - lastPlayerPos).sqrMagnitude > 0.0004f)
        {
            lastPlayerPos   = player.position;
            playerIdleSince = Time.time;
        }
    }

    private void MaybeIdleChat()
    {
        if (IsThinking || Time.time < nextIdleChatTime) return;
        if (nextIdleChatTime == 0f) { nextIdleChatTime = Time.time + 60f; return; }
        if (Time.time - playerIdleSince < 20f) return;
        Say(NpcPersona.RandomIdleLine(persona), 3.5f);
        nextIdleChatTime = Time.time + Random.Range(60f, 120f);
    }

    private void Report(string text) { Say(text); Log(text); }

    private string L(string key, params object[] args) => NpcPersona.Line(persona, key, args);

    // 폰트에 없는 글자 제거
    private string FontSafe(string text)
    {
        var font = bubble != null ? bubble.font : null;
        if (font == null || string.IsNullOrEmpty(text)) return text;

        var sb = new System.Text.StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsSurrogate(c)) continue;
            if (c == ' ' || c == '\n' || font.HasCharacter(c, true, true)) sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    private static string NoParse(string text)
        => string.IsNullOrEmpty(text) ? text : $"<noparse>{text.Replace("</noparse>", "")}</noparse>";

    private void Log(string text) => NotificationUI.Instance?.Show($"{NpcName}: {text}", chatColor);

    public void Say(string text, float seconds = -1f)
    {
        if (bubble == null) return;
        bubble.text = text;
        bubble.gameObject.SetActive(true);

        bubble.ForceMeshUpdate();
        var b = bubble.textBounds;
        var size = new Vector2(b.size.x + 0.35f, b.size.y + 0.2f);
        bubbleBg.transform.localPosition = new Vector3(b.center.x, b.center.y, 0.01f);
        bubbleBg.size   = size;
        bubbleFill.size = size - new Vector2(BubbleBorder * 2f, BubbleBorder * 2f);
        bubbleTail.localPosition = new Vector3(0f, -size.y * 0.5f, 0f);
        bubbleUntil = Time.time + (seconds > 0f ? seconds : Mathf.Clamp(2f + text.Length * 0.08f, 3f, 8f));
    }

    private void HideBubble() { if (bubble != null) bubble.gameObject.SetActive(false); }

    private void BuildVisual(Player p)
    {
        var playerSr = p.GetComponentInChildren<SpriteRenderer>();
        var bodyGO   = new GameObject("Body");
        bodyGO.transform.SetParent(transform, false);
        body = bodyGO.AddComponent<SpriteRenderer>();
        if (playerSr != null)
        {
            body.sprite         = CatSkin.Original(playerSr.sprite);
            body.sharedMaterial = playerSr.sharedMaterial;
            body.sortingLayerID = playerSr.sortingLayerID;
            body.sortingOrder   = playerSr.sortingOrder;
            bodyGO.transform.localScale = playerSr.transform.lossyScale;
        }
        body.color = tint;

        var playerAnim = p.GetComponentInChildren<Animator>();
        if (playerAnim != null && playerAnim.runtimeAnimatorController != null)
            bodyGO.AddComponent<Animator>().runtimeAnimatorController = playerAnim.runtimeAnimatorController;

        float top = body.sprite != null ? body.sprite.bounds.extents.y * bodyGO.transform.localScale.y : 0.5f;

        var layers = SortingLayer.layers;
        topSortingLayer = layers.Length > 0 ? layers[layers.Length - 1].id : 0;

        var nameTag = MakeWorldText("Name", new Vector3(0f, top + 0.15f, 0f), 2.2f);
        nameTag.text      = NpcName;
        nameTag.color     = Color.white;
        nameTag.fontStyle = FontStyles.Bold;
        nameTag.ForceMeshUpdate();
        var nb = nameTag.textBounds;
        var namePill = WorldSprite(nameTag.transform, "Pill", CuteUIKit.Rounded(13), CuteUIKit.Strawberry, 199);
        namePill.size = new Vector2(nb.size.x + 0.26f, Mathf.Max(0.3f, nb.size.y + 0.1f));
        namePill.transform.localPosition = new Vector3(nb.center.x, nb.center.y, 0.01f);

        bubble = MakeWorldText("Bubble", new Vector3(0f, top + 0.6f, 0f), 2.6f);
        bubble.color     = CuteUIKit.Cocoa;
        bubble.alignment = TextAlignmentOptions.Bottom;
        bubble.textWrappingMode = TextWrappingModes.Normal;
        bubble.rectTransform.pivot     = new Vector2(0.5f, 0f);
        bubble.rectTransform.sizeDelta = new Vector2(4.5f, 3f);

        bubbleBg   = WorldSprite(bubble.transform, "Background", CuteUIKit.Rounded(12), CuteUIKit.Latte, 198);
        bubbleFill = WorldSprite(bubbleBg.transform, "Fill", CuteUIKit.Rounded(8), CuteUIKit.Cream, 199);
        var tail   = WorldSprite(bubbleBg.transform, "Tail", CuteUIKit.Rounded(3), CuteUIKit.Latte, 198);
        tail.size  = new Vector2(TailSize, TailSize);
        bubbleTail = tail.transform;
        bubbleTail.localRotation = Quaternion.Euler(0f, 0f, 45f);
        float tailFill = TailSize - BubbleBorder * 2f;
        WorldSprite(bubbleTail, "Fill", CuteUIKit.Rounded(2), CuteUIKit.Cream, 199).size = new Vector2(tailFill, tailFill);
        HideBubble();
    }

    private const float BubbleBorder = 0.04f;
    private const float TailSize     = 0.19f;

    private SpriteRenderer WorldSprite(Transform parent, string objName, Sprite sprite, Color color, int order)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite         = sprite;
        sr.drawMode       = SpriteDrawMode.Sliced;
        sr.sharedMaterial = WorldConnectionLine.SharedMaterial;
        sr.color          = color;
        sr.sortingLayerID = topSortingLayer;
        sr.sortingOrder   = order;
        return sr;
    }

    private TextMeshPro MakeWorldText(string objName, Vector3 localPos, float size)
    {
        var go = new GameObject(objName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        var t = go.AddComponent<TextMeshPro>();
        t.fontSize  = size;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.rectTransform.sizeDelta = new Vector2(4f, 0.5f);
        t.sortingLayerID = topSortingLayer;
        t.sortingOrder   = 200;
        return t;
    }
}
