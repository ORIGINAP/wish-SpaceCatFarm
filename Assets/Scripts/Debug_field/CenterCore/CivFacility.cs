using System.Collections.Generic;
using UnityEngine;

// 프리팹에 정수로 저장, 끝에만 추가
public enum CivEffect
{
    None,
    Research,
    Water,
    Greenhouse,
    Factory,
    Comms,
    Observatory,
    Housing,
    Control,
}

public class CivFacility : TileItem, ICoverageArea
{
    [Header("문명 시설")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite onSprite;
    [SerializeField] private Sprite offSprite;
    [Tooltip("전기가 들어오는 동안 쓰는 전기")]
    [SerializeField, Min(0f)] private float powerUse = 0.2f;

    [Header("시설 효과 (전기가 들어오는 동안)")]
    [SerializeField] private CivEffect effect = CivEffect.None;
    [Tooltip("효과 범위")]
    [SerializeField, Min(0)] private int effectRadius = 0;

    public const float FactorySpeed = 1.3f;

    private PowerLink power;

    private static readonly List<CivFacility> placed = new();

    public bool IsPowered => power != null && power.IsPowered;

    public CivEffect Effect => effect;

    public int   CoverageRadiusCells => effectRadius;
    public Color CoverageColor       => effect switch
    {
        CivEffect.Water      => new Color(0.35f, 0.65f, 1f, 0.14f),
        CivEffect.Greenhouse => new Color(0.45f, 1f, 0.55f, 0.13f),
        CivEffect.Factory    => new Color(1f, 0.7f, 0.35f, 0.12f),
        _                    => new Color(1f, 1f, 1f, 0f),
    };

    public override string AbilityText => effect switch
    {
        CivEffect.Research    => $"문명 시설 — 전기가 들어오면 연구비가 {TechTreeManager.LabDiscount * 100f:0}% 싸져요",
        CivEffect.Water       => $"문명 시설 — 전기가 들어오면 {effectRadius}칸 안의 밭에 물을 줘요 (작물 {FarmConditions.WaterMult:0.##}배 빨리 · 물 좋아함 작물은 {FarmConditions.WaterLoverMult:0.#}배)",
        CivEffect.Greenhouse  => $"문명 시설 — 전기가 들어오면 {effectRadius}칸 안의 작물이 {FarmConditions.GreenhouseMult:0.#}배 빨리 자라고 폭풍에 날아가지 않아요",
        CivEffect.Factory     => $"문명 시설 — 전기가 들어오면 {effectRadius}칸 안의 제련소 · 고양이 주방 · 퇴비통이 {FactorySpeed:0.#}배 빨리 일해요",
        CivEffect.Comms       => $"문명 시설 — 전기가 들어오면 이주 셔틀이 한 척 더 다녀요 (착륙장이 하나 더 있어야 해요 · 최대 {ShuttleService.MaxShuttles}척)",
        CivEffect.Observatory => "문명 시설 — 전기가 들어오면 날씨를 한 칸 더 멀리 내다보고, 오로라 · 유성우가 더 자주 와요",
        CivEffect.Housing     => "문명 시설 — 전기가 들어오면 주민들이 살고, 셔틀이 고양이를 한 마리 더 태워 와요 (최대 4채)",
        CivEffect.Control     => $"문명 시설 — 전기가 들어오면 셔틀 왕복이 {(1f - ShuttleService.ControlTripMult) * 100f:0}% 빨라지고 짐값이 {ShuttleService.ControlReward * 100f:0}% 늘어요",
        _                     => "문명 시설 — 전기가 들어오면 센터코어 발전 조건에 세어져요",
    };

    public static bool IsFacilityItem(FarmingItemData item)
        => item != null && item.tilePrefab != null && item.tilePrefab.GetComponent<CivFacility>() != null;

    private void Start()
    {
        ApplyLook();
        if (IsGhost) return;
        placed.Add(this);

        power = PowerLink.Attach(this, PowerLink.Kind.Consumer);
        if (power != null) power.OnPowerChanged += ApplyLook;
        ApplyLook();
    }

    private void OnDestroy() => placed.Remove(this);

    private void Update()
    {
        if (IsGhost || !IsPowered) return;
        EnergyManager.DrawPower(power.Source, powerUse * Time.deltaTime);
    }

    private void ApplyLook()
    {
        if (spriteRenderer == null) return;
        var sprite = IsGhost || IsPowered ? onSprite : offSprite;
        if (sprite != null) spriteRenderer.sprite = sprite;
    }

    public static bool Covers(CivEffect e, Vector2Int cell)
    {
        foreach (var f in placed)
        {
            if (f == null || f.effect != e || !f.IsPowered) continue;
            int dx = Mathf.Max(Mathf.Max(f.FootprintMin.x - cell.x, 0), cell.x - f.FootprintMax.x);
            int dy = Mathf.Max(Mathf.Max(f.FootprintMin.y - cell.y, 0), cell.y - f.FootprintMax.y);
            if (dx + dy <= f.effectRadius) return true;
        }
        return false;
    }

    public static bool Covers(CivEffect e, TileItem tile)
    {
        if (tile == null) return false;
        foreach (var f in placed)
        {
            if (f == null || f.effect != e || !f.IsPowered) continue;
            int dx = Mathf.Max(0, Mathf.Max(f.FootprintMin.x - tile.FootprintMax.x, tile.FootprintMin.x - f.FootprintMax.x));
            int dy = Mathf.Max(0, Mathf.Max(f.FootprintMin.y - tile.FootprintMax.y, tile.FootprintMin.y - f.FootprintMax.y));
            if (dx + dy <= f.effectRadius) return true;
        }
        return false;
    }

    public static int CountPowered(CivEffect e)
    {
        int n = 0;
        foreach (var f in placed)
            if (f != null && f.effect == e && f.IsPowered) n++;
        return n;
    }

    public static void CollectPowered(CivEffect e, List<CivFacility> list)
    {
        list.Clear();
        foreach (var f in placed)
            if (f != null && f.effect == e && f.IsPowered) list.Add(f);
    }

    public static float WorkSpeedFor(TileItem tile) => Covers(CivEffect.Factory, tile) ? FactorySpeed : 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Init() => placed.Clear();
}
