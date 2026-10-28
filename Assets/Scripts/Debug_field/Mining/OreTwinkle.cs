using System.Collections.Generic;
using UnityEngine;

public class OreTwinkle : MonoBehaviour
{
    private const int   Pool     = 10;
    private const float Life     = 0.6f;
    private const float OrePpu   = 28.07f;
    private const float PixelFit = LabArt.PixelsPerUnit / OrePpu;
    private const float Gap      = 0.003f;

    private static readonly float[] Lifts = { -0.45f, -0.25f, -0.05f, 0.15f, 0.35f, 0.5f };
    private const string Palette = "##DFF6FF w#FFFFFF";
    private static readonly string[] BigRows =
    {
        "...#...",
        "...#...",
        "..#w#..",
        "##www##",
        "..#w#..",
        "...#...",
        "...#...",
    };
    private static readonly string[] SmallRows =
    {
        "..#..",
        ".#w#.",
        "#www#",
        ".#w#.",
        "..#..",
    };

    private readonly SpriteRenderer[] stars = new SpriteRenderer[Pool];
    private readonly float[] born = new float[Pool];
    private readonly int[]   lift = new int[Pool];
    private readonly List<ResourceBlock> near = new();
    private Sprite[] big, small;
    private float next;

    public static void Ensure()
    {
        if (FindFirstObjectByType<OreTwinkle>() == null) new GameObject("OreTwinkle").AddComponent<OreTwinkle>();
    }

    private void Awake()
    {
        var bigBase   = LabArt.Pixel("ore_twinkle_big", BigRows, Palette, new Vector2(0.5f, 0.5f));
        var smallBase = LabArt.Pixel("ore_twinkle_small", SmallRows, Palette, new Vector2(0.5f, 0.5f));
        big   = new Sprite[Lifts.Length];
        small = new Sprite[Lifts.Length];
        for (int i = 0; i < Lifts.Length; i++)
        {
            big[i]   = Lifted(bigBase, Lifts[i]);
            small[i] = Lifted(smallBase, Lifts[i]);
        }
        for (int i = 0; i < Pool; i++)
        {
            stars[i] = LabArt.Make("Twinkle", big[0], Vector3.zero, ResourceBlock.BaseSortingOrder, Color.white, transform);
            stars[i].spriteSortPoint = SpriteSortPoint.Pivot;
            stars[i].transform.localScale = Vector3.one * PixelFit;
            stars[i].enabled = false;
            born[i] = -10f;
        }
    }

    private static Sprite Lifted(Sprite s, float liftUnits)
    {
        float worldPerPx = 1f / OrePpu;
        float pivotY = 0.5f - (liftUnits + Gap) / (s.rect.height * worldPerPx);
        return Sprite.Create(s.texture, s.rect, new Vector2(0.5f, pivotY), LabArt.PixelsPerUnit, 0, SpriteMeshType.FullRect);
    }

    private void Update()
    {
        float now = Time.time;
        for (int i = 0; i < Pool; i++)
        {
            var s = stars[i];
            if (!s.enabled) continue;
            float t = (now - born[i]) / Life;
            if (t >= 1f) { s.enabled = false; continue; }
            float k = Mathf.Sin(t * Mathf.PI);
            s.sprite = k > 0.55f ? big[lift[i]] : small[lift[i]];
            s.color  = new Color(1f, 1f, 1f, Mathf.Clamp01(k * 1.8f));
        }

        if (now < next) return;
        next = now + Random.Range(0.08f, 0.22f);

        var cam = Camera.main;
        if (cam == null) return;
        float dist = -cam.transform.position.z;
        if (dist <= 0f) return;
        Vector3 a = cam.ViewportToWorldPoint(new Vector3(0f, 0f, dist));
        Vector3 b = cam.ViewportToWorldPoint(new Vector3(1f, 1f, dist));
        var view = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        ResourceBlock.Near(view, near);
        if (near.Count == 0) return;

        var ore = near[Random.Range(0, near.Count)];
        if (ore == null || ore.Data == null || !view.Contains((Vector2)ore.OriginPosition)) return;
        float chance = ore.Data.minToolGrade >= 3 ? 1f : ore.Data.minToolGrade == 2 ? 0.6f : 0.35f;
        if (Random.value > chance) return;

        int slot = -1;
        for (int i = 0; i < Pool; i++)
            if (!stars[i].enabled) { slot = i; break; }
        if (slot < 0) return;

        float sc = ore.transform.localScale.x;
        int li;
        do li = Random.Range(0, Lifts.Length); while (Mathf.Abs(Lifts[li]) > 0.5f * sc + 0.05f && li != 2);
        var star = stars[slot];
        Vector3 o = ore.OriginPosition;
        bool hasSr = ore.TryGetComponent<SpriteRenderer>(out var sr);
        float key = hasSr ? sr.bounds.center.y : o.y;
        star.transform.position = new Vector3(o.x + Random.Range(-0.6f, 0.6f) * sc, key - Gap, o.z);
        star.sortingOrder = hasSr ? sr.sortingOrder : ResourceBlock.BaseSortingOrder;
        star.sprite  = small[li];
        star.color   = new Color(1f, 1f, 1f, 0f);
        star.enabled = true;
        lift[slot] = li;
        born[slot] = now;
    }
}
