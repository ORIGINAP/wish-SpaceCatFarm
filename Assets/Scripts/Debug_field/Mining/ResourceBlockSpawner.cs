using UnityEngine;
using System.Collections.Generic;

[DefaultExecutionOrder(50)]
public class ResourceBlockSpawner : MonoBehaviour
{
    [System.Serializable]
    public class SpawnEntry
    {
        public ResourceData data;
        [Tooltip("0이면 희소도 기반 자동 계산")]
        public int count = 0;
    }

    [Header("스폰 영역")]
    [Tooltip("스폰 영역 중심 좌표 (월드)")]
    [SerializeField] private Vector2 spawnCenter = Vector2.zero;
    [Tooltip("스폰 영역 크기")]
    [SerializeField] private Vector2 spawnSize   = new Vector2(75f, 75f);

    [Header("희소도별 밀도 (기준 면적 75×75 당 개수)")]
    [SerializeField] private float densityCommon   = 40f;
    [SerializeField] private float densityUncommon = 20f;
    [SerializeField] private float densityRare     = 8f;
    [SerializeField] private float densityEpic     = 3f;
    private const float ReferenceArea = 75f * 75f;

    [Header("군집 설정")]
    [Tooltip("군집 하나당 평균 블록 수")]
    [SerializeField] private int   blocksPerCluster = 8;
    [Tooltip("군집 반경")]
    [SerializeField] private float clusterRadius = 3.5f;
    [Tooltip("군집 근처에 배치할 확률. 나머지는 전역 산포")]
    [Range(0f, 1f)] [SerializeField] private float clusterRatio = 0.85f;
    [Tooltip("서로 다른 종류의 군집 중심끼리 유지할 최소 거리")]
    [SerializeField] private float minClusterCenterDistance = 6f;

    [Header("배치 규칙")]
    [Tooltip("같은 종류 블록끼리의 최소 거리")]
    [SerializeField] private float minDistanceSameType = 1.0f;
    [Tooltip("다른 종류 블록과의 최소 거리")]
    [SerializeField] private float minDistanceDifferentType = 1.6f;
    [Tooltip("배치 시도 최대 횟수")]
    [SerializeField] private int   maxAttempts   = 40;

    [Header("자원 항목")]
    [SerializeField] private List<SpawnEntry> entries = new();

    private Vector2 reservedCenter;
    private float   reservedRadius;

    private readonly List<Vector2> placedPositions = new();
    private readonly Dictionary<SpawnEntry, List<Vector2>> perEntryPlaced = new();
    private readonly Dictionary<SpawnEntry, List<Vector2>> perEntryClusters = new();
    private readonly List<Vector2> allClusterCenters = new();
    private Transform playerTransform;

    public void SetSpawnArea(Vector2 center, Vector2 size)
    {
        spawnCenter = center;
        spawnSize   = size;
    }

    public void SetReservedArea(Vector2 center, float radius)
    {
        reservedCenter = center;
        reservedRadius = Mathf.Max(0f, radius);
    }

    private bool InReservedArea(Vector2 p, float margin = 0f)
        => reservedRadius > 0f && Vector2.Distance(p, reservedCenter) < reservedRadius + margin;

    private void Start()
    {
        var ptc = PlayerTileController.Instance;
        playerTransform = ptc != null ? ptc.transform : null;

        if (playerTransform == null)
            Debug.LogWarning("[ResourceBlockSpawner] PlayerTileController를 찾을 수 없습니다. 드롭 아이템 자석 효과가 비활성화됩니다.");

        // 로드나 멀티 참가면 스폰 안 함
        if (!SaveManager.IsLoading && GameLaunchContext.IsNewGame && !NetSession.IsReplica)
            SpawnAll();
    }

    public Transform GetPlayerTransform() => playerTransform;

    private int ResolveCount(SpawnEntry entry)
    {
        if (entry.count > 0) return entry.count;

        float density = entry.data.rarity switch
        {
            ResourceRarity.Common   => densityCommon,
            ResourceRarity.Uncommon => densityUncommon,
            ResourceRarity.Rare     => densityRare,
            ResourceRarity.Epic     => densityEpic,
            _ => densityCommon
        };
        float area = spawnSize.x * spawnSize.y;
        return Mathf.Max(1, Mathf.RoundToInt(density * (area / ReferenceArea)));
    }

    private void SpawnAll()
    {
        placedPositions.Clear();
        perEntryPlaced.Clear();
        perEntryClusters.Clear();
        allClusterCenters.Clear();

        foreach (var entry in entries)
        {
            if (entry.data == null) continue;
            int count = ResolveCount(entry);
            int clusterCount = Mathf.Max(1, Mathf.RoundToInt((float)count / blocksPerCluster));
            var centers = GenerateClusterCenters(clusterCount);
            perEntryClusters[entry] = centers;
            allClusterCenters.AddRange(centers);
            perEntryPlaced[entry] = new List<Vector2>();
        }

        foreach (var entry in entries)
        {
            if (entry.data == null)
            {
                Debug.LogWarning("[ResourceBlockSpawner] ResourceData가 비어 있는 항목이 있습니다.");
                continue;
            }

            int count   = ResolveCount(entry);
            var centers = perEntryClusters[entry];
            var mine    = perEntryPlaced[entry];

            for (int i = 0; i < count; i++)
            {
                bool nearCluster = centers.Count > 0 && Random.value < clusterRatio;
                bool placed = TryFindPosition(entry, nearCluster, centers, mine, out Vector2 pos);

                if (!placed && nearCluster)
                    placed = TryFindPosition(entry, false, centers, mine, out pos);

                if (placed)
                {
                    SpawnBlock(entry, pos);
                    mine.Add(pos);
                    placedPositions.Add(pos);
                }
                else
                {
                    Debug.LogWarning($"[ResourceBlockSpawner] '{entry.data.resourceName}' 배치 위치를 찾지 못했습니다. 스폰 영역을 넓히거나 최소 거리를 줄여보세요.");
                }
            }
        }
    }

    private List<Vector2> GenerateClusterCenters(int count)
    {
        var result = new List<Vector2>(count);
        float halfX = Mathf.Max(0f, spawnSize.x * 0.5f - clusterRadius);
        float halfY = Mathf.Max(0f, spawnSize.y * 0.5f - clusterRadius);

        for (int i = 0; i < count; i++)
        {
            Vector2 candidate = Vector2.zero;
            bool found = false;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                candidate = new Vector2(
                    spawnCenter.x + Random.Range(-halfX, halfX),
                    spawnCenter.y + Random.Range(-halfY, halfY));

                bool tooClose = InReservedArea(candidate, clusterRadius)
                                || !ResourceBlock.FitsAt(candidate);
                foreach (var c in allClusterCenters)
                {
                    if (Vector2.Distance(candidate, c) < minClusterCenterDistance) { tooClose = true; break; }
                }
                foreach (var c in result)
                {
                    if (Vector2.Distance(candidate, c) < minClusterCenterDistance) { tooClose = true; break; }
                }

                if (!tooClose) { found = true; break; }
            }

            result.Add(candidate);
            if (!found) {  }
        }

        return result;
    }

    private bool TryFindPosition(SpawnEntry entry, bool nearCluster, List<Vector2> centers, List<Vector2> mine, out Vector2 result)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var candidate = nearCluster ? RandomPointInZone(centers) : RandomPointInArea();

            bool tooClose = InReservedArea(candidate) || !ResourceBlock.FitsAt(candidate);

            foreach (var p in mine)
            {
                if (Vector2.Distance(candidate, p) < minDistanceSameType) { tooClose = true; break; }
            }

            if (!tooClose)
            {
                foreach (var kv in perEntryPlaced)
                {
                    if (kv.Key == entry) continue;
                    foreach (var p in kv.Value)
                    {
                        if (Vector2.Distance(candidate, p) < minDistanceDifferentType) { tooClose = true; break; }
                    }
                    if (tooClose) break;
                }
            }

            if (!tooClose) { result = candidate; return true; }
        }

        result = Vector2.zero;
        return false;
    }

    private Vector2 RandomPointInArea()
        => new Vector2(spawnCenter.x + Random.Range(-spawnSize.x * 0.5f, spawnSize.x * 0.5f),
                       spawnCenter.y + Random.Range(-spawnSize.y * 0.5f, spawnSize.y * 0.5f));

    private Vector2 RandomPointInZone(List<Vector2> centers)
    {
        Vector2 center = centers[Random.Range(0, centers.Count)];
        float   angle  = Random.Range(0f, Mathf.PI * 2f);
        float   dist   = clusterRadius * Random.value;
        return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
    }

    private void SpawnBlock(SpawnEntry entry, Vector2 pos)
    {
        var go = new GameObject($"ResourceBlock_{entry.data.resourceName}");
        go.transform.position = new Vector3(pos.x, pos.y, 0f);

        go.AddComponent<SpriteRenderer>();

        var col = go.AddComponent<CircleCollider2D>();
        col.radius    = 0.5f;
        col.isTrigger = true;

        var block = go.AddComponent<ResourceBlock>();
        block.Initialize(entry.data, playerTransform);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.2f);
        Gizmos.DrawCube(new Vector3(spawnCenter.x, spawnCenter.y, 0f),
                        new Vector3(spawnSize.x, spawnSize.y, 0f));

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(new Vector3(spawnCenter.x, spawnCenter.y, 0f),
                            new Vector3(spawnSize.x, spawnSize.y, 0f));

        if (reservedRadius > 0f)
        {
            Gizmos.color = new Color(0.6f, 0.5f, 1f, 0.9f);
            Gizmos.DrawWireSphere(new Vector3(reservedCenter.x, reservedCenter.y, 0f), reservedRadius);
        }

        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
        foreach (var c in allClusterCenters)
            Gizmos.DrawWireSphere(new Vector3(c.x, c.y, 0f), clusterRadius);
    }
#endif
}
