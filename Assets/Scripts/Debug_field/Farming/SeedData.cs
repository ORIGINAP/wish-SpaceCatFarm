using UnityEngine;

[System.Serializable]
public class HarvestDrop
{
    public FarmingItemData item;
    [Min(1)] public int    amount     = 1;
    [Range(0f, 1f)] public float dropChance = 1f;
}

[CreateAssetMenu(fileName = "NewSeed", menuName = "Farming/Seed Data")]
public class SeedData : ScriptableObject
{
    public string          seedName;
    public float           growthTime    = 30f;

    [Header("수확 드롭 (여러 종류 설정 가능)")]
    [Tooltip("수확 시 드롭할 아이템 목록")]
    public HarvestDrop[] drops;

    [Header("시각적 성장 데이터")]
    public CropData cropData;

    [Header("작물 성향")]
    [Tooltip("잘 자라는 조건")]
    public CropTrait trait = CropTrait.None;

    public float TotalGrowthSeconds => cropData != null && cropData.StageCount > 1 ? cropData.TotalGrowthTime : growthTime;
}