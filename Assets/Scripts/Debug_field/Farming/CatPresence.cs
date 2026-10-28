using System.Collections.Generic;
using UnityEngine;

public static class CatPresence
{
    private static readonly List<Transform> cats = new();

    public static void Register(Transform t)
    {
        if (t != null && !cats.Contains(t)) cats.Add(t);
    }

    public static void Unregister(Transform t) => cats.Remove(t);

    public static bool AnyWithin(Vector3 pos, float radius)
    {
        float r2 = radius * radius;
        var me = PlayerTileController.Instance;
        if (me != null && ((Vector2)(me.transform.position - pos)).sqrMagnitude <= r2) return true;
        for (int i = cats.Count - 1; i >= 0; i--)
        {
            var t = cats[i];
            if (t == null) { cats.RemoveAt(i); continue; }
            if (((Vector2)(t.position - pos)).sqrMagnitude <= r2) return true;
        }
        return false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init() => cats.Clear();
}
