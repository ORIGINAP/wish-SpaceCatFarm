using System.Collections.Generic;
using UnityEngine;

// 물리 대신 칸 정보로 충돌 판정
public static class TileCollision
{
    private static readonly List<ResourceBlock> nearBlocks = new();

    public static bool Blocks(TileItem tile) => tile != null && !tile.IsGhost && tile.TileType != TileType.Plant;

    public static Rect SolidRect(TileItem tile)
    {
        var gm = GridManager.Instance;
        float cs = gm.CellSize;
        Vector3 lo = gm.GridToWorld(tile.FootprintMin), hi = gm.GridToWorld(tile.FootprintMax);
        var r = Rect.MinMaxRect(lo.x - cs * 0.5f, lo.y - cs * 0.5f, hi.x + cs * 0.5f, hi.y + cs * 0.5f);
        if (tile.SolidDepth > 0f && tile.Rotation == 0) r.yMax = Mathf.Min(r.yMax, r.yMin + tile.SolidDepth * cs);
        return r;
    }

    public static bool Overlaps(Vector2 center, Vector2 half)
    {
        var gm = GridManager.Instance;
        if (gm == null) return false;
        var box = new Rect(center - half, half * 2f);

        Vector2Int min = gm.WorldToGrid(new Vector3(box.xMin, box.yMin));
        Vector2Int max = gm.WorldToGrid(new Vector3(box.xMax, box.yMax));
        var terrain = TerrainMap.Current;
        float quarter = gm.CellSize * 0.5f;
        for (int x = min.x; x <= max.x; x++)
        for (int y = min.y; y <= max.y; y++)
        {
            var tile = gm.GetTile(new Vector2Int(x, y));
            if (Blocks(tile) && Touches(box, SolidRect(tile))) return true;
            if (terrain == null) continue;

            Vector3 c = gm.GridToWorld(new Vector2Int(x, y));
            for (int qy = 0; qy <= 1; qy++)
            for (int qx = 0; qx <= 1; qx++)
                if (terrain.IsObstacleVertex(x + qx, y + qy) &&
                    Touches(box, new Rect(c.x - quarter + qx * quarter, c.y - quarter + qy * quarter, quarter, quarter)))
                    return true;
        }

        ResourceBlock.Near(box, nearBlocks);
        foreach (var block in nearBlocks)
            if (block != null && Touches(box, block.SolidRect)) return true;
        return false;
    }

    private static bool Touches(Rect a, Rect b)
    {
        const float eps = 0.001f;
        if (b.width <= 0f || b.height <= 0f) return false;
        return a.xMax > b.xMin + eps && a.xMin < b.xMax - eps &&
               a.yMax > b.yMin + eps && a.yMin < b.yMax - eps;
    }

    public static Vector2 Move(Vector2 from, Vector2 to, Vector2 feetOffset, Vector2 feetHalf)
    {
        if (GridManager.Instance == null || Overlaps(from + feetOffset, feetHalf)) return to;

        Vector2 pos = from;
        pos.x += Allowed(pos, to.x - from.x, Vector2.right, feetOffset, feetHalf);
        pos.y += Allowed(pos, to.y - from.y, Vector2.up,    feetOffset, feetHalf);
        return pos;
    }

    private static float Allowed(Vector2 pos, float d, Vector2 axis, Vector2 feetOffset, Vector2 feetHalf)
    {
        if (d == 0f || !Overlaps(pos + axis * d + feetOffset, feetHalf)) return d;

        float free = 0f, blocked = d;
        // 이분 탐색으로 벽 앞까지
        for (int i = 0; i < 8; i++)
        {
            float mid = (free + blocked) * 0.5f;
            if (Overlaps(pos + axis * mid + feetOffset, feetHalf)) blocked = mid;
            else                                                    free    = mid;
        }
        return free;
    }
}
