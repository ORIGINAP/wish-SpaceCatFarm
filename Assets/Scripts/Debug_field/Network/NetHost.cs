using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// 호스트: 0.2초마다 변경분만 전송
public class NetHost
{
    public static NetHost Current => Services.Get<NetHost>();

    private const float TickInterval     = 0.2f;
    private const float VolatileInterval = 2f;
    private const float PosInterval      = 1f / 12f;

    private sealed class Peer
    {
        public ulong  id;
        public string playerId, name;
        public int    color, accessory;
        public bool   welcomed, ready;
        public int    ack, sentAck = -1;
        public readonly HashSet<Vector2Int> forcedTiles = new();
        public readonly HashSet<Vector2Int> forcedRocks = new();
        public bool   forceGlobals;
        public Vector2 pos;
        public bool   hasPos;
    }

    private readonly Dictionary<ulong, Peer> peers = new();

    private readonly Dictionary<Vector2Int, string> tileKeys = new();
    private readonly Dictionary<Vector2Int, string> tileFull = new();
    private readonly Dictionary<Vector2Int, int>    rockRemaining = new();
    private readonly HashSet<Vector2Int> seenTiles = new(), seenRocks = new();
    private readonly Dictionary<Vector2Int, ResourceBlock> rockByKey = new();
    private string globalsKey, globalsFull;
    private float tickTimer, volatileTimer, posTimer;

    public NetHost()
    {
        Services.Replace(this);
    }

    public void Dispose()
    {
        Services.Unregister(this);
    }

    public int PlayerCount => 1 + CountWelcomed();

    private int CountWelcomed()
    {
        int n = 0;
        foreach (var p in peers.Values) if (p.welcomed) n++;
        return n;
    }

    private bool AnyReady
    {
        get
        {
            foreach (var p in peers.Values) if (p.ready) return true;
            return false;
        }
    }

    public void OnClientConnected(ulong id)
    {
        if (id == NetworkManager.ServerClientId) return;
        peers[id] = new Peer { id = id };
    }

    public void OnClientDisconnected(ulong id)
    {
        if (!peers.TryGetValue(id, out var p)) return;
        peers.Remove(id);
        NetBootstrap.Avatars.Remove(id);
        if (!p.welcomed) return;
        NotificationUI.Instance?.Show($"{p.name}님이 떠났어요.");
        NoticeAll($"{p.name}님이 떠났어요.");
        BroadcastRoster();
        NetBootstrap.RaiseChanged();
    }

    public void OnHello(ulong id, byte[] data)
    {
        if (!peers.TryGetValue(id, out var p)) peers[id] = p = new Peer { id = id };
        var hello = NetWire.Decode<HelloMsg>(data);
        if (hello == null || hello.protocol != NetSession.Protocol)
        {
            NetWire.SendJson(NetWire.Welcome, id, new WelcomeMsg { reason = "게임 버전이 달라요. 호스트와 같은 버전으로 참가해 주세요." });
            return;
        }

        p.playerId  = string.IsNullOrEmpty(hello.id) ? $"guest-{id}" : hello.id;
        p.name      = string.IsNullOrEmpty(hello.name) ? $"대원 {id}" : hello.name;
        p.color     = hello.color;
        p.accessory = hello.accessory;
        p.welcomed  = true;

        var saved = NetGuests.Find(p.playerId);
        NetWire.SendJson(NetWire.Welcome, id, new WelcomeMsg
        {
            ok         = true,
            worldSize  = (int)GameLaunchContext.PendingWorldSize,
            worldTheme = (int)GameLaunchContext.PendingWorldTheme,
            worldSeed  = GameLaunchContext.PendingWorldSeed,
            gridWidth  = GridManager.Instance != null ? GridManager.Instance.GridWidth : 0,
            gridHeight = GridManager.Instance != null ? GridManager.Instance.GridHeight : 0,
            hostName   = PlayerCharacter.Name,
            knownGuest = saved != null,
            guest      = saved ?? new GuestSave(),
            lab        = Lab.Active,
        });
        NotificationUI.Instance?.Show($"{p.name}님이 들어오는 중이에요...");
        BroadcastRoster();
        NetBootstrap.RaiseChanged();
    }

    public void OnReady(ulong id)
    {
        if (!peers.TryGetValue(id, out var p) || !p.welcomed || SaveManager.Instance == null) return;

        // 쌓인 변경 먼저 보내고 스냅샷
        if (AnyReady) SendChanges(includeVolatile: false);
        else ResetBaseline();

        var snapshot = new SnapshotMsg { ack = p.ack, world = SaveManager.Instance.CaptureWorld() };
        if (TerrainMap.Current != null) snapshot.carved = TerrainMap.Current.CarvedVertices();
        NetWire.SendJson(NetWire.Snapshot, id, snapshot, compress: true);
        p.ready   = true;
        p.sentAck = p.ack;
        p.forcedTiles.Clear();
        p.forcedRocks.Clear();
        if (Lab.Active) Lab.OnPeerReady(id);

        NotificationUI.Instance?.Show($"{p.name}님이 도착했어요!");
        NoticeAll($"{p.name}님이 도착했어요!", except: id);
    }

    public void CollectReadyPeers(List<NetPeerInfo> list)
    {
        foreach (var p in peers.Values)
            if (p.ready) list.Add(new NetPeerInfo { id = p.id, name = p.name, color = p.color, pos = p.pos, hasPos = p.hasPos });
    }

    public void OnGuestData(ulong id, byte[] data)
    {
        if (!peers.TryGetValue(id, out var p) || !p.welcomed) return;
        var g = NetWire.Decode<GuestSave>(data);
        if (g == null) return;
        g.id   = p.playerId;
        g.name = p.name;
        NetGuests.Store(g);
    }

    public void OnChat(ulong id, byte[] data)
    {
        if (!peers.TryGetValue(id, out var p) || !p.welcomed) return;
        var text = NetBootstrap.CleanChat(NetWire.Decode<ChatMsg>(data)?.text);
        if (text.Length == 0) return;
        NetBootstrap.ShowChat(p.name, text);
        NetBootstrap.Avatars.Say(id, text);
        RelayChat(id, p.name, text);
    }

    public void RelayChat(ulong from, string name, string text)
    {
        foreach (var p in peers.Values)
            if (p.welcomed && p.id != from) NetWire.SendJson(NetWire.Chat, p.id, new ChatMsg { from = from, name = name, text = text });
    }

    public void OnPosition(ulong id, FastBufferReader r)
    {
        if (!peers.TryGetValue(id, out var p) || !p.ready) return;
        r.ReadValueSafe(out float x);
        r.ReadValueSafe(out float y);
        p.pos    = new Vector2(x, y);
        p.hasPos = true;
        NetBootstrap.Avatars.SetTarget(id, p.pos, p.name, p.color, p.accessory);
    }

    public void OnLook(ulong id, byte[] data)
    {
        if (!peers.TryGetValue(id, out var p) || !p.welcomed) return;
        var look = NetWire.Decode<CatLook>(data);
        if (look == null) return;
        var name = CatLook.CleanName(look.name);
        if (name.Length > 0) p.name = name;
        p.color     = look.color;
        p.accessory = look.accessory;
        BroadcastRoster();
        NetBootstrap.RaiseChanged();
    }

    public void OnHostLookChanged() => BroadcastRoster();

    public void OnOp(ulong id, byte[] data)
    {
        if (!peers.TryGetValue(id, out var p) || !p.ready) return;
        var op = NetWire.Decode<NetOp>(data);
        if (op == null) return;
        if (op.seq > p.ack) p.ack = op.seq;
        p.forceGlobals = true;

        var gm = GridManager.Instance;
        if (gm == null) return;
        var at   = new Vector2Int(op.x, op.y);
        var item = NetWorld.Item(op.item);

        switch (op.type)
        {
            case NetOpType.Place:
                if (item == null || item.tilePrefab == null) break;
                if (gm.PlaceTile(at, item.tilePrefab, item.tileWidth, item.tileHeight, op.n, item) == null)
                    Give(p, item, 1);
                break;

            case NetOpType.Remove:
            {
                var t = gm.GetTile(at);
                if (t != null && t.GridPosition == at && t.CanDismantle) gm.RemoveTile(at);
                break;
            }

            case NetOpType.StorageAdd:
            {
                int added = TileAt<TileItem>(at) is IItemBox s && item != null ? s.AddItem(item, op.n) : 0;
                if (added < op.n) Give(p, item, op.n - added);
                break;
            }
            case NetOpType.StorageRemove:
            {
                int removed = TileAt<TileItem>(at) is IItemBox s && item != null ? s.RemoveItem(item, op.n) : 0;
                if (removed < op.n) Give(p, item, -(op.n - removed));
                break;
            }

            case NetOpType.Fuel:
            {
                int used = TileAt<Generator>(at) is { } g && item != null ? g.AddFuel(item, op.n) : 0;
                if (used < op.n) Give(p, item, op.n - used);
                break;
            }

            case NetOpType.SmelterInsert:
            {
                int n = TileAt<Smelter>(at) is { } sm && item != null ? sm.InsertOre(item, op.n) : 0;
                if (n < op.n) Give(p, item, op.n - n);
                break;
            }
            case NetOpType.SmelterTakeInput:
            case NetOpType.SmelterTakeOutput:
            {
                int n = 0;
                if (TileAt<Smelter>(at) is { } sm)
                    n = op.type == NetOpType.SmelterTakeInput ? sm.TakeInput(op.n, out _) : sm.TakeOutput(op.n, out _);
                if (n < op.n) Give(p, item, -(op.n - n));
                break;
            }

            case NetOpType.DrillEquip:
                if (!(TileAt<ResourceExtractor>(at) is { } ex1 && ex1.TryEquip(item))) Give(p, item, 1);
                break;
            case NetOpType.DrillUnequip:
                if (!(TileAt<ResourceExtractor>(at) is { } ex2) || ex2.Unequip() == null) Give(p, item, -1);
                break;

            case NetOpType.Link:
            case NetOpType.Unlink:
            {
                var b = new Vector2Int(op.x2, op.y2);
                var ta = gm.GetTile(at);
                var tb = gm.GetTile(b);
                if (op.type == NetOpType.Unlink) TileItem.Unlink(ta, tb);
                else if (ta != null && tb != null && LinkRules.CanLink(ta, tb, out _)) TileItem.Link(ta, tb);
                p.forcedTiles.Add(b);
                break;
            }
            case NetOpType.UnlinkAll:
                gm.GetTile(at)?.UnlinkAll();
                break;

            case NetOpType.Plant:
            {
                var seed = NetWorld.Seed(op.item);
                if (!(TileAt<PlantTile>(at) is { } plant && plant.PlantSeed(seed))) Give(p, NetWorld.ItemForSeed(seed), 1);
                break;
            }
            case NetOpType.Harvest:
                // 그사이 호스트가 먼저 수확했을 수 있음
                if (!(TileAt<PlantTile>(at) is { } crop && crop.Harvest(remote: true) != null))
                    foreach (var (got, amount) in NetOps.ParseItems(op.item)) Give(p, got, -amount);
                break;
            case NetOpType.Fertilize:
                if (!(TileAt<PlantTile>(at) is { } soil && soil.Fertilize(item))) Give(p, item, 1);
                break;

            case NetOpType.KitchenSetRecipe:
                TileAt<CatKitchen>(at)?.SetRecipe(CatKitchen.FindRecipe(op.item));
                break;
            case NetOpType.KitchenInsert:
            {
                int n = TileAt<CatKitchen>(at) is { } k && item != null ? k.InsertIngredient(item, op.n) : 0;
                if (n < op.n) Give(p, item, op.n - n);
                break;
            }
            case NetOpType.KitchenTakeInput:
            {
                int n = TileAt<CatKitchen>(at) is { } k && item != null ? k.TakeInput(item, op.n) : 0;
                if (n < op.n) Give(p, item, -(op.n - n));
                break;
            }
            case NetOpType.KitchenTakeOutput:
            {
                // 요리 바뀌었으면 도로 가져감
                int n = TileAt<CatKitchen>(at) is { } k && k.OutputItem == item ? k.TakeOutput(op.n, out _) : 0;
                if (n < op.n) Give(p, item, -(op.n - n));
                break;
            }
            case NetOpType.CompostInsert:
            {
                int n = TileAt<Composter>(at) is { } c && item != null ? c.Insert(item, op.n) : 0;
                if (n < op.n) Give(p, item, op.n - n);
                break;
            }
            case NetOpType.CompostTakeOutput:
            {
                int n = TileAt<Composter>(at) is { } c ? c.TakeOutput(op.n) : 0;
                if (n < op.n) Give(p, item, -(op.n - n));
                break;
            }

            case NetOpType.Mine:
                FindRock(at)?.MineRemote(op.n);
                p.forcedRocks.Add(at);
                break;

            case NetOpType.ShopBuy:
            {
                var shop = ShopManager.Instance;
                long price = shop != null && item != null ? (long)shop.BuyPrice(item) * op.n : 0;
                if (price <= 0 || CurrencyManager.Instance == null || !CurrencyManager.Instance.TrySpend(price))
                    Give(p, item, -op.n);
                break;
            }
            case NetOpType.ShopSell:
                if (ShopManager.Instance != null && item != null)
                {
                    CurrencyManager.Instance?.Add((long)ShopManager.Instance.SellPrice(item) * op.n);
                    QuestManager.Instance?.AddRemoteProgress(QuestObjectiveType.SellItem, item, op.n);
                }
                break;

            case NetOpType.Research:
            {
                var tech = TechTreeManager.Instance;
                var node = tech != null ? tech.GetNode(op.item) : null;
                if (node != null) tech.ApplyRemoteUpgrade(node);
                break;
            }
            case NetOpType.Quest:
                QuestManager.Instance?.AddRemoteProgress((QuestObjectiveType)op.x, item, op.n);
                break;

            case NetOpType.ShuttleLoad:
            {
                var svc = ShuttleService.Instance;
                var refund = new System.Collections.Generic.List<(FarmingItemData item, int amount)>();
                if (svc != null)
                {
                    svc.LoadFromPeer(op.n, op.item, refund, out var departed);
                    var bonus = departed != null ? NetWorld.Item(departed.bonusItem) : null;
                    if (bonus != null && departed.bonusAmount > 0) Give(p, bonus, departed.bonusAmount);
                }
                else refund.AddRange(NetOps.ParseItems(op.item));
                foreach (var (item2, amount) in refund) Give(p, item2, amount);
                break;
            }
            case NetOpType.ShuttleReroll:
            {
                var refund = new System.Collections.Generic.List<(FarmingItemData item, int amount)>();
                ShuttleService.Instance?.RerollFromPeer(op.n, refund);
                foreach (var (item2, amount) in refund) Give(p, item2, amount);
                break;
            }

            case NetOpType.ResidentTalk:
            case NetOpType.ResidentGift:
            {
                bool gift = op.type == NetOpType.ResidentGift;
                var r = ShuttleService.Instance != null ? ShuttleService.Instance.FindResident(op.x) : null;
                if (r == null)
                {
                    if (gift) Give(p, item, 1);
                    break;
                }
                var outcome = ResidentBond.ApplyRemote(r, gift, item, out var rewards);
                if (gift && !outcome.accepted) Give(p, item, 1);
                foreach (var reward in rewards)
                {
                    if (reward.item != null) Give(p, reward.item, reward.amount);
                    if (reward.coins > 0) CurrencyManager.Instance?.Add(reward.coins);
                }
                break;
            }
        }

        if (op.type < NetOpType.Mine) p.forcedTiles.Add(at);
    }

    private static T TileAt<T>(Vector2Int anchor) where T : TileItem
    {
        var t = GridManager.Instance != null ? GridManager.Instance.GetTile(anchor) : null;
        return t is T typed && t.GridPosition == anchor ? typed : null;
    }

    private ResourceBlock FindRock(Vector2Int key)
    {
        if (rockByKey.TryGetValue(key, out var b) && b != null) return b;
        foreach (var block in ResourceBlock.Active)
            if (block != null && NetWorld.RockKey(block) == key) return rockByKey[key] = block;
        return null;
    }

    private static void Give(Peer p, FarmingItemData item, int amount)
    {
        if (item == null || amount == 0) return;
        NetWire.SendJson(NetWire.Give, p.id, new GiveMsg { item = item.name, amount = amount });
    }

    public static void GiveAll(FarmingItemData item, int amount)
    {
        if (Current == null || item == null || amount <= 0) return;
        foreach (var p in Current.peers.Values)
            if (p.ready) Give(p, item, amount);
    }

    public static void NoticeAll(string text, ulong except = ulong.MaxValue)
    {
        if (Current == null || string.IsNullOrEmpty(text)) return;
        foreach (var p in Current.peers.Values)
            if (p.welcomed && p.id != except) NetWire.SendJson(NetWire.Notice, p.id, new NoticeMsg { text = text });
    }

    public static void StormAll(WorldTheme theme, float warning)
    {
        if (Current == null) return;
        foreach (var p in Current.peers.Values)
            if (p.ready) NetWire.SendJson(NetWire.Storm, p.id, new StormMsg { theme = (int)theme, warning = warning });
    }

    private void BroadcastRoster()
    {
        var msg = new RosterMsg();
        var me = PlayerCharacter.Current;
        msg.players.Add(new RosterEntry { clientId = NetworkManager.ServerClientId, name = me.name, color = me.color, accessory = me.accessory });
        foreach (var p in peers.Values)
            if (p.welcomed) msg.players.Add(new RosterEntry { clientId = p.id, name = p.name, color = p.color, accessory = p.accessory });
        foreach (var p in peers.Values)
            if (p.welcomed) NetWire.SendJson(NetWire.Roster, p.id, msg);
    }

    public void Tick(float dt)
    {
        if (GridManager.Instance == null) return;

        posTimer += dt;
        if (posTimer >= PosInterval)
        {
            posTimer = 0f;
            BroadcastPositions();
        }

        tickTimer     += dt;
        volatileTimer += dt;
        if (tickTimer < TickInterval) return;
        tickTimer = 0f;
        bool includeVolatile = volatileTimer >= VolatileInterval;
        if (includeVolatile) volatileTimer = 0f;

        if (AnyReady) SendChanges(includeVolatile);
    }

    private void BroadcastPositions()
    {
        var ptc = PlayerTileController.Instance;
        var list = new List<(ulong id, Vector2 pos)>();
        if (ptc != null) list.Add((NetworkManager.ServerClientId, ptc.transform.position));
        foreach (var p in peers.Values)
            if (p.ready && p.hasPos) list.Add((p.id, p.pos));

        foreach (var target in peers.Values)
        {
            if (!target.ready) continue;
            using var w = new FastBufferWriter(4 + list.Count * 16, Unity.Collections.Allocator.Temp);
            w.WriteValueSafe(list.Count);
            foreach (var (id, pos) in list)
            {
                w.WriteValueSafe(id);
                w.WriteValueSafe(pos.x);
                w.WriteValueSafe(pos.y);
            }
            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(NetWire.Pos, target.id, w, NetworkDelivery.UnreliableSequenced);
        }
    }

    private void ResetBaseline()
    {
        tileKeys.Clear();
        tileFull.Clear();
        rockRemaining.Clear();
        globalsKey = globalsFull = null;
        CollectChanges(includeVolatile: true);
    }

    private DeltaMsg CollectChanges(bool includeVolatile)
    {
        var delta = new DeltaMsg();
        var gm = GridManager.Instance;

        // GC 줄이려고 재사용
        var seen = seenTiles;
        seen.Clear();
        foreach (var tile in gm.GetDynamicTiles())
        {
            var ts = SaveManager.CaptureTile(tile);
            if (ts == null) continue;
            var anchor = tile.GridPosition;
            seen.Add(anchor);

            string key = NetWorld.TileKey(ts);
            bool changed = !tileKeys.TryGetValue(anchor, out var oldKey) || oldKey != key;
            string full = changed || includeVolatile ? JsonUtility.ToJson(ts) : null;
            if (!changed && includeVolatile && tileFull.TryGetValue(anchor, out var oldFull) && oldFull != full) changed = true;
            if (!changed) continue;

            tileKeys[anchor] = key;
            tileFull[anchor] = full;
            delta.tiles.Add(ts);
        }
        if (seen.Count != tileKeys.Count)
            foreach (var anchor in new List<Vector2Int>(tileKeys.Keys))
            {
                if (seen.Contains(anchor)) continue;
                tileKeys.Remove(anchor);
                tileFull.Remove(anchor);
                delta.removed.Add(new LinkSave { x = anchor.x, y = anchor.y });
            }

        seenRocks.Clear();
        foreach (var block in ResourceBlock.Active)
        {
            if (block == null || block.Data == null) continue;
            var key = NetWorld.RockKey(block);
            seenRocks.Add(key);
            rockByKey[key] = block;
            if (rockRemaining.TryGetValue(key, out int old) && old == block.RemainingResources) continue;
            rockRemaining[key] = block.RemainingResources;
            delta.rocks.Add(RockSave(block));
        }
        if (seenRocks.Count != rockRemaining.Count)
            foreach (var key in new List<Vector2Int>(rockRemaining.Keys))
            {
                if (seenRocks.Contains(key)) continue;
                rockRemaining.Remove(key);
                rockByKey.Remove(key);
                delta.rocks.Add(new ResourceBlockSave { posX = key.x / 64f, posY = key.y / 64f, remaining = 0 });
            }

        var g = NetWorld.CaptureGlobals();
        string gKey  = NetWorld.GlobalsKey(g);
        string gFull = includeVolatile || gKey != globalsKey ? JsonUtility.ToJson(g) : null;
        if (gKey != globalsKey || (includeVolatile && gFull != globalsFull))
        {
            globalsKey  = gKey;
            globalsFull = gFull;
            delta.hasGlobals = true;
            delta.globals    = g;
        }
        return delta;
    }

    private static ResourceBlockSave RockSave(ResourceBlock b) => new()
    {
        posX = b.OriginPosition.x, posY = b.OriginPosition.y,
        resourceDataName = b.Data.name, remaining = b.RemainingResources,
    };

    private void SendChanges(bool includeVolatile)
    {
        var shared = CollectChanges(includeVolatile);
        var gm = GridManager.Instance;

        foreach (var p in peers.Values)
        {
            if (!p.ready) continue;
            bool forced = p.forcedTiles.Count > 0 || p.forcedRocks.Count > 0 || p.forceGlobals;
            if (!forced && p.ack == p.sentAck && shared.tiles.Count == 0 && shared.removed.Count == 0
                && shared.rocks.Count == 0 && !shared.hasGlobals) continue;

            var msg = new DeltaMsg
            {
                ack = p.ack, tiles = shared.tiles, removed = shared.removed, rocks = shared.rocks,
                hasGlobals = shared.hasGlobals, globals = shared.globals,
            };
            if (forced)
            {
                msg.tiles   = new List<TileSave>(shared.tiles);
                msg.removed = new List<LinkSave>(shared.removed);
                msg.rocks   = new List<ResourceBlockSave>(shared.rocks);
                foreach (var anchor in p.forcedTiles)
                {
                    var t = gm.GetTile(anchor);
                    if (t != null && t.GridPosition == anchor && !t.IsPersistent) msg.tiles.Add(SaveManager.CaptureTile(t));
                    else msg.removed.Add(new LinkSave { x = anchor.x, y = anchor.y });
                }
                foreach (var key in p.forcedRocks)
                {
                    var b = FindRock(key);
                    msg.rocks.Add(b != null && b.Data != null ? RockSave(b)
                                                              : new ResourceBlockSave { posX = key.x / 64f, posY = key.y / 64f });
                }
                if (p.forceGlobals && !msg.hasGlobals)
                {
                    msg.hasGlobals = true;
                    msg.globals    = NetWorld.CaptureGlobals();
                }
                p.forcedTiles.Clear();
                p.forcedRocks.Clear();
                p.forceGlobals = false;
            }

            p.sentAck = p.ack;
            NetWire.SendJson(NetWire.Delta, p.id, msg, compress: true);
        }
    }
}

public static class NetGuests
{
    private static readonly Dictionary<string, GuestSave> byId = new();

    public static GuestSave Find(string id) => id != null && byId.TryGetValue(id, out var g) ? g : null;

    public static void Store(GuestSave g)
    {
        if (!string.IsNullOrEmpty(g?.id)) byId[g.id] = g;
    }

    public static void Clear() => byId.Clear();

    // 이전 월드 참가자 기록 섞임 방지
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => byId.Clear();

    public static void Load(List<GuestSave> list)
    {
        byId.Clear();
        if (list == null) return;
        foreach (var g in list) Store(g);
    }

    public static List<GuestSave> ToList() => new(byId.Values);
}

public struct NetPeerInfo
{
    public ulong   id;
    public string  name;
    public int     color;
    public Vector2 pos;
    public bool    hasPos;
}
