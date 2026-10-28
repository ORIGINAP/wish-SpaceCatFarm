using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(CircleCollider2D))]
public class ResourceBlock : MonoBehaviour
{
    public static event System.Action<FarmingItemData, int> OnAnyResourceMined;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnAnyResourceMined = null;
        active.Clear();
        cells.Clear();
        lastToolHintTime = -10f;
    }

    [Header("데이터")]
    [SerializeField] private ResourceData data;

    [Header("실시간 상태")]
    [SerializeField] private int   remainingResources;
    [SerializeField] private float resourcePercent;
    [SerializeField] private bool  isMiningNow;

    private SpriteRenderer sr;
    private Transform      playerTransform;
    private bool           isMining   = false;
    private float          lastDropTime = float.NegativeInfinity;
    private Vector3        originPos;
    private Coroutine      shakeCoroutine;

    public const int BaseSortingOrder = 1;

    private const float HitRadius   = 0.5f;
    private const float MinHitScale = 0.7f;
    private CircleCollider2D hitCollider;

    private static readonly Rect SolidArea = new(13f / 64f, 11f / 64f, 38f / 64f, 11f / 64f);

    public Rect SolidRect { get; private set; }

    private static readonly List<ResourceBlock> active = new();

    public static IReadOnlyList<ResourceBlock> Active => active;

    private void Awake()
    {
        active.Add(this);
        // 광석 1000개 넘어서 Update 끔
        enabled = false;
    }

    private void OnDestroy()
    {
        active.Remove(this);
        RemoveFromCell();
    }

    // 근처 광석 조회용 공간 분할
    private const float CellWorldSize = 4f;

    public const float MaxReach = 1.5f;

    private static readonly Dictionary<Vector2Int, List<ResourceBlock>> cells = new();
    private Vector2Int cell;
    private bool       inCell;

    private static Vector2Int CellOf(float x, float y)
        => new(Mathf.FloorToInt(x / CellWorldSize), Mathf.FloorToInt(y / CellWorldSize));

    private void AddToCell()
    {
        RemoveFromCell();
        cell = CellOf(originPos.x, originPos.y);
        if (!cells.TryGetValue(cell, out var list)) cells[cell] = list = new List<ResourceBlock>(8);
        list.Add(this);
        inCell = true;
    }

    private void RemoveFromCell()
    {
        if (!inCell) return;
        inCell = false;
        if (!cells.TryGetValue(cell, out var list)) return;
        list.Remove(this);
        if (list.Count == 0) cells.Remove(cell);
    }

    public static void Near(Rect area, List<ResourceBlock> results)
    {
        results.Clear();
        Vector2Int a = CellOf(area.xMin - MaxReach, area.yMin - MaxReach);
        Vector2Int b = CellOf(area.xMax + MaxReach, area.yMax + MaxReach);
        for (int y = a.y; y <= b.y; y++)
        for (int x = a.x; x <= b.x; x++)
            if (cells.TryGetValue(new Vector2Int(x, y), out var list)) results.AddRange(list);
    }

    private static readonly Rect ArtArea = new(6f / 64f, 3f / 64f, 52f / 64f, 57f / 64f);
    private const float ArtWorldSize = 64f / 28.070175f;

    private static readonly List<ResourceBlock> nearby = new();

    public static Rect ArtRect(Vector2 center)
        => new(center + (ArtArea.position - new Vector2(0.5f, 0.5f)) * ArtWorldSize, ArtArea.size * ArtWorldSize);

    public static bool FitsAt(Vector2 center)
    {
        var gm = GridManager.Instance;
        if (gm == null) return true;
        if (!gm.IsInBounds(gm.WorldToGrid(center))) return false;

        var art = ArtRect(center);
        Vector2Int a = gm.WorldToGrid(new Vector3(art.xMin, art.yMin));
        Vector2Int b = gm.WorldToGrid(new Vector3(art.xMax, art.yMax));
        for (int y = a.y; y <= b.y; y++)
        for (int x = a.x; x <= b.x; x++)
            if (gm.GetTile(new Vector2Int(x, y)) != null) return false;

        var terrain = TerrainMap.Current;
        if (terrain == null) return true;
        a = gm.WorldToGrid(new Vector3(center.x - 1.2f, center.y - 1.2f));
        b = gm.WorldToGrid(new Vector3(center.x + 1.2f, center.y + 0.6f));
        for (int y = a.y; y <= b.y; y++)
        for (int x = a.x; x <= b.x; x++)
            if (terrain.IsCellBlocked(new Vector2Int(x, y))) return false;
        return true;
    }

    public bool BlocksCell(Rect cell) => (SolidRect.width > 0f && cell.Overlaps(SolidRect)) || HitCircleTouches(cell);

    private bool HitCircleTouches(Rect cell)
    {
        float r  = HitRadius * Mathf.Max(transform.localScale.x, MinHitScale);
        float dx = Mathf.Max(cell.xMin - originPos.x, 0f, originPos.x - cell.xMax);
        float dy = Mathf.Max(cell.yMin - originPos.y, 0f, originPos.y - cell.yMax);
        return dx * dx + dy * dy < r * r;
    }

    public static int RelocateOverlapping()
    {
        var gm = GridManager.Instance;
        if (gm == null) return 0;
        int moved = 0;
        foreach (var block in new List<ResourceBlock>(active))
        {
            if (block == null || !block.SitsOnTile(gm)) continue;
            if (!FindSpotNear(block.originPos, 6f, out var spot, block)) continue;
            block.MoveTo(spot);
            moved++;
        }
        return moved;
    }

    private bool SitsOnTile(GridManager gm)
    {
        float r  = HitRadius * Mathf.Max(transform.localScale.x, MinHitScale);
        float cs = gm.CellSize;
        Vector2Int a = gm.WorldToGrid(new Vector3(originPos.x - r, originPos.y - r));
        Vector2Int b = gm.WorldToGrid(new Vector3(originPos.x + r, originPos.y + r));
        for (int y = a.y; y <= b.y; y++)
        for (int x = a.x; x <= b.x; x++)
        {
            var cell = new Vector2Int(x, y);
            if (gm.GetTile(cell) == null) continue;
            Vector3 c = gm.GridToWorld(cell);
            if (HitCircleTouches(new Rect(c.x - cs * 0.5f, c.y - cs * 0.5f, cs, cs))) return true;
        }
        return false;
    }

    public static bool FindSpotNear(Vector2 from, float maxDistance, out Vector2 spot, ResourceBlock ignore = null)
    {
        const float step = 0.375f, minGap = 1f;
        for (float d = 0f; d <= maxDistance; d += step)
        {
            int dirs = d <= 0f ? 1 : Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * d / step));
            for (int i = 0; i < dirs; i++)
            {
                float ang = i * Mathf.PI * 2f / dirs;
                var p = from + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * d;
                if (!FitsAt(p)) continue;
                Near(new Rect(p.x - minGap, p.y - minGap, minGap * 2f, minGap * 2f), nearby);
                bool crowded = false;
                foreach (var other in nearby)
                    if (other != null && other != ignore && Vector2.Distance(other.originPos, p) < minGap) { crowded = true; break; }
                if (crowded) continue;
                spot = p;
                return true;
            }
        }
        spot = from;
        return false;
    }

    private void MoveTo(Vector2 pos)
    {
        if (shakeCoroutine != null) { StopCoroutine(shakeCoroutine); shakeCoroutine = null; }
        originPos          = new Vector3(pos.x, pos.y, originPos.z);
        transform.position = originPos;
        AddToCell();
        UpdateSolidRect(transform.localScale.x);
    }

    public void Initialize(ResourceData resourceData, Transform player)
    {
        data              = resourceData;
        playerTransform   = player;
        originPos         = transform.position;
        AddToCell();
        sr                = GetComponent<SpriteRenderer>();
        sr.sortingOrder   = BaseSortingOrder;
        if (data == null)
        {
            Debug.LogWarning("[ResourceBlock] ResourceData가 비어 있는 블록이 생성되었습니다.");
            return;
        }

        remainingResources = data.maxResources;
        sr.sprite = data.SpriteFor(originPos);

        sr.flipX = Random.value > 0.5f;

        UpdateVisual();
    }

    public void StartMining()
    {
        if (isMining) return;

        int toolGrade = PlayerTileController.Instance?.CurrentToolGrade ?? 0;
        if (toolGrade < data.minToolGrade)
        {
            NotificationUI.Instance?.Show(ToolMessage(toolGrade));
            Sfx.Play(SfxId.UiError);
            if (Time.unscaledTime - lastToolHintTime > 1.2f)
            {
                lastToolHintTime = Time.unscaledTime;
                LabFx.FloatText(transform.position + Vector3.up * 0.9f,
                                toolGrade <= 0 ? "드릴을 들어요!" : $"티어{data.minToolGrade} 드릴 필요!", new Color(1f, 0.55f, 0.6f), 2.6f);
            }
            return;
        }

        isMining    = true;
        isMiningNow = isMining;
        enabled     = true;

        if (Time.time - lastDropTime >= GetEffectiveInterval())
        {
            lastDropTime = Time.time;
            DropResource();
        }
    }

    public void StopMining()
    {
        isMining    = false;
        isMiningNow = isMining;
        enabled     = false;
    }

    public int          RemainingResources => remainingResources;
    public ResourceData Data               => data;

    public Vector3      OriginPosition     => originPos;

    public FarmingItemData DropItem => data?.dropItem;

    public (FarmingItemData item, int amount) ExtractResources(int requestAmount)
    {
        if (remainingResources <= 0 || data?.dropItem == null) return (null, 0);

        int extracted = Mathf.Max(1, requestAmount);
        // 손으로 캘 때와 같게 1씩
        remainingResources -= 1;

        OnAnyResourceMined?.Invoke(data.dropItem, extracted);

        if (shakeCoroutine != null) StopCoroutine(shakeCoroutine);
        shakeCoroutine = StartCoroutine(Shake());

        UpdateVisual();

        if (remainingResources <= 0)
        {
            Sfx.PlayAt(SfxId.BlockBreak, transform.position);
            Destroy(gameObject);
        }

        return (data.dropItem, extracted);
    }

    public void MineRemote(int amount) => DropResource(Mathf.Max(1, amount));

    public void RestoreState(int remaining)
    {
        remainingResources = Mathf.Clamp(remaining, 0, data != null ? data.maxResources : remaining);
        UpdateVisual();
    }

    public static ResourceBlock CreateSaved(ResourceData resData, Vector2 pos, Transform player, int remaining)
    {
        var go          = new GameObject($"ResourceBlock_{resData.resourceName}");
        go.transform.position = new Vector3(pos.x, pos.y, 0f);

        go.AddComponent<SpriteRenderer>();

        var col       = go.AddComponent<CircleCollider2D>();
        col.radius    = 0.5f;
        col.isTrigger = true;

        var block = go.AddComponent<ResourceBlock>();
        block.Initialize(resData, player);
        block.RestoreState(remaining);
        return block;
    }

    private static float lastToolHintTime = -10f;

    private string ToolMessage(int toolGrade)
        => toolGrade <= 0
            ? $"{data.resourceName}: 드릴을 들어야 캘 수 있어요 (핫바에서 드릴 고르기)"
            : $"{data.resourceName}: 티어{data.minToolGrade} 드릴이 필요해요 (지금 티어{toolGrade})";

    private void Update()
    {
        if (!isMining || data == null) { enabled = false; return; }
        if (Time.time - lastDropTime < GetEffectiveInterval()) return;

        int toolGrade = PlayerTileController.Instance?.CurrentToolGrade ?? 0;
        if (toolGrade < data.minToolGrade)
        {
            StopMining();
            NotificationUI.Instance?.Show(ToolMessage(toolGrade));
            Sfx.Play(SfxId.UiError);
            return;
        }

        lastDropTime = Time.time;
        DropResource();
    }

    private float GetEffectiveInterval()
    {
        var ptc = PlayerTileController.Instance;
        float baseInterval = (ptc?.CurrentToolGrade > 0 && ptc.SelectedItem?.miningSpeed > 0)
            ? 1f / ptc.SelectedItem.miningSpeed
            : data.dropInterval;

        float techMult  = TechTreeManager.Instance?.MiningSpeedMult ?? 1f;
        float equipMult = EquipmentManager.Instance?.GetBonus(TechStatType.MiningSpeed) ?? 0f;
        return baseInterval / (techMult + equipMult);
    }

    private void DropResource(int remoteAmount = 0)
    {
        if (remainingResources <= 0) return;
        if (data.dropItem == null)
        {
            Debug.LogWarning($"[ResourceBlock] '{data.resourceName}'의 dropItem이 비어 있습니다.");
            return;
        }

        remainingResources--;

        int amount = remoteAmount;
        if (amount <= 0)
        {
            float toolPower = PlayerTileController.Instance?.CurrentToolMiningPower ?? 1f;
            amount = Mathf.Max(1,
                Mathf.RoundToInt(Random.Range(data.dropAmountMin, data.dropAmountMax + 1) * toolPower));

            Vector3 spawnOffset = (Vector3)(Random.insideUnitCircle * 0.3f);
            DroppedItem.Spawn(data.dropItem, amount, transform.position + spawnOffset, playerTransform, homing: true);
            NetOps.Mine(this, amount);

            GamepadRumble.Play(remainingResources <= 0 ? Haptic.MineBreak : Haptic.MineHit);

            Vector3 hit = transform.position + Vector3.up * 0.25f * transform.localScale.y;
            if (remainingResources <= 0)
            {
                LabFx.Burst(hit, data.chipColor, 14, 3.2f, 0.55f);
                LabFx.Dust(transform.position + Vector3.down * 0.5f, 1.2f);
            }
            else LabFx.Burst(hit, data.chipColor, 5, 2.2f, 0.32f);
        }
        Sfx.PlayAt(remainingResources <= 0 ? SfxId.BlockBreak : SfxId.MineHit, transform.position);

        OnAnyResourceMined?.Invoke(data.dropItem, amount);

        if (shakeCoroutine != null) StopCoroutine(shakeCoroutine);
        shakeCoroutine = StartCoroutine(Shake());

        UpdateVisual();

        if (remainingResources <= 0)
        {
            Destroy(gameObject);
        }
    }

    private void UpdateVisual()
    {
        if (data == null) return;

        float ratio = (float)remainingResources / data.maxResources;
        resourcePercent = ratio * 100f;

        float scale = Mathf.Lerp(data.scaleMin, data.scaleMax, ratio);
        transform.localScale = Vector3.one * scale;
        UpdateSolidRect(scale);

        if (hitCollider == null) hitCollider = GetComponent<CircleCollider2D>();
        if (hitCollider != null && scale > 0f) hitCollider.radius = HitRadius * Mathf.Max(scale, MinHitScale) / scale;

        if (sr == null || data.stageSprites == null || data.stageSprites.Length == 0) return;
        int total = data.stageSprites.Length;
        if (total == 1) return;

        int idx = Mathf.Clamp(
            Mathf.FloorToInt((1f - ratio) * total),
            0, total - 1);
        sr.sprite = data.stageSprites[idx];
    }

    private void UpdateSolidRect(float scale)
    {
        var sprite = sr != null ? sr.sprite : null;
        if (sprite == null) { SolidRect = default; return; }

        Vector2 size       = sprite.rect.size / sprite.pixelsPerUnit * scale;
        Vector2 bottomLeft = (Vector2)originPos - sprite.pivot / sprite.pixelsPerUnit * scale;
        SolidRect = new Rect(bottomLeft + Vector2.Scale(SolidArea.position, size), Vector2.Scale(SolidArea.size, size));
    }

    private IEnumerator Shake()
    {
        float duration  = 0.25f;
        float magnitude = 0.07f;
        float frequency = 45f;
        float elapsed   = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t       = elapsed / duration;
            float offsetX = Mathf.Sin(elapsed * frequency) * magnitude * (1f - t);
            transform.position = originPos + new Vector3(offsetX, 0f, 0f);
            yield return null;
        }

        transform.position = originPos;
        shakeCoroutine = null;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
#endif
}
