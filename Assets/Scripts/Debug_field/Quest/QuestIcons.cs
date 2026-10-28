using UnityEngine;

public static class QuestIcons
{
    public static Sprite For(QuestObjective obj)
    {
        if (obj == null) return null;
        if (obj.targetItem != null && obj.targetItem.icon != null) return obj.targetItem.icon;
        return obj.type switch
        {
            QuestObjectiveType.PlantSeed     => Icon("Item_Seed_BlueStar"),
            QuestObjectiveType.HarvestPlant  => Icon("BlueStar"),
            QuestObjectiveType.CropVarieties => Icon("RainbowCarrot") ?? Icon("BlueStar"),
            QuestObjectiveType.CompleteOrder => HudDock.RocketIcon,
            QuestObjectiveType.OrdersTotal   => HudDock.RocketIcon,
            QuestObjectiveType.Residents     => HudDock.CatIcon,
            QuestObjectiveType.AutoHarvest   => Icon("AutoHarvester"),
            QuestObjectiveType.UpgradeTech   => HudDock.FlaskIcon,
            QuestObjectiveType.CoreLevel     => CoreIcon(),
            _                                => null,
        };
    }

    private static Sprite Icon(string itemName) => NetWorld.Item(itemName)?.icon;

    private static Sprite core;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => core = null;

    private static Sprite CoreIcon()
    {
        var c = CenterCore.Current;
        var stage = c != null ? c.CurrentStage : null;
        if (stage != null && stage.spriteOn != null) return stage.spriteOn;
        return core;
    }
}
