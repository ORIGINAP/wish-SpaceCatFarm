using UnityEngine;
using System;

public class PlayerWallet : MonoBehaviour
{
    [SerializeField] private int currentMoney = 0;
    public int CurrentMoney => currentMoney;

    public event Action<int> OnMoneyChanged;

    public bool CanAfford(int amount)
    {
        return currentMoney >= amount;
    }

    public bool TrySpend(int amount)
    {
        if (!CanAfford(amount)) return false;

        currentMoney -= amount;
        OnMoneyChanged?.Invoke(currentMoney);
        return true;
    }

    public void AddMoney(int amount)
    {
        if (amount <= 0) return;

        currentMoney += amount;
        OnMoneyChanged?.Invoke(currentMoney);
    }

    [ContextMenu("Add Test Money (1000)")]
    private void AddTestMoney()
    {
        AddMoney(1000);
    }
}
