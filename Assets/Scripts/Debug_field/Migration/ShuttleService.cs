using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[Serializable]
public class CargoLine
{
    public string item;
    public int    amount;
    public int    loaded;

    public int Missing => Mathf.Max(0, amount - loaded);
}

[Serializable]
public class ResidentSave
{
    public string name;
    public int    color, accessory;
    public int    padX, padY;
    public int    id;
    public int    friendship;
    public int    talkedAt;
    public int    giftedAt;
}

[Serializable]
public class ShuttleSave
{
    public int    id;
    public int    padX, padY;
    public bool   away;
    public float  timer;
    public float  tripTime;
    public List<CargoLine> cargo = new();
    public string family;
    public List<CatLook> cats = new();
    public string message;
    public long   reward;
    public string bonusItem;
    public int    bonusAmount;

    public ShuttleSave Clone() => JsonUtility.FromJson<ShuttleSave>(JsonUtility.ToJson(this));
}

// 이주 셔틀 본체
public class ShuttleService : MonoBehaviour
{
    public static ShuttleService Instance => Services.Get<ShuttleService>();

    public static int Population => Instance != null ? Instance.population : 0;

    public static int Trips => Instance != null ? Instance.trips : 0;

    public static event Action OnAnyArrival;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnAnyArrival = null;
        oreOf.Clear();
    }

    public event Action OnChanged;

    public const int   MaxShuttles      = 3;
    public const int   MaxNamed         = 40;
    public const float TakeoffSeconds   = 1.6f;
    public const float LandSeconds      = 2.4f;
    public const float FirstTripSeconds = 15f;
    public const float TripSeconds      = 40f;
    public const float ApproachSeconds  = 6f;
    public const float ControlTripMult  = 0.6f;
    public const float ControlReward    = 0.2f;
    public const float RewardMultiplier = 2f;
    public const int   NoPad            = int.MinValue;

    private readonly List<ShuttleSave>  shuttles  = new();
    private readonly List<ResidentSave> residents = new();
    private readonly List<TileItem>     pads      = new();
    private int   serial;
    private int   population;
    private int   trips;
    private bool  padGranted;
    private float settleUntil;
    private float nextSlowTick;
    private FlashBeam beam;

    public IReadOnlyList<ShuttleSave>  Shuttles  => shuttles;
    public IReadOnlyList<ResidentSave> Residents => residents;

    public static int Capacity => 1 + Mathf.Min(MaxShuttles - 1, CivFacility.CountPowered(CivEffect.Comms));

    public static FarmingItemData PadItem => NetWorld.Item("Item_ShuttlePad");

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(this); return; }
        settleUntil = Time.unscaledTime + 1.5f;
    }

    private void OnDestroy()
    {
        Services.Unregister(this);
    }

    private void Update()
    {
        // 시계는 모두, 도착 처리는 호스트만
        float dt = Time.deltaTime;
        foreach (var s in shuttles)
            if (s.away && s.timer > 0f) s.timer = Mathf.Max(0f, s.timer - dt);

        if (NetSession.IsReplica || SaveManager.IsLoading || Time.unscaledTime < settleUntil) return;

        if (Time.unscaledTime >= nextSlowTick)
        {
            nextSlowTick = Time.unscaledTime + 0.5f;
            bool changed = SyncPads();
            if (!padGranted) EnsurePad();
            changed |= PullFromStorages();
            if (changed) OnChanged?.Invoke();
        }

        for (int i = shuttles.Count - 1; i >= 0; i--)
        {
            var s = shuttles[i];
            if (s.away && s.timer <= 0f && s.padX != NoPad) Arrive(s);
        }
    }

    public ShuttleSave ShuttleAt(Vector2Int pad)
    {
        foreach (var s in shuttles)
            if (s.padX == pad.x && s.padY == pad.y) return s;
        return null;
    }

    public ShuttleSave Find(int id) => shuttles.Find(s => s.id == id);

    public static ShuttlePad PadOf(ShuttleSave s)
    {
        var gm = GridManager.Instance;
        if (s == null || gm == null || s.padX == NoPad) return null;
        var pos = new Vector2Int(s.padX, s.padY);
        return gm.GetTile(pos) is ShuttlePad pad && pad.GridPosition == pos && !pad.IsGhost ? pad : null;
    }

    private bool SyncPads()
    {
        var gm = GridManager.Instance;
        if (gm == null) return false;
        gm.CollectTiles(TileType.ShuttlePad, pads);
        pads.RemoveAll(p => p == null || p.IsGhost);
        if (pads.Count > 0) padGranted = true;
        bool changed = false;

        for (int i = shuttles.Count - 1; i >= 0; i--)
        {
            var s = shuttles[i];
            if (PadOf(s) != null) continue;
            var free = FreePad();
            if (free != null)
            {
                s.padX = free.GridPosition.x;
                s.padY = free.GridPosition.y;
                changed = true;
            }
            else if (!s.away)
            {
                int loaded = LoadedTotal(s);
                RefundLoaded(s, Inventory.Instance, s.padX == NoPad ? (Vector3?)null : gm.GridToWorld(new Vector2Int(s.padX, s.padY)));
                shuttles.RemoveAt(i);
                changed = true;
                NotificationUI.Instance?.Show(loaded > 0 ? $"착륙장이 없어져 셔틀이 돌아갔어요 — 실어 둔 짐 {loaded}개는 가방으로 돌려받았어요"
                                                         : "착륙장이 없어져 셔틀이 돌아갔어요 — 착륙장을 다시 놓으면 새 셔틀이 와요");
            }
            else if (s.padX != NoPad)
            {
                s.padX = s.padY = NoPad;
                changed = true;
            }
        }

        int cap = Capacity;
        foreach (var pad in pads)
        {
            if (shuttles.Count >= cap) break;
            if (ShuttleAt(pad.GridPosition) != null) continue;
            shuttles.Add(NewShuttle(pad.GridPosition));
            changed = true;
        }

        for (int i = shuttles.Count - 1; i >= 0 && shuttles.Count > cap; i--)
            if (!shuttles[i].away && LoadedTotal(shuttles[i]) == 0) { shuttles.RemoveAt(i); changed = true; }
        return changed;
    }

    private TileItem FreePad()
    {
        foreach (var p in pads)
            if (ShuttleAt(p.GridPosition) == null) return p;
        return null;
    }

    private ShuttleSave NewShuttle(Vector2Int pad)
    {
        var s = new ShuttleSave { padX = pad.x, padY = pad.y };
        if (serial == 0) Request(s);
        else
        {
            s.id    = ++serial;
            s.away  = true;
            s.timer = s.tripTime = ApproachSeconds;
        }
        return s;
    }

    public static int LoadedTotal(ShuttleSave s)
    {
        int n = 0;
        if (s != null) foreach (var l in s.cargo) n += l.loaded;
        return n;
    }

    public static bool IsFull(ShuttleSave s)
    {
        if (s == null || s.cargo.Count == 0) return false;
        foreach (var l in s.cargo) if (l.Missing > 0) return false;
        return true;
    }

    public static bool CanComplete(ShuttleSave s)
    {
        var inv = Inventory.Instance;
        if (s == null || s.away || inv == null || s.cargo.Count == 0) return false;
        foreach (var l in s.cargo)
        {
            if (l.Missing <= 0) continue;
            var item = NetWorld.Item(l.item);
            if (item == null || inv.GetCount(item) < l.Missing) return false;
        }
        return true;
    }

    public static int Loadable(ShuttleSave s)
    {
        var inv = Inventory.Instance;
        if (s == null || s.away || inv == null) return 0;
        int n = 0;
        foreach (var l in s.cargo)
        {
            if (l.Missing <= 0) continue;
            var item = NetWorld.Item(l.item);
            if (item != null) n += Mathf.Min(l.Missing, inv.GetCount(item));
        }
        return n;
    }

    public int CompletableCount()
    {
        int n = 0;
        foreach (var s in shuttles) if (CanComplete(s)) n++;
        return n;
    }

    public static int NeededBy(FarmingItemData item)
    {
        var svc = Instance;
        if (svc == null || item == null) return 0;
        int n = 0;
        foreach (var s in svc.shuttles)
        {
            if (s.away) continue;
            foreach (var l in s.cargo)
                if (l.item == item.name) n += l.Missing;
        }
        return n;
    }

    public int LoadFromBag(ShuttleSave s, List<(FarmingItemData item, int amount)> got = null)
    {
        var inv = Inventory.Instance;
        if (s == null || s.away || inv == null) return 0;
        int total = 0;
        StringBuilder key = null;
        foreach (var line in s.cargo)
        {
            int miss = line.Missing;
            if (miss <= 0) continue;
            var item = NetWorld.Item(line.item);
            if (item == null) continue;
            int n = Mathf.Min(miss, inv.GetCount(item));
            if (n <= 0 || !inv.ConsumeItem(item, n)) continue;
            line.loaded += n;
            total += n;
            got?.Add((item, n));
            if (NetSession.IsReplica)
            {
                key ??= new StringBuilder();
                if (key.Length > 0) key.Append(';');
                key.Append(item.name).Append(':').Append(n);
            }
        }
        if (total <= 0) return 0;

        if (NetSession.IsReplica) NetOps.Global(NetOpType.ShuttleLoad, key.ToString(), s.id);
        else if (IsFull(s)) Depart(s, inv);
        OnChanged?.Invoke();
        return total;
    }

    public void LoadFromPeer(int id, string key, List<(FarmingItemData item, int amount)> refund, out ShuttleSave departed)
    {
        departed = null;
        var s = Find(id);
        foreach (var (item, amount) in NetOps.ParseItems(key))
        {
            var line = s != null && !s.away ? s.cargo.Find(l => l.item == item.name) : null;
            int put = line != null ? Mathf.Min(amount, line.Missing) : 0;
            if (line != null) line.loaded += put;
            if (amount - put > 0) refund.Add((item, amount - put));
        }
        if (s != null && !s.away && IsFull(s))
        {
            Depart(s, null);
            departed = s;
        }
        OnChanged?.Invoke();
    }

    private void Depart(ShuttleSave s, Inventory bonusTo)
    {
        CurrencyManager.Instance?.Add(s.reward);
        var bonus = NetWorld.Item(s.bonusItem);
        if (bonusTo != null && bonus != null && s.bonusAmount > 0)
            bonusTo.AddItemOrDrop(bonus, s.bonusAmount, PlayerTileController.Instance != null ? PlayerTileController.Instance.transform.position : Vector3.zero);

        s.away     = true;
        s.tripTime = s.timer = TripTime();
        NotificationUI.Instance?.Show($"짐을 다 실었어요! 셔틀이 고향별로 떠나요 — 짐값 +{CurrencyManager.Format(s.reward)}코인" +
                                      (bonus != null && s.bonusAmount > 0 ? $" · 덤 {bonus.itemName} ×{s.bonusAmount}" : ""), NotificationUI.GoodNewsColor);
        NetHost.NoticeAll($"셔틀이 고향별로 떠났어요 — 짐값 +{CurrencyManager.Format(s.reward)}코인");
        OnChanged?.Invoke();
    }

    private float TripTime()
    {
        if (trips == 0 && population == 0) return FirstTripSeconds;
        return TripSeconds * (CivFacility.CountPowered(CivEffect.Control) > 0 ? ControlTripMult : 1f) / TechTreeManager.ShuttleSpeed;
    }

    private void Arrive(ShuttleSave s)
    {
        int n = s.cats.Count;
        if (n > 0)
        {
            population += n;
            trips++;
            int nextId = NextResidentId();
            foreach (var c in s.cats)
                residents.Add(new ResidentSave { id = nextId++, name = c.name, color = c.color, accessory = c.accessory, padX = s.padX, padY = s.padY });
            TrimResidents();

            string msg = $"{s.family} 가족 {n}마리가 셔틀에서 내렸어요! 주민 {population}마리";
            NotificationUI.Instance?.Show(msg);
            NetHost.NoticeAll(msg);
            OnAnyArrival?.Invoke();
        }

        if (shuttles.Count > Capacity) shuttles.Remove(s);
        else Request(s);
        OnChanged?.Invoke();
    }

    public void Reroll(ShuttleSave s)
    {
        if (s == null || s.away) return;
        if (NetSession.IsReplica) { NetOps.Global(NetOpType.ShuttleReroll, null, s.id); return; }
        RefundLoaded(s, Inventory.Instance, null);
        Request(s);
        OnChanged?.Invoke();
    }

    public void RerollFromPeer(int id, List<(FarmingItemData item, int amount)> refund)
    {
        var s = Find(id);
        if (s == null || s.away) return;
        foreach (var l in s.cargo)
        {
            var item = NetWorld.Item(l.item);
            if (item != null && l.loaded > 0) refund.Add((item, l.loaded));
        }
        Request(s);
        OnChanged?.Invoke();
    }

    private static void RefundLoaded(ShuttleSave s, Inventory inv, Vector3? dropAt)
    {
        foreach (var l in s.cargo)
        {
            var item = NetWorld.Item(l.item);
            if (item == null || l.loaded <= 0) continue;
            Vector3 at = dropAt ?? (PlayerTileController.Instance != null ? PlayerTileController.Instance.transform.position : Vector3.zero);
            if (inv != null) inv.AddItemOrDrop(item, l.loaded, at);
            else DroppedItem.Spawn(item, l.loaded, at, null);
            l.loaded = 0;
        }
    }

    private bool PullFromStorages()
    {
        bool any = false;
        foreach (var s in shuttles)
        {
            if (s.away) continue;
            var pad = PadOf(s);
            if (pad == null || pad.Links.Count == 0) continue;
            bool pulled = false;
            foreach (var line in s.cargo)
            {
                var item = NetWorld.Item(line.item);
                if (item == null) continue;
                for (int i = 0; i < pad.Links.Count && line.Missing > 0; i++)
                {
                    if (pad.Links[i] is not ItemStorage storage) continue;
                    int have = storage.GetCount(item);
                    if (have <= 0) continue;
                    int taken = storage.RemoveItem(item, Mathf.Min(line.Missing, have));
                    if (taken <= 0) continue;
                    line.loaded += taken;
                    pulled = true;
                    if (beam == null) beam = FlashBeam.Attach(transform, FlashBeam.Kind.Generic);
                    beam.Flash(storage.transform.position, pad.transform.position, new Color(1f, 0.72f, 0.82f), 0.05f, 0.4f);
                }
            }
            if (!pulled) continue;
            any = true;
            if (IsFull(s)) Depart(s, Inventory.Instance);
        }
        return any;
    }

    public static string RandomCatName(System.Random rng) => Names[rng.Next(Names.Length)];

    private static readonly string[] Names =
    {
        "치즈", "나비", "모모", "보리", "쿠키", "두부", "콩이", "망고", "레오", "코코아", "호두", "감자", "율무", "써니",
        "루나", "토토", "밤톨", "인절미", "까미", "하양", "단추", "구름", "별이", "꼬마", "젤리", "솜사탕", "카라멜", "초코",
        "토리", "라떼", "깨비", "누룽지", "모찌", "콩떡", "마루", "가을", "봄이", "달래", "쑥떡", "우유",
    };

    // 문명 단계별 짐 크기, 밭 일 단위
    private static readonly int[] UnitsByLevel = { 0, 8, 24, 45, 70, 100, 140, 160 };
    private const float UnitSeconds = 20f;
    private const float KitchenSecondsPerUnit = 10f;

    private static readonly int[] ValueByLevel = { 0, 90, 260, 700, 1800, 4500, 10000, 20000 };

    private static readonly int[] CatsByLevel = { 0, 3, 3, 4, 4, 5, 6, 7 };
    private const int MaxHousingCats = 4;

    private static readonly string[] MaterialNames =
    {
        "Item_CopperIngot", "Item_IronIngot", "item_Alloy", "Item_AluminumIngot", "Item_LithiumIngot",
        "Item_LightAlloy", "Item_CircuitBoard", "Item_TitaniumIngot", "Item_TitaniumAlloy", "Item_ReinforcedAlloy",
        "Item_EnergyCell", "Item_StarAlloy",
    };

    private readonly List<FarmingItemData> crops = new(), dishes = new(), materials = new(), goldens = new(), seeds = new();

    public static float RewardBonus()
    {
        float m = TechTreeManager.Instance != null ? TechTreeManager.Instance.OrderRewardMult : 1f;
        if (CivFacility.CountPowered(CivEffect.Control) > 0) m += ControlReward;
        return m;
    }

    public static int HousingCats => Mathf.Min(MaxHousingCats, CivFacility.CountPowered(CivEffect.Housing));

    private void Request(ShuttleSave s)
    {
        s.id       = ++serial;
        s.away     = false;
        s.timer    = s.tripTime = 0f;
        s.cargo    = new List<CargoLine>();
        s.cats     = new List<CatLook>();
        s.bonusItem   = "";
        s.bonusAmount = 0;
        if (trips == 0 && population == 0 && FirstRequest(s)) return;

        int level = Mathf.Clamp(CenterCore.CurrentLevel, 1, UnitsByLevel.Length - 1);
        var rng   = new System.Random(unchecked(Environment.TickCount * 31 + serial * 7919));
        CollectCandidates(level);

        float size  = 0.75f + (float)rng.NextDouble() * 0.55f;
        float value = ValueByLevel[level] * size;
        float units = UnitsByLevel[level] * size;
        double roll = rng.NextDouble();
        string kind;
        var taken = new List<FarmingItemData>();
        foreach (var other in shuttles)
            if (other != s && other.cargo != null)
                foreach (var l in other.cargo) { var it = NetWorld.Item(l.item); if (it != null) taken.Add(it); }
        if (level >= 3 && goldens.Count > 0 && roll < 0.06)
        {
            var g = goldens[rng.Next(goldens.Count)];
            s.cargo.Add(new CargoLine { item = g.name, amount = 1 + (rng.NextDouble() < 0.3 ? 1 : 0) });
            kind = "golden";
        }
        else if (level >= 2 && materials.Count > 0 && roll < 0.22)
        {
            float mv = value * MaterialValueShare;
            var line = Line(Pick(materials, mv, rng, taken) ?? Pick(materials, mv, rng, null), mv);
            line.amount = Mathf.Min(line.amount, MaxMaterialAmount);
            s.cargo.Add(line);
            kind = "material";
        }
        else
        {
            int lines = level <= 1 ? 1
                      : level <= 3 ? 1 + (rng.NextDouble() < (level == 2 ? 0.4 : 0.6) ? 1 : 0)
                      : 2 + (rng.NextDouble() < 0.5 ? 1 : 0);
            var used = new List<FarmingItemData>(taken);
            var own  = new List<FarmingItemData>();
            bool anyDish = false;
            for (int i = 0; i < lines; i++)
            {
                bool dish = dishes.Count > 0 && rng.NextDouble() < 0.45;
                var pool  = dish ? dishes : crops.Count > 0 ? crops : dishes;
                if (pool.Count == 0) break;
                var pick = PickFarm(pool, level, rng, used) ?? PickFarm(pool, level, rng, own);
                if (pick == null) continue;
                used.Add(pick);
                own.Add(pick);
                anyDish |= dishes.Contains(pick);
                s.cargo.Add(FarmLine(pick, units / lines));
            }
            kind = anyDish ? "dish" : "crop";
        }
        if (s.cargo.Count == 0 || s.cargo.Exists(l => string.IsNullOrEmpty(l.item)))
        {
            s.cargo.Clear();
            var blue = NetWorld.Item("BlueStar");
            if (blue != null) s.cargo.Add(FarmLine(blue, units));
            kind = "crop";
        }

        long baseValue = 0;
        foreach (var l in s.cargo)
        {
            var item = NetWorld.Item(l.item);
            if (item != null) baseValue += (long)item.basePrice * l.amount;
        }
        float mult = RewardMultiplier * RewardBonus() * (kind == "golden" ? 2.5f : 1f);
        s.reward = Math.Max(10, (long)Math.Round(baseValue * mult / 10.0) * 10);

        if (rng.NextDouble() < 0.35) PickBonus(s, level, rng);
        MakeFamily(s, CatsByLevel[level] + (rng.NextDouble() < 0.5 ? 1 : 0) + HousingCats, rng, RecentNames());
        s.message = Message(s, kind, rng);
    }

    private static bool FirstRequest(ShuttleSave s)
    {
        var blue = NetWorld.Item("BlueStar");
        if (blue == null) return false;
        s.cargo.Add(new CargoLine { item = blue.name, amount = 5 });
        s.family  = "별이네";
        s.cats    = new List<CatLook>
        {
            new() { name = "별이", color = 2, accessory = 1 },
            new() { name = "콩이", color = 2, accessory = 0 },
            new() { name = "두부", color = 5, accessory = 3 },
        };
        s.message = "블루스타를 보내 주면 고향 친구들이 힘을 낼 거다냥! 우리 가족은 그 셔틀로 건너갈게냥~";
        s.reward  = Math.Max(10, (long)Math.Round(blue.basePrice * 5 * RewardMultiplier * RewardBonus() / 10.0) * 10);
        var seed  = NetWorld.Item("Item_Seed_BlueStar");
        if (seed != null) { s.bonusItem = seed.name; s.bonusAmount = 5; }
        return true;
    }

    private HashSet<string> RecentNames()
    {
        var set = new HashSet<string>();
        foreach (var r in residents) if (r != null && !string.IsNullOrEmpty(r.name)) set.Add(r.name);
        foreach (var s in shuttles)
            if (s.cats != null) foreach (var c in s.cats) if (c != null && !string.IsNullOrEmpty(c.name)) set.Add(c.name);
        return set;
    }

    private static void MakeFamily(ShuttleSave s, int count, System.Random rng, HashSet<string> avoid)
    {
        count = Mathf.Max(1, count);
        var used = new HashSet<string>(avoid ?? new HashSet<string>());
        int familyColor = rng.Next(9);
        for (int i = 0; i < count; i++)
        {
            string name = Names[rng.Next(Names.Length)];
            for (int t = 0; t < 12 && used.Contains(name); t++) name = Names[rng.Next(Names.Length)];
            used.Add(name);
            s.cats.Add(new CatLook
            {
                name      = name,
                color     = i == 0 || rng.NextDouble() < 0.6 ? familyColor : rng.Next(9),
                accessory = rng.Next(8),
            });
        }
        s.family = s.cats[0].name + "네";
    }

    private const int ComfortableAmount = 30;
    private const int MaxAmount         = 60;
    private const float MaterialValueShare = 0.4f;
    private const int   MaxMaterialAmount  = 12;
    private const int   MaxFarmAmount      = 80;

    private readonly Dictionary<FarmingItemData, float>           unitCache = new();
    private readonly Dictionary<FarmingItemData, int>             unlockCache = new();
    private readonly Dictionary<FarmingItemData, FarmingItemData> seedOfCrop = new();

    public float FarmUnits(FarmingItemData item) => FarmUnits(item, 0);

    private float FarmUnits(FarmingItemData item, int depth)
    {
        if (item == null) return 1f;
        if (unitCache.TryGetValue(item, out float u)) return u;
        var seed = SeedOf(item);
        if (seed != null) u = Mathf.Max(0.1f, seed.seedData.TotalGrowthSeconds / UnitSeconds);
        else
        {
            var cm = CraftingManager.Instance;
            if (cm == null || depth > 6) return 1f;
            u = 1f;
            foreach (var r in cm.Recipes)
            {
                if (r == null || !r.IsKitchenRecipe || r.outputItem != item) continue;
                float sum = (r.craftSeconds > 0f ? r.craftSeconds : 10f) / KitchenSecondsPerUnit;
                foreach (var ing in r.ingredients) if (ing.item != null) sum += FarmUnits(ing.item, depth + 1) * ing.amount;
                u = Mathf.Max(0.1f, sum / Mathf.Max(1, r.outputAmount));
                break;
            }
        }
        unitCache[item] = u;
        return u;
    }

    private FarmingItemData SeedOf(FarmingItemData crop)
    {
        if (crop == null || !crop.isCropProduct) return null;
        if (seedOfCrop.Count == 0)
            foreach (var it in GameDataRegistry.FindAll<FarmingItemData>())
            {
                if (it == null || it.itemType != FarmingItemType.Seed || it.seedData?.drops == null) continue;
                foreach (var d in it.seedData.drops)
                    if (d.item != null && d.item.isCropProduct && !seedOfCrop.ContainsKey(d.item)) seedOfCrop[d.item] = it;
            }
        return seedOfCrop.TryGetValue(crop, out var s) ? s : null;
    }

    private int UnlockLevel(FarmingItemData item)
    {
        if (item == null) return 1;
        if (unlockCache.TryGetValue(item, out int lv)) return lv;
        lv = 1;
        var seed = SeedOf(item);
        if (seed != null) lv = Mathf.Max(1, seed.unlockCoreLevel);
        else if (CraftingManager.Instance != null)
            foreach (var r in CraftingManager.Instance.Recipes)
                if (r != null && r.IsKitchenRecipe && r.outputItem == item) { lv = Mathf.Max(1, r.requiredCoreLevel); break; }
        unlockCache[item] = lv;
        return lv;
    }

    private FarmingItemData PickFarm(List<FarmingItemData> pool, int level, System.Random rng, List<FarmingItemData> used)
    {
        float total = 0f;
        foreach (var c in pool)
            if (c != null && (used == null || !used.Contains(c))) total += Recency(c, level);
        if (total <= 0f) return null;
        double roll = rng.NextDouble() * total;
        foreach (var c in pool)
        {
            if (c == null || (used != null && used.Contains(c))) continue;
            if ((roll -= Recency(c, level)) < 0) return c;
        }
        return null;
    }

    private float Recency(FarmingItemData item, int level) => 1 + Mathf.Max(0, 3 - (level - UnlockLevel(item)));

    private CargoLine FarmLine(FarmingItemData item, float units)
    {
        if (item == null) return new CargoLine();
        int amount = Mathf.Clamp(Mathf.RoundToInt(units / FarmUnits(item)), 1, MaxFarmAmount);
        if (amount > 5) amount = Mathf.RoundToInt(amount / 5f) * 5;
        return new CargoLine { item = item.name, amount = Mathf.Max(1, amount) };
    }

    private static FarmingItemData Pick(List<FarmingItemData> pool, float value, System.Random rng, List<FarmingItemData> used)
    {
        FarmingItemData best = null;
        float bestAmount = float.MaxValue;
        for (int t = 0; t < 8; t++)
        {
            var c = pool[rng.Next(pool.Count)];
            if (c == null || (used != null && used.Contains(c))) continue;
            float amount = value / Mathf.Max(1, c.basePrice);
            if (amount <= ComfortableAmount) return c;
            if (amount < bestAmount) { bestAmount = amount; best = c; }
        }
        return best;
    }

    private static CargoLine Line(FarmingItemData item, float value)
    {
        if (item == null) return new CargoLine();
        int price  = Mathf.Max(1, item.basePrice);
        int amount = Mathf.Clamp(Mathf.RoundToInt(value / price), 1, MaxAmount);
        if (amount > 5) amount = Mathf.RoundToInt(amount / 5f) * 5;
        return new CargoLine { item = item.name, amount = Mathf.Max(1, amount) };
    }

    private void PickBonus(ShuttleSave s, int level, System.Random rng)
    {
        var compost = NetWorld.Item("Item_Compost");
        double r = rng.NextDouble();
        if (compost != null && r < 0.5)
        {
            s.bonusItem   = compost.name;
            s.bonusAmount = 2 + rng.Next(2 + level);
        }
        else if (seeds.Count > 0 && r < 0.85)
        {
            var seed = seeds[rng.Next(seeds.Count)];
            s.bonusItem   = seed.name;
            s.bonusAmount = 3 + rng.Next(6);
        }
        else if (level >= 5 && NetWorld.Item("Item_EnergyCell") is { } cell)
        {
            s.bonusItem   = cell.name;
            s.bonusAmount = 2;
        }
    }

    private static string Message(ShuttleSave s, string kind, System.Random rng)
    {
        if (s.cargo.Count == 0) return "";
        var first = NetWorld.Item(s.cargo[0].item);
        string name = first != null ? first.itemName : "그거";
        string eul  = ItemCellUI.Eul(name);
        string ro   = name + (ItemCellUI.HasBatchim(name) && !EndsWithRieul(name) ? "으로" : "로");
        string[] pool = kind switch
        {
            "golden"   => new[] { $"고향 아기 고양이들에게 반짝이는 {eul} 보여 주고 싶다냥!", $"{eul} 보내면 고향에 희망이 생길 거다냥" },
            "material" => new[] { $"고향 대피소를 {ro} 튼튼하게 고치고 있다냥", $"셔틀이 낡아서 {ro} 손봐야 한다냥" },
            "dish"     => new[] { $"긴 우주 여행엔 {name} 도시락이 최고다냥!", $"고향 친구들이 {eul} 기다린다냥~", $"{eul} 먹으면 무서운 우주도 괜찮다냥" },
            _          => new[] { $"고향은 자원이 바닥나 밭이 메말랐다냥… {eul} 보내 주면 차례를 기다리는 친구들이 버틸 거다냥",
                                  $"새 행성에서 자란 {eul} 고향에 보여 주고 싶다냥!", $"{eul} 보내 주면 우리 가족도 곧 간다냥" },
        };
        return pool[rng.Next(pool.Length)];
    }

    private static bool EndsWithRieul(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        char c = word[word.Length - 1];
        return c >= 0xAC00 && c <= 0xD7A3 && (c - 0xAC00) % 28 == 8;
    }

    private void CollectCandidates(int level)
    {
        crops.Clear(); dishes.Clear(); materials.Clear(); goldens.Clear(); seeds.Clear();

        bool starsOnly = level <= 1 && trips < 3;
        foreach (var item in GameDataRegistry.FindAll<FarmingItemData>())
        {
            if (item == null || item.seedData == null || item.itemType != FarmingItemType.Seed) continue;
            if (item.unlockCoreLevel > level || item.hideFromShop) continue;
            if (starsOnly && item.seedData.trait != CropTrait.StarFriends) continue;
            seeds.Add(item);
            if (item.seedData.drops == null) continue;
            foreach (var d in item.seedData.drops)
                if (d.item != null && d.item.isCropProduct && !crops.Contains(d.item))
                {
                    crops.Add(d.item);
                    if (level >= 3 && d.item.goldenVariant != null) goldens.Add(d.item.goldenVariant);
                }
        }

        var cm = CraftingManager.Instance;
        if (cm != null && trips >= 2 && HasKitchen())
            foreach (var r in cm.Recipes)
                if (r != null && r.IsKitchenRecipe && cm.IsRecipeUnlocked(r) && !dishes.Contains(r.outputItem)
                    && Obtainable(r.outputItem, level, 0)) dishes.Add(r.outputItem);

        if (cm != null)
            foreach (var n in MaterialNames)
            {
                var m = NetWorld.Item(n);
                if (m == null || materials.Contains(m)) continue;
                if (Obtainable(m, level, 0)) materials.Add(m);
            }
    }

    private bool Obtainable(FarmingItemData item, int level, int depth)
    {
        if (item == null || depth > 6) return false;
        if (item.isGolden) return true;
        if (item.isCropProduct && SeedOf(item) != null) return crops.Contains(item);
        var ore = OreDataFor(item);
        if (ore != null) return DrillLevel(ore.minToolGrade) <= level;

        var cm = CraftingManager.Instance;
        if (cm == null) return false;
        foreach (var r in cm.Recipes)
        {
            if (r == null || r.outputItem != item || !cm.IsRecipeUnlocked(r)) continue;
            bool all = true;
            foreach (var ing in r.ingredients)
                if (ing.item != null && !Obtainable(ing.item, level, depth + 1)) { all = false; break; }
            if (all) return true;
        }
        return false;
    }

    private static readonly Dictionary<FarmingItemData, ResourceData> oreOf = new();

    private static ResourceData OreDataFor(FarmingItemData item)
    {
        if (oreOf.TryGetValue(item, out var found)) return found;
        foreach (var d in GameDataRegistry.FindAll<ResourceData>())
            if (d != null && d.dropItem == item) { found = d; break; }
        oreOf[item] = found;
        return found;
    }

    private static int DrillLevel(int grade)
    {
        if (grade <= 1) return 1;
        var cm = CraftingManager.Instance;
        int best = int.MaxValue;
        if (cm != null)
            foreach (var r in cm.Recipes)
                if (r != null && r.outputItem != null && r.outputItem.IsDrill && r.outputItem.toolGrade >= grade)
                    best = Mathf.Min(best, Mathf.Max(1, Mathf.Max(r.requiredCoreLevel, CraftingManager.TierCoreLevel(r.tier))));
        return best;
    }

    private static readonly List<TileItem> kitchenBuffer = new();

    private static bool HasKitchen()
    {
        var gm = GridManager.Instance;
        if (gm == null) return false;
        gm.CollectTiles(TileType.Kitchen, kitchenBuffer);
        return kitchenBuffer.Count > 0;
    }

    private static readonly Vector2Int PadOffset = new(5, -4);

    public static void PlaceStarterPad()
    {
        if (NetSession.IsReplica) return;
        var gm = GridManager.Instance;
        var item = PadItem;
        var spot = FindPadSpot();
        if (gm == null || item == null || item.tilePrefab == null || spot == null) return;
        gm.PlaceTile(spot.Value, item.tilePrefab, item.tileWidth, item.tileHeight, 0, item);
        if (Instance != null) Instance.padGranted = true;
    }

    private static Vector2Int? FindPadSpot()
    {
        var gm   = GridManager.Instance;
        var core = CenterCore.Current;
        var item = PadItem;
        if (gm == null || core == null || item == null) return null;
        int w = item.tileWidth, h = item.tileHeight;
        var origin = core.GridPosition + PadOffset;
        for (int r = 0; r <= 14; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
                    var cell = origin + new Vector2Int(dx, dy);
                    if (gm.CanPlaceAt(cell, w, h)) return cell;
                }
        return null;
    }

    private void EnsurePad()
    {
        padGranted = true;
        var gm   = GridManager.Instance;
        var item = PadItem;
        if (gm == null || item == null || item.tilePrefab == null) return;
        gm.CollectTiles(TileType.ShuttlePad, pads);
        if (pads.Count > 0) return;
        var spot = FindPadSpot();
        if (spot != null && gm.PlaceTile(spot.Value, item.tilePrefab, item.tileWidth, item.tileHeight, 0, item) != null)
        {
            NotificationUI.Instance?.Show("센터코어 곁에 셔틀 착륙장이 생겼어요! 셔틀에 짐을 실어 보내면 고향별 고양이들이 와요", NotificationUI.GoodNewsColor);
            return;
        }
        Inventory.Instance?.AddItem(item, 1);
        NotificationUI.Instance?.Show("셔틀 착륙장을 받았어요 — 가방에서 꺼내 기지 곁에 설치해요", NotificationUI.GoodNewsColor);
    }

    public void CaptureTo(SaveData data)
    {
        data.shuttles          = new List<ShuttleSave>(shuttles);
        data.shuttleSerial     = serial;
        data.population        = population;
        data.shuttleTrips      = trips;
        data.residents         = new List<ResidentSave>(residents);
        data.shuttlePadGranted = padGranted;
    }

    public void Restore(SaveData data)
    {
        shuttles.Clear();
        residents.Clear();
        if (data.shuttles != null)
            foreach (var s in data.shuttles)
                if (s != null && (s.away || s.cargo != null && s.cargo.Count > 0))
                {
                    s.cargo ??= new List<CargoLine>();
                    s.cats  ??= new List<CatLook>();
                    shuttles.Add(s);
                }
        if (data.residents != null) residents.AddRange(data.residents.FindAll(r => r != null));
        serial     = Mathf.Max(data.shuttleSerial, MaxId());
        population = Mathf.Max(0, data.population);
        trips      = Mathf.Max(0, data.shuttleTrips);
        padGranted = data.shuttlePadGranted;

        // 구버전 주문 게시판 세이브 변환
        if (serial == 0 && population == 0 && data.ordersCompleted > 0)
        {
            population = data.ordersCompleted * 3;
            trips      = data.ordersCompleted;
            var rng = new System.Random(data.ordersCompleted * 7919 + 17);
            for (int i = 0; i < Mathf.Min(population, 16); i++)
                residents.Add(new ResidentSave { name = Names[rng.Next(Names.Length)], color = rng.Next(9), accessory = rng.Next(8), padX = NoPad, padY = NoPad });
        }
        AssignResidentIds();
        settleUntil = Time.unscaledTime + 1.5f;
        OnChanged?.Invoke();
    }

    private int NextResidentId()
    {
        int max = 0;
        foreach (var r in residents) if (r != null) max = Mathf.Max(max, r.id);
        return max + 1;
    }

    private void AssignResidentIds()
    {
        int next = NextResidentId();
        foreach (var r in residents)
            if (r != null && r.id <= 0) r.id = next++;
    }

    private void TrimResidents()
    {
        for (int i = 0; residents.Count > MaxNamed && i < residents.Count;)
        {
            if (residents[i] == null || residents[i].friendship <= 0) residents.RemoveAt(i);
            else i++;
        }
        if (residents.Count > MaxNamed) residents.RemoveRange(0, residents.Count - MaxNamed);
    }

    public ResidentSave FindResident(int id)
    {
        if (id <= 0) return null;
        foreach (var r in residents) if (r != null && r.id == id) return r;
        return null;
    }

    public void NotifyResidentsChanged() => OnChanged?.Invoke();

    private int MaxId()
    {
        int m = 0;
        foreach (var s in shuttles) m = Mathf.Max(m, s.id);
        return m;
    }

    public void SyncFromHost(List<ShuttleSave> hostShuttles, int hostPopulation, int hostTrips, List<ResidentSave> hostResidents)
    {
        shuttles.Clear();
        if (hostShuttles != null) shuttles.AddRange(hostShuttles);
        residents.Clear();
        if (hostResidents != null) residents.AddRange(hostResidents);
        population = hostPopulation;
        trips      = hostTrips;
        OnChanged?.Invoke();
    }

    // 남은 시간은 빼고 비교
    public static string StateKey(List<ShuttleSave> list)
    {
        var sb = new StringBuilder();
        if (list != null)
            foreach (var s in list)
            {
                sb.Append(s.id).Append(s.away ? 'a' : 'd').Append(s.padX).Append(',').Append(s.padY).Append(':');
                foreach (var l in s.cargo) sb.Append(l.loaded).Append('/');
                sb.Append('|');
            }
        return sb.ToString();
    }

    public void DebugRerollAll()
    {
        if (NetSession.IsReplica) return;
        foreach (var s in shuttles)
            if (!s.away)
            {
                RefundLoaded(s, Inventory.Instance, null);
                Request(s);
            }
        OnChanged?.Invoke();
    }

    public void DebugArriveAll()
    {
        if (NetSession.IsReplica) return;
        foreach (var s in shuttles)
            if (s.away) s.timer = Mathf.Min(s.timer, LandSeconds);
    }
}
