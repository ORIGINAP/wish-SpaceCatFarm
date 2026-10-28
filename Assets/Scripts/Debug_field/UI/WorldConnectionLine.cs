using UnityEngine;

public static class WorldConnectionLine
{
    private static Material sharedMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => sharedMaterial = null;

    // 땅과 같은 층, 설비 아래
    public const int   UnderSpritesOrder = -1;
    public const float UnderSpritesZ     = -0.1f;

    public static Vector3 UnderSprites(Vector3 p) => new(p.x, p.y, UnderSpritesZ);

    // 공유해야 배칭됨
    public static Material SharedMaterial
        => sharedMaterial != null ? sharedMaterial : (sharedMaterial = new Material(Shader.Find("Sprites/Default")));

    public static LineRenderer Create(Vector3 from, Vector3 to, Color color, float width = 0.03f, int sortingOrder = 55, Transform owner = null)
    {
        var go = new GameObject("ConnectionLine");
        if (owner != null) go.transform.SetParent(owner, true);

        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace  = true;
        line.positionCount  = 2;
        line.startWidth     = width;
        line.endWidth       = width;
        line.sharedMaterial = SharedMaterial;
        line.startColor     = color;
        line.endColor       = color;
        line.numCapVertices = 2;
        line.sortingOrder   = sortingOrder;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        return line;
    }

    public static LineRenderer Create(Transform from, Transform to, Color color, float width = 0.03f, int sortingOrder = 55)
        => Create(from.position, to.position, color, width, sortingOrder, from);
}
