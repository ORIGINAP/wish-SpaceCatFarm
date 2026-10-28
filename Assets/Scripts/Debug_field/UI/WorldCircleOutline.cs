using UnityEngine;

public static class WorldCircleOutline
{
    public static LineRenderer Create(Transform parent, float radius, Color color,
        float width = 0.04f, int segments = 64, int sortingOrder = 50)
    {
        var go = new GameObject("RangeOutline");
        go.transform.SetParent(parent, false);

        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace  = false;
        line.loop           = true;
        line.positionCount  = segments;
        line.startWidth     = width;
        line.endWidth       = width;
        line.sharedMaterial = WorldConnectionLine.SharedMaterial;
        line.startColor     = color;
        line.endColor       = color;
        line.numCapVertices = 2;
        line.sortingOrder   = sortingOrder;

        for (int i = 0; i < segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius);
        }
        return line;
    }
}
