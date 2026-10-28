using UnityEngine;

public class ResourceNode : MonoBehaviour
{
    [Header("Resource Settings")]
    [SerializeField] private FarmingItemData resourceItem;
    [SerializeField] private int             maxAmount   = 10;
    [SerializeField] private float           respawnTime = 60f;

    [Header("Visual")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite         fullSprite;
    [SerializeField] private Sprite         depletedSprite;

    public FarmingItemData ResourceItem  => resourceItem;
    public bool            IsDepleted    => currentAmount <= 0;

    private int   currentAmount;
    private float respawnTimer;

    private void Awake()
    {
        currentAmount = maxAmount;
        UpdateVisual();
    }

    private void Update()
    {
        if (!IsDepleted) return;

        respawnTimer += Time.deltaTime;
        if (respawnTimer >= respawnTime)
        {
            currentAmount = maxAmount;
            respawnTimer  = 0f;
            UpdateVisual();
        }
    }

    public bool CanMine(FarmingItemData tool)
    {
        if (IsDepleted)                          return false;
        if (tool == null)                        return false;
        if (tool.itemType != FarmingItemType.Tool) return false;
        if (resourceItem  == null)               return false;
        return tool.toolGrade >= resourceItem.minToolGrade;
    }

    public int Mine(FarmingItemData tool)
    {
        if (!CanMine(tool)) return 0;

        int amount    = Mathf.Max(1, Mathf.FloorToInt(tool.miningPower));
        amount        = Mathf.Min(amount, currentAmount);
        currentAmount -= amount;
        UpdateVisual();
        return amount;
    }

    public float GetAmountRatio() => (float)currentAmount / maxAmount;

    private void UpdateVisual()
    {
        if (spriteRenderer == null) return;
        spriteRenderer.sprite = IsDepleted ? depletedSprite : fullSprite;
    }
}
