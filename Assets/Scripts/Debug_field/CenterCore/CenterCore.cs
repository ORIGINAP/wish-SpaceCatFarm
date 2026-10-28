using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 중앙 고정, 해체 불가
public class CenterCore : Generator
{
    public const int Size = 4;

    private const string PrefabPath = "CentralPlant";

    [Header("문명 단계")]
    [Tooltip("비워 두면 Resources/CoreStages 단계표를 씁니다")]
    [SerializeField] private CoreStageTable stageTable;
    [Tooltip("다음 단계 조건을 확인하는 주기 (초)")]
    [SerializeField, Min(0.1f)] private float checkInterval = 1f;
    [Tooltip("새로 해금된 씨앗을 선물로 주는 개수")]
    [SerializeField, Min(0)] private int seedGiftAmount = 5;

    public CoreStageTable Stages => stageTable != null ? stageTable : (stageTable = CoreStageTable.Load());

    public int  Level      { get; private set; } = 1;
    public int  MaxLevel   => Stages != null ? Stages.MaxLevel : 1;
    public bool IsMaxLevel => Level >= MaxLevel;

    public CoreStage CurrentStage => Stages?.Stage(Level);
    public CoreStage NextStage    => IsMaxLevel ? null : Stages?.Stage(Level + 1);

    public override float MaxFuel => CurrentStage != null ? CurrentStage.maxFuel : base.MaxFuel;

    // 연료 없이 항상 가동
    public override bool IsActive => true;
    public override bool UsesFuel => false;

    public static int CurrentLevel => Current != null ? Current.Level : 1;

    public event System.Action OnChanged;

    public event System.Action<int> OnLevelUp;

    public static event System.Action<CenterCore, int> OnAnyLevelUp;

    // 도메인 리로드 없이 재시작 대비
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => OnAnyLevelUp = null;

    private float     nextCheckTime;
    private Coroutine popCo;

    protected override void Start()
    {
        base.Start();
        ApplyStageLook();
    }

    private void OnDestroy() => Services.Unregister(this);

    protected override void Update()
    {
        base.Update();
        if (IsGhost || NetSession.IsReplica || Time.time < nextCheckTime) return;
        nextCheckTime = Time.time + checkInterval;
        TryAdvance();
    }

    public bool TryAdvance()
    {
        var next = NextStage;
        if (IsMaxLevel || !CoreProgress.AllMet(next)) return false;
        long funds = CoreProgress.Funds(next);
        if (funds > 0 && (CurrencyManager.Instance == null || !CurrencyManager.Instance.TrySpend(funds))) return false;
        SetLevel(Level + 1, celebrate: true);
        if (funds > 0) NotificationUI.Instance?.Show($"발전 자금 {CurrencyManager.Format(funds)}코인을 냈어요", NotificationUI.GoodNewsColor);
        return true;
    }

    public void ForceAdvance()
    {
        if (!IsMaxLevel) SetLevel(Level + 1, celebrate: true);
    }

    // 단계 먼저, 연료통 크기가 달라짐
    public void RestoreState(int level, float fuel)
    {
        SetLevel(level, celebrate: false);
        RestoreFromSave(fuel);
    }

    // 멀티: 참가자는 호스트 값으로
    public void SyncFromHost(int level, float fuel)
    {
        if (level != Level) SetLevel(level, celebrate: level == Level + 1);
        if (!Mathf.Approximately(CurrentFuel, fuel)) RestoreFromSave(fuel);
    }

    public static List<CraftingRecipe> RecipesUnlockedAt(int level)
    {
        var list = new List<CraftingRecipe>();
        var cm = CraftingManager.Instance;
        if (cm == null) return list;
        foreach (var r in cm.Recipes)
            if (r != null && r.outputItem != null && r.requiredCoreLevel == level) list.Add(r);
        return list;
    }

    public static List<FarmingItemData> ItemsUnlockedAt(int level)
    {
        var list = new List<FarmingItemData>();
        foreach (var item in GameDataRegistry.FindAll<FarmingItemData>())
            if (item != null && item.unlockCoreLevel == level) list.Add(item);
        list.Sort((a, b) => string.CompareOrdinal(a.itemName, b.itemName));
        return list;
    }

    public void MoveOutside(Transform t)
    {
        if (t == null || DistanceFrom(t.position) > 0f || GridManager.Instance == null) return;
        var gm = GridManager.Instance;
        Vector3 bottom = gm.GridToWorldCenter(GridPosition, Size, 1) + Vector3.down * gm.CellSize * 1.4f;
        t.position = new Vector3(transform.position.x, bottom.y, t.position.z);
    }

    private void SetLevel(int level, bool celebrate)
    {
        Level = Mathf.Clamp(level, 1, MaxLevel);
        ApplyStageLook();
        EnergyManager.Instance?.RefreshEnergyNetwork();
        OnChanged?.Invoke();
        if (!celebrate) return;

        OnLevelUp?.Invoke(Level);
        OnAnyLevelUp?.Invoke(this, Level);
        Celebrate();
    }

    private void ApplyStageLook()
    {
        var stage = CurrentStage;
        if (stage != null) SetLookSprites(stage.spriteOn, stage.spriteOff);
        if (TryGetComponent<SpriteRenderer>(out var sr)) sr.sortingOrder = 1;
    }

    private void Celebrate()
    {
        var stage = CurrentStage;
        Sfx.PlayAt(SfxId.CoreLevelUp, transform.position);
        CameraShake.GetOrCreate().Shake(0.6f, 0.35f, 0f);
        Vector3 center = transform.position + Vector3.up * 1.2f;
        LabFx.Burst(center, CuteUIKit.Butter, 28, 4.5f, 1.1f);
        LabFx.Burst(center, CuteUIKit.Strawberry, 16, 3f, 0.9f);
        LabFx.Ring(center, CuteUIKit.Butter, 3.2f, 0.9f);
        if (popCo != null) StopCoroutine(popCo);
        popCo = StartCoroutine(Pop());

        var notify = NotificationUI.Instance;
        notify?.Show($"센터코어가 Lv.{Level} {stage?.title} 단계로 발전했어요!", NotificationUI.GoodNewsColor);
        bool listed = LevelUpUI.IsOpen;

        var recipes = RecipesUnlockedAt(Level);
        if (recipes.Count > 0 && !listed)
            notify?.Show($"새로 만들 수 있어요: {string.Join(", ", recipes.ConvertAll(r => r.outputItem.itemName))}", NotificationUI.GoodNewsColor);

        var items = ItemsUnlockedAt(Level);
        if (items.Count > 0)
        {
            var player = PlayerTileController.Instance;
            Vector3 drop = player != null ? player.transform.position : transform.position;
            var names = new List<string>();
            foreach (var item in items)
            {
                if (item.itemType == FarmingItemType.Seed && seedGiftAmount > 0 && Inventory.Instance != null)
                    Inventory.Instance.AddItemOrDrop(item, seedGiftAmount, drop);
                names.Add(item.itemName);
            }
            if (!listed) notify?.Show($"새로 해금: {string.Join(", ", names)} (상점에서 살 수 있어요)", NotificationUI.GoodNewsColor);
        }
    }

    private IEnumerator Pop()
    {
        const float duration = 0.6f;
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float k = t / duration;
            float s = 1f + Mathf.Sin(k * Mathf.PI) * 0.12f * (1f - k * 0.5f);
            transform.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        transform.localScale = Vector3.one;
        popCo = null;
    }

    public static CenterCore Current => Services.Get<CenterCore>();

    public static Vector2Int CenterAnchor => new(-Size / 2, -Size / 2);

    public static CenterCore Spawn(Vector2Int anchor)
    {
        var gm     = GridManager.Instance;
        var prefab = Resources.Load<GameObject>(PrefabPath);
        if (gm == null || prefab == null)
        {
            Debug.LogError($"[CenterCore] 중앙 발전소를 세울 수 없습니다 (GridManager: {gm != null}, 프리팹 Resources/{PrefabPath}: {prefab != null}).");
            return null;
        }

        Despawn();
        if (!gm.CanPlaceAt(anchor, Size, Size)) anchor = FindFreeAnchorNearCenter();

        var go   = Instantiate(prefab, gm.GridToWorldCenter(anchor, Size, Size), Quaternion.identity);
        go.name  = "CentralPlant";
        var core = go.GetComponent<CenterCore>();
        core.Initialize(anchor, gm.GetOccupiedCells(anchor, Size, Size).ToArray(), 0);
        gm.RegisterExisting(core);
        EnergyManager.Instance?.Register(core);
        Services.Replace(core);
        core.ApplyStageLook();
        CoreProgressHUD.Ensure();
        return core;
    }

    public static void Despawn()
    {
        if (Current == null) return;
        var gm = GridManager.Instance;
        if (gm != null && gm.GetTile(Current.GridPosition) == Current) gm.RemoveTile(Current.GridPosition);
        else Destroy(Current.gameObject);
        Services.Unregister(Current);
    }

    // 구버전 세이브 옮길 때 사용
    public static Vector2Int FindFreeAnchorNearCenter(int maxRing = 40)
    {
        var gm     = GridManager.Instance;
        var center = CenterAnchor;
        if (gm == null) return center;

        Physics2D.SyncTransforms();
        for (int r = 0; r <= maxRing; r++)
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        {
            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r) continue;
            var a = new Vector2Int(center.x + dx, center.y + dy);
            if (gm.CanPlaceAt(a, Size, Size)) return a;
        }
        return center;
    }
}
