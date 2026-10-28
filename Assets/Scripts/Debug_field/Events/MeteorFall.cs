using System;
using System.Collections;
using UnityEngine;

public class MeteorFall : MonoBehaviour
{
    private const float FallSeconds = 0.9f;
    private static readonly Vector3 FromOffset = new(-5.5f, 8f, 0f);

    private Vector3        target;
    private Action         onLanded;
    private SpriteRenderer head;
    private SpriteRenderer[] trail;

    public static void Play(Vector3 at, Action onLanded, float fallSeconds = FallSeconds)
    {
        var go = new GameObject("MeteorFall");
        var m  = go.AddComponent<MeteorFall>();
        m.target   = at;
        m.onLanded = onLanded;
        m.StartCoroutine(m.Run(fallSeconds));
    }

    private IEnumerator Run(float seconds)
    {
        var dot = CuteUIKit.Rounded(8);
        head = WorldFx.Sprite(transform, "Head", dot, new Color(1f, 0.93f, 0.7f, 1f), 160, 0.42f);
        trail = new SpriteRenderer[6];
        for (int i = 0; i < trail.Length; i++)
        {
            float k = 1f - i / (float)trail.Length;
            trail[i] = WorldFx.Sprite(transform, "Trail", dot, new Color(1f, 0.6f + 0.3f * k, 0.35f, 0.75f * k), 159, 0.36f * k + 0.08f);
        }

        Vector3 from = target + FromOffset;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float u = t / seconds;
            Vector3 p = Vector3.Lerp(from, target, u * u);
            head.transform.position = p;
            for (int i = 0; i < trail.Length; i++)
            {
                float back = Mathf.Max(0f, u * u - 0.035f * (i + 1));
                trail[i].transform.position = Vector3.Lerp(from, target, back);
            }
            yield return null;
        }

        foreach (var s in trail) s.enabled = false;
        head.enabled = false;
        CameraShake.GetOrCreate().Shake(0.35f, 0.5f);
        Sfx.PlayAt(SfxId.BlockBreak, target);
        try { onLanded?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }

        var ring = WorldFx.Sprite(transform, "Dust", CuteUIKit.Rounded(16), new Color(0.95f, 0.85f, 0.7f, 0.7f), 158, 0.3f);
        ring.transform.position = target;
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            float u = t / 0.6f;
            ring.transform.localScale = Vector3.one * Mathf.Lerp(0.3f, 2.2f, u);
            ring.color = new Color(0.95f, 0.85f, 0.7f, 0.7f * (1f - u));
            yield return null;
        }
        Destroy(gameObject);
    }
}
