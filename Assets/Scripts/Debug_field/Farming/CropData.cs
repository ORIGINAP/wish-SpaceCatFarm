using UnityEngine;

[CreateAssetMenu(fileName = "NewCrop", menuName = "Farming/Crop Data")]
public class CropData : ScriptableObject
{
    [Header("기본 정보")]
    public string cropName;

    [Header("성장 단계")]
    [Tooltip("단계별 스프라이트")]
    public Sprite[] stageSprites;

    [Tooltip("각 단계 지속 시간(초)")]
    public float[]  stageTimes;

    public int StageCount => stageSprites != null ? stageSprites.Length : 0;

    public float TotalGrowthTime
    {
        get
        {
            if (stageTimes == null) return 0f;
            float total = 0f;
            foreach (float t in stageTimes) total += t;
            return total;
        }
    }

    public Sprite GetSprite(int stage)
    {
        if (stageSprites == null || stageSprites.Length == 0) return null;
        int idx = Mathf.Clamp(stage, 0, stageSprites.Length - 1);
        return stageSprites[idx];
    }

    public Sprite HarvestSprite =>
        (stageSprites != null && stageSprites.Length > 0)
            ? stageSprites[stageSprites.Length - 1]
            : null;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (stageSprites == null || stageSprites.Length == 0) return;

        int expectedTimeCount = stageSprites.Length - 1;

        // stageTimes 길이 자동 맞춤
        if (stageTimes == null || stageTimes.Length != expectedTimeCount)
        {
            float[] newTimes = new float[expectedTimeCount];

            for (int i = 0; i < expectedTimeCount; i++)
                newTimes[i] = (stageTimes != null && i < stageTimes.Length)
                              ? stageTimes[i]
                              : 10f;

            stageTimes = newTimes;

            UnityEditor.EditorUtility.SetDirty(this);
        }

        for (int i = 0; i < stageTimes.Length; i++)
        {
            if (stageTimes[i] <= 0f)
                Debug.LogWarning($"[CropData '{cropName}'] stageTimes[{i}]가 0 이하입니다. 성장이 즉시 완료됩니다.");
        }
    }
#endif
}
