using UnityEngine;
using UnityEngine.Serialization;
using System.Collections;
using System.Collections.Generic;

public class PlantTile : TileItem
{
    public enum PlantState { Empty, Seeded, Growing, ReadyToHarvest }
    public PlantState CurrentState { get; private set; } = PlantState.Empty;

    public override string AbilityText => "씨앗을 심는 밭 — 전기가 들어오면 작물이 자라요 (퇴비 · 물을 주면 더 좋아요)";

    public static event System.Action<PlantTile, HarvestResult[]> OnAnyPlantHarvested;

    public static event System.Action<PlantTile, SeedData> OnAnySeedPlanted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnAnyPlantHarvested = null;
        OnAnySeedPlanted = null;
    }

    public const int MaxFertility = 9;

    private const float FallbackStageTime    = 10f;
    private const float FactorRefreshSeconds = 1f;

    [Header("Growth Settings")]
    [SerializeField] private float defaultGrowthTime = 30f;

    [Header("Visual - Renderers")]
    [SerializeField] private SpriteRenderer baseRenderer;
    [SerializeField] private SpriteRenderer cropRenderer;

    [Header("Visual - Base Sprites")]
    [SerializeField] private Sprite emptySprite;
    [SerializeField] private Sprite poweredBaseSprite;

    [Header("Visual - Seed Sprites")]
    [FormerlySerializedAs("beforeEnergySprite")]
    [SerializeField] private Sprite defaultSeedSprite;
    [SerializeField] private Sprite defaultHarvestSprite;

    [Header("Visual - Power Tint")]
    [SerializeField] private Color poweredColor   = Color.white;
    [SerializeField] private Color unpoweredColor = new Color(0.7f, 0.7f, 0.75f, 1f);

    private static readonly Color WetTint     = new Color(0.78f, 0.86f, 1f, 1f);
    private static readonly Color FertileTint = new Color(0.86f, 0.74f, 0.62f, 1f);

    [Header("우주 폭풍 파괴 연출")]
    [Tooltip("파괴 전 흔들리는 연출 지속 시간(초)")]
    [SerializeField] private float stormShakeDuration = 1f;

    private SeedData      currentSeed;
    private Coroutine     growthCoroutine;
    private bool          isPowered;

    private int           currentStage;
    private float         stageTimer;
    private float         growthMultiplier = 1f;
    private float         nextFactorRefresh;
    private int           fertility;
    private int           plantSerial;
    private bool          shownWet;

    private Coroutine     stormDestroyCoroutine;

    public bool CanGrow => isPowered || (currentSeed != null && currentSeed.trait == CropTrait.Shade);

    public bool  IsPowered        => isPowered;
    public int   Fertility        => fertility;
    public bool  IsFertile        => fertility > 0;
    public float GrowthMultiplier => growthMultiplier;

    public float Progress01
    {
        get
        {
            if (CurrentState == PlantState.ReadyToHarvest) return 1f;
            if (currentSeed == null) return 0f;
            float total = TotalSeconds();
            return total > 0f ? Mathf.Clamp01(ElapsedSeconds() / total) : 0f;
        }
    }

    public float RemainingSeconds
    {
        get
        {
            if (currentSeed == null || CurrentState == PlantState.ReadyToHarvest) return 0f;
            if (!CanGrow) return -1f;
            float left = Mathf.Max(0f, TotalSeconds() - ElapsedSeconds());
            return left / Mathf.Max(0.01f, growthMultiplier);
        }
    }

    public bool PlantSeed(SeedData seed)
    {
        if (CurrentState != PlantState.Empty || seed == null) return false;

        currentSeed  = seed;
        CurrentState = PlantState.Seeded;
        currentStage = 0;
        stageTimer   = 0f;
        plantSerial++;
        RefreshFactors();

        if (CanGrow)
            StartGrowth();
        else
            UpdateVisual();

        NetOps.Plant(this, seed);
        OnAnySeedPlanted?.Invoke(this, seed);
        return true;
    }

    public bool Fertilize(FarmingItemData fertilizer)
    {
        if (fertilizer == null || !fertilizer.IsFertilizer || fertility >= MaxFertility) return false;
        fertility = Mathf.Min(MaxFertility, fertility + fertilizer.fertilizerCharges);
        UpdateVisual();
        NetOps.Tile(NetOpType.Fertilize, this, fertilizer, 1);
        return true;
    }

    public HarvestResult[] Harvest(bool remote = false)
    {
        if (CurrentState != PlantState.ReadyToHarvest) return null;

        var results = new List<HarvestResult>();
        bool fertile = fertility > 0;
        FarmingItemData golden = null;
        if (currentSeed == null || currentSeed.drops == null || currentSeed.drops.Length == 0)
        {
            UnityEngine.Debug.LogWarning($"[PlantTile @ {GridPosition}] 씨앗 데이터 또는 drops가 비어 있어 수확물 없이 밭을 비웁니다.");
        }
        else
        {
            FarmingItemData product = null;
            foreach (var drop in currentSeed.drops)
            {
                if (drop.item == null) continue;
                if (drop.item.isCropProduct && product == null) product = drop.item;
                if (drop.dropChance < 1f && UnityEngine.Random.value > drop.dropChance) continue;
                AddResult(results, drop.item, drop.amount);
            }

            // 참가자 수확은 그쪽에서 이미 굴림
            if (product != null && !remote)
            {
                int extra = FarmConditions.RollExtraCrops(this, fertile);
                if (extra > 0) AddResult(results, product, extra);
                if (product.goldenVariant != null && UnityEngine.Random.value < FarmConditions.GoldenChance(this, fertile))
                {
                    golden = product.goldenVariant;
                    AddResult(results, golden, 1);
                    Codex.RecordGolden(golden);
                }
            }
        }

        if (fertile) fertility--;
        var array = results.ToArray();
        HarvestPop.Play(cropRenderer);
        ResetPlant();
        NetOps.TileNamed(NetOpType.Harvest, this, NetOps.ItemsKey(results));
        OnAnyPlantHarvested?.Invoke(this, array);
        if (golden != null) CelebrateGolden(golden);
        return array;
    }

    private static void AddResult(List<HarvestResult> results, FarmingItemData item, int amount)
    {
        var same = results.Find(r => r.item == item);
        if (same != null) same.amount += amount;
        else              results.Add(new HarvestResult(item, amount));
    }

    private void CelebrateGolden(FarmingItemData golden)
    {
        NotificationUI.Instance?.Show($"황금 작물! {ItemCellUI.Ga(golden.itemName)} 열렸어요 (판매가 {golden.basePrice:N0}코인)", NotificationUI.GoodNewsColor);
        Sfx.PlayAt(SfxId.QuestComplete, transform.position);
        GoldenSparkle.Play(transform.position);
    }

    public void OnEnergyChanged(bool hasEnergy)
    {
        isPowered = hasEnergy;

        if (CanGrow && CurrentState == PlantState.Seeded)
            StartGrowth();
        else if (!CanGrow && CurrentState == PlantState.Growing)
        {
            StopGrowth();
            CurrentState = PlantState.Seeded;
        }

        UpdateVisual();
    }

    public bool StormBlowAway()
    {
        if (currentSeed == null || CurrentState == PlantState.Empty) return false;
        if (currentSeed.trait == CropTrait.StormLover) return false;
        if (stormDestroyCoroutine != null) return false;
        stormDestroyCoroutine = StartCoroutine(StormBlowRoutine());
        return true;
    }

    private void RefreshFactors()
    {
        nextFactorRefresh = Time.time + FactorRefreshSeconds * UnityEngine.Random.Range(0.8f, 1.2f);
        growthMultiplier  = currentSeed != null ? FarmConditions.GrowthMultiplier(this) : 1f;
        bool wet = FarmConditions.IsWatered(GridPosition);
        if (wet != shownWet) UpdateVisual(wet);
    }

    private void StartGrowth()
    {
        CurrentState = PlantState.Growing;
        if (growthCoroutine != null) StopCoroutine(growthCoroutine);
        // 멀티: 성장은 호스트가 보내줌
        if (!NetSession.IsReplica) growthCoroutine = StartCoroutine(GrowthRoutine());
        UpdateVisual();
    }

    private void StopGrowth()
    {
        if (growthCoroutine == null) return;
        StopCoroutine(growthCoroutine);
        growthCoroutine = null;
    }

    private IEnumerator GrowthRoutine()
    {
        CropData crop = currentSeed?.cropData;

        if (crop != null && crop.StageCount > 1)
        {
            while (currentStage < crop.StageCount - 1)
            {
                float duration = StageDuration(crop, currentStage);
                while (stageTimer < duration)
                {
                    if (Time.time >= nextFactorRefresh) RefreshFactors();
                    stageTimer += Time.deltaTime * growthMultiplier;
                    yield return null;
                }
                currentStage++;
                stageTimer = 0f;
                UpdateVisual();
            }
        }
        else
        {
            float totalDuration = currentSeed != null ? currentSeed.growthTime : defaultGrowthTime;
            while (stageTimer < totalDuration)
            {
                if (Time.time >= nextFactorRefresh) RefreshFactors();
                stageTimer += Time.deltaTime * growthMultiplier;
                yield return null;
            }
        }

        CurrentState = PlantState.ReadyToHarvest;
        UpdateVisual();
    }

    private static float StageDuration(CropData crop, int stage)
        => crop.stageTimes != null && stage < crop.stageTimes.Length ? crop.stageTimes[stage] : FallbackStageTime;

    private float TotalSeconds()
    {
        var crop = currentSeed?.cropData;
        if (crop == null || crop.StageCount <= 1) return currentSeed != null ? currentSeed.growthTime : defaultGrowthTime;
        float total = 0f;
        for (int i = 0; i < crop.StageCount - 1; i++) total += StageDuration(crop, i);
        return total;
    }

    private float ElapsedSeconds()
    {
        var crop = currentSeed?.cropData;
        if (crop == null || crop.StageCount <= 1) return stageTimer;
        float done = 0f;
        for (int i = 0; i < currentStage && i < crop.StageCount - 1; i++) done += StageDuration(crop, i);
        return done + stageTimer;
    }

    public SeedData CurrentSeed  => currentSeed;
    public int      GrowthStage  => currentStage;
    public float    StageTimer   => stageTimer;

    public void RestoreFromSave(PlantState state, SeedData seed, int stage, float timer)
    {
        StopGrowth();

        if (seed == null && state != PlantState.Empty)
        {
            state = PlantState.Empty;
            stage = 0;
            timer = 0f;
        }

        currentSeed  = seed;
        CurrentState = state;
        currentStage = stage;
        stageTimer   = timer;
        RefreshFactors();

        if (state == PlantState.Growing && !NetSession.IsReplica)
            StartCoroutine(ResumeGrowth());

        UpdateVisual();
    }

    public void RestoreFertility(int value)
    {
        int v = Mathf.Clamp(value, 0, MaxFertility);
        if (v == fertility) return;
        fertility = v;
        UpdateVisual();
    }

    private System.Collections.IEnumerator ResumeGrowth()
    {
        yield return null;
        if (CurrentState != PlantState.Growing || growthCoroutine != null) yield break;
        if (CanGrow) StartGrowth();
        else CurrentState = PlantState.Seeded;
    }

    private void ResetPlant()
    {
        StopGrowth();
        currentSeed  = null;
        CurrentState = PlantState.Empty;
        currentStage = 0;
        stageTimer   = 0f;
        growthMultiplier = 1f;
        UpdateVisual();
    }

    private void UpdateVisual() => UpdateVisual(FarmConditions.IsWatered(GridPosition));

    private void UpdateVisual(bool wet)
    {
        Color tint = isPowered ? poweredColor : unpoweredColor;
        shownWet = wet;

        if (baseRenderer != null)
        {
            Color soil = tint;
            if (shownWet)      soil *= WetTint;
            if (fertility > 0) soil *= FertileTint;
            baseRenderer.sprite = isPowered ? poweredBaseSprite : emptySprite;
            baseRenderer.color  = soil;
        }

        if (cropRenderer != null)
        {
            CropData crop = currentSeed?.cropData;

            if (CurrentState == PlantState.Empty)
            {
                cropRenderer.sprite = null;
            }
            else if (CurrentState == PlantState.ReadyToHarvest)
            {
                cropRenderer.sprite = crop?.HarvestSprite ?? defaultHarvestSprite;
            }
            else
            {
                cropRenderer.sprite = crop != null
                    ? crop.GetSprite(currentStage)
                    : defaultSeedSprite;
            }

            cropRenderer.color = CanGrow ? poweredColor : tint;
            SetRipeBob(CurrentState == PlantState.ReadyToHarvest);
        }
    }

    private Coroutine ripeCo;
    private Vector3   cropBaseScale = Vector3.one;
    private bool      cropScaleKnown;

    private void SetRipeBob(bool on)
    {
        if (cropRenderer == null || cropRenderer.transform == transform) return;
        if (!cropScaleKnown) { cropBaseScale = cropRenderer.transform.localScale; cropScaleKnown = true; }
        if (on)
        {
            if (ripeCo == null && isActiveAndEnabled && !IsGhost) ripeCo = StartCoroutine(RipeBob());
            return;
        }
        if (ripeCo != null) { StopCoroutine(ripeCo); ripeCo = null; }
        cropRenderer.transform.localScale = cropBaseScale;
    }

    private IEnumerator RipeBob()
    {
        float phase = GridPosition.x * 0.9f + GridPosition.y * 1.7f;
        while (true)
        {
            float k = Mathf.Sin(Time.time * 3.4f + phase);
            cropRenderer.transform.localScale = new Vector3(cropBaseScale.x * (1f - 0.03f * k), cropBaseScale.y * (1f + 0.06f * k), cropBaseScale.z);
            yield return null;
        }
    }

    public override void DestroyByStorm() => StormBlowAway();

    private IEnumerator StormBlowRoutine()
    {
        int     serialAtStart = plantSerial;
        Vector3 originalPos   = transform.position;
        const float shakeInterval  = 0.08f;
        const float shakeMagnitude = 0.03f;
        float nextShakeTime  = Time.time;
        float elapsed        = 0f;

        while (elapsed < stormShakeDuration)
        {
            elapsed += Time.deltaTime;

            if (Time.time >= nextShakeTime)
            {
                transform.position = originalPos + (Vector3)(UnityEngine.Random.insideUnitCircle * shakeMagnitude);
                nextShakeTime = Time.time + shakeInterval;
            }

            yield return null;
        }

        transform.position = originalPos;
        stormDestroyCoroutine = null;
        if (CurrentState != PlantState.Empty && plantSerial == serialAtStart) ResetPlant();
    }
}
