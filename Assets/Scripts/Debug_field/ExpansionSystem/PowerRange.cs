using UnityEngine;

public static class PowerRange
{
    public static Vector2 Center(Vector2Int min, Vector2Int max)
        => new((min.x + max.x + 1) * 0.5f, (min.y + max.y + 1) * 0.5f);

    public static float Reach(Vector2Int min, Vector2Int max, int radius)
        => radius + Mathf.Max(max.x - min.x + 1, max.y - min.y + 1) * 0.5f;

    public static float Reach(int w, int h, float radius) => radius + Mathf.Max(w, h) * 0.5f;

    // 원형 범위, 맨해튼 아님
    public static float SqrDistance(Vector2Int srcMin, Vector2Int srcMax, Vector2Int min, Vector2Int max)
    {
        Vector2 c = Center(srcMin, srcMax);
        float dx = Mathf.Clamp(c.x, min.x + 0.5f, max.x + 0.5f) - c.x;
        float dy = Mathf.Clamp(c.y, min.y + 0.5f, max.y + 0.5f) - c.y;
        return dx * dx + dy * dy;
    }

    public static bool Contains(Vector2Int srcMin, Vector2Int srcMax, int radius,
                                Vector2Int min, Vector2Int max, out float sqrDistance)
    {
        sqrDistance = SqrDistance(srcMin, srcMax, min, max);
        float reach = Reach(srcMin, srcMax, radius);
        return sqrDistance <= reach * reach + 0.001f;
    }
}
