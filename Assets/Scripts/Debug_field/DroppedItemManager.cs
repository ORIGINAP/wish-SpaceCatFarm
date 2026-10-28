using UnityEngine;

public class DroppedItemManager : MonoBehaviour
{
    public static DroppedItemManager Instance => Services.Get<DroppedItemManager>();

    [Header("아이콘 시각")]
    [Tooltip("드롭 아이템 아이콘 크기")]
    public float iconScale     = 0.45f;

    [Tooltip("아이콘 정렬 순서 (높을수록 앞에 표시)")]
    public int   sortingOrder  = 20;

    [Header("드롭 물리")]
    [Tooltip("스폰 시 튀어나가는 초기 속도")]
    public float popSpeed      = 3.5f;

    [Tooltip("감속 계수. 클수록 빨리 멈춤")]
    public float drag          = 7f;

    [Header("자석 설정")]
    [Tooltip("이 거리 이내 진입 시 자석 활성화")]
    public float magnetRadius  = 6f;

    [Tooltip("자석 이동 속도 (유닛/초)")]
    public float magnetSpeed   = 12f;

    [Tooltip("스폰 후 자석 활성화까지 대기 시간 (초)")]
    public float magnetDelay   = 0.3f;

    [Tooltip("이 거리 이내 도달 시 즉시 픽업")]
    public float pickupRadius  = 0.6f;

    private void Awake()
    {
        if (!Services.Register(this)) Destroy(gameObject);
    }

    private void OnDestroy() => Services.Unregister(this);

    public static DroppedItemManager GetOrDefault()
    {
        if (Instance != null) return Instance;

        var dummy = new GameObject("DroppedItemManager_Auto").AddComponent<DroppedItemManager>();
        return dummy;
    }
}
