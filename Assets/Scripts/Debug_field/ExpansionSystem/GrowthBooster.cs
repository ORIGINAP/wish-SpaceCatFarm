using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GrowthBooster : TileItem, ICoverageArea
{
    [Header("성장 촉진")]
    [Tooltip("효과 범위 (맨해튼 칸)")]
    [SerializeField, Min(1)] private int   boostRadius      = 3;
    [Tooltip("성장 속도 배율 (1.5 = 50% 빨리)")]
    [SerializeField, Min(1f)] private float growthMultiplier = 1.5f;

    [Header("모양")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite offSprite;
    [SerializeField] private Sprite onSprite;

    [Header("작업 범위 표시 (마우스 호버 시)")]
    [SerializeField] private Color coverageColor = new Color(0.45f, 1f, 0.55f, 0.12f);

    [Header("실시간 상태")]
    [SerializeField] private bool isPowered;

    public int   CoverageRadiusCells => boostRadius;
    public Color CoverageColor       => coverageColor;
    public bool  IsPowered           => isPowered;

    public override string AbilityText => $"전기가 들어오면 {boostRadius}칸 안의 작물이 {growthMultiplier:0.#}배 빨리 자라요";

    private static readonly List<GrowthBooster> placed = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => placed.Clear();

    private void Start()
    {
        ApplyVisual();
        if (IsGhost) return;
        placed.Add(this);
        StartCoroutine(PowerLoop());
        PowerLink.Attach(this, PowerLink.Kind.Consumer);
    }

    private void OnDestroy() => placed.Remove(this);

    private IEnumerator PowerLoop()
    {
        var wait = new WaitForSeconds(1f);
        while (true)
        {
            bool powered = EnergyManager.Instance != null && EnergyManager.Instance.HasEnergy(GridPosition);
            if (powered != isPowered)
            {
                isPowered = powered;
                ApplyVisual();
                EnergyManager.Instance?.RefreshEnergyNetwork();
            }
            yield return wait;
        }
    }

    private void ApplyVisual()
    {
        if (spriteRenderer == null) return;
        var sprite = isPowered ? onSprite : offSprite;
        if (sprite != null) spriteRenderer.sprite = sprite;
    }

    public static float MultiplierAt(Vector2Int cell)
    {
        float best = 1f;
        foreach (var b in placed)
        {
            if (b == null || !b.isPowered || b.growthMultiplier <= best) continue;
            int dx = Mathf.Max(Mathf.Max(b.FootprintMin.x - cell.x, 0), cell.x - b.FootprintMax.x);
            int dy = Mathf.Max(Mathf.Max(b.FootprintMin.y - cell.y, 0), cell.y - b.FootprintMax.y);
            if (dx + dy <= b.boostRadius) best = b.growthMultiplier;
        }
        return best;
    }
}
