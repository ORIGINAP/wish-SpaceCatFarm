using System.Collections;
using UnityEngine;

public class GoldenSparkle : MonoBehaviour
{
    private const int   Count    = 12;
    private const float Duration = 1.1f;

    public static void Play(Vector3 at)
    {
        var go = new GameObject("GoldenSparkle");
        go.transform.position = at;
        go.AddComponent<GoldenSparkle>();
    }

    private IEnumerator Start()
    {
        var dot   = CuteUIKit.Rounded(6);
        var parts = new SpriteRenderer[Count];
        var vel   = new Vector2[Count];
        for (int i = 0; i < Count; i++)
        {
            parts[i] = WorldFx.Sprite(transform, "Spark", dot, new Color(1f, 0.85f, 0.3f, 1f), 170, Random.Range(0.08f, 0.16f));
            float a = (i / (float)Count) * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f);
            vel[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(1.2f, 2.4f) + Vector2.up * 1.2f;
        }

        for (float t = 0f; t < Duration; t += Time.deltaTime)
        {
            float u = t / Duration;
            for (int i = 0; i < Count; i++)
            {
                vel[i] += Vector2.down * 4f * Time.deltaTime;
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
