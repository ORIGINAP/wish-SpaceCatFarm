using UnityEngine;

public class Generator : TileItem
{
    [Header("Generator Settings")]
    [SerializeField] private float maxFuel             = 100f;
    [SerializeField] private float fuelConsumeRate     = 2f;
    [SerializeField] private float startingFuel    = 0f;
    [Tooltip("바로 전기를 주는 범위")]
    [SerializeField, Min(0)] private int directSupplyRadius = 0;
    [Tooltip("연료 보충 기준량")]
    [SerializeField, Min(0f)] private float autoRefuelThreshold = 3f;
    [Tooltip("연료가 떨어져 가는데 가져올 연료가 없을 때 다시 찾아보는 간격")]
    [SerializeField, Min(0.1f)] private float autoRefuelRetryInterval = 0.5f;

    [Header("Visual")]
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Sprite         activeSprite;
    [SerializeField] private Sprite         inactiveSprite;
    [SerializeField] private Color          activeColor   = Color.white;
    [SerializeField] private Color          inactiveColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    public virtual bool  IsActive  => currentFuel > 0f;
    public virtual bool  UsesFuel  => true;
    public float CurrentFuel      => currentFuel;
    public virtual float MaxFuel  => maxFuel;
    public virtual float FuelRatio => Mathf.Clamp01(currentFuel / MaxFuel);
    public virtual int   DirectSupplyRadius  => directSupplyRadius;
    public float StartingFuel => startingFuel;

    public Vector2Int CenterGridPosition
    {
        get
        {
            if (OccupiedCells == null || OccupiedCells.Length == 0)
                return GridPosition;

            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            foreach (var c in OccupiedCells)
            {
                if (c.x < minX) minX = c.x;
                if (c.y < minY) minY = c.y;
                if (c.x > maxX) maxX = c.x;
                if (c.y > maxY) maxY = c.y;
            }
            return new Vector2Int(Mathf.RoundToInt((minX + maxX) * 0.5f),
                                  Mathf.RoundToInt((minY + maxY) * 0.5f));
        }
    }

    private float currentFuel = 0f;
    private bool  wasActive   = false;
    private float nextAutoRefuelTime;
    private static readonly System.Collections.Generic.List<FarmingItemData> fuelBuffer    = new();
    private static readonly System.Collections.Generic.List<ItemStorage>     storageBuffer = new();
    private bool  restoredFromSave;

    private static readonly Color FuelBarHighColor = new Color(0.20f, 0.80f, 0.30f, 1f);
    private static readonly Color FuelBarLowColor  = new Color(0.85f, 0.18f, 0.12f, 1f);

    [Header("실시간 상태")]
    [SerializeField] private float fuelRemaining;
    [SerializeField] private float fuelPercent;
    [SerializeField] private bool  isRunning;

    protected virtual void Start()
    {
        if (IsGhost)
        {
            UpdateVisual();
            return;
        }

        // 로드된 연료 덮어쓰기 방지
        if (startingFuel > 0f && !restoredFromSave)
            currentFuel = Mathf.Clamp(startingFuel, 0f, MaxFuel);

        wasActive = IsActive;
        UpdateVisual();

        if (IsActive)
            EnergyManager.Instance?.RefreshEnergyNetwork();

        if (UsesFuel)
            TilePowerBar.Attach(this, () => FuelRatio, FuelBarHighColor, FuelBarLowColor);
    }

    public void RestoreFromSave(float fuel)
    {
        currentFuel = Mathf.Clamp(fuel, 0f, MaxFuel);
        restoredFromSave = true;
        wasActive   = IsActive;
        UpdateVisual();
        EnergyManager.Instance?.RefreshEnergyNetwork();
    }

    protected virtual void Update()
    {
        if (IsGhost || NetSession.IsReplica) return;

        if (IsActive && UsesFuel)
        {
            currentFuel = Mathf.Max(0f, currentFuel - fuelConsumeRate * Time.deltaTime);

            if (wasActive && !IsActive)
            {
                wasActive = false;
                UpdateVisual();
                EnergyManager.Instance?.RefreshEnergyNetwork();
            }
        }

        if (UsesFuel && Links.Count > 0 && currentFuel <= autoRefuelThreshold && Time.time >= nextAutoRefuelTime)
        {
            nextAutoRefuelTime = Time.time + autoRefuelRetryInterval;
            AutoRefuelFromLinks();
        }

        fuelRemaining = currentFuel;
        fuelPercent   = MaxFuel > 0f ? currentFuel / MaxFuel * 100f : 0f;
        isRunning     = IsActive;
    }

    public virtual int AddFuel(FarmingItemData fuelItem, int amount = 1)
    {
        if (fuelItem == null || !fuelItem.canUseFuel)
        {
            Debug.LogWarning($"[Generator] '{fuelItem?.itemName}'은 연료로 사용할 수 없습니다.");
            return 0;
        }

        float canFill   = MaxFuel - currentFuel;
        float addAmount = fuelItem.fuelValue * amount;

        int needed = Mathf.CeilToInt(canFill / fuelItem.fuelValue);
        int used   = Mathf.Min(amount, needed);
        if (used <= 0) return 0;

        float filled = fuelItem.fuelValue * used;
        currentFuel  = Mathf.Min(currentFuel + filled, MaxFuel);

        bool nowActive = IsActive;
        if (!wasActive && nowActive)
        {
            wasActive = true;
            UpdateVisual();
            EnergyManager.Instance?.RefreshEnergyNetwork();
        }

        NetOps.Tile(NetOpType.Fuel, this, fuelItem, used);
        return used;
    }

    public void BurnFuel(float amount)
    {
        if (amount <= 0f || !UsesFuel) return;
        currentFuel = Mathf.Max(0f, currentFuel - amount);
    }

    private void AutoRefuelFromLinks()
    {
        storageBuffer.Clear();
        for (int i = 0; i < Links.Count; i++)
        {
            if (Links[i] is ItemStorage storage)
            {
                if (!storageBuffer.Contains(storage)) storageBuffer.Add(storage);
            }
            else if (Links[i] is AutoFuelPipe pipe && PipeNetworks.Of(pipe) is PipeNetwork net)
            {
                foreach (var s in net.Storages)
                    if (s != null && !storageBuffer.Contains(s)) storageBuffer.Add(s);
            }
        }
        if (storageBuffer.Count == 0) return;

        fuelBuffer.Clear();
        foreach (var s in storageBuffer)
            foreach (var item in s.StockedItems)
                if (item != null && item.canUseFuel && item.fuelValue > 0f && !fuelBuffer.Contains(item))
                    fuelBuffer.Add(item);
        fuelBuffer.Sort((a, b) => a.fuelValue.CompareTo(b.fuelValue));

        foreach (var fuel in fuelBuffer)
            foreach (var s in storageBuffer)
            {
                int fits = Mathf.FloorToInt((MaxFuel - currentFuel) / fuel.fuelValue);
                if (fits <= 0) break;
                int n = Mathf.Min(fits, s.GetCount(fuel));
                if (n <= 0) continue;
                int used = AddFuel(fuel, n);
                if (used > 0) s.RemoveItem(fuel, used);
            }
    }

    protected void UpdateVisual()
    {
        if (spriteRenderer == null) return;
        spriteRenderer.sprite = IsActive ? activeSprite : inactiveSprite;
        spriteRenderer.color  = IsActive ? activeColor   : inactiveColor;
    }

    protected void SetLookSprites(Sprite active, Sprite inactive)
    {
        if (active   != null) activeSprite   = active;
        if (inactive != null) inactiveSprite = inactive;
        UpdateVisual();
    }
}