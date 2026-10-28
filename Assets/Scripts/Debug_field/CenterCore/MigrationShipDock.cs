using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

public class MigrationShipDock : MonoBehaviour
{
    [SerializeField] private Sprite   shipSprite;
    [SerializeField] private Sprite[] flameBurn;
    [SerializeField] private Sprite   shadowSprite;

    private const float DropHeight   = 14f;
    private const float LandSeconds  = 4.2f;
    private const float ThudAt       = 2.3f;
    private const float DoorY        = -0.3f;
    private const int   Passengers   = 8;
    private const float ShadowScale  = 1.8f;

    private static readonly List<MigrationShipDock> all = new();

    public static bool Landing { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        all.Clear();
        Landing = false;
    }

    private TileItem       tile;
    private SpriteRenderer ship, flame, shadow;
    private int            shipOrder;
    private bool           landing;

    private void Awake() => tile = GetComponent<TileItem>();

    private void OnEnable()
    {
        all.Add(this);
        CenterCore.OnAnyLevelUp += OnLevelUp;
    }

    private void OnDisable()
    {
        all.Remove(this);
        CenterCore.OnAnyLevelUp -= OnLevelUp;
        if (landing) { landing = false; Landing = false; }
    }

    private void Start()
    {
        if (tile != null && tile.IsGhost) { enabled = false; return; }
        Build();
    }

    private static bool Arrived => CenterCore.Current != null && CenterCore.Current.IsMaxLevel;

    private void Update()
    {
        if (ship == null || landing) return;
        bool show = Arrived && Chosen() == this;
        if (ship.enabled != show) SetShip(show, 0f, false);
    }

    private static MigrationShipDock Chosen()
    {
        var core = CenterCore.Current;
        MigrationShipDock best = null;
        float bestScore = float.MaxValue;
        foreach (var d in all)
        {
            if (d == null || d.ship == null) continue;
            float score = core != null ? Vector2.Distance(d.transform.position, core.transform.position) : 0f;
            if (d.tile is CivFacility f && !f.IsPowered) score += 10000f;
            if (score < bestScore) { bestScore = score; best = d; }
        }
        return best;
    }

    private void OnLevelUp(CenterCore core, int level)
    {
        if (core == null || !core.IsMaxLevel || ship == null || landing || Chosen() != this) return;
        landing = true;
        Landing = true;
        StartCoroutine(Land());
    }

    private IEnumerator Land()
    {
        var restoreCamera = FocusCamera();
        NotificationUI.Instance?.Show("고향별에 남은 고양이들을 모두 태운 큰 이주선이 착륙장으로 내려와요!", NotificationUI.GoodNewsColor);
        bool thud = false, puffed = false;
        for (float t = 0f; t < LandSeconds; t += Time.deltaTime)
        {
            float u = t / LandSeconds;
            SetShip(true, DropHeight * Mathf.Pow(1f - u, 2.4f), true);
            if (!thud && t >= LandSeconds - ThudAt) { thud = true; Sfx.PlayAt(SfxId.ShuttleLand, transform.position); }
            if (!puffed && u > 0.86f) { puffed = true; for (int i = 0; i < 8; i++) StartCoroutine(Puff(i, 8)); }
            yield return null;
        }
        SetShip(true, 0f, false);
        CameraShake.GetOrCreate().Shake(0.5f, 0.3f, 0f);
        LabFx.Dust(transform.position + Vector3.down * 0.35f, 1.6f);
        LabFx.Ring(transform.position + Vector3.down * 0.2f, CuteUIKit.Butter, 2.6f, 0.8f);
        yield return new WaitForSeconds(0.6f);

        var rng = new System.Random();
        Vector2 door = transform.position + Vector3.up * DoorY;
        for (int i = 0; i < Passengers; i++)
            ResidentCat.Spawn(door, tile, ShuttleService.RandomCatName(rng), rng.Next(CatSkin.ColorCount), rng.Next(CatSkin.AccessoryCount),
                              true, i * 0.3f, i, i == 0);
        yield return new WaitForSeconds(Passengers * 0.3f + 1.2f);

        restoreCamera?.Invoke();
        landing = false;
        Landing = false;
    }

    private System.Action FocusCamera()
    {
        var vcam = FindFirstObjectByType<CinemachineCamera>();
        if (vcam == null) return null;
        var previous = vcam.Follow;
        var focus = new GameObject("MigrationShipFocus").transform;
        focus.position = transform.position + Vector3.up * 1.2f;
        vcam.Follow = focus;
        return () =>
        {
            if (vcam != null && vcam.Follow == focus) vcam.Follow = previous;
            if (focus != null) Destroy(focus.gameObject);
        };
    }

    private void Build()
    {
        var pad   = GetComponent<SpriteRenderer>();
        int layer = pad != null ? pad.sortingLayerID : 0;
        // 플레이어와 같은 머티리얼
        var mat = WorldFx.Material() ?? (pad != null ? pad.sharedMaterial : null);

        shadow = NewRenderer("ShipShadow", shadowSprite, transform, layer, 0, mat);
        shadow.transform.localPosition = new Vector3(0f, -0.4f, 0f);
        shadow.transform.localScale    = Vector3.one * ShadowScale;

        shipOrder = pad != null ? pad.sortingOrder + 1 : 1;
        ship = NewRenderer("MigrationShip", shipSprite, transform, layer, shipOrder, mat);
        ship.spriteSortPoint = SpriteSortPoint.Pivot;

        flame = NewRenderer("Flame", null, ship.transform, layer, shipOrder, mat);
        flame.spriteSortPoint = SpriteSortPoint.Pivot;

        SetShip(false, 0f, false);
    }

    private void SetShip(bool visible, float height, bool burn)
    {
        ship.enabled   = visible;
        shadow.enabled = visible;
        flame.enabled  = visible && burn && flameBurn != null && flameBurn.Length > 0;
        if (!visible) return;

        ship.transform.localPosition = new Vector3(0f, height, 0f);
        bool flying = height > 0.05f;
        ship.sortingLayerID = flying ? WorldFx.TopLayer : shadow.sortingLayerID;
        ship.sortingOrder   = flying ? 300 : shipOrder;
        if (flame.enabled)
        {
            flame.sprite = flameBurn[(int)(Time.time * 12f) % flameBurn.Length];
            flame.sortingLayerID = ship.sortingLayerID;
            flame.sortingOrder   = ship.sortingOrder;
        }

        float k = Mathf.Clamp01(1f - height / DropHeight);
        shadow.transform.localScale = Vector3.one * ShadowScale * Mathf.Lerp(0.3f, 1f, k);
        shadow.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.1f, 1f, k));
    }

    private IEnumerator Puff(int i, int n)
    {
        float a = (i + Random.Range(0f, 0.6f)) / n * Mathf.PI * 2f;
        Vector2 dir = new(Mathf.Cos(a) * 0.8f, Mathf.Sin(a) * 0.3f);
        var sr = WorldFx.Sprite(transform, "Puff", CuteUIKit.Rounded(8), new Color(0.97f, 0.94f, 0.9f, 0.9f), 50, Random.Range(0.3f, 0.45f));
        sr.sortingLayerID = shadow.sortingLayerID;
        sr.sortingOrder   = shipOrder + 1;
        Vector3 from  = new(0f, -0.4f, 0f);
        Vector3 scale = sr.transform.localScale;
        const float T = 0.9f;
        for (float t = 0f; t < T; t += Time.deltaTime)
        {
            float u = t / T;
            sr.transform.localPosition = from + (Vector3)(dir * (1f - (1f - u) * (1f - u)) * 2f) + Vector3.up * u * 0.2f;
            sr.transform.localScale    = scale * (1f + u);
            var c = sr.color; c.a = 0.9f * (1f - u); sr.color = c;
            yield return null;
        }
        Destroy(sr.gameObject);
    }

    private static SpriteRenderer NewRenderer(string name, Sprite sprite, Transform parent, int layer, int order, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite         = sprite;
        sr.sortingLayerID = layer;
        sr.sortingOrder   = order;
        if (mat != null) sr.sharedMaterial = mat;
        return sr;
    }
}
