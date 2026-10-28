using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class FlashBeam : MonoBehaviour
{
    public enum Kind { Mining, Seeding, Harvesting, Generic }

    private struct Palette
    {
        public Color core;
        public Color glow;
        public Color spark;
    }

    private const float ShootTime  = 0.09f;
    private const float HoldTime   = 0.17f;
    private const float FadeTime   = 0.26f;
    private const float TotalTime  = ShootTime + HoldTime + FadeTime;

    private const float GlowWidth  = 0.20f;
    private const float CoreWidth  = 0.055f;
    private const float ArcWidth   = 0.028f;
    private const float ArcAmp     = 0.07f;
    private const int   ArcPoints  = 12;

    private const float MuzzleSize = 0.38f;
    private const float ImpactSize = 0.75f;
    private const float RingSize   = 1.05f;
    private const int   SparkCount = 5;
    private const int   MoteCount  = 4;
    // 고급 설비가 한 번에 6칸
    private const int   MaxBeams   = 6;

    // Bloom 임계값 넘기기
    private const float CoreHdr   = 3.2f;
    private const float GlowHdr   = 1.6f;
    private const float SpriteHdr = 2.4f;

    // 공유 머티리얼로 배칭
    private static Material matCoreLine, matGlowLine, matSprite;
    private static Sprite   radialSprite, ringSprite;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        matCoreLine = matGlowLine = matSprite = null;
        radialSprite = ringSprite = null;
    }

    private Kind defaultKind;
    private int  baseOrder;
    private readonly List<Beam> pool = new();

    private class Beam
    {
        public GameObject     root;
        public LineRenderer   glow, core, arc;
        public SpriteRenderer muzzle, impact, ring;
        public SpriteRenderer[] sparks, motes;
        public Vector3[]      sparkVel;
        public Coroutine      co;
        public float          startedAt;
        public bool           busy;
        public float          noiseSeed;
    }

    public static FlashBeam Attach(Transform owner, Kind kind = Kind.Generic, int sortingOrder = 60)
    {
        var go = new GameObject("FlashBeam");
        go.transform.SetParent(owner, false);

        var beam = go.AddComponent<FlashBeam>();
        beam.defaultKind = kind;
        beam.baseOrder   = sortingOrder;
        return beam;
    }

    public static FlashBeam Attach(Transform owner, int sortingOrder) => Attach(owner, Kind.Generic, sortingOrder);

    public void Fire(Vector3 from, Vector3 to) => Fire(from, to, defaultKind);

    public void Fire(Vector3 from, Vector3 to, Kind kind) => Launch(from, to, PaletteOf(kind), kind);

    public void Flash(Vector3 from, Vector3 to, Color color, float width, float duration)
    {
        var p = new Palette { glow = color, core = Color.Lerp(color, Color.white, 0.75f), spark = color };
        Launch(from, to, p, Kind.Generic);
    }

    private void Launch(Vector3 from, Vector3 to, Palette palette, Kind kind)
    {
        if (!isActiveAndEnabled) return;
        EnsureSharedResources();
        Sfx.PlayAt(SoundOf(kind), (from + to) * 0.5f);

        var beam = AcquireBeam();
        if (beam.co != null) StopCoroutine(beam.co);
        beam.co = StartCoroutine(Run(beam, from, to, palette, kind));
    }

    private Beam AcquireBeam()
    {
        foreach (var b in pool)
            if (!b.busy) return b;

        if (pool.Count < MaxBeams)
        {
            var created = CreateBeam(pool.Count);
            pool.Add(created);
            return created;
        }

        // 다 쓰면 가장 오래된 것 재사용
        Beam oldest = pool[0];
        foreach (var b in pool)
            if (b.startedAt < oldest.startedAt) oldest = b;
        return oldest;
    }

    private IEnumerator Run(Beam b, Vector3 from, Vector3 to, Palette p, Kind kind)
    {
        b.busy      = true;
        b.startedAt = Time.time;
        b.noiseSeed = Random.value * 100f;
        b.root.SetActive(true);
        b.root.transform.rotation = Quaternion.identity;

        from.z = to.z = 0f;
        Vector3 dir  = to - from;
        float   len  = dir.magnitude;
        Vector3 unit = len > 0.0001f ? dir / len : Vector3.right;
        Vector3 perp = new Vector3(-unit.y, unit.x, 0f);
        Vector3 start = from + unit * Mathf.Min(0.18f, len * 0.25f);

        float inv = 1f / Mathf.Max(0.0001f, b.root.transform.lossyScale.x);

        for (int i = 0; i < b.sparks.Length; i++)
        {
            Vector2 rnd = Random.insideUnitCircle.normalized;
            Vector3 v   = (Vector3)rnd * 0.6f - unit * 0.9f;
            b.sparkVel[i] = v.normalized * Random.Range(1.4f, 2.8f);
            b.sparks[i].enabled = false;
        }
        foreach (var m in b.motes) m.enabled = false;

        bool  moteToSource = kind == Kind.Mining || kind == Kind.Harvesting;
        bool  impacted     = false;
        float impactAt     = 0f;
        float t            = 0f;

        while (t < TotalTime)
        {
            t += Time.deltaTime;

            float headP = Mathf.Clamp01(t / ShootTime);
            headP = 1f - (1f - headP) * (1f - headP) * (1f - headP);
            float fadeP = Mathf.Clamp01((t - ShootTime - HoldTime) / FadeTime);
            float life  = 1f - fadeP;
            float tailP = fadeP * fadeP * 0.92f;

            Vector3 head = Vector3.Lerp(start, to, headP);
            Vector3 tail = Vector3.Lerp(start, to, tailP);

            float flicker = 0.8f + 0.2f * Mathf.PerlinNoise(t * 38f, b.noiseSeed);
            float pulse   = 1f + 0.22f * Mathf.Sin(t * 55f) * life;

            SetLine(b.glow, tail, head, GlowWidth * pulse * (0.55f + 0.45f * life), WithAlpha(p.glow, 0.55f * life * flicker));
            SetLine(b.core, tail, head, CoreWidth * (0.75f + 0.25f * pulse) * Mathf.Max(0.15f, life), WithAlpha(p.core, life));
            UpdateArc(b, tail, head, perp, t, WithAlpha(p.glow, 0.85f * life * flicker));

            float muzzleScale = MuzzleSize * (1f + 0.3f * Mathf.Sin(t * 48f)) * (0.35f + 0.65f * life);
            b.muzzle.transform.position   = start;
            b.muzzle.transform.localScale = Vector3.one * muzzleScale * inv;
            b.muzzle.color = WithAlpha(p.glow, 0.9f * life);

            if (!impacted && headP >= 0.999f)
            {
                impacted = true;
                impactAt = t;
                b.impact.enabled = b.ring.enabled = true;
                for (int i = 0; i < b.sparks.Length; i++)
                {
                    b.sparks[i].enabled = true;
                    b.sparks[i].transform.position = to;
                }
            }

            if (impacted)
            {
                float it   = Mathf.Clamp01((t - impactAt) / (HoldTime + FadeTime));
                float easeOut = 1f - (1f - it) * (1f - it);

                b.impact.transform.position   = to;
                b.impact.transform.localScale = Vector3.one * Mathf.Lerp(0.25f, ImpactSize, easeOut) * (0.9f + 0.1f * pulse) * inv;
                b.impact.color = WithAlpha(p.glow, (1f - it) * 0.95f);

                b.ring.transform.position   = to;
                b.ring.transform.localScale = Vector3.one * Mathf.Lerp(0.15f, RingSize, easeOut) * inv;
                b.ring.color = WithAlpha(p.glow, (1f - it) * (1f - it) * 0.9f);

                float dt = Time.deltaTime;
                for (int i = 0; i < b.sparks.Length; i++)
                {
                    var s = b.sparks[i];
                    s.transform.position += b.sparkVel[i] * dt;
                    b.sparkVel[i] *= Mathf.Exp(-6f * dt);
                    s.transform.localScale = Vector3.one * Mathf.Lerp(0.11f, 0.02f, it) * inv;
                    s.color = WithAlpha(p.spark, 1f - it);
                }
            }

            UpdateMotes(b, start, to, perp, t, impacted ? t - impactAt : -1f, moteToSource, p.spark, inv);

            yield return null;
        }

        b.root.SetActive(false);
        b.busy = false;
        b.co   = null;
    }

    private static void SetLine(LineRenderer line, Vector3 a, Vector3 b, float width, Color color)
    {
        line.SetPosition(0, a);
        line.SetPosition(1, b);
        line.widthMultiplier = width;
        line.startColor = color;
        line.endColor   = color;
    }

    private static void UpdateArc(Beam b, Vector3 tail, Vector3 head, Vector3 perp, float t, Color color)
    {
        float frame = Mathf.Floor(t * 30f);
        for (int i = 0; i < ArcPoints; i++)
        {
            float s   = i / (float)(ArcPoints - 1);
            float env = Mathf.Sin(s * Mathf.PI);
            float n   = Mathf.PerlinNoise(b.noiseSeed + s * 6f, frame * 0.37f) * 2f - 1f;
            b.arc.SetPosition(i, Vector3.Lerp(tail, head, s) + perp * (n * ArcAmp * env));
        }
        b.arc.widthMultiplier = ArcWidth;
        b.arc.startColor = color;
        b.arc.endColor   = color;
    }

    private static void UpdateMotes(Beam b, Vector3 start, Vector3 to, Vector3 perp, float t, float sinceImpact,
                                    bool toSource, Color color, float inv)
    {
        const float travel = 0.24f;
        const float gap    = 0.045f;

        for (int i = 0; i < b.motes.Length; i++)
        {
            float local = toSource ? sinceImpact - i * gap : t - i * gap;
            var   m     = b.motes[i];
            if ((toSource && sinceImpact < 0f) || local < 0f || local > travel)
            {
                m.enabled = false;
                continue;
            }

            float p      = local / travel;
            float eased  = p * p * (3f - 2f * p);
            Vector3 a    = toSource ? to : start;
            Vector3 c    = toSource ? start : to;
            float wobble = Mathf.Sin(p * Mathf.PI * 2f + i * 1.7f) * 0.05f * Mathf.Sin(p * Mathf.PI);

            m.enabled = true;
            m.transform.position   = Vector3.Lerp(a, c, eased) + perp * wobble;
            m.transform.localScale = Vector3.one * Mathf.Lerp(0.13f, 0.06f, p) * inv;
            m.color = WithAlpha(color, Mathf.Sin(p * Mathf.PI) * 0.6f + 0.4f);
        }
    }

    private static Color WithAlpha(Color c, float a) { c.a = Mathf.Clamp01(a); return c; }

    private static SfxId SoundOf(Kind kind) => kind switch
    {
        Kind.Seeding    => SfxId.LaserSeeding,
        Kind.Harvesting => SfxId.LaserHarvest,
        _               => SfxId.LaserMining,
    };

    private static Palette PaletteOf(Kind kind) => kind switch
    {
        Kind.Mining => new Palette
        {
            core  = new Color(0.85f, 1.00f, 1.00f),
            glow  = new Color(0.18f, 0.78f, 1.00f),
            spark = new Color(0.55f, 0.95f, 1.00f),
        },
        Kind.Seeding => new Palette
        {
            core  = new Color(0.90f, 1.00f, 0.88f),
            glow  = new Color(0.28f, 1.00f, 0.42f),
            spark = new Color(0.72f, 1.00f, 0.50f),
        },
        Kind.Harvesting => new Palette
        {
            core  = new Color(1.00f, 0.97f, 0.85f),
            glow  = new Color(1.00f, 0.60f, 0.12f),
            spark = new Color(1.00f, 0.86f, 0.35f),
        },
        _ => new Palette
        {
            core  = Color.white,
            glow  = new Color(0.75f, 0.85f, 1.00f),
            spark = Color.white,
        },
    };

    private Beam CreateBeam(int index)
    {
        var b = new Beam();
        b.root = new GameObject($"Laser_{index}");
        b.root.transform.SetParent(transform, false);

        b.glow = CreateLine("Glow", b.root.transform, matGlowLine, 2, baseOrder);
        b.arc  = CreateLine("Arc",  b.root.transform, matCoreLine, ArcPoints, baseOrder + 1);
        b.core = CreateLine("Core", b.root.transform, matCoreLine, 2, baseOrder + 2);
        b.arc.numCapVertices = 0;

        b.muzzle = CreateSprite("Muzzle", b.root.transform, radialSprite, baseOrder + 3);
        b.impact = CreateSprite("Impact", b.root.transform, radialSprite, baseOrder + 3);
        b.ring   = CreateSprite("Ring",   b.root.transform, ringSprite,   baseOrder + 3);

        b.sparks   = new SpriteRenderer[SparkCount];
        b.sparkVel = new Vector3[SparkCount];
        for (int i = 0; i < SparkCount; i++)
            b.sparks[i] = CreateSprite($"Spark_{i}", b.root.transform, radialSprite, baseOrder + 4);

        b.motes = new SpriteRenderer[MoteCount];
        for (int i = 0; i < MoteCount; i++)
            b.motes[i] = CreateSprite($"Mote_{i}", b.root.transform, radialSprite, baseOrder + 4);

        b.impact.enabled = b.ring.enabled = false;
        b.root.SetActive(false);
        return b;
    }

    private static LineRenderer CreateLine(string name, Transform parent, Material mat, int points, int order)
    {
        var go   = new GameObject(name);
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace     = true;
        line.positionCount     = points;
        line.sharedMaterial    = mat;
        line.textureMode       = LineTextureMode.Stretch;
        line.numCapVertices    = 4;
        line.sortingOrder      = order;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows    = false;
        line.startWidth = line.endWidth = 1f;
        return line;
    }

    private static SpriteRenderer CreateSprite(string name, Transform parent, Sprite sprite, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite         = sprite;
        sr.sharedMaterial = matSprite;
        sr.sortingOrder   = order;
        return sr;
    }

    private static void EnsureSharedResources()
    {
        if (matCoreLine != null && matGlowLine != null && matSprite != null && radialSprite != null && ringSprite != null)
            return;

        var shader  = Shader.Find("Sprites/Default");
        var lineTex = BuildSoftLineTexture();

        matCoreLine = new Material(shader) { name = "Laser_Core", mainTexture = lineTex, color = new Color(CoreHdr, CoreHdr, CoreHdr, 1f) };
        matGlowLine = new Material(shader) { name = "Laser_Glow", mainTexture = lineTex, color = new Color(GlowHdr, GlowHdr, GlowHdr, 1f) };
        matSprite   = new Material(shader) { name = "Laser_Fx",   color = new Color(SpriteHdr, SpriteHdr, SpriteHdr, 1f) };

        radialSprite = BuildRadialSprite(false);
        ringSprite   = BuildRadialSprite(true);
    }

    private static Texture2D BuildSoftLineTexture()
    {
        const int w = 4, h = 64;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name = "Laser_SoftLine", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
        };
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            float v = y / (float)(h - 1);
            float a = Mathf.Pow(1f - Mathf.Abs(v * 2f - 1f), 1.5f);
            for (int x = 0; x < w; x++) px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    private static Sprite BuildRadialSprite(bool ring)
    {
        const int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = ring ? "Laser_Ring" : "Laser_Radial", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
        };
        var px = new Color32[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = ring
                ? Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.14f) * (d <= 1f ? 1f : 0f)
                : Mathf.Pow(Mathf.Clamp01(1f - d), 2.2f);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
