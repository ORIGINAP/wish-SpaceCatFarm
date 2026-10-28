using UnityEngine;
using System.Collections.Generic;

public class EnergyManager : MonoBehaviour
{
    public static EnergyManager Instance => Services.Get<EnergyManager>();

    [SerializeField] private float refreshInterval = 2f;
    private float refreshTimer;

    private readonly List<PlantTile>  plants     = new();
    private readonly List<Generator>  generators = new();
    private readonly List<PowerRelay> relays     = new();

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }
        PowerRangeOverlay.Ensure();
    }

    private void OnDestroy() => Services.Unregister(this);

    private void Update()
    {
        refreshTimer += Time.deltaTime;
        if (refreshTimer >= refreshInterval)
        {
            refreshTimer = 0f;
            RefreshEnergyNetwork();
        }
    }

    public void Register(TileItem tile)
    {
        switch (tile)
        {
            case PlantTile p:  plants.Add(p);     break;
            case Generator g:  generators.Add(g); break;
            case PowerRelay r: relays.Add(r);     break;
            // 전력 무관 타일은 재계산 생략
            default:           return;
        }
        RefreshEnergyNetwork();
    }

    public void Unregister(TileItem tile)
    {
        switch (tile)
        {
            case PlantTile p:  plants.Remove(p);      break;
            case Generator g:  generators.Remove(g);  break;
            case PowerRelay r: relays.Remove(r);      break;
            default:           return;
        }
        RefreshEnergyNetwork();
    }

    public void RefreshEnergyNetwork()
    {
        RebuildRelayNetwork();

        foreach (var plant in plants)
            plant.OnEnergyChanged(HasEnergy(plant.GridPosition));

        OnNetworkRefreshed?.Invoke();
    }

    public event System.Action OnNetworkRefreshed;

    public bool HasEnergy(Vector2Int plantPos) => HasEnergy(plantPos, plantPos);

    public bool HasEnergy(Vector2Int min, Vector2Int max)
        => FindPowerSource(min, max, out bool powered) != null && powered;

    public TileItem FindPowerSource(Vector2Int min, Vector2Int max, out bool powered)
    {
        TileItem on = null, off = null;
        float onDist = float.MaxValue, offDist = float.MaxValue;

        foreach (var gen in generators)
        {
            if (gen == null || gen.DirectSupplyRadius <= 0) continue;
            if (!PowerRange.Contains(gen.FootprintMin, gen.FootprintMax, gen.DirectSupplyRadius, min, max, out float d)) continue;
            if (gen.IsActive) { if (d < onDist)  { onDist = d;  on = gen; } }
            else              { if (d < offDist) { offDist = d; off = gen; } }
        }
        foreach (var relay in relays)
        {
            if (relay == null) continue;
            if (!PowerRange.Contains(relay.FootprintMin, relay.FootprintMax, relay.SupplyRadius, min, max, out float d)) continue;
            if (relay.IsEnergized) { if (d < onDist)  { onDist = d;  on = relay; } }
            else                   { if (d < offDist) { offDist = d; off = relay; } }
        }

        powered = on != null;
        return on != null ? on : off;
    }

    public static void DrawPower(TileItem source, float amount)
    {
        if (amount <= 0f || NetSession.IsReplica) return;
        var gen = source as Generator ?? (source as PowerRelay)?.Root;
        if (gen != null) gen.BurnFuel(amount * 0.5f);
    }

    public IReadOnlyList<Generator> Generators => generators;

    public IReadOnlyList<PowerRelay> Relays => relays;

    public int PlantCount => plants.Count;
    public int RelayCount => relays.Count;

    private readonly Queue<PowerRelay> relayQueue = new();

    // 중계기 BFS
    private void RebuildRelayNetwork()
    {
        foreach (var r in relays)
            if (r != null) r.ClearNetwork();

        relayQueue.Clear();
        foreach (var gen in generators)
        {
            if (gen == null || !gen.IsActive || gen.DirectSupplyRadius <= 0) continue;
            foreach (var r in relays)
            {
                if (r == null || r.IsEnergized) continue;
                if (!PowerRange.Contains(gen.FootprintMin, gen.FootprintMax, gen.DirectSupplyRadius, r.FootprintMin, r.FootprintMax, out _)) continue;
                r.SetNetwork(true, gen, gen);
                relayQueue.Enqueue(r);
            }
        }

        while (relayQueue.Count > 0)
        {
            var from = relayQueue.Dequeue();
            foreach (var r in relays)
            {
                if (r == null || r.IsEnergized) continue;
                if (!PowerRange.Contains(from.FootprintMin, from.FootprintMax, from.SupplyRadius, r.FootprintMin, r.FootprintMax, out _)) continue;
                r.SetNetwork(true, from, from.Root);
                relayQueue.Enqueue(r);
            }
        }

        foreach (var r in relays)
        {
            if (r == null || r.IsEnergized) continue;
            TileItem best = null;
            float bestD = float.MaxValue;
            foreach (var gen in generators)
            {
                if (gen == null || gen.DirectSupplyRadius <= 0) continue;
                if (PowerRange.Contains(gen.FootprintMin, gen.FootprintMax, gen.DirectSupplyRadius, r.FootprintMin, r.FootprintMax, out float d)
                    && d < bestD) { bestD = d; best = gen; }
            }
            foreach (var other in relays)
            {
                if (other == null || other == r) continue;
                if (PowerRange.Contains(other.FootprintMin, other.FootprintMax, other.SupplyRadius, r.FootprintMin, r.FootprintMax, out float d)
                    && d < bestD) { bestD = d; best = other; }
            }
            r.SetNetwork(false, best, null);
        }
    }

    public float BenchmarkRefresh(int fakePlants, int fakeRelays)
    {
        var plantPos = new Vector2Int[fakePlants];
        var relayPos = new Vector2Int[fakeRelays];
        for (int i = 0; i < fakePlants; i++) plantPos[i] = new Vector2Int(Random.Range(-50, 50), Random.Range(-50, 50));
        for (int i = 0; i < fakeRelays; i++) relayPos[i] = new Vector2Int(Random.Range(-50, 50), Random.Range(-50, 50));

        var sw = new System.Diagnostics.Stopwatch();
        sw.Start();

        float reach = PowerRange.Reach(3, 3, PowerRelay.DefaultSupplyRadius);
        float reachSqr = reach * reach;
        for (int p = 0; p < fakePlants; p++)
        {
            bool powered = false;
            for (int r = 0; r < fakeRelays; r++)
            {
                float dx = relayPos[r].x - plantPos[p].x;
                float dy = relayPos[r].y - plantPos[p].y;
                if (dx * dx + dy * dy <= reachSqr) { powered = true; break; }
            }
            _ = powered;
        }

        sw.Stop();
        return (float)sw.Elapsed.TotalMilliseconds;
    }
}
