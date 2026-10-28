using System;
using UnityEngine;

public static class ExploredMap
{
    private static byte[] bits = Array.Empty<byte>();
    private static int    width, height;

    private static bool    dirty;
    private static RectInt dirtyRect;

    public static int Width  => width;
    public static int Height => height;

    public static int MinX => -(width / 2);
    public static int MinY => -(height / 2);

    public static int Generation { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        bits = Array.Empty<byte>();
        width = height = 0;
        dirty = false;
        dirtyRect = default;
        Generation = 0;
    }

    public static void Clear(int w, int h)
    {
        width  = Mathf.Max(0, w);
        height = Mathf.Max(0, h);
        bits   = new byte[(width * height + 7) / 8];
        dirty  = false;
        Generation++;
    }

    public static bool IsExplored(int x, int y)
    {
        int i = x - MinX, j = y - MinY;
        if (i < 0 || j < 0 || i >= width || j >= height) return false;
        int idx = j * width + i;
        return (bits[idx >> 3] & (1 << (idx & 7))) != 0;
    }

    public static void Reveal(Vector2Int center, int radius)
    {
        if (width == 0) return;
        int x0 = Mathf.Max(center.x - radius, MinX), x1 = Mathf.Min(center.x + radius, MinX + width - 1);
        int y0 = Mathf.Max(center.y - radius, MinY), y1 = Mathf.Min(center.y + radius, MinY + height - 1);
        int r2 = radius * radius + radius;
        int ax = int.MaxValue, ay = int.MaxValue, bx = int.MinValue, by = int.MinValue;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int dx = x - center.x, dy = y - center.y;
                if (dx * dx + dy * dy > r2) continue;
                int  idx  = (y - MinY) * width + (x - MinX);
                byte mask = (byte)(1 << (idx & 7));
                if ((bits[idx >> 3] & mask) != 0) continue;
                bits[idx >> 3] |= mask;
                ax = Mathf.Min(ax, x); ay = Mathf.Min(ay, y);
                bx = Mathf.Max(bx, x); by = Mathf.Max(by, y);
            }
        if (bx < ax) return;
        var rect = new RectInt(ax, ay, bx - ax + 1, by - ay + 1);
        if (!dirty) { dirtyRect = rect; dirty = true; return; }
        int minX = Mathf.Min(dirtyRect.xMin, rect.xMin), minY = Mathf.Min(dirtyRect.yMin, rect.yMin);
        int maxX = Mathf.Max(dirtyRect.xMax, rect.xMax), maxY = Mathf.Max(dirtyRect.yMax, rect.yMax);
        dirtyRect = new RectInt(minX, minY, maxX - minX, maxY - minY);
    }

    public static bool TakeDirty(out RectInt rect)
    {
        rect  = dirtyRect;
        bool had = dirty;
        dirty = false;
        return had;
    }

    public static string ToSave() => width == 0 ? "" : Convert.ToBase64String(bits);

    // 크기 안 맞으면 구버전 세이브
    public static bool Restore(string saved)
    {
        if (string.IsNullOrEmpty(saved) || width == 0) return false;
        byte[] data;
        try { data = Convert.FromBase64String(saved); }
        catch (FormatException) { return false; }
        if (data.Length != bits.Length) return false;
        bits  = data;
        dirty = false;
        Generation++;
        return true;
    }

    // 구버전 세이브 호환
    public static void RevealAroundWorld(int radius)
    {
        var gm = GridManager.Instance;
        if (gm == null) return;
        foreach (var tile in gm.GetDynamicTiles())
            if (tile != null && !tile.IsGhost) Reveal(tile.GridPosition, radius);
        if (CenterCore.Current != null) Reveal(CenterCore.Current.GridPosition, radius + CenterCore.Size);
        var ptc = PlayerTileController.Instance;
        if (ptc != null) Reveal(gm.WorldToGrid(ptc.transform.position), radius);
    }
}
