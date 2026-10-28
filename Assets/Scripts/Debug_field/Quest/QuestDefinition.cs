using UnityEngine;
using System.Collections.Generic;

// 숫자로 저장됨, 끝에만 추가
public enum QuestObjectiveType
{
    CollectItem,
    CraftItem,
    HarvestPlant,
    PlaceTile,
    SellItem,
    UpgradeTech,
    CoreLevel,
    CompleteOrder,
    PlantSeed,
    CropVarieties,
    OrdersTotal,
    Residents,
    AutoHarvest,
}

[System.Serializable]
public class QuestObjective
{
    public QuestObjectiveType type;

    [Tooltip("대상 아이템")]
    public FarmingItemData targetItem;

    [Tooltip("UpgradeTech 에서 대상 노드 ID")]
    public string targetTechNodeId = "";

    public int targetAmount = 1;

    // 런타임 진행도
    [System.NonSerialized] public int currentAmount;

    public bool IsComplete => currentAmount >= targetAmount;

    public string GetLabel()
    {
        string name = targetItem != null ? targetItem.itemName : "?";
        return type switch
        {
            QuestObjectiveType.CollectItem   => $"{name} 모으기",
            QuestObjectiveType.CraftItem     => $"{name} 만들기",
            QuestObjectiveType.HarvestPlant  => targetItem != null ? $"{name} 수확하기" : "작물 수확하기",
            QuestObjectiveType.PlaceTile     => $"{name} 설치하기",
            QuestObjectiveType.SellItem      => $"{name} 팔기",
            QuestObjectiveType.UpgradeTech   => string.IsNullOrEmpty(targetTechNodeId) ? "연구 하나 해금하기" : $"연구: {GetTechNodeDisplayName(targetTechNodeId)}",
            QuestObjectiveType.CoreLevel     => $"센터코어 문명 Lv.{targetAmount}",
            QuestObjectiveType.CompleteOrder => "셔틀 보내기",
            QuestObjectiveType.PlantSeed     => targetItem != null ? $"{name} 심기" : "씨앗 심기",
            QuestObjectiveType.CropVarieties => "작물 종류 모으기 (수확해 본 종류)",
            QuestObjectiveType.OrdersTotal   => "셔틀 보내기 (지금까지)",
            QuestObjectiveType.Residents     => "셔틀로 주민 데려오기",
            QuestObjectiveType.AutoHarvest   => "자동수확기로 작물 거두기",
            _ => "?"
        };
    }

    private static string GetTechNodeDisplayName(string nodeId)
        => TechTreeManager.Instance?.GetNode(nodeId)?.displayName ?? nodeId;
}

[System.Serializable]
public class QuestReward
{
    public FarmingItemData item;
    public int amount = 1;
}

[CreateAssetMenu(fileName = "NewQuest", menuName = "Quest/QuestDefinition")]
public class QuestDefinition : ScriptableObject
{
    [Header("기본 정보")]
    public string questName  = "새 퀘스트";
    [TextArea(2, 4)]
    public string description = "";

    [Header("코코 대사")]
    [TextArea(2, 3)]
    public string talk = "";

    [Header("퀘스트 안내")]
    [Tooltip("키보드 · 마우스용 한 줄 안내")]
    public string hint = "";
    [Tooltip("게임패드로 조작할 때 대신 보여 줄 한 줄")]
    public string padHint = "";

    [Header("자세히 (퀘스트 상세를 펼치면 보이는 설명)")]
    [TextArea(2, 5)]
    public string tutorialTip = "";

    [Header("목표 (모두 달성해야 완료)")]
    public List<QuestObjective> objectives = new();

    [Header("보상")]
    public List<QuestReward> rewards = new();
    [Tooltip("보상 코인 (0이면 없음)")]
    [Min(0)] public int rewardCoins = 0;

    public string HintText => Pad.Active && !string.IsNullOrEmpty(padHint) ? padHint : hint;
}
