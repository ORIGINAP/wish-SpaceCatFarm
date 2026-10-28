using System.Collections.Generic;
using UnityEngine;

public sealed class PipeNetwork
{
    public readonly List<ItemStorage> Storages = new();
}

// 버전 바뀔 때만 파이프망 재구성
public static class PipeNetworks
{
    private static int builtVersion = -1;
    private static int builtLinkVersion = -1;
    private static GridManager builtFor;
    private static readonly Dictionary<AutoFuelPipe, PipeNetwork> byPipe = new();
    private static readonly List<TileItem> allPipes = new();
    private static readonly Queue<AutoFuelPipe> queue = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        builtVersion = builtLinkVersion = -1;
        builtFor = null;
        byPipe.Clear();
        allPipes.Clear();
        queue.Clear();
    }

    public static PipeNetwork Of(AutoFuelPipe pipe)
    {
        EnsureBuilt();
        return pipe != null && byPipe.TryGetValue(pipe, out var net) ? net : null;
    }

    private static void EnsureBuilt()
    {
        var gm = GridManager.Instance;
        if (gm == null) { byPipe.Clear(); builtVersion = -1; builtFor = null; return; }
        if (builtVersion == gm.Version && builtLinkVersion == TileItem.LinkVersion && builtFor == gm) return;
        builtVersion     = gm.Version;
        builtLinkVersion = TileItem.LinkVersion;
        builtFor         = gm;
        byPipe.Clear();

        gm.CollectTiles(TileType.AutoFuelPipe, allPipes);
        foreach (var tile in allPipes)
        {
            if (tile is not AutoFuelPipe start || byPipe.ContainsKey(start)) continue;

            var net = new PipeNetwork();
            byPipe[start] = net;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var pipe = queue.Dequeue();
                foreach (var t in pipe.Links)
                {
                    if (t is AutoFuelPipe other)
                    {
                        if (byPipe.ContainsKey(other)) continue;
                        byPipe[other] = net;
                        queue.Enqueue(other);
                    }
                    else if (t is ItemStorage storage && !net.Storages.Contains(storage))
                        net.Storages.Add(storage);
                }
            }
        }
    }
}
