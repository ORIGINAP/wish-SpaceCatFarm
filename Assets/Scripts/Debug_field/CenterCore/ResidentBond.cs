using System.Collections.Generic;
using UnityEngine;

public static class ResidentBond
{
    public const int MaxFriendship  = 20;
    public const int PointsPerHeart = 4;
    public const int MaxHearts      = MaxFriendship / PointsPerHeart;
    public const int TalkCooldown   = 60;
    public const int GiftCooldown   = 120;

    public enum Reaction { Talk, LoveDish, LoveCrop, Golden, Dish, Crop, Full }

    public struct Outcome
    {
        public Reaction kind;
        public bool     accepted;
        public int      gained;
        public int      heartsBefore, heartsAfter;
        public bool     HeartUp => heartsAfter > heartsBefore;
    }

    public struct Reward
    {
        public FarmingItemData item;
        public int             amount;
        public long            coins;

        public string Label => item != null ? $"{item.itemName} ×{amount}" : coins > 0 ? $"{coins:N0}코인" : "";
    }

    public static string FirstArrivalTip
        => Pad.Active ? "주민 고양이 앞에서 A — 이야기 · 작물이나 요리를 든 채 A — 선물 (친해지면 답례를 줘요)"
         : MobileMode.Active ? "주민 고양이를 누르면 이야기, 작물이나 요리를 든 채 누르면 선물이에요 (친해지면 답례를 줘요)"
         : "주민 고양이를 클릭하면 이야기, 작물이나 요리를 든 채 클릭하면 선물이에요 (친해지면 답례를 줘요)";

    public static int Hearts(ResidentSave r) => r == null ? 0 : Mathf.Clamp(r.friendship, 0, MaxFriendship) / PointsPerHeart;

    public static string HeartText(ResidentSave r)
    {
        int h = Hearts(r);
        string full  = ColorUtility.ToHtmlStringRGB(CuteUIKit.Strawberry);
        string empty = ColorUtility.ToHtmlStringRGB(CuteUIKit.CocoaMuted);
        return $"<color=#{full}>{new string('■', h)}</color><color=#{empty}>{new string('□', MaxHearts - h)}</color>";
    }

    public static int Now
    {
        get
        {
            var clock = GameTimeManager.Instance;
            if (clock == null) return Mathf.FloorToInt(Time.unscaledTime / 2f);
            return (clock.CurrentDay * 24 + clock.CurrentHour) * 60 + clock.CurrentMinute;
        }
    }

    public static bool IsGift(FarmingItemData item) => item != null && item.isCropProduct;

    public static bool TakesClick(FarmingItemData held)
        => held == null || IsGift(held) || (held.itemType != FarmingItemType.Tile && held.itemType != FarmingItemType.Seed && !held.IsFertilizer);

    public static string HoverVerb(FarmingItemData held)
        => IsGift(held) ? $"클릭: {held.itemName} 선물하기" : TakesClick(held) ? "클릭: 이야기하기" : "F: 이야기하기";

    public static string PadVerb(FarmingItemData held) => IsGift(held) ? "선물" : "이야기";

    private static bool Cooled(int at, int now, int cooldown) => at <= 0 || now < at || now - at >= cooldown;

    public static bool CanGift(ResidentSave r, int now) => r != null && Cooled(r.giftedAt, now, GiftCooldown);

    public static Outcome ApplyTalk(ResidentSave r, int now)
    {
        var o = new Outcome { kind = Reaction.Talk, accepted = true, heartsBefore = Hearts(r) };
        if (r != null && r.friendship < MaxFriendship && Cooled(r.talkedAt, now, TalkCooldown))
        {
            r.friendship = Mathf.Min(MaxFriendship, r.friendship + 1);
            r.talkedAt   = now;
            o.gained     = 1;
        }
        o.heartsAfter = Hearts(r);
        return o;
    }

    public static Outcome ApplyGift(ResidentSave r, FarmingItemData item, int now)
    {
        var o = new Outcome { heartsBefore = Hearts(r) };
        if (r == null || !IsGift(item) || !CanGift(r, now))
        {
            o.kind = Reaction.Full;
            o.heartsAfter = o.heartsBefore;
            return o;
        }
        int gain = GiftGain(r, item, out o.kind);
        int before = r.friendship;
        r.friendship = Mathf.Min(MaxFriendship, r.friendship + gain);
        r.giftedAt   = now;
        o.accepted    = true;
        o.gained      = r.friendship - before;
        o.heartsAfter = Hearts(r);
        return o;
    }

    private static int GiftGain(ResidentSave r, FarmingItemData item, out Reaction kind)
    {
        var dish = FavoriteDish(r);
        var crop = FavoriteCrop(r);
        if (item == dish)                                                { kind = Reaction.LoveDish; return 8; }
        if (crop != null && (item == crop || item == crop.goldenVariant)) { kind = Reaction.LoveCrop; return 6; }
        if (item.isGolden)                                               { kind = Reaction.Golden;   return 5; }
        if (IsDish(item))                                                { kind = Reaction.Dish;     return 4; }
        kind = Reaction.Crop;
        return 2;
    }

    public static List<Reward> RewardsFor(Outcome o, ResidentSave r)
    {
        var list = new List<Reward>();
        for (int h = o.heartsBefore + 1; h <= o.heartsAfter; h++) list.Add(RewardFor(h, r));
        return list;
    }

    public static string Labels(List<Reward> rewards)
    {
        var parts = new List<string>();
        foreach (var rw in rewards) if (rw.Label.Length > 0) parts.Add(rw.Label);
        return string.Join(" · ", parts);
    }

    // 호스트, 참가자 같은 결과
    public static Reward RewardFor(int hearts, ResidentSave r)
    {
        int lv = CenterCore.Current != null ? Mathf.Max(1, CenterCore.Current.Level) : 1;
        var crop = FavoriteCrop(r);
        switch (hearts)
        {
            case 1:
            {
                var compost = NetWorld.Item("Item_Compost");
                if (compost != null) return new Reward { item = compost, amount = 2 };
                return new Reward { coins = 50L * lv };
            }
            case 2:
            {
                var seed = SeedOf(crop);
                if (seed != null && seed.unlockCoreLevel <= lv) return new Reward { item = seed, amount = 4 };
                return new Reward { coins = 100L * lv };
            }
            case 3:
                return new Reward { coins = 150L * lv };
            case 4:
                if (crop != null && crop.goldenVariant != null) return new Reward { item = crop.goldenVariant, amount = 1 };
                return new Reward { coins = 250L * lv };
            default:
                return new Reward { coins = 400L * lv };
        }
    }

    private static List<FarmingItemData> dishes, crops;
    private static readonly Dictionary<FarmingItemData, FarmingItemData> seedOfCrop = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        dishes = crops = null;
        seedOfCrop.Clear();
    }

    public static bool IsDish(FarmingItemData item) => item != null && Dishes().Contains(item);

    private static List<FarmingItemData> Dishes()
    {
        if (dishes != null && dishes.Count > 0) return dishes;
        var list = new List<FarmingItemData>();
        foreach (var r in CatKitchen.Menu())
            if (r != null && r.outputItem != null && !list.Contains(r.outputItem)) list.Add(r.outputItem);
        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        if (list.Count > 0) dishes = list;
        return list;
    }

    private static List<FarmingItemData> Crops()
    {
        if (crops != null && crops.Count > 0) return crops;
        var list = new List<FarmingItemData>();
        foreach (var seed in GameDataRegistry.FindAll<FarmingItemData>())
        {
            if (seed == null || seed.itemType != FarmingItemType.Seed || seed.seedData?.drops == null) continue;
            foreach (var d in seed.seedData.drops)
            {
                if (d?.item == null || !d.item.isCropProduct || d.item.isGolden || list.Contains(d.item)) continue;
                list.Add(d.item);
                seedOfCrop[d.item] = seed;
            }
        }
        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        if (list.Count > 0) crops = list;
        return list;
    }

    private static FarmingItemData SeedOf(FarmingItemData crop)
    {
        if (crop == null) return null;
        Crops();
        return seedOfCrop.TryGetValue(crop, out var s) ? s : null;
    }

    // 실행마다 같은 해시
    private static int Hash(ResidentSave r)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char ch in r.name ?? "") h = (h ^ ch) * 16777619;
            h = (h ^ (uint)r.color) * 16777619;
            h = (h ^ (uint)r.accessory) * 16777619;
            return (int)(h & 0x7fffffff);
        }
    }

    public static FarmingItemData FavoriteDish(ResidentSave r)
    {
        var list = Dishes();
        return r == null || list.Count == 0 ? null : list[Hash(r) % list.Count];
    }

    public static FarmingItemData FavoriteCrop(ResidentSave r)
    {
        var list = Crops();
        return r == null || list.Count == 0 ? null : list[(Hash(r) / 7) % list.Count];
    }

    // 참가자는 먼저 보이고 호스트로 전송
    public static void Interact(ResidentCat cat, FarmingItemData held, Vector3 playerPos)
    {
        var r = cat != null ? cat.Resident : null;
        if (r == null) return;
        cat.FaceTowards(playerPos);
        int now = Now;
        bool replica = NetSession.IsReplica;

        Outcome o;
        if (IsGift(held))
        {
            if (!CanGift(r, now))
            {
                cat.Reply(Line(r, new Outcome { kind = Reaction.Full }, held), 0);
                Sfx.PlayAt(SfxId.UiError, cat.transform.position);
                return;
            }
            var inv = Inventory.Instance;
            if (inv == null || !inv.ConsumeItem(held, 1)) return;
            o = ApplyGift(r, held, now);
            if (replica) NetOps.Resident(NetOpType.ResidentGift, r.id, held);
            Sfx.PlayAt(SfxId.StoreItem, cat.transform.position);
            GamepadRumble.Play(Haptic.Store);
        }
        else
        {
            o = ApplyTalk(r, now);
            if (replica) NetOps.Resident(NetOpType.ResidentTalk, r.id);
            Sfx.PlayAt(SfxId.UiConfirm, cat.transform.position);
            GamepadRumble.Play(Haptic.Confirm);
        }

        string line = Line(r, o, held);
        if (o.HeartUp)
        {
            var rewards = RewardsFor(o, r);
            if (!replica) foreach (var rw in rewards) GiveLocal(rw, playerPos);
            line = o.heartsAfter >= MaxHearts ? "우린 이제 단짝이다냥! 이거 받아라냥" : "대원님이랑 더 친해졌다냥! 이거 받아라냥";
            NotificationUI.Instance?.Show($"{r.name}의 답례 선물: {Labels(rewards)} (친밀도 {o.heartsAfter})");
            Sfx.Play(SfxId.QuestComplete, 0.6f);
            GamepadRumble.Play(Haptic.Upgrade);
        }
        cat.Reply(line, Mathf.Clamp(o.gained >= 6 ? 3 : o.gained >= 4 ? 2 : o.gained, 0, 3));
        if (!replica) ShuttleService.Instance?.NotifyResidentsChanged();
    }

    private static void GiveLocal(Reward rw, Vector3 at)
    {
        if (rw.item != null && rw.amount > 0) Inventory.Instance?.AddItemOrDrop(rw.item, rw.amount, at);
        if (rw.coins > 0) CurrencyManager.Instance?.Add(rw.coins);
    }

    public static Outcome ApplyRemote(ResidentSave r, bool gift, FarmingItemData item, out List<Reward> rewards)
    {
        int now = Now;
        var o = gift ? ApplyGift(r, item, now) : ApplyTalk(r, now);
        rewards = RewardsFor(o, r);
        var cat = ResidentCat.Showing(r);
        if (cat != null) cat.Reply(Line(r, o, item), Mathf.Clamp(o.gained, 0, 3));
        ShuttleService.Instance?.NotifyResidentsChanged();
        return o;
    }

    private static string Line(ResidentSave r, Outcome o, FarmingItemData item)
    {
        string name = item != null ? item.itemName : "";
        switch (o.kind)
        {
            case Reaction.Full:     return Pick("아까 먹어서 배부르다냥… 이따 줘라냥", "배가 빵빵하다냥, 조금 있다가 받을게냥");
            case Reaction.LoveDish: return Pick($"{name}! 제일 좋아하는 거다냥!!", $"{ItemCellUI.Eun(name)} 꿈에도 나왔다냥, 고맙다냥!");
            case Reaction.LoveCrop: return Pick($"{name}! 고향 냄새가 난다냥!", $"{ItemCellUI.Eul(name)} 제일 좋아한다냥, 고맙다냥!");
            case Reaction.Golden:   return Pick("반짝반짝하다냥! 이런 귀한 걸…", "황금빛이다냥! 아껴 먹을게냥");
            case Reaction.Dish:     return Pick("맛있겠다냥, 고맙다냥", "대원님 요리 최고다냥", "냠냠, 잘 먹겠다냥");
            case Reaction.Crop:     return Pick("냠, 고맙다냥", "싱싱하다냥, 잘 먹겠다냥", "밭에서 막 딴 거냥? 고맙다냥");
            default:                return TalkLine(r);
        }
    }

    private static string TalkLine(ResidentSave r)
    {
        int h = Hearts(r);
        var dish = FavoriteDish(r);
        var crop = FavoriteCrop(r);
        string d = dish != null ? dish.itemName : "맛있는 요리";
        string c = crop != null ? crop.itemName : "작물";
        if (h <= 0)
            return Pick("새 행성은 아직 낯설다냥…", "대원님이 우리를 데려온 거냥? 고맙다냥",
                        $"{ItemCellUI.Eul(c)} 보면 고향 생각이 난다냥", "여기 공기는 처음 맡아 본다냥");
        if (h <= 2)
            return Pick($"요즘 {ItemCellUI.Ga(d)} 자꾸 생각난다냥", $"{ItemCellUI.Eun(c)} 고향에서도 키웠다냥",
                        "밭 구경하는 게 제일 재밌다냥", "셔틀이 올 때마다 가슴이 두근거린다냥");
        if (h < MaxHearts)
            return Pick("대원님 오면 꼬리가 절로 흔들린다냥", "고향 가족한테 여기 좋다고 편지 썼다냥",
                        $"{d} 한 그릇이면 하루가 행복하다냥", "언젠가 큰 이주선이 오면 다 같이 축하하자냥");
        return Pick("우린 단짝이다냥!", "이 행성이 이제 진짜 고향 같다냥", "대원님 덕분에 매일이 즐겁다냥",
                    $"{d} 또 같이 먹자냥!");
    }

    private static string Pick(params string[] pool) => pool[Random.Range(0, pool.Length)];
}
