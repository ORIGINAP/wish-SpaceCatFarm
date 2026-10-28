using System.Collections.Generic;
using UnityEngine;

public static class Codex
{
    public static readonly (int count, long coins)[] Milestones =
    {
        (5, 300), (10, 1000), (15, 3000), (20, 8000), (25, 20000), (30, 40000), (35, 80000), (43, 200000),
    };

    private static readonly HashSet<string> golden = new();
    private static readonly HashSet<string> dishes = new();
    private static int milestonesPaid;

    public static event System.Action OnChanged;

    public static bool HasGolden(FarmingItemData item) => item != null && golden.Contains(item.name);
    public static bool HasDish(FarmingItemData item)   => item != null && dishes.Contains(item.name);
    public static int  GoldenCount  => golden.Count;
    public static int  DishCount    => dishes.Count;
    public static int  MilestonesPaid => milestonesPaid;

    public static int Total => HarvestLog.Count + golden.Count + dishes.Count;

    public static void RecordGolden(FarmingItemData item)
    {
        // 멀티: 호스트만 기록
        if (item == null || !item.isGolden || NetSession.IsReplica) return;
        if (!golden.Add(item.name)) return;
        NotificationUI.Instance?.Show($"도감에 새 황금 작물: {item.itemName}", NotificationUI.GoodNewsColor);
        AfterRecord();
    }

    public static void RecordDish(FarmingItemData item)
    {
        if (item == null || NetSession.IsReplica) return;
        if (!dishes.Add(item.name)) return;
        NotificationUI.Instance?.Show($"도감에 새 요리: {item.itemName}", NotificationUI.GoodNewsColor);
        AfterRecord();
    }

    private static void AfterRecord()
    {
        PayMilestones();
        OnChanged?.Invoke();
    }

    private static void PayMilestones()
    {
        if (NetSession.IsReplica) return;
        int total = Total;
        while (milestonesPaid < Milestones.Length && total >= Milestones[milestonesPaid].count)
        {
            var (count, coins) = Milestones[milestonesPaid];
            milestonesPaid++;
            CurrencyManager.Instance?.Add(coins);
            string text = $"도감 {count}종 달성! 보상 {CurrencyManager.Format(coins)}코인";
            NotificationUI.Instance?.Show(text, NotificationUI.GoodNewsColor);
            NetHost.NoticeAll(text);
            Sfx.Play(SfxId.QuestComplete);
        }
    }

    public static List<string> GoldenList() => new(golden);
    public static List<string> DishList()   => new(dishes);

    public static void Clear()
    {
        Hook();
        golden.Clear();
        dishes.Clear();
        milestonesPaid = 0;
        OnChanged?.Invoke();
    }

    public static void Restore(List<string> savedGolden, List<string> savedDishes, int paid)
    {
        golden.Clear();
        dishes.Clear();
        if (savedGolden != null) foreach (var n in savedGolden) if (!string.IsNullOrEmpty(n)) golden.Add(n);
        if (savedDishes != null) foreach (var n in savedDishes) if (!string.IsNullOrEmpty(n)) dishes.Add(n);
        milestonesPaid = Mathf.Clamp(paid, 0, Milestones.Length);
        OnChanged?.Invoke();
    }

    public static void SyncFromHost(List<string> hostGolden, List<string> hostDishes, int paid)
    {
        if (SameSet(golden, hostGolden) && SameSet(dishes, hostDishes) && paid == milestonesPaid) return;
        Restore(hostGolden, hostDishes, paid);
    }

    private static bool SameSet(HashSet<string> set, List<string> list)
    {
        if (list == null) return set.Count == 0;
        if (list.Count != set.Count) return false;
        foreach (var n in list) if (!set.Contains(n)) return false;
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init()
    {
        golden.Clear();
        dishes.Clear();
        milestonesPaid = 0;
        OnChanged = null;
    }

    private static void Hook()
    {
        HarvestLog.OnChanged -= OnHarvestLogChanged;
        HarvestLog.OnChanged += OnHarvestLogChanged;
    }

    private static void OnHarvestLogChanged()
    {
        if (!SaveManager.IsLoading) PayMilestones();
        OnChanged?.Invoke();
    }
}
