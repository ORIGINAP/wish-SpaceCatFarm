using UnityEngine;
using System.Collections;

public class ResourceExtractor : TileItem
{
    [Header("채굴기 설정")]
    [SerializeField] private float extractInterval = 15f;
    [SerializeField] private int   extractAmount   = 1;
    [SerializeField] private float searchRadius    = 2f;

    [Header("채굴 범위 표시")]
    [SerializeField] private Color rangeOutlineColor = new Color(1f, 1f, 1f, 0.35f);
    [SerializeField] [Range(0.01f, 0.2f)] private float rangeOutlineWidth = 0.025f;

    [Header("실시간 상태")]
    [SerializeField] private bool  isConnected;
    [SerializeField] private float nextExtractAt;
    [SerializeField] private string connectedBlockName;

    [Header("장착 드릴 (ResourceExtractorUI로 장착)")]
    [SerializeField] private FarmingItemData equippedDrill;

    private ResourceBlock targetBlock;
    private Coroutine     extractCoroutine;
    private FlashBeam     flashBeam;

    public FarmingItemData EquippedDrill => equippedDrill;

    public int EffectiveToolGrade => equippedDrill != null ? equippedDrill.toolGrade : 1;

    public override string AbilityText =>
        $"{extractInterval:0.#}초마다 곁의 광석을 {extractAmount}개씩 캐요 (범위 {SearchRadiusCells}칸)\n드릴을 끼우면 드릴 능력만큼 더 빠르고 많이 캐요";
    public override float WorkRate => extractInterval > 0f ? extractAmount / extractInterval : 0f;

    private float EffectiveInterval => ((equippedDrill != null && equippedDrill.miningSpeed > 0f)
        ? extractInterval / equippedDrill.miningSpeed
        : extractInterval) / TechTreeManager.MachineSpeed;

    private int EffectiveAmount => equippedDrill != null
        ? Mathf.Max(1, Mathf.RoundToInt(extractAmount * equippedDrill.miningPower))
        : extractAmount;

    public bool TryEquip(FarmingItemData drill)
    {
        if (drill == null || !drill.IsDrill) return false;
        equippedDrill = drill;
        FindTargetBlock();
        NetOps.Tile(NetOpType.DrillEquip, this, drill);
        return true;
    }

    public FarmingItemData Unequip()
    {
        var prev = equippedDrill;
        equippedDrill = null;
        FindTargetBlock();
        if (prev != null) NetOps.Tile(NetOpType.DrillUnequip, this, prev);
        return prev;
    }

    protected internal override System.Collections.Generic.IEnumerable<(FarmingItemData item, int amount)> GetAttachedDrops()
    {
        if (equippedDrill != null) yield return (equippedDrill, 1);
    }

    private void Start()
    {
        if (IsGhost) return;
        FindTargetBlock();

        WorldCircleOutline.Create(transform, searchRadius, rangeOutlineColor, rangeOutlineWidth);
        flashBeam = FlashBeam.Attach(transform, FlashBeam.Kind.Mining);
        LinkRules.HintOnPlace(this);

        if (targetBlock == null && !SaveManager.RecentlyLoaded)
            NotificationUI.Instance?.Show("채굴기: 근처에 자원 블록이 없습니다. 범위 안에 블록이 생기면 채굴을 시작합니다.");
        // 멀티: 채굴은 호스트만
        if (!NetSession.IsReplica) extractCoroutine = StartCoroutine(ExtractLoop());
    }

    private void OnDestroy()
    {
        if (extractCoroutine != null) StopCoroutine(extractCoroutine);
    }

    private void FindTargetBlock()
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, searchRadius);
        ResourceBlock best = null, fallback = null;
        float bestD = float.MaxValue, fallbackD = float.MaxValue;
        foreach (var hit in hits)
        {
            var block = hit.GetComponent<ResourceBlock>();
            if (block == null || block.RemainingResources <= 0) continue;

            float d = (block.OriginPosition - transform.position).sqrMagnitude;
            if (d < fallbackD) { fallbackD = d; fallback = block; }

            if (block.Data != null && block.Data.minToolGrade <= EffectiveToolGrade && d < bestD)
            {
                bestD = d;
                best  = block;
            }
        }

        targetBlock = best != null ? best : fallback;
        isConnected = targetBlock != null;
        if (isConnected) connectedBlockName = targetBlock.name;
    }

    private bool HasRoomFor(FarmingItemData item)
    {
        if (item == null) return false;
        for (int i = 0; i < Links.Count; i++)
            if (Links[i] is ItemStorage storage && storage.Accepts(item) && storage.HasSpace)
                return true;
        return Inventory.Instance != null && Inventory.Instance.CanAddItem(item);
    }

    private IEnumerator ExtractLoop()
    {
        while (true)
        {
            float interval = EffectiveInterval;
            nextExtractAt  = Time.time + interval;
            yield return new WaitForSeconds(interval);

            if (targetBlock == null)
            {
                FindTargetBlock();
                if (targetBlock == null)
                {
                    connectedBlockName = "없음";
                    continue;
                }
            }

            if (targetBlock.Data != null && targetBlock.Data.minToolGrade > EffectiveToolGrade)
                continue;

            // 넣을 곳 없으면 이번 주기 쉼
            if (!HasRoomFor(targetBlock.DropItem))
            {
                connectedBlockName = "보관 공간 없음";
                continue;
            }

            Vector3 blockPos = targetBlock.transform.position;
            var (item, amount) = targetBlock.ExtractResources(EffectiveAmount);
            if (item == null || amount <= 0) continue;

            flashBeam?.Fire(transform.position, blockPos, FlashBeam.Kind.Mining);

            int remaining = TrySupplyToStorage(item, amount);

            if (remaining > 0)
                Inventory.Instance?.AddItemOrDrop(item, remaining, transform.position + Vector3.down * 0.4f);
        }
    }

    private int TrySupplyToStorage(FarmingItemData item, int amount)
    {
        for (int i = 0; i < Links.Count && amount > 0; i++)
            if (Links[i] is ItemStorage storage && storage.Accepts(item))
                amount -= storage.AddItem(item, amount);
        return amount;
    }

    public int SearchRadiusCells => SearchRadiusInCells();

    private int SearchRadiusInCells()
    {
        float cellSize = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
        return Mathf.Max(1, Mathf.CeilToInt(searchRadius / cellSize));
    }

    private void Update()
    {
        if (IsGhost) return;

        if (targetBlock == null && isConnected)
        {
            isConnected      = false;
            connectedBlockName = "소진됨";
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        UnityEditor.Handles.color = Color.cyan;
        UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.forward, searchRadius);
    }
#endif
}
