using System.Collections;
using TMPro;
using UnityEngine;

public class LabFx : MonoBehaviour
{
    private static LabFx runner;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => runner = null;

    private static LabFx Runner
    {
        get
        {
            if (runner == null) runner = new GameObject("LabFx").AddComponent<LabFx>();
            return runner;
        }
    }

    public static void Burst(Vector3 pos, Color color, int count = 12, float speed = 2.6f, float life = 0.7f)
        => Runner.StartCoroutine(BurstCo(pos, color, count, speed, life));

    public static void Ring(Vector3 pos, Color color, float radius = 1.2f, float duration = 0.6f)
        => Runner.StartCoroutine(RingCo(pos, color, radius, duration));

    public static void FloatText(Vector3 pos, string text, Color color, float size = 3.2f)
        => Runner.StartCoroutine(FloatTextCo(pos, text, color, size));

    public static void Dust(Vector3 pos, float scale = 1f)
        => Runner.StartCoroutine(BurstCo(pos, new Color(0.86f, 0.74f, 0.6f, 0.9f), 10, 1.6f * scale, 0.6f, LabArt.Disc(), 0.28f * scale, true));

    // 실험이 꺼져도 끝까지 돌게
    public static Coroutine Run(IEnumerator routine) => Runner.StartCoroutine(routine);

    private static readonly string[] SparkRows = { ".#.", "###", ".#." };

    private static Sprite Spark() => LabArt.Pixel("spark", SparkRows, "##FFFFFF", new Vector2(0.5f, 0.5f));

    private static IEnumerator BurstCo(Vector3 pos, Color color, int count, float speed, float life,
                                       Sprite sprite = null, float size = 1f, bool ground = false)
    {
        sprite ??= Spark();
        var parts = new Transform[count];
        var vel   = new Vector2[count];
        var srs   = new SpriteRenderer[count];
        for (int i = 0; i < count; i++)
        {
            var sr = LabArt.Make("Spark", sprite, pos, ground ? LabArt.OrderGround + 1 : LabArt.OrderAir, color, null);
            sr.transform.localScale = Vector3.one * size * Random.Range(0.7f, 1.2f);
            parts[i] = sr.transform;
            srs[i]   = sr;
            float a  = (i + Random.value * 0.6f) / count * Mathf.PI * 2f;
            vel[i]   = new Vector2(Mathf.Cos(a), Mathf.Sin(a) * (ground ? 0.55f : 1f)) * speed * Random.Range(0.6f, 1.1f);
        }
        for (float t = 0f; t < life; t += Time.deltaTime)
        {
            float k = t / life;
            for (int i = 0; i < count; i++)
            {
                if (parts[i] == null) continue;
                parts[i].position += (Vector3)(vel[i] * Time.deltaTime);
                vel[i] *= 1f - 3.5f * Time.deltaTime;
                var c = srs[i].color;
                c.a = color.a * (1f - k * k);
                srs[i].color = c;
            }
            yield return null;
        }
        foreach (var p in parts) if (p != null) Destroy(p.gameObject);
    }

    private static IEnumerator RingCo(Vector3 pos, Color color, float radius, float duration)
    {
        var sr = LabArt.Make("Ring", LabArt.Ring(), pos, LabArt.OrderGround + 1, color, null);
        for (float t = 0f; t < duration && sr != null; t += Time.deltaTime)
        {
            float k = t / duration;
            float s = Mathf.Lerp(0.2f, radius * 2f, 1f - (1f - k) * (1f - k));
            sr.transform.localScale = new Vector3(s, s * 0.6f, 1f);
            var c = color;
            c.a = color.a * (1f - k);
            sr.color = c;
            yield return null;
        }
        if (sr != null) Destroy(sr.gameObject);
    }

    private static IEnumerator FloatTextCo(Vector3 pos, string text, Color color, float size)
    {
        var holder = new GameObject("FloatText").transform;
        holder.position = pos;
        var label = LabArt.Label(holder, text, size, color, Vector3.zero, 220);
        const float life = 1.3f;
        for (float t = 0f; t < life && holder != null; t += Time.deltaTime)
        {
            float k = t / life;
            holder.position = pos + new Vector3(0f, 0.25f + k * 0.9f, 0f);
            float pop = k < 0.15f ? CuteUIKit.EaseOutBack(k / 0.15f, 2.4f) : 1f;
            holder.localScale = Vector3.one * pop;
            label.alpha = k < 0.7f ? 1f : 1f - (k - 0.7f) / 0.3f;
            yield return null;
        }
        if (holder != null) Destroy(holder.gameObject);
    }
}
