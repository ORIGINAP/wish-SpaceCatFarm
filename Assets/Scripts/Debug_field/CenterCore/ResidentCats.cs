using System.Collections.Generic;
using UnityEngine;

public class ResidentCats : MonoBehaviour
{
    public const int MaxVisible = 16;
    private const float CheckInterval = 0.5f;

    private static ResidentCats instance;

    public static int Population => ShuttleService.Population;

    private readonly List<ResidentCat>  cats  = new();
    private readonly List<ResidentSave> shown = new();
    private readonly List<TileItem>     homes = new();
    private readonly List<TileItem>     buffer = new();
    private readonly List<ResidentSave> visible = new();
    private float nextCheck;
    private int   seenPopulation = -1;

    private void Awake() => instance = this;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + CheckInterval;
        var svc = ShuttleService.Instance;
        if (svc == null || SaveManager.IsLoading) return;

        CollectHomes();
        if (homes.Count == 0) return;

        var list = svc.Residents;

        int arrived = seenPopulation < 0 || SaveManager.RecentlyLoaded ? 0 : Mathf.Max(0, ShuttleService.Population - seenPopulation);
        if (seenPopulation == 0 && arrived > 0) NotificationUI.Instance?.Show(ResidentBond.FirstArrivalTip);
        seenPopulation = ShuttleService.Population;

        // 친해진 주민 먼저
        visible.Clear();
        for (int i = list.Count - 1; i >= 0 && visible.Count < MaxVisible; i--)
            if (list[i] != null && list[i].friendship > 0) visible.Add(list[i]);
        for (int i = list.Count - 1; i >= 0 && visible.Count < MaxVisible; i--)
            if (list[i] != null && list[i].friendship <= 0) visible.Add(list[i]);

        for (int i = cats.Count - 1; i >= 0; i--)
        {
            if (cats[i] == null) { cats.RemoveAt(i); shown.RemoveAt(i); continue; }
            var now = Find(visible, shown[i]);
            if (now != null) { shown[i] = now; cats[i].Resident = now; continue; }
            cats[i].GoHome();
            cats.RemoveAt(i);
            shown.RemoveAt(i);
        }
        int hopIndex = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            if (r == null || !visible.Contains(r) || Contains(r)) continue;
            bool hop = i >= list.Count - arrived && PadPosition(r, out _);
            var home = homes[(i + homes.Count) % homes.Count];
            Vector2 at = hop && PadPosition(r, out var door) ? door : Near(home);
            var cat = ResidentCat.Spawn(at, home, r.name, r.color, r.accessory, hop, hop ? hopIndex * 0.35f : 0f, hopIndex, hopIndex == 0);
            if (hop) hopIndex++;
            if (cat == null) continue;
            cat.Resident = r;
            cats.Add(cat);
            shown.Add(r);
        }

        for (int i = 0; i < cats.Count; i++)
        {
            var cat = cats[i];
            if (cat == null) continue;
            if (cat.Home == null || !homes.Contains(cat.Home)) cat.SetHome(homes[i % homes.Count]);
        }
    }

    private void CollectHomes()
    {
        homes.Clear();
        var gm = GridManager.Instance;
        if (gm == null) return;
        gm.CollectTiles(TileType.Civilization, buffer);
        foreach (var t in buffer)
            if (t is CivFacility f && !f.IsGhost && f.IsPowered && !homes.Contains(f))
            {
                homes.Add(f);
                if (f.Effect == CivEffect.Housing) homes.Add(f);
            }
        gm.CollectTiles(TileType.ShuttlePad, buffer);
        foreach (var t in buffer)
            if (t != null && !t.IsGhost && !homes.Contains(t)) homes.Add(t);
        if (CenterCore.Current != null) homes.Add(CenterCore.Current);
    }

    private bool Contains(ResidentSave r)
    {
        foreach (var s in shown) if (Same(s, r)) return true;
        return false;
    }

    private static ResidentSave Find(List<ResidentSave> list, ResidentSave r)
    {
        foreach (var x in list) if (Same(x, r)) return x;
        return null;
    }

    // 참가자는 id로 비교
    private static bool Same(ResidentSave a, ResidentSave b)
        => a == b || (a != null && b != null && (a.id > 0 && b.id > 0
               ? a.id == b.id
               : a.name == b.name && a.color == b.color && a.accessory == b.accessory && a.padX == b.padX && a.padY == b.padY));

    private static bool PadPosition(ResidentSave r, out Vector2 door)
    {
        door = default;
        var gm = GridManager.Instance;
        if (gm == null || r.padX == ShuttleService.NoPad) return false;
        var pos = new Vector2Int(r.padX, r.padY);
        if (gm.GetTile(pos) is not ShuttlePad pad || pad.GridPosition != pos) return false;
        door = (Vector2)pad.transform.position + Vector2.up * 0.1f;
        return true;
    }

    private static Vector2 Near(TileItem home)
    {
        Vector2 p = (Vector2)home.transform.position + Random.insideUnitCircle * 2.2f + Vector2.down * 0.8f;
        return GridManager.Instance != null ? GridManager.Instance.ClampToWorldBounds(p) : p;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        foreach (var cat in cats)
            if (cat != null) Destroy(cat.gameObject);
        cats.Clear();
        shown.Clear();
    }
}
