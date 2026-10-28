using System;
using System.Collections.Generic;
using UnityEngine;

public static class LabLoot
{
    [Serializable]
    public class Bundle
    {
        public int          id;
        public float        x, y;
        public long         coins;
        public List<string> items  = new();
        public List<int>    counts = new();
        public List<ulong>  who    = new();
    }

    private struct Entry
    {
        public string item; public int min, max; public float chance;
        public Entry(string item, int min, int max, float chance) { this.item = item; this.min = min; this.max = max; this.chance = chance; }
    }

    private static readonly Entry[] Supply =
    {
        new("Item_Copper", 6, 12, 1f),       new("Item_iron", 6, 12, 0.8f),
        new("Item_CopperIngot", 2, 5, 0.45f), new("Item_IronIngot", 2, 5, 0.45f),
        new("Item_Seed_Catnip", 2, 4, 0.35f), new("Item_Seed_RedStar", 2, 4, 0.35f), new("Item_Seed_MoonRadish", 2, 4, 0.3f),
        new("Item_Lithium", 3, 6, 0.2f),      new("Item_Aluminum", 3, 6, 0.2f),
        new("Item_StarAlloy", 1, 1, 0.08f),   new("Item_Diamond", 1, 1, 0.05f),
    };

    private static readonly Entry[] Vault =
    {
        new("Item_CopperIngot", 4, 8, 0.8f),  new("Item_IronIngot", 4, 8, 0.8f),
        new("Item_LithiumIngot", 2, 4, 0.4f), new("Item_AluminumIngot", 2, 4, 0.4f),
        new("Item_Seed_ChuruCorn", 2, 4, 0.35f), new("Item_Seed_YarnFlower", 2, 4, 0.35f),
        new("Item_StarAlloy", 1, 2, 0.25f),   new("Item_Diamond", 1, 2, 0.15f), new("Item_MagnetBell", 1, 1, 0.1f),
    };

    public static Bundle RollSupply(int maxEntries = 4) => Roll(Supply, maxEntries, 150, 400);
    public static Bundle RollVault(int maxEntries = 5)  => Roll(Vault, maxEntries, 300, 700);

    private static Bundle Roll(Entry[] table, int maxEntries, int coinMin, int coinMax)
    {
        var b = new Bundle { coins = UnityEngine.Random.Range(coinMin, coinMax + 1) / 10 * 10 };
        var order = new List<int>();
        for (int i = 0; i < table.Length; i++) order.Insert(UnityEngine.Random.Range(0, order.Count + 1), i);
        foreach (int i in order)
        {
            if (b.items.Count >= maxEntries) break;
            var e = table[i];
            if (UnityEngine.Random.value > e.chance || NetWorld.Item(e.item) == null) continue;
            b.items.Add(e.item);
            b.counts.Add(UnityEngine.Random.Range(e.min, e.max + 1));
        }
        if (b.items.Count == 0) { b.items.Add("Item_Copper"); b.counts.Add(8); }
        return b;
    }

    public static void Settle(Bundle b, Vector2 pos, float radius, List<Lab.Member> members)
    {
        b.x = pos.x;
        b.y = pos.y;
        Lab.CollectMembers(members);
        foreach (var m in members)
            if (Vector2.Distance(m.pos, pos) <= radius) b.who.Add(m.id);
        if (b.coins > 0) CurrencyManager.Instance?.Add(b.coins);
    }

    // 받는 사람 PC에서 직접 드롭
    public static void Deliver(Bundle b, Color color)
    {
        var at = new Vector3(b.x, b.y, 0f);
        LabFx.Burst(at + new Vector3(0f, 0.5f, 0f), color, 16, 2.8f, 0.7f);
        LabFx.Ring(at, color, 1.6f, 0.7f);
        if (b.coins > 0) LabFx.FloatText(at + new Vector3(0f, 1.1f, 0f), $"+{CurrencyManager.Format(b.coins)} 코인", CuteUIKit.AmberText, 3f);

        if (!b.who.Contains(Lab.LocalId)) return;
        var me = PlayerTileController.Instance != null ? PlayerTileController.Instance.transform : null;
        var names = new System.Text.StringBuilder();
        for (int i = 0; i < b.items.Count && i < b.counts.Count; i++)
        {
            var item = NetWorld.Item(b.items[i]);
            if (item == null || b.counts[i] <= 0) continue;
            DroppedItem.Spawn(item, b.counts[i], at + (Vector3)(UnityEngine.Random.insideUnitCircle * 0.3f), me);
            if (names.Length > 0) names.Append(", ");
            names.Append($"{item.itemName} {b.counts[i]}");
        }
        Sfx.Play(SfxId.QuestComplete, 0.8f);
        Lab.Notify($"받았어요! {names}" + (b.coins > 0 ? $"  ·  코인 +{CurrencyManager.Format(b.coins)} (모두의 지갑)" : ""));
    }
}
