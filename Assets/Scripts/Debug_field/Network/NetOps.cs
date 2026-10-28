using System;
using UnityEngine;

public enum NetOpType
{
    Place, Remove,
    StorageAdd, StorageRemove,
    Fuel,
    SmelterInsert, SmelterTakeInput, SmelterTakeOutput,
    DrillEquip, DrillUnequip,
    Link, Unlink, UnlinkAll,
    Plant, Harvest, Fertilize,
    KitchenSetRecipe, KitchenInsert, KitchenTakeInput, KitchenTakeOutput,
    CompostInsert, CompostTakeOutput,
    Mine,
    ShopBuy, ShopSell, Research, Quest,
    ShuttleLoad, ShuttleReroll,
    ResidentTalk, ResidentGift,
}

[Serializable]
public class NetOp
{
    public int       seq;
    public NetOpType type;
    public int       x, y;
    public int       x2, y2;
    public string    item;
    public int       n;
}

// 참가자 조작 먼저 적용 후 호스트로
public static class NetOps
{
    private static int applying;
    private static int nested;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => applying = nested = 0;

    // 호스트 상태 적용 중엔 재전송 안 함
    public static bool Applying => applying > 0;

    private static bool Sending => NetSession.IsReplica && applying == 0 && nested == 0 && NetClient.CanSend;

    public readonly struct Scope : IDisposable
    {
        private readonly int kind;

        public Scope(int kind) => this.kind = kind;

        public void Dispose()
        {
            if (kind == 1) applying--;
            else if (kind == 2) nested--;
        }
    }

    public static Scope ApplyingHost()
    {
        applying++;
        return new Scope(1);
    }

    public static void Tile(NetOpType type, TileItem tile, FarmingItemData item = null, int n = 0)
    {
        if (!Sending || tile == null) return;
        if (type == NetOpType.Place && item == null) return;
        Send(new NetOp { type = type, x = tile.GridPosition.x, y = tile.GridPosition.y, item = item != null ? item.name : null, n = n },
             tile: true);
    }

    public static void TileNamed(NetOpType type, TileItem tile, string name, int n = 0)
    {
        if (!Sending || tile == null) return;
        Send(new NetOp { type = type, x = tile.GridPosition.x, y = tile.GridPosition.y, item = name, n = n }, tile: true);
    }

    public static Scope Removing(TileItem tile)
    {
        if (!Sending || tile == null) return default;
        Tile(NetOpType.Remove, tile);
        nested++;
        return new Scope(2);
    }

    public static void Link(NetOpType type, TileItem a, TileItem b)
    {
        if (!Sending || a == null || b == null) return;
        Send(new NetOp { type = type, x = a.GridPosition.x, y = a.GridPosition.y, x2 = b.GridPosition.x, y2 = b.GridPosition.y },
             tile: true);
    }

    public static void Plant(PlantTile plant, SeedData seed)
    {
        if (!Sending || plant == null || seed == null) return;
        Send(new NetOp { type = NetOpType.Plant, x = plant.GridPosition.x, y = plant.GridPosition.y, item = seed.name }, tile: true);
    }

    public static void Mine(ResourceBlock block, int amount)
    {
        if (!Sending || block == null) return;
        var key = NetWorld.RockKey(block);
        Send(new NetOp { type = NetOpType.Mine, x = key.x, y = key.y, n = amount }, rock: true);
    }

    public static void Global(NetOpType type, string item, int n, int x = 0)
    {
        if (!Sending) return;
        Send(new NetOp { type = type, item = item, n = n, x = x });
    }

    public static void Resident(NetOpType type, int residentId, FarmingItemData gift = null)
        => Global(type, gift != null ? gift.name : null, 1, residentId);

    public static void Quest(QuestObjectiveType type, FarmingItemData item, int amount)
    {
        if (item != null) Global(NetOpType.Quest, item.name, amount, (int)type);
    }

    // 호스트, 싱글에선 null
    public static string ItemsKey(System.Collections.Generic.List<HarvestResult> items)
    {
        if (!Sending || items == null || items.Count == 0) return null;
        var sb = new System.Text.StringBuilder();
        foreach (var r in items)
        {
            if (r?.item == null || r.amount <= 0) continue;
            if (sb.Length > 0) sb.Append(';');
            sb.Append(r.item.name).Append(':').Append(r.amount);
        }
        return sb.ToString();
    }

    public static System.Collections.Generic.List<(FarmingItemData item, int amount)> ParseItems(string key)
    {
        var list = new System.Collections.Generic.List<(FarmingItemData, int)>();
        if (string.IsNullOrEmpty(key)) return list;
        foreach (var part in key.Split(';'))
        {
            int c = part.LastIndexOf(':');
            if (c <= 0) continue;
            var item = NetWorld.Item(part.Substring(0, c));
            if (item != null && int.TryParse(part.Substring(c + 1), out int n) && n > 0) list.Add((item, n));
        }
        return list;
    }

    private static void Send(NetOp op, bool tile = false, bool rock = false) => NetClient.Current?.SendOp(op, tile, rock);
}
