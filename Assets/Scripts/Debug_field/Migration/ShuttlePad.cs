using System.Collections.Generic;
using TMPro;
using UnityEngine;

// 2x2 셔틀 착륙장
public class ShuttlePad : TileItem
{
    [Header("셔틀 그림")]
    [SerializeField] private Sprite shipSprite;
    [SerializeField] private Sprite flameSmall;
    [SerializeField] private Sprite flameBig;
    [SerializeField] private Sprite shadowSprite;

    private const float ShipBaseY  = -0.08f;
    private const float RiseHeight = 9f;
    private const float BubbleY    = 1.62f;
    private const int   MaxBubbleLines = 3;

    private SpriteRenderer ship, flame, shadow;
    private BoxCollider2D  shipHit;
    private Transform      bubble;
    private SpriteRenderer bubbleBg;
    private readonly List<(SpriteRenderer icon, TextMeshPro text)> bubbleLines = new();
    private TextMeshPro    awayLabel;
    private SpriteRenderer awayBg;
    private float          nextBubbleRefresh;
    private int            shownId = -1;
    private bool           wasAway;
    private float          puffCooldown;
    private int            landSoundFor = -1;

    public Sprite ShipSprite => shipSprite;

    public Vector3 HintAnchor
        => transform.position + Vector3.up * (bubble != null && bubble.gameObject.activeSelf ? BubbleY + 0.42f : ShipBaseY + 1.55f);

    public ShuttleSave Shuttle => ShuttleService.Instance != null ? ShuttleService.Instance.ShuttleAt(GridPosition) : null;

    public override string AbilityText =>
        "셔틀을 클릭하면 짐이 실리고, 다 실으면 고향별에 다녀와 고양이 가족을 태워 와요. " +
        $"통신탑마다 셔틀 한 척 더 (최대 {ShuttleService.MaxShuttles}척)";

    private void Start()
    {
        if (IsGhost) return;
        BuildVisuals();
    }

    private void Update()
    {
        if (IsGhost || ship == null) return;
        var s = Shuttle;
        float dt = Time.deltaTime;
        puffCooldown -= dt;

        if (s == null)
        {
            SetShip(false, 0f, 0, 0f);
            SetBubble(null);
            SetAway(null);
            return;
        }

        if (!s.away)
        {
            SetShip(true, 0f, 0, 1f);
            SetBubble(s);
            SetAway(null);
            wasAway = false;
            return;
        }

        SetBubble(null);
        bool approaching = s.cargo == null || s.cargo.Count == 0;
        float elapsed = s.tripTime - s.timer;
        if (!approaching && elapsed < ShuttleService.TakeoffSeconds)
        {
            float t = elapsed / ShuttleService.TakeoffSeconds;
            float lift = t < 0.25f ? 0f : Mathf.Pow((t - 0.25f) / 0.75f, 2.2f);
            float shake = t < 0.3f ? Mathf.Sin(Time.time * 70f) * 0.025f : 0f;
            SetShip(true, lift * RiseHeight, 2, 1f - Mathf.Clamp01((t - 0.8f) / 0.2f), shake);
            if (!wasAway)
            {
                Puffs(6);
                Sfx.PlayAt(SfxId.ShuttleLaunch, transform.position);
            }
            SetAway(null);
        }
        else if (s.timer <= ShuttleService.LandSeconds && s.padX != ShuttleService.NoPad)
        {
            float t = 1f - s.timer / ShuttleService.LandSeconds;
            float h = 1f - (1f - Mathf.Pow(1f - t, 2.4f));
            SetShip(true, h * RiseHeight, t > 0.55f ? 2 : 1, Mathf.Clamp01(t / 0.15f));
            if (landSoundFor != s.id && t < 0.3f)
            {
                landSoundFor = s.id;
                Sfx.PlayAt(SfxId.ShuttleLand, transform.position);
            }
            if (t > 0.86f && puffCooldown <= 0f) { Puffs(5); puffCooldown = 10f; }
            SetAway(null);
        }
        else
        {
            SetShip(false, 0f, 0, 0f);
            SetAway(s);
            puffCooldown = 0f;
        }
        wasAway = true;
    }

    private void SetShip(bool visible, float height, int fire, float alpha, float shakeX = 0f)
    {
        ship.enabled = visible && alpha > 0.01f;
        shipHit.enabled = visible && height < 0.05f;
        if (!ship.enabled)
        {
            flame.enabled = false;
            shadow.enabled = false;
            return;
        }
        ship.transform.localPosition = new Vector3(shakeX, ShipBaseY + height, 0f);
        ship.color = new Color(1f, 1f, 1f, alpha);

        flame.enabled = fire > 0;
        if (flame.enabled)
        {
            bool big = fire == 2 && Mathf.Repeat(Time.time * 12f, 1f) < 0.5f;
            flame.sprite = big ? flameBig : flameSmall;
            flame.transform.localPosition = new Vector3(shakeX, ShipBaseY + height + 0.05f, 0f);
            flame.color = new Color(1f, 1f, 1f, alpha);
        }

        float k = Mathf.Clamp01(1f - height / RiseHeight);
        shadow.enabled = true;
        shadow.transform.localScale = shadowScale * Mathf.Lerp(0.35f, 1f, k);
        shadow.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.15f, 1f, k));
    }

    private Vector3 shadowScale = Vector3.one;

    private void Puffs(int n)
    {
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 v = cam.WorldToViewportPoint(transform.position);
            if (v.x < -0.2f || v.x > 1.2f || v.y < -0.2f || v.y > 1.2f) return;
        }
        for (int i = 0; i < n; i++)
        {
            float a = (i + Random.Range(0f, 0.6f)) / n * Mathf.PI * 2f;
            StartCoroutine(Puff(new Vector2(Mathf.Cos(a) * 0.55f, Mathf.Sin(a) * 0.22f) + Vector2.up * ShipBaseY));
        }
    }

    private System.Collections.IEnumerator Puff(Vector2 dir)
    {
        var sr = WorldFx.Sprite(transform, "Puff", CuteUIKit.Rounded(8), new Color(0.97f, 0.94f, 0.9f, 0.9f), 50, Random.Range(0.22f, 0.34f));
        sr.sortingLayerID = ship.sortingLayerID;
        sr.sortingOrder   = 2;
        Vector3 from  = new(0f, ShipBaseY, 0f);
        Vector3 scale = sr.transform.localScale;
        const float T = 0.7f;
        for (float t = 0f; t < T; t += Time.deltaTime)
        {
            float u = t / T;
            sr.transform.localPosition = from + (Vector3)(dir * (1f - (1f - u) * (1f - u)) * 1.5f) + Vector3.up * u * 0.15f;
            sr.transform.localScale    = scale * (1f + u * 0.9f);
            var c = sr.color; c.a = 0.9f * (1f - u); sr.color = c;
            yield return null;
        }
        Destroy(sr.gameObject);
    }

    private void SetBubble(ShuttleSave s)
    {
        bool show = s != null && s.cargo != null && s.cargo.Count > 0;
        if (bubble.gameObject.activeSelf != show) bubble.gameObject.SetActive(show);
        if (!show) { shownId = -1; return; }
        if (s.id == shownId && Time.unscaledTime < nextBubbleRefresh) return;
        shownId = s.id;
        nextBubbleRefresh = Time.unscaledTime + 0.25f;

        const float IconSize = 0.36f, Gap = 0.12f, Pad = 0.14f;
        float x = 0f;
        int n = Mathf.Min(MaxBubbleLines, s.cargo.Count);
        var widths = new float[n];
        for (int i = 0; i < bubbleLines.Count; i++)
        {
            var (icon, text) = bubbleLines[i];
            bool on = i < n;
            icon.gameObject.SetActive(on);
            text.gameObject.SetActive(on);
            if (!on) continue;
            var line = s.cargo[i];
            var item = NetWorld.Item(line.item);
            icon.sprite = item != null ? item.icon : null;
            if (icon.sprite != null) icon.transform.localScale = Vector3.one * (IconSize / Mathf.Max(0.01f, icon.sprite.bounds.size.x));
            bool done = line.Missing <= 0;
            text.text  = done ? $"<color=#3F8A66>{line.amount}/{line.amount}</color>" : $"{line.loaded}/{line.amount}";
            text.ForceMeshUpdate();
            widths[i] = IconSize + 0.04f + text.textBounds.size.x;
        }
        float total = 0f;
        for (int i = 0; i < n; i++) total += widths[i] + (i > 0 ? Gap : 0f);
        x = -total * 0.5f;
        for (int i = 0; i < n; i++)
        {
            var (icon, text) = bubbleLines[i];
            icon.transform.localPosition = new Vector3(x + IconSize * 0.5f, 0f, 0f);
            text.transform.localPosition = new Vector3(x + IconSize + 0.04f + (widths[i] - IconSize - 0.04f) * 0.5f, -0.01f, 0f);
            x += widths[i] + Gap;
        }
        bubbleBg.size = new Vector2(total + Pad * 2f, IconSize + 0.14f);
    }

    private void SetAway(ShuttleSave s)
    {
        bool show = s != null;
        if (awayLabel.gameObject.activeSelf != show) awayLabel.gameObject.SetActive(show);
        if (!show) return;
        int sec = Mathf.CeilToInt(Mathf.Max(0f, s.timer));
        bool approaching = s.cargo == null || s.cargo.Count == 0;
        awayLabel.text = approaching ? $"셔틀 오는 중 {sec / 60}:{sec % 60:00}" : $"고향별 왕복 {sec / 60}:{sec % 60:00}";
        awayLabel.ForceMeshUpdate();
        var b = awayLabel.textBounds;
        awayBg.size = new Vector2(b.size.x + 0.26f, b.size.y + 0.12f);
        awayBg.transform.localPosition = new Vector3(b.center.x, b.center.y, 0.01f);
    }

    private void BuildVisuals()
    {
        var padRenderer = GetComponent<SpriteRenderer>();
        int layer = padRenderer != null ? padRenderer.sortingLayerID : 0;
        // 플레이어와 같은 머티리얼로 정렬
        var mat   = WorldFx.Material() ?? (padRenderer != null ? padRenderer.sharedMaterial : null);

        shadow = NewRenderer("ShipShadow", shadowSprite, layer, 0, mat);
        shadow.transform.localPosition = new Vector3(0f, ShipBaseY + 0.04f, 0f);
        shadowScale = shadow.transform.localScale;

        ship = NewRenderer("Ship", shipSprite, layer, 1, mat);
        ship.spriteSortPoint = SpriteSortPoint.Pivot;
        shipHit = ship.gameObject.AddComponent<BoxCollider2D>();
        shipHit.isTrigger = true;
        if (shipSprite != null)
        {
            shipHit.size   = shipSprite.bounds.size;
            shipHit.offset = shipSprite.bounds.center;
        }

        flame = NewRenderer("Flame", flameSmall, layer, 1, mat);
        flame.spriteSortPoint = SpriteSortPoint.Pivot;
        flame.enabled = false;

        var go = new GameObject("CargoBubble");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, BubbleY, 0f);
        bubble = go.transform;
        bubbleBg = WorldFx.Sprite(bubble, "Background", CuteUIKit.Rounded(12), CuteUIKit.Cream, 196, 1f);
        bubbleBg.transform.localScale = Vector3.one;
        bubbleBg.drawMode = SpriteDrawMode.Sliced;
        var border = WorldFx.Sprite(bubbleBg.transform, "Border", CuteUIKit.Rounded(12), CuteUIKit.Latte, 195, 1f);
        border.transform.localScale = Vector3.one;
        border.drawMode = SpriteDrawMode.Sliced;
        border.gameObject.AddComponent<FollowSize>().Bind(bubbleBg, 0.06f);
        for (int i = 0; i < MaxBubbleLines; i++)
        {
            var icon = WorldFx.Sprite(bubble, "Icon" + i, null, Color.white, 197, 1f);
            var text = NewText(bubble, "Count" + i, 2.3f);
            text.sortingOrder = 198;
            bubbleLines.Add((icon, text));
        }
        go.SetActive(false);

        awayLabel = NewText(transform, "AwayLabel", 2f);
        awayLabel.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        awayLabel.sortingOrder = 198;
        awayBg = WorldFx.Sprite(awayLabel.transform, "Background", CuteUIKit.Rounded(12), new Color(1f, 0.97f, 0.92f, 0.86f), 197, 1f);
        awayBg.transform.localScale = Vector3.one;
        awayBg.drawMode = SpriteDrawMode.Sliced;
        awayLabel.gameObject.SetActive(false);
    }

    private SpriteRenderer NewRenderer(string name, Sprite sprite, int layer, int order, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerID = layer;
        sr.sortingOrder   = order;
        if (mat != null) sr.sharedMaterial = mat;
        return sr;
    }

    private static TextMeshPro NewText(Transform parent, string name, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshPro>();
        t.fontSize         = size;
        t.alignment        = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.rectTransform.sizeDelta = new Vector2(4f, 0.5f);
        t.color            = CuteUIKit.Cocoa;
        t.fontStyle        = FontStyles.Bold;
        t.sortingLayerID   = WorldFx.TopLayer;
        return t;
    }

    private class FollowSize : MonoBehaviour
    {
        private SpriteRenderer target, self;
        private float margin;

        public void Bind(SpriteRenderer t, float m)
        {
            target = t;
            margin = m;
            self   = GetComponent<SpriteRenderer>();
        }

        private void LateUpdate()
        {
            if (target == null || self == null) return;
            self.size = target.size + Vector2.one * margin;
        }
    }
}
