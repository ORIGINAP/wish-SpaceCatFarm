using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Composter : TileItem
{
    [Header("퇴비")]
    [Tooltip("만든 퇴비 아이템")]
    [SerializeField] private FarmingItemData compostItem;
    [Tooltip("퇴비 1개에 드는 점수")]
    [SerializeField, Min(1)] private int pointsPerCompost = 10;
    [Tooltip("퇴비 1개를 만드는 시간 (초)")]
    [SerializeField, Min(0.1f)] private float secondsPerCompost = 6f;
    [Tooltip("담을 수 있는 점수")]
    [SerializeField, Min(1)] private int pointCapacity = 300;
    [Tooltip("쌓아 둘 수 있는 퇴비 수")]
    [SerializeField, Min(1)] private int outputCapacity = 60;

    [Header("연결 창고")]
    [SerializeField, Min(0.2f)] private float transferInterval = 3f;
    [Tooltip("창고에서 씨앗을 가져올 때 종류마다 남겨 두는 수")]
    [SerializeField, Min(0)] private int seedReserve = 20;

    [Header("모양")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite idleSprite;
    [SerializeField] private Sprite workingSprite;

    private static readonly Color CompostBeamColor = new Color(0.65f, 0.48f, 0.3f, 1f);

    private int   points;
    private int   outputCount;
    private float progress;
    private bool  shownWorking;
    private FlashBeam flashBeam;
    private static readonly List<FarmingItemData> stockBuffer = new();

    public override string AbilityText =>
        $"남는 씨앗 · 작물을 넣으면 {secondsPerCompost:0.#}초마다 퇴비가 돼요 (전기 필요 없음)\n창고와 이으면 넘치는 씨앗만 가져와 퇴비로 만들어요";

    public event System.Action OnChanged;

    public FarmingItemData CompostItem    => compostItem;
    public int             Points         => points;
    public int             PointCapacity  => pointCapacity;
    public int             PointsPerCompost => pointsPerCompost;
    public int             OutputCount    => outputCount;
    public int             OutputCapacity => outputCapacity;
    public float           Progress       => progress;
    public bool            IsWorking      => points >= pointsPerCompost && outputCount < outputCapacity;
    public float           SecondsPerCompost => secondsPerCompost / (CivFacility.WorkSpeedFor(this) * TechTreeManager.ProcessSpeed);
    public float           Progress01     => Mathf.Clamp01(progress / SecondsPerCompost);

    public static int PointsOf(FarmingItemData item)
    {
        if (item == null || item.isGolden) return 0;
        if (item.itemType == FarmingItemType.Seed && item.seedData != null) return 1;
        if (item.isCropProduct && IsRawCrop(item)) return Mathf.Clamp(Mathf.RoundToInt(item.basePrice / 5f), 2, 12);
        return 0;
    }

    private static HashSet<FarmingItemData> rawCrops;

    private static bool IsRawCrop(FarmingItemData item)
    {
        if (rawCrops == null)
        {
            rawCrops = new HashSet<FarmingItemData>();
            foreach (var s in GameDataRegistry.FindAll<SeedData>())
                if (s != null && s.drops != null)
                    foreach (var d in s.drops)
                        if (d.item != null && d.item.isCropProduct) rawCrops.Add(d.item);
        }
        return rawCrops.Contains(item);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => rawCrops = null;

    private void Start()
    {
        ApplyLook(false);
        if (IsGhost) return;
        flashBeam = FlashBeam.Attach(transform, FlashBeam.Kind.Generic);
        if (!NetSession.IsReplica) StartCoroutine(TransferLoop());
        LinkRules.HintOnPlace(this);
    }

    private void Update()
    {
        if (IsGhost) return;
        if (!NetSession.IsReplica && IsWorking)
        {
            progress += Time.deltaTime;
            if (progress >= SecondsPerCompost)
            {
                progress = 0f;
                points  -= pointsPerCompost;
                outputCount++;
                CraftingManager.Instance?.NotifyMachineCrafted(compostItem, 1);
                OnChanged?.Invoke();
            }
        }
        ApplyLook(IsWorking);
    }

    public int RoomFor(FarmingItemData item)
    {
        int p = PointsOf(item);
        return p > 0 ? Mathf.Max(0, (pointCapacity - points) / p) : 0;
    }

    public int Insert(FarmingItemData item, int amount)
    {
        int n = Mathf.Min(amount, RoomFor(item));
        if (n <= 0) return 0;
        points += n * PointsOf(item);
        OnChanged?.Invoke();
        NetOps.Tile(NetOpType.CompostInsert, this, item, n);
        return n;
    }

    public int TakeOutput(int amount)
    {
        int n = Mathf.Min(amount, outputCount);
        if (n <= 0 || compostItem == null) return 0;
        outputCount -= n;
        OnChanged?.Invoke();
        NetOps.Tile(NetOpType.CompostTakeOutput, this, compostItem, n);
        return n;
    }

    public void RestoreFromSave(int savedPoints, int savedOutput, float savedProgress)
    {
        points      = Mathf.Clamp(savedPoints, 0, pointCapacity);
        outputCount = Mathf.Clamp(savedOutput, 0, outputCapacity);
        progress    = Mathf.Max(0f, savedProgress);
        OnChanged?.Invoke();
    }

    protected internal override IEnumerable<(FarmingItemData item, int amount)> GetAttachedDrops()
    {
        if (compostItem != null && outputCount > 0) yield return (compostItem, outputCount);
    }

    private IEnumerator TransferLoop()
    {
        var wait = new WaitForSeconds(transferInterval);
        while (true)
        {
            yield return wait;
            Push();
            Pull();
        }
    }

    private void Push()
    {
        for (int i = 0; i < Links.Count && outputCount > 0; i++)
        {
            if (Links[i] is not ItemStorage storage || !storage.Accepts(compostItem)) continue;
            int added = storage.AddItem(compostItem, outputCount);
            if (added <= 0) continue;
            TakeOutput(added);
            flashBeam?.Flash(transform.position, storage.transform.position, CompostBeamColor, 0.05f, 0.4f);
        }
    }

    private void Pull()
    {
        for (int i = 0; i < Links.Count; i++)
        {
            if (Links[i] is not ItemStorage storage) continue;
            stockBuffer.Clear();
            stockBuffer.AddRange(storage.StockedItems);
            foreach (var item in stockBuffer)
            {
                if (item == null || item.itemType != FarmingItemType.Seed) continue;
                int spare = storage.GetCount(item) - seedReserve;
                int want  = Mathf.Min(spare, RoomFor(item));
                if (want <= 0) continue;
                int taken = storage.RemoveItem(item, want);
                if (taken <= 0) continue;
                int put = Insert(item, taken);
                if (put < taken) storage.AddItem(item, taken - put);
                if (put > 0) flashBeam?.Flash(storage.transform.position, transform.position, CompostBeamColor, 0.05f, 0.4f);
            }
        }
    }

    private void ApplyLook(bool working)
    {
        if (spriteRenderer == null || (working == shownWorking && spriteRenderer.sprite != null)) return;
        shownWorking = working;
        var sprite = working ? workingSprite : idleSprite;
        if (sprite != null) spriteRenderer.sprite = sprite;
    }
}
