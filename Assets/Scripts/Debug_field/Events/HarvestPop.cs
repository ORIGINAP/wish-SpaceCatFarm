using System.Collections;
using UnityEngine;

public class HarvestPop : MonoBehaviour
{
    private const int   MaxLive  = 24;
    private const float Duration = 0.5f;
    private const int   Dots     = 6;

    private static readonly Color Leaf    = new(0.55f, 0.86f, 0.45f, 1f);
    private static readonly Color Sparkle = new(1f, 0.97f, 0.85f, 1f);

    private static int live;

    private Sprite  sprite;
    private float   width;
    private Vector3 center;

    public static void Play(SpriteRenderer crop)
    {
        if (crop == null || crop.sprite == null || live >= MaxLive) return;
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 v = cam.WorldToViewportPoint(crop.transform.position);
            if (v.x < -0.1f || v.x > 1.1f || v.y < -0.1f || v.y > 1.1f) return;
        }
        var go = new GameObject("HarvestPop");
        go.transform.position = crop.transform.position;
        var pop = go.AddComponent<HarvestPop>();
        pop.sprite = crop.sprite;
        pop.width  = crop.bounds.size.x;
        pop.center = crop.bounds.center - crop.transform.position;
        live++;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init() => live = 0;

    private void OnDestroy() => live = Mathf.Max(0, live - 1);

    private IEnumerator Start()
    {
        var ghost = WorldFx.Sprite(transform, "Crop", sprite, Color.white, 165, Mathf.Max(0.2f, width));
        Vector3 baseScale = ghost.transform.localScale;

        var dot   = CuteUIKit.Rounded(6);
        var parts = new SpriteRenderer[Dots];
        var vel   = new Vector2[Dots];
        for (int i = 0; i < Dots; i++)
        {
            parts[i] = WorldFx.Sprite(transform, "Bit", dot, i < 4 ? Leaf : Sparkle, 166, Random.Range(0.07f, 0.12f));
            parts[i].transform.localPosition = center;
            float a = Mathf.Lerp(0.15f, 0.85f, (i + Random.Range(0f, 1f)) / Dots) * Mathf.PI;
            vel[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(1.4f, 2.4f);
        }

        for (float t = 0f; t < Duration; t += Time.deltaTime)
        {
            float u = t / Duration;
            float rise = 1f - (1f - Mathf.Min(1f, u / 0.7f)) * (1f - Mathf.Min(1f, u / 0.7f));
            ghost.transform.localPosition = Vector3.up * 0.25f * rise;
            ghost.transform.localScale    = baseScale * Mathf.Lerp(1f, 1.35f, Mathf.Min(1f, u / 0.35f));
            ghost.color = new Color(1f, 1f, 1f, u < 0.35f ? 1f : 1f - (u - 0.35f) / 0.65f);

            for (int i = 0; i < Dots; i++)
            {
                vel[i] += Vector2.down * 6f * Time.deltaTime;
                parts[i].transform.localPosition += (Vector3)(vel[i] * Time.deltaTime);
                var c = parts[i].color;
                c.a = 1f - u * u;
                parts[i].color = c;
            }
            yield return null;
        }
        Destroy(gameObject);
    }
}
