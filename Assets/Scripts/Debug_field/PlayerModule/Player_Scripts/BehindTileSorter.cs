using System.Collections.Generic;
using UnityEngine;

// 큰 설치물 뒤로 지나갈 때만 정렬 올림
[RequireComponent(typeof(Player))]
public class BehindTileSorter : MonoBehaviour
{
    private const int   ScanColumns = 4;
    private const int   ScanRows    = 5;
    private const float BlockRange  = 3f;

    private Player         player;
    private SpriteRenderer playerSr;

    private readonly Dictionary<Component, SpriteRenderer[]> bodies = new();
    private readonly Dictionary<Component, int[]> raised = new();
    private readonly HashSet<Component>   behind   = new();
    private readonly List<Component>      stale    = new();
    private readonly List<SpriteRenderer> srBuffer = new();
    private readonly List<ResourceBlock>  nearBlocks = new();
    private int gridVersion = -1;

    private void Awake()
    {
        player   = GetComponent<Player>();
        playerSr = GetComponent<SpriteRenderer>();
    }

    private void OnDisable()
    {
        stale.Clear();
        stale.AddRange(raised.Keys);
        foreach (var c in stale) Restore(c);
    }

    private void LateUpdate()
    {
        var gm = GridManager.Instance;
        if (gm != null && gm.Version != gridVersion)
        {
            gridVersion = gm.Version;
            stale.Clear();
            foreach (var c in bodies.Keys) if (c == null) stale.Add(c);
            foreach (var c in stale) bodies.Remove(c);
        }

        FindBehind(gm);

        foreach (var c in behind) Raise(c);

        stale.Clear();
        foreach (var c in raised.Keys)
            if (c == null || !behind.Contains(c)) stale.Add(c);
        foreach (var c in stale) Restore(c);
    }

    private void FindBehind(GridManager gm)
    {
        behind.Clear();
        if (gm == null || playerSr == null || !playerSr.enabled) return;

        Rect   feet = player.FeetRect;
        Bounds pb   = playerSr.bounds;

        Vector2Int fc = gm.WorldToGrid(new Vector3(feet.center.x, feet.yMin));
        for (int x = fc.x - ScanColumns; x <= fc.x + ScanColumns; x++)
        for (int y = fc.y - ScanRows;    y <= fc.y;               y++)
        {
            var tile = gm.GetTile(new Vector2Int(x, y));
            if (!TileCollision.Blocks(tile) || behind.Contains(tile)) continue;
            if (IsBehind(tile, TileCollision.SolidRect(tile).yMax, feet.yMin, pb)) behind.Add(tile);
        }

        // 발 근처 광석만 조회
        ResourceBlock.Near(new Rect(feet.center.x - BlockRange, feet.yMin - BlockRange, BlockRange * 2f, BlockRange * 2f), nearBlocks);
        foreach (var block in nearBlocks)
        {
            if (block == null) continue;
            Rect solid = block.SolidRect;
            if (Mathf.Abs(solid.center.x - feet.center.x) > BlockRange || Mathf.Abs(solid.center.y - feet.yMin) > BlockRange) continue;
            if (IsBehind(block, solid.yMax, feet.yMin, pb)) behind.Add(block);
        }
    }

    private bool IsBehind(Component owner, float baseTop, float feetY, Bounds playerBounds)
    {
        if (feetY < baseTop - 0.01f) return false;

        foreach (var sr in Bodies(owner))
        {
            if (sr == null || !sr.enabled || !sr.gameObject.activeInHierarchy || sr.sprite == null) continue;
            Bounds b = sr.bounds;
            if (b.max.y > feetY && b.min.x < playerBounds.max.x && b.max.x > playerBounds.min.x) return true;
        }
        return false;
    }

    private SpriteRenderer[] Bodies(Component owner)
    {
        if (bodies.TryGetValue(owner, out var found)) return found;

        srBuffer.Clear();
        owner.GetComponentsInChildren(true, srBuffer);
        int layer = playerSr.sortingLayerID, order = playerSr.sortingOrder;
        srBuffer.RemoveAll(sr => sr.sortingLayerID != layer || sr.sortingOrder > order);
        found = srBuffer.ToArray();
        bodies[owner] = found;
        return found;
    }

    private void Raise(Component owner)
    {
        var srs = Bodies(owner);
        if (srs.Length == 0) return;
        if (!raised.TryGetValue(owner, out var original))
        {
            original = new int[srs.Length];
            for (int i = 0; i < srs.Length; i++) original[i] = srs[i] != null ? srs[i].sortingOrder : 0;
            raised[owner] = original;
        }

        int lowest = int.MaxValue;
        foreach (int o in original) lowest = Mathf.Min(lowest, o);
        int top = playerSr.sortingOrder + 1;
        for (int i = 0; i < srs.Length; i++)
            if (srs[i] != null) srs[i].sortingOrder = top + (original[i] - lowest);
    }

    private void Restore(Component owner)
    {
        if (raised.TryGetValue(owner, out var original) && owner != null && bodies.TryGetValue(owner, out var srs))
            for (int i = 0; i < srs.Length && i < original.Length; i++)
                if (srs[i] != null) srs[i].sortingOrder = original[i];
        raised.Remove(owner);
    }
}
