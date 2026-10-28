using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class WeatherFx : MonoBehaviour
{
    private const float RefWidth  = 1600f;
    private const float RefHeight = 900f;
    private const int   RainCount = 150;
    private const float FadeSpeed = 0.35f;

    private static readonly (float hour, Color color)[] SkyKeys =
    {
        (0f,    new Color(0.03f, 0.06f, 0.27f, 0.6f)),
        (4.5f,  new Color(0.03f, 0.06f, 0.27f, 0.6f)),
        (5.5f,  new Color(0.85f, 0.45f, 0.55f, 0.24f)),
        (6.5f,  new Color(1f, 0.8f, 0.6f, 0f)),
        (16.5f, new Color(1f, 0.7f, 0.4f, 0f)),
        (18f,   new Color(1f, 0.45f, 0.25f, 0.26f)),
        (19.5f, new Color(0.03f, 0.06f, 0.27f, 0.6f)),
        (24f,   new Color(0.03f, 0.06f, 0.27f, 0.6f)),
    };

    private Canvas canvas;
    private Image  sky;
    private Image  rainHaze;
    private readonly List<RectTransform> rain      = new();
    private readonly List<Image>         rainImgs  = new();
    private readonly List<float>         rainSpeed = new();
    private readonly List<Image>         aurora    = new();
    private RectTransform shootingStar;
    private Image         shootingImg;
    private Vector2       shootingVel;
    private float         shootingLife;
    private float         nextShooting;

    private float rainLevel, auroraLevel, meteorLevel;

    private void Awake() => Build();

    private void Update()
    {
        float dt = Time.deltaTime;
        var w = WeatherManager.Current;

        rainLevel   = Mathf.MoveTowards(rainLevel,   w == WeatherType.StarRain ? 1f : 0f, FadeSpeed * dt);
        auroraLevel = Mathf.MoveTowards(auroraLevel, w == WeatherType.Aurora && FarmConditions.IsNight ? 1f : 0f, FadeSpeed * 0.5f * dt);
        meteorLevel = Mathf.MoveTowards(meteorLevel, w == WeatherType.MeteorShower && FarmConditions.IsNight ? 1f : 0f, FadeSpeed * dt);

        UpdateSky();
        UpdateRain(dt);
        UpdateAurora();
        UpdateShootingStar(dt);
    }

    private void UpdateSky()
    {
        var t = GameTimeManager.Instance;
        if (t == null) { sky.color = Color.clear; return; }
        float h = t.CurrentHour + t.CurrentMinute / 60f;
        Color c = SkyKeys[SkyKeys.Length - 1].color;
        for (int i = 0; i < SkyKeys.Length - 1; i++)
        {
            var (h0, c0) = SkyKeys[i];
            var (h1, c1) = SkyKeys[i + 1];
            if (h < h0 || h > h1) continue;
            float u = h1 > h0 ? (h - h0) / (h1 - h0) : 0f;
            c = Color.Lerp(c0, c1, u);
            break;
        }
        c = Color.Lerp(c, new Color(0.35f, 0.42f, 0.55f, Mathf.Max(c.a, 0.18f)), rainLevel * 0.5f);
        sky.color = c;
    }

    private void UpdateRain(float dt)
    {
        rainHaze.color = new Color(0.55f, 0.68f, 0.9f, 0.09f * rainLevel);
        bool on = rainLevel > 0.001f;
        for (int i = 0; i < rain.Count; i++)
        {
            var img = rainImgs[i];
            float visible = Mathf.Clamp01(rainLevel * rain.Count - i * 0.7f);
            img.enabled = on && visible > 0f;
            if (!img.enabled) continue;
            img.color = new Color(0.85f, 0.92f, 1f, 0.55f * visible);

            var rt = rain[i];
            Vector2 p = rt.anchoredPosition + new Vector2(-0.28f, -1f) * rainSpeed[i] * dt;
            if (p.y < -30f) { p.y += RefHeight + 60f; p.x = Random.Range(0f, RefWidth + 300f); }
            if (p.x < -30f) p.x += RefWidth + 60f;
            rt.anchoredPosition = p;
        }
    }

    private void UpdateAurora()
    {
        float time = Time.time;
        for (int i = 0; i < aurora.Count; i++)
        {
            var img = aurora[i];
            img.enabled = auroraLevel > 0.001f;
            if (!img.enabled) continue;
            float pulse = 0.6f + 0.4f * Mathf.Sin(time * (0.35f + i * 0.13f) + i * 1.7f);
            Color baseColor = i % 3 == 0 ? new Color(0.45f, 1f, 0.75f) : i % 3 == 1 ? new Color(0.55f, 0.75f, 1f) : new Color(0.8f, 0.55f, 1f);
            img.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0.2f * pulse * auroraLevel);
            var rt = img.rectTransform;
            rt.anchoredPosition = new Vector2(Mathf.Sin(time * 0.07f + i) * 140f, -60f - i * 34f + Mathf.Sin(time * 0.21f + i * 2f) * 12f);
            rt.localRotation    = Quaternion.Euler(0f, 0f, Mathf.Sin(time * 0.05f + i) * 4f - 3f + i * 2f);
        }
    }

    private void UpdateShootingStar(float dt)
    {
        if (shootingLife > 0f)
        {
            shootingLife -= dt;
            shootingStar.anchoredPosition += shootingVel * dt;
            shootingImg.color = new Color(1f, 0.95f, 0.8f, Mathf.Clamp01(shootingLife * 2.5f) * 0.9f);
            if (shootingLife <= 0f) shootingImg.enabled = false;
            return;
        }
        if (meteorLevel < 0.5f || Time.time < nextShooting) return;

        nextShooting = Time.time + Random.Range(0.8f, 2.6f);
        shootingLife = 0.6f;
        shootingImg.enabled = true;
        shootingStar.anchoredPosition = new Vector2(Random.Range(300f, RefWidth + 200f), Random.Range(RefHeight * 0.55f, RefHeight));
        shootingVel = new Vector2(-1f, -0.55f).normalized * Random.Range(900f, 1400f);
        shootingStar.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(shootingVel.y, shootingVel.x) * Mathf.Rad2Deg);
    }

    private void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -3;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(RefWidth, RefHeight);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.Expand;

        sky      = FullScreen("Sky");
        rainHaze = FullScreen("RainHaze");

        var band = BandSprite();
        for (int i = 0; i < 3; i++)
        {
            var img = NewImage("Aurora", band);
            var rt  = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot     = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(RefWidth * 1.5f, 170f - i * 25f);
            img.enabled  = false;
            aurora.Add(img);
        }

        var dot = CuteUIKit.Rounded(2);
        for (int i = 0; i < RainCount; i++)
        {
            var img = NewImage("Rain", dot);
            var rt  = img.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.sizeDelta = new Vector2(2.2f, Random.Range(16f, 28f));
            rt.localRotation    = Quaternion.Euler(0f, 0f, 15.6f);
            rt.anchoredPosition = new Vector2(Random.Range(0f, RefWidth + 300f), Random.Range(0f, RefHeight));
            img.enabled = false;
            rain.Add(rt);
            rainImgs.Add(img);
            rainSpeed.Add(Random.Range(900f, 1300f));
        }

        shootingImg = NewImage("ShootingStar", CuteUIKit.Rounded(3));
        shootingStar = shootingImg.rectTransform;
        shootingStar.anchorMin = shootingStar.anchorMax = Vector2.zero;
        shootingStar.sizeDelta = new Vector2(70f, 3f);
        shootingImg.enabled = false;
    }

    private Image FullScreen(string name)
    {
        var img = NewImage(name, null);
        var rt  = img.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        img.color = Color.clear;
        return img;
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

    private static Sprite BandSprite()
    {
        const int h = 64;
        var tex = new Texture2D(4, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < h; y++)
        {
            float v = y / (float)(h - 1);
            float a = Mathf.Pow(Mathf.Sin(v * Mathf.PI), 2.2f);
            for (int x = 0; x < 4; x++) tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 4, h), new Vector2(0.5f, 0.5f), 100f);
    }
}
