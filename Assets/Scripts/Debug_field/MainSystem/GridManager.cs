using UnityEngine;
using System.Collections.Generic;

[DefaultExecutionOrder(-200)]
public class GridManager : MonoBehaviour
{
    public static GridManager Instance => Services.Get<GridManager>();

    [Header("Grid Settings")]
    [SerializeField] private int gridWidth  = 20;
    [SerializeField] private int gridHeight = 20;
    [SerializeField] private float cellSize = 1f;
    [SerializeField] private Vector2 gridOrigin = Vector2.zero;

    public int     GridWidth  => gridWidth;
    public int     GridHeight => gridHeight;
    public float   CellSize   => cellSize;
    public Vector2 GridOrigin => gridOrigin;

    private Dictionary<Vector2Int, TileItem> grid = new();
    private static readonly List<ResourceBlock> nearOres = new();

    // NearbyTiles 캐시 무효화용
    public int Version { get; private set; }

    public event System.Action OnTilesChanged;
    private int notifiedVersion;

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }
    }

    private void OnDestroy() => Services.Unregister(this);

    private void LateUpdate()
    {
        // 프레임당 한 번만 알림
        if (notifiedVersion == Version) return;
        notifiedVersion = Version;
        OnTilesChanged?.Invoke();
    }

    public void Initialize(int width, int height, float newCellSize)
    {
        gridWidth  = width;
        gridHeight = height;
        cellSize   = newCellSize;
        grid.Clear();
        Version++;
    }

    public TileItem PlaceTile(Vector2Int anchorPos, GameObject prefab,
                              int width = 1, int height = 1, int rotation = 0,
                              FarmingItemData sourceItem = null)
    {
        int effW = (rotation == 90 || rotation == 270) ? height : width;
        int effH = (rotation == 90 || rotation == 270) ? width  : height;

        var cells = GetOccupiedCells(anchorPos, effW, effH);

        if (!CanPlaceCells(cells)) return null;

        Vector3 worldPos = GridToWorldCenter(anchorPos, effW, effH);
        GameObject obj   = Instantiate(prefab, worldPos, Quaternion.Euler(0f, 0f, -rotation));

        TileItem tile = GetMostDerivedTileItem(obj);

        if (tile == null)
        {
            Debug.LogError($"[GridManager] 프리팹 '{prefab.name}'에 TileItem 컴포넌트가 없습니다. 설치를 취소합니다.");
            Destroy(obj);
            return null;
        }

        tile.Initialize(anchorPos, cells.ToArray(), rotation);
        if (sourceItem != null) tile.SetSourceItem(sourceItem);

        foreach (var cell in cells)
            grid[cell] = tile;
        Version++;

        EnergyManager.Instance?.Register(tile);

        if (!SaveManager.IsLoading) TileItem.NotifyPlaced(tile);

        NetOps.Tile(NetOpType.Place, tile, sourceItem, rotation);
        return tile;
    }

    public bool CanPlaceAt(Vector2Int anchorPos, int effW, int effH)
        => CanPlaceCells(GetOccupiedCells(anchorPos, effW, effH));

    private bool CanPlaceCells(List<Vector2Int> cells)
    {
        foreach (var cell in cells)
        {
            if (!IsInBounds(cell) || grid.ContainsKey(cell)) return false;

            if (!SaveManager.IsLoading && TerrainMap.Current != null && TerrainMap.Current.IsCellBlocked(cell)) return false;

            Vector3 c   = GridToWorldCenter(cell, 1, 1);
            var     box = new Rect(c.x - cellSize * 0.5f, c.y - cellSize * 0.5f, cellSize, cellSize);
            ResourceBlock.Near(box, nearOres);
            foreach (var ore in nearOres)
                if (ore != null && ore.BlocksCell(box)) return false;
        }
        return true;
    }

    public bool RemoveTile(Vector2Int gridPos)
    {
        if (!grid.TryGetValue(gridPos, out TileItem tile)) return false;
        using var net = NetOps.Removing(tile);

        EnergyManager.Instance?.Unregister(tile);
        tile.UnlinkAll();

        foreach (var cell in tile.OccupiedCells)
            grid.Remove(cell);
        Version++;

        Destroy(tile.gameObject);
        return true;
    }

    public TileItem GetTile(Vector2Int gridPos)
        => grid.TryGetValue(gridPos, out var tile) ? tile : null;

    public void RegisterExisting(TileItem tile)
    {
        if (tile == null || tile.OccupiedCells == null) return;
        tile.MarkPersistent();
        foreach (var cell in tile.OccupiedCells)
            grid[cell] = tile;
        Version++;
    }

    public List<TileItem> GetDynamicTiles()
    {
        var seen   = new HashSet<TileItem>();
        var result = new List<TileItem>();
        foreach (var tile in grid.Values)
            if (!tile.IsPersistent && seen.Add(tile))
                result.Add(tile);
        return result;
    }

    public void ClearDynamicTiles()
    {
        var toRemove = GetDynamicTiles();
        foreach (var tile in toRemove)
        {
            EnergyManager.Instance?.Unregister(tile);
            tile.UnlinkAll();
            if (tile.OccupiedCells != null)
                foreach (var cell in tile.OccupiedCells)
                    grid.Remove(cell);
            if (tile != null) Destroy(tile.gameObject);
        }
        Version++;
    }

    public List<TileItem> GetTilesInRadius(Vector2Int center, int radius, TileType type)
    {
        var result = new List<TileItem>();
        GetTilesInRadius(center, radius, type, result);
        return result;
    }

    public void CollectTiles(TileType type, List<TileItem> results)
    {
        results.Clear();
        var seen = new HashSet<TileItem>();
        foreach (var tile in grid.Values)
            if (tile != null && tile.TileType == type && seen.Add(tile))
                results.Add(tile);
    }

    public void GetTilesInRadius(Vector2Int center, int radius, TileType type, List<TileItem> results)
        => GetTilesNearArea(center, center, radius, type, results);

    public void GetTilesNearArea(Vector2Int min, Vector2Int max, int radius, TileType type, List<TileItem> results)
    {
        results.Clear();
        for (int x = min.x - radius; x <= max.x + radius; x++)
        for (int y = min.y - radius; y <= max.y + radius; y++)
        {
            if (AreaDistance(min, max, x, y) > radius) continue;
            if (grid.TryGetValue(new Vector2Int(x, y), out var tile) && tile.TileType == type && !results.Contains(tile))
                results.Add(tile);
        }
    }

    public static int AreaDistance(Vector2Int min, Vector2Int max, int x, int y)
    {
        int dx = Mathf.Max(0, Mathf.Max(min.x - x, x - max.x));
        int dy = Mathf.Max(0, Mathf.Max(min.y - y, y - max.y));
        return dx + dy;
    }

    public static int AreaDistance(Vector2Int aMin, Vector2Int aMax, Vector2Int bMin, Vector2Int bMax)
    {
        int dx = Mathf.Max(0, Mathf.Max(aMin.x - bMax.x, bMin.x - aMax.x));
        int dy = Mathf.Max(0, Mathf.Max(aMin.y - bMax.y, bMin.y - aMax.y));
        return dx + dy;
    }

    public Vector2Int WorldToGrid(Vector3 worldPos)
        => new(Mathf.FloorToInt((worldPos.x - gridOrigin.x) / cellSize),
               Mathf.FloorToInt((worldPos.y - gridOrigin.y) / cellSize));

    public Vector3 GridToWorld(Vector2Int gridPos)
        => GridToWorldCenter(gridPos, 1, 1);

    public Vector3 GridToWorldCenter(Vector2Int anchor, int w, int h)
        => new(gridOrigin.x + anchor.x * cellSize + w * cellSize * 0.5f,
               gridOrigin.y + anchor.y * cellSize + h * cellSize * 0.5f, 0f);

    public List<Vector2Int> GetOccupiedCells(Vector2Int anchor, int w, int h)
    {
        var cells = new List<Vector2Int>(w * h);
        for (int x = 0; x < w; x++)
        for (int y = 0; y < h; y++)
            cells.Add(new Vector2Int(anchor.x + x, anchor.y + y));
        return cells;
    }

    public bool IsInBounds(Vector2Int pos)
    {
        int halfW = gridWidth / 2;
        int halfH = gridHeight / 2;
        return pos.x >= -halfW && pos.x < (gridWidth - halfW) &&
               pos.y >= -halfH && pos.y < (gridHeight - halfH);
    }

    public Vector2 WorldMin => new(
        gridOrigin.x - (gridWidth  / 2) * cellSize,
        gridOrigin.y - (gridHeight / 2) * cellSize);

    public Vector2 WorldMax => new(
        gridOrigin.x + (gridWidth  - gridWidth  / 2) * cellSize,
        gridOrigin.y + (gridHeight - gridHeight / 2) * cellSize);

    public Vector2 ClampToWorldBounds(Vector2 worldPos)
    {
        Vector2 min = WorldMin;
        Vector2 max = WorldMax;
        return new Vector2(Mathf.Clamp(worldPos.x, min.x, max.x), Mathf.Clamp(worldPos.y, min.y, max.y));
    }

    public bool IsEmpty(Vector2Int pos) => !grid.ContainsKey(pos);

    private static TileItem GetMostDerivedTileItem(GameObject obj)
    {
        TileItem best = null;
        foreach (var comp in obj.GetComponents<TileItem>())
        {
            if (best == null)
            {
                best = comp;
                continue;
            }
            if (comp.GetType().IsSubclassOf(best.GetType()))
                best = comp;
        }
        return best;
    }
}