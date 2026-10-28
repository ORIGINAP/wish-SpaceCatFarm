using UnityEngine;

public class SolarPanel : Generator
{
    [Header("태양광")]
    [Tooltip("가동을 시작하는 시각 (게임 시간, 포함)")]
    [SerializeField] private int dayStartHour = 6;
    [Tooltip("가동을 멈추는 시각 (게임 시간, 미포함)")]
    [SerializeField] private int dayEndHour   = 18;

    private bool lastActive;

    public override bool  UsesFuel  => false;
    public override bool  IsActive  => IsDaytime;
    public override float FuelRatio => IsActive ? 1f : 0f;

    public override string AbilityText => $"{dayStartHour}시~{dayEndHour}시 낮 동안 연료 없이 전기를 만들어요";

    public bool IsDaytime
    {
        get
        {
            var time = GameTimeManager.Instance;
            if (time == null) return true;
            int h = time.CurrentHour;
            return h >= dayStartHour && h < dayEndHour;
        }
    }

    public override int AddFuel(FarmingItemData fuelItem, int amount = 1) => 0;

    protected override void Start()
    {
        lastActive = IsActive;
        UpdateVisual();
        if (IsGhost) return;
        if (lastActive) EnergyManager.Instance?.RefreshEnergyNetwork();
    }

    protected override void Update()
    {
        if (IsGhost) return;

        bool active = IsActive;
        if (active == lastActive) return;
        lastActive = active;
        UpdateVisual();
        EnergyManager.Instance?.RefreshEnergyNetwork();
    }
}
