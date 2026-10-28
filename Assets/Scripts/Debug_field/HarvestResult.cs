[System.Serializable]
public class HarvestResult
{
    public FarmingItemData item;
    public int             amount;

    public HarvestResult(FarmingItemData item, int amount)
    {
        this.item   = item;
        this.amount = amount;
    }
}
