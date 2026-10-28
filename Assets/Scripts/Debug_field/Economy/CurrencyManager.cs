using UnityEngine;

public class CurrencyManager : MonoBehaviour
{
    public static CurrencyManager Instance => Services.Get<CurrencyManager>();

    [Header("초기 코인 (새 게임 시작 시)")]
    [SerializeField] private int startingCurrency = 200;

    [Header("코인 획득량")]
    // 수확 덤 코인, 주 수입은 판매
    [SerializeField] private int coinsPerHarvest       = 1;
    [SerializeField] private int coinsPerHarvestAmount = 0;

    public int StartingCurrency => startingCurrency;

    [Header("실시간 상태")]
    [SerializeField] private long _currency;

    public long Currency => _currency;

    public event System.Action<long> OnCurrencyChanged;

    public static string Format(long coins)
    {
        if (coins < 100_000_000L) return coins.ToString("N0");
        (long unit, string big, long subUnit, string small) = coins switch
        {
            < 1_000_000_000_000L         => (100_000_000L, "억", 10_000L, "만"),
            < 10_000_000_000_000_000L    => (1_000_000_000_000L, "조", 100_000_000L, "억"),
            _                            => (10_000_000_000_000_000L, "경", 1_000_000_000_000L, "조"),
        };
        long head = coins / unit;
        long rest = coins % unit / subUnit;
        return rest > 0 ? $"{head:N0}{big} {rest:N0}{small}" : $"{head:N0}{big}";
    }

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }
        // 이어하기면 Load가 덮어씀
        _currency = Mathf.Max(0, startingCurrency);
    }

    private void Start()
    {
        PlantTile.OnAnyPlantHarvested += OnHarvested;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        PlantTile.OnAnyPlantHarvested -= OnHarvested;
        Services.Unregister(this);
    }

    private void OnHarvested(PlantTile plant, HarvestResult[] results)
    {
        int totalAmount = 0;
        if (results != null)
            foreach (var r in results) totalAmount += r.amount;

        Add(coinsPerHarvest + totalAmount * coinsPerHarvestAmount);
    }

    public void Add(long amount)
    {
        if (amount <= 0) return;
        _currency = amount > long.MaxValue - _currency ? long.MaxValue : _currency + amount;
        OnCurrencyChanged?.Invoke(_currency);
    }

    public bool TrySpend(long amount)
    {
        if (amount <= 0 || _currency < amount) return false;
        _currency -= amount;
        OnCurrencyChanged?.Invoke(_currency);
        return true;
    }

    public bool CanAfford(long amount) => _currency >= amount;

    public void SetCurrency(long amount)
    {
        _currency = System.Math.Max(0L, amount);
        OnCurrencyChanged?.Invoke(_currency);
    }

#if UNITY_EDITOR
    [ContextMenu("코인 +200 (테스트)")]
    private void DebugAdd200() => Add(200);

    [ContextMenu("코인 +1000 (테스트)")]
    private void DebugAdd1000() => Add(1000);
#endif
}
