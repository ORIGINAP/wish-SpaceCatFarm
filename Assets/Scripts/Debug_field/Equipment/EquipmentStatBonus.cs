using UnityEngine;

[System.Serializable]
public class EquipmentStatBonus
{
    public TechStatType statType;
    [Tooltip("보너스 비율 (0.10 = +10%)")]
    public float bonus;
}
