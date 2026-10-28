using UnityEngine;

public enum ResourceRarity { Common, Uncommon, Rare, Epic }

[CreateAssetMenu(fileName = "NewResource", menuName = "Mining/Resource Data")]
public class ResourceData : ScriptableObject
{
    [Header("기본 정보")]
    public string resourceName = "돌";
    [Tooltip("희귀할수록 ResourceBlockSpawner의 자동 스폰 개수가 줄어듭니다")]
    public ResourceRarity rarity = ResourceRarity.Common;

    [Header("스테이지 스프라이트")]
    public Sprite[] stageSprites;

    [Header("모양 (같은 광석이라도 무리 안에서 모양이 섞이게)")]
    [Tooltip("비우면 stageSprites[0]만 씁니다")]
    public Sprite[] variants;

    public Sprite SpriteFor(Vector2 pos)
    {
        if (variants != null && variants.Length > 0)
        {
            int hx = Mathf.RoundToInt(pos.x * 100f), hy = Mathf.RoundToInt(pos.y * 100f);
            uint h = (uint)(hx * 73856093) ^ (uint)(hy * 19349663);
            var s = variants[(int)(h % (uint)variants.Length)];
            if (s != null) return s;
        }
        return stageSprites != null && stageSprites.Length > 0 ? stageSprites[0] : null;
    }

    [Tooltip("캘 때 튀는 조각 색 (광석 색)")]
    public Color chipColor = new Color(0.85f, 0.8f, 0.75f);

    [Header("드롭 아이템")]
    public FarmingItemData dropItem;
    [Min(1)] public int dropAmountMin = 1;
    [Min(1)] public int dropAmountMax = 1;

    [Header("채굴 설정")]
    [Tooltip("맨손(0) 또는 이 등급 이상 도구가 있어야 채굴할 수 있습니다")]
    public int minToolGrade = 0;
    [Tooltip("클릭 유지 시 드롭 발생 간격")]
    public float dropInterval   = 0.4f;
    [Tooltip("최대 자원량 — 이 횟수만큼 드롭하면 블록 소멸")]
    [Min(1)] public int maxResources = 8;

    [Header("크기 변화")]
    [Tooltip("자원이 가득할 때 블록 크기")]
    public float scaleMax = 1f;
    [Tooltip("자원이 거의 소진됐을 때 블록 크기")]
    [Range(0.1f, 1f)] public float scaleMin = 0.4f;
}
