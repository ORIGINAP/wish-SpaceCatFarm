using UnityEngine;
using System.Collections.Generic;

public enum TileType { Plant, Battery, Generator, Extractor, AutoFuelPipe, StormShield, Storage, AutoSeeder, AutoHarvester, CenterCore,
                       SolarPanel, GrowthBooster, AutoSeller, Decoration, Smelter, Civilization, PowerRelay,
                       Kitchen, Composter, ShuttlePad }

public class TileItem : MonoBehaviour
{
    [SerializeField] private TileType tileType;
    public TileType TileType => tileType;

    [Header("해체")]
    [Tooltip("우클릭 해체에 걸리는 기본 시간(초)")]
    [SerializeField] private float dismantleTime = 3f;
    [Tooltip("해체 완료 시 드롭될 아이템 데이터.")]
    [SerializeField] private FarmingItemData dropItemData;
    [Tooltip("false면 우클릭 해체가 불가능합니다.")]
    [SerializeField] private bool canDismantle = true;

    [Header("충돌")]
    [Tooltip("플레이어를 막는 깊이 — 설치 칸 아랫변에서 몇 칸까지 막는지 (0이면 설치 칸 전체). " +
             "받침이 설치 칸보다 얕은 탑(우주 관제탑 · 이주선 착륙장 · 전력 중계탑 등)은 받침까지만 막아, 그 뒤쪽은 지나갈 수 있게 합니다.")]
    [SerializeField, Min(0f)] private float solidDepth = 0f;

    public float          DismantleTime  => dismantleTime;
    public FarmingItemData DropItemData  => dropItemData;
    public bool           CanDismantle   => canDismantle && dismantleTime > 0f;

    public float          SolidDepth     => solidDepth;

    public Vector2Int GridPosition { get; private set; }

    public Vector2Int[] OccupiedCells { get; private set; }

    public Vector2Int FootprintMin { get; private set; }
    public Vector2Int FootprintMax { get; private set; }

    public int Rotation { get; private set; }

    public bool IsGhost { get; private set; }

    public void SetGhostMode() => IsGhost = true;

    public bool IsPersistent { get; private set; }

    public void MarkPersistent() => IsPersistent = true;

    public FarmingItemData SourceItem { get; private set; }

    public void SetSourceItem(FarmingItemData item) => SourceItem = item;

    public static event System.Action<TileItem> OnAnyTilePlaced;

    public static void NotifyPlaced(TileItem tile) => OnAnyTilePlaced?.Invoke(tile);

    private readonly List<TileItem> links = new();

    public IReadOnlyList<TileItem> Links => links;

    public static int LinkVersion { get; private set; }

    public static event System.Action OnAnyLinkChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        OnAnyTilePlaced = null;
        OnAnyLinkChanged = null;
        LinkVersion = 0;
    }

    public bool IsLinkedTo(TileItem other) => other != null && links.Contains(other);

    public static bool Link(TileItem a, TileItem b)
    {
        if (a == null || b == null || a == b || a.links.Contains(b)) return false;
        a.links.Add(b);
        b.links.Add(a);
        LinkChanged();
        NetOps.Link(NetOpType.Link, a, b);
        return true;
    }

    public static bool Unlink(TileItem a, TileItem b)
    {
        if (a is null || b is null || !a.links.Remove(b)) return false;
        b.links.Remove(a);
        LinkChanged();
        NetOps.Link(NetOpType.Unlink, a, b);
        return true;
    }

    public void UnlinkAll()
    {
        if (links.Count == 0) return;
        NetOps.Tile(NetOpType.UnlinkAll, this);
        foreach (var other in links)
            if (other is not null) other.links.Remove(this);
        links.Clear();
        LinkChanged();
    }

    private static void LinkChanged()
    {
        LinkVersion++;
        LinkLinesView.Refresh();
        OnAnyLinkChanged?.Invoke();
    }

    public float DistanceFrom(Vector3 p)
    {
        var gm = GridManager.Instance;
        if (gm == null || OccupiedCells == null) return Vector2.Distance(p, transform.position);
        float cs  = gm.CellSize;
        Vector2 min = gm.GridOrigin + (Vector2)FootprintMin * cs;
        Vector2 max = gm.GridOrigin + ((Vector2)FootprintMax + Vector2.one) * cs;
        float dx = Mathf.Max(min.x - p.x, 0f, p.x - max.x);
        float dy = Mathf.Max(min.y - p.y, 0f, p.y - max.y);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    public virtual void Initialize(Vector2Int anchorPos, Vector2Int[] occupiedCells, int rotation = 0)
    {
        GridPosition  = anchorPos;
        OccupiedCells = occupiedCells;
        Rotation      = rotation;

        FootprintMin = FootprintMax = anchorPos;
        if (occupiedCells != null)
            foreach (var c in occupiedCells)
            {
                FootprintMin = Vector2Int.Min(FootprintMin, c);
                FootprintMax = Vector2Int.Max(FootprintMax, c);
            }
    }

    public virtual void DestroyByStorm()
    {
        if (!IsGhost && OccupiedCells != null && GridManager.Instance != null
            && GridManager.Instance.GetTile(GridPosition) == this)
            GridManager.Instance.RemoveTile(GridPosition);
        else
            Destroy(gameObject);
    }

    protected internal virtual IEnumerable<(FarmingItemData item, int amount)> GetAttachedDrops() => null;

    public virtual string AbilityText => null;

    public virtual float WorkRate => 0f;
}