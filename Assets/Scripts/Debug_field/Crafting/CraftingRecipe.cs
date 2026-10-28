using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "NewRecipe", menuName = "Crafting/Recipe")]
public class CraftingRecipe : ScriptableObject
{
    [System.Serializable]
    public class Ingredient
    {
        public FarmingItemData item;
        [Min(1)] public int amount = 1;
    }

    [Header("레시피 정보")]
    public string recipeName;
    [TextArea(2, 4)]
    public string description;
    [Tooltip("이 레시피의 제작 등급")]
    public CraftingTier tier = CraftingTier.Beginner;
    [Tooltip("만들 때 곁에 있어야 하는 설비. Smelter + 재료 한 가지(광석 → 주괴)면 제련소에 넣어 녹이는 레시피, " +
             "Smelter + 재료 여럿(합금)이면 전기가 들어오는 제련소 근처에서 조합창으로 만듭니다.")]
    public CraftingStation station = CraftingStation.None;
    [Tooltip("센터코어가 이 문명 단계에 올라야 만들 수 있습니다")]
    [Min(0)] public int requiredCoreLevel = 0;

    [Header("재료")]
    public List<Ingredient> ingredients = new();

    [Header("결과물")]
    public FarmingItemData outputItem;
    [Min(1)] public int outputAmount = 1;

    [Header("설비에서 만드는 시간")]
    [Tooltip("고양이 주방 요리 한 번에 걸리는 시간(초)")]
    [Min(0f)] public float craftSeconds = 0f;

    public bool IsFurnaceRecipe => station == CraftingStation.Smelter && ingredients != null
                                   && ingredients.Count == 1 && ingredients[0].item != null && outputItem != null;

    public bool IsKitchenRecipe => station == CraftingStation.Kitchen && outputItem != null;

    public bool IsMachineRecipe => IsFurnaceRecipe || IsKitchenRecipe;

    public bool CanCraft(Inventory inventory)
    {
        if (inventory == null || outputItem == null) return false;
        foreach (var ing in ingredients)
        {
            if (ing.item == null) return false;
            if (inventory.GetCount(ing.item) < ing.amount) return false;
        }
        return true;
    }

    public bool Craft(Inventory inventory)
    {
        if (!CanCraft(inventory)) return false;

        foreach (var ing in ingredients)
            inventory.ConsumeItem(ing.item, ing.amount);

        if (inventory.AddItem(outputItem, outputAmount)) return true;

        // 결과물 못 넣으면 재료 되돌림
        foreach (var ing in ingredients)
            inventory.AddItem(ing.item, ing.amount);

        NotificationUI.Instance?.Show("인벤토리가 가득 차서 제작할 수 없습니다.");
        return false;
    }
}
