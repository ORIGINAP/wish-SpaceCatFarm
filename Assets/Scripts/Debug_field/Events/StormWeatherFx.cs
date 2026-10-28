using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class StormWeatherFx : MonoBehaviour
{
    private const int   ParticleCount = 200;
    private const float RefWidth  = 1600f;
    private const float RefHeight = 900f;

    private static StormWeatherFx instance;

    private Canvas                    canvas;
    private RectTransform             canvasRT;
    private Image                     haze;
    private readonly List<RectTransform> particles = new();
    private readonly List<Image>      particleImages = new();
    private readonly List<Vector2>    velocities = new();
    private readonly List<float>      phases = new();
    private WorldTheme builtFor;
    private bool       built;
    private float      intensity;
    private Coroutine  playCo;
    private static Sprite dotSprite;

    public static void Play(WorldTheme theme, float rampUp, float hold, float fadeOut)
    {
        if (instance == null) instance = new GameObject("StormWeatherFx").AddComponent<StormWeatherFx>();
        instance.StartStorm(theme, rampUp, hold, fadeOut);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance  = null;
        dotSprite = null;
    }

    private void StartStorm(WorldTheme theme, float rampUp, float hold, float fadeOut)
    {
        if (!built || builtFor != theme) Build(theme);
        if (playCo != null) StopCoroutine(playCo);
        playCo = StartCoroutine(Run(rampUp, hold, fadeOut));
    }

    private IEnumerator Run(float rampUp, float hold, float fadeOut)
    {
        canvas.enabled = true;
        float from = intensity;
        for (float t = 0f; t < rampUp; t += Time.deltaTime) { SetIntensity(Mathf.Lerp(from, 1f, t / rampUp)); yield return null; }
        SetIntensity(1f);
        yield return new WaitForSeconds(hold);
        for (float t = 0f; t < fadeOut; t += Time.deltaTime) { SetIntensity(1f - t / fadeOut); yield return null; }
        SetIntensity(0f);
        canvas.enabled = false;
        playCo = null;
    }

    private void SetIntensity(float value)
    {
        intensity = Mathf.Clamp01(value);
        var tint = WorldThemeConfig.StormTint(builtFor);
        haze.color = new Color(tint.r, tint.g, tint.b, tint.a * intensity);
        var pc = WorldThemeConfig.StormParticle(builtFor);
        for (int i = 0; i < particleImages.Count; i++)
        {
            float visible = Mathf.Clamp01(intensity * particleImages.Count - i * 0.6f);
            particleImages[i].color = new Color(pc.r, pc.g, pc.b, pc.a * visible);
        }
    }

    private void Update()
    {
        if (!built || !canvas.enabled) return;
        float dt   = Time.deltaTime;
        float gust = 0.75f + 0.5f * intensity;
        for (int i = 0; i < particles.Count; i++)
        {
            var rt = particles[i];
            Vector2 p = rt.anchoredPosition + velocities[i] * gust * dt;
            if (builtFor == WorldTheme.Tundra)
                p.y += Mathf.Sin(Time.time * 3f + phases[i]) * 40f * dt;

            if (p.x < -20f) p.x += RefWidth + 40f;
            if (p.x > RefWidth + 20f) p.x -= RefWidth + 40f;
            if (p.y < -20f) p.y += RefHeight + 40f;
            if (p.y > RefHeight + 20f) p.y -= RefHeight + 40f;
            rt.anchoredPosition = p;
        }
    }

    private void Build(WorldTheme theme)
    {
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = -2;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
            scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.Expand;
            canvasRT = (RectTransform)transform;

            haze = NewImage("Haze", null);
            var hrt = haze.rectTransform;
            hrt.anchorMin = Vector2.zero;
            hrt.anchorMax = Vector2.one;
            hrt.offsetMin = hrt.offsetMax = Vector2.zero;
        }

        foreach (var p in particles) Destroy(p.gameObject);
        particles.Clear(); particleImages.Clear(); velocities.Clear(); phases.Clear();

        bool snow = theme == WorldTheme.Tundra;
        for (int i = 0; i < ParticleCount; i++)
        {
            var img = NewImage("Particle", DotSprite());
            var rt  = img.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            if (snow)
            {
                var v = new Vector2(Random.Range(-620f, -380f), Random.Range(-340f, -200f));
                velocities.Add(v);
                if (i % 3 == 0)
                {
                    rt.sizeDelta     = new Vector2(Random.Range(12f, 20f), Random.Range(3f, 4.5f));
                    rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg);
                }
                else
                {
                    float size = Random.Range(5f, 10f);
                    rt.sizeDelta = new Vector2(size, size);
                }
            }
            else
            {
                rt.sizeDelta     = new Vector2(Random.Range(10f, 22f), Random.Range(2f, 3.5f));
                rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-8f, -2f));
                velocities.Add(new Vector2(Random.Range(-1500f, -900f), Random.Range(-140f, -40f)));
            }
            rt.anchoredPosition = new Vector2(Random.Range(0f, RefWidth), Random.Range(0f, RefHeight));
            phases.Add(Random.Range(0f, Mathf.PI * 2f));
            particles.Add(rt);
            particleImages.Add(img);
        }

        builtFor = theme;
        built    = true;
        canvas.enabled = false;
        SetIntensity(intensity);
    }

    private Image NewImage(string name, Sprite sprite)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var img = go.AddComponent<Image>();
        img.sprite        = sprite;
        img.raycastTarget = false;
        return img;
    }

    private static Sprite DotSprite() => dotSprite != null ? dotSprite : (dotSprite = CuteUIKit.Rounded(4));
}
