using System.Collections.Generic;
using UnityEngine;

public enum CoreRequirementType
{
    Facility,
    CropVarieties,
    Research,
    Coins,
    Orders,
    Residents,
}

[System.Serializable]
public class CoreRequirement
{
    public CoreRequirementType type;

    [Tooltip("Facility: 세울 설치물 아이템")]
    public FarmingItemData item;

    [Tooltip("목표 수치")]
    [Min(1)] public int amount = 1;

    [Tooltip("Facility: 전기가 들어오는 것만 셉니다")]
    public bool needsPower;

    [Tooltip("Research: 테크트리 노드 ID")]
    public string techNodeId;
}

[System.Serializable]
public class CoreStage
{
    [Tooltip("단계 이름 (예: 개척 캠프)")]
    public string title;

    [TextArea(2, 3)]
    public string description;

    [Header("모습 (연료 있음 / 없음)")]
    public Sprite spriteOn;
    public Sprite spriteOff;

    [Header("발전소 연료통 크기")]
    public float maxFuel = 300f;

    [Header("이 단계에 오르는 조건 (Lv.1은 비워 둠)")]
    public List<CoreRequirement> requirements = new();
}

[CreateAssetMenu(fileName = "CoreStages", menuName = "WISH/Core Stage Table")]
public class CoreStageTable : ScriptableObject
{
    public const string ResourcePath = "CoreStages";

    public List<CoreStage> stages = new();

    private static CoreStageTable loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => loaded = null;

    public static CoreStageTable Load() => loaded != null ? loaded : (loaded = Resources.Load<CoreStageTable>(ResourcePath));

    public int MaxLevel => Mathf.Max(1, stages.Count);

    public CoreStage Stage(int level) => stages.Count == 0 ? null : stages[Mathf.Clamp(level - 1, 0, stages.Count - 1)];
}
