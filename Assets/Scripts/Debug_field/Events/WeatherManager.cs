using System.Collections.Generic;
using UnityEngine;

// 정수로 저장됨, 끝에만 추가
public enum WeatherType { Clear, StarRain, Aurora, MeteorShower, Storm }

// 게임 6시간마다 날씨 변경
public class WeatherManager : MonoBehaviour
{
    public static WeatherManager Instance => Services.Get<WeatherManager>();

    public static WeatherType Current => Instance != null ? Instance.current : WeatherType.Clear;

    public static bool IsRaining => Current == WeatherType.StarRain;

    public static event System.Action OnWeatherChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => OnWeatherChanged = null;

    public const int PeriodHours = 6;
    private const int PeriodsPerDay = 24 / PeriodHours;

    private const float StormWarningSeconds = 20f;
    private const int   MaxMeteorsPerPeriod = 10;

    private WeatherType current = WeatherType.Clear, next = WeatherType.Clear, next2 = WeatherType.Clear;
    private int   period = -1;
    private int   stormStruckPeriod = -1;
    private float stormAt = -1f;
    private float nextMeteorAt = -1f;
    private int   meteorsThisPeriod;

    private readonly List<ResourceData> meteorOres = new();

    public WeatherType Next  => next;
    public WeatherType Next2 => next2;
    public int Period            => period;
    public int StormStruckPeriod => stormStruckPeriod;

    public static int ForecastDepth => CivFacility.CountPowered(CivEffect.Observatory) > 0 ? 2 : 1;

    public float SecondsUntilStorm => stormAt > 0f ? Mathf.Max(0f, stormAt - Time.time) : -1f;

    public static string Name(WeatherType w) => w switch
    {
        WeatherType.StarRain     => "별비",
        WeatherType.Aurora       => "오로라",
        WeatherType.MeteorShower => "유성우",
        WeatherType.Storm        => WorldThemeConfig.StormName(GameLaunchContext.PendingWorldTheme),
        _                        => "맑음",
    };

    public static string Describe(WeatherType w) => w switch
    {
        WeatherType.StarRain     => $"모든 밭에 물이 닿아요 (작물 {FarmConditions.WaterMult:0.##}배 빨리 · 목화는 {FarmConditions.RainLoverMult:0.#}배)",
        WeatherType.Aurora       => $"황금 작물이 {FarmConditions.AuroraGoldenMult:0}배 잘 나와요 (별빛 작물은 {FarmConditions.StarlightAuroraMult:0}배)",
        WeatherType.MeteorShower => "곁에 운석이 떨어져요 — 희귀 광석을 캘 기회!",
        WeatherType.Storm        => "보호받지 못한 작물이 날아갈 수 있어요 (침입방어막 · 온실 곁은 안전, 혜성 고추는 오히려 쑥쑥)",
        _                        => "평화로운 하늘이에요",
    };

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(this); return; }
    }

    private void Start()
    {
        if (GameTimeManager.Instance != null) GameTimeManager.Instance.OnMinuteChanged += OnMinute;
        if (NetSession.IsReplica) return;

        if (period < 0) StartFresh();
        else
        {
            Enter(announce: false);
            OnMinute();
            Raise();
        }
    }

    private void OnDestroy()
    {
        if (GameTimeManager.Instance != null) GameTimeManager.Instance.OnMinuteChanged -= OnMinute;
        Services.Unregister(this);
    }

    private void Update()
    {
        if (NetSession.IsReplica) return;

        if (stormAt > 0f && Time.time >= stormAt)
        {
            stormAt = -1f;
            stormStruckPeriod = period;
            if (GameEventManager.Instance != null) GameEventManager.Instance.StartStorm(StormWarningSeconds);
        }

        if (current == WeatherType.MeteorShower && nextMeteorAt > 0f && Time.time >= nextMeteorAt)
        {
            if (meteorsThisPeriod < MaxMeteorsPerPeriod && TrySpawnMeteor()) meteorsThisPeriod++;
            nextMeteorAt = meteorsThisPeriod < MaxMeteorsPerPeriod ? Time.time + Random.Range(25f, 45f) : -1f;
        }
    }

    private static int PeriodNow()
    {
        var t = GameTimeManager.Instance;
        return t == null ? 1 : (t.CurrentDay - 1) * PeriodsPerDay + t.CurrentHour / PeriodHours;
    }

    private void StartFresh()
    {
        period  = PeriodNow();
        current = Roll(period);
        next    = Roll(period + 1);
        next2   = Roll(period + 2);
        Enter(announce: false);
        Raise();
    }

    private void OnMinute()
    {
        if (NetSession.IsReplica || period < 0) return;
        int p = PeriodNow();
        if (p == period) return;

        if (p == period + 1)
        {
            current = next;
            next    = next2;
            next2   = Roll(p + 2);
        }
        else
        {
            current = Roll(p);
            next    = Roll(p + 1);
            next2   = Roll(p + 2);
        }
        period = p;
        Enter(announce: true);
        Raise();
    }

    private void Enter(bool announce)
    {
        stormAt           = -1f;
        nextMeteorAt      = -1f;
        meteorsThisPeriod = 0;
        if (current == WeatherType.Storm && stormStruckPeriod != period) stormAt = Time.time + Random.Range(60f, 240f);
        if (current == WeatherType.MeteorShower) nextMeteorAt = Time.time + Random.Range(6f, 15f);
        if (announce) Announce(current);
    }

    private static void Announce(WeatherType w)
    {
        string time = GameTimeManager.Instance != null ? $"[{GameTimeManager.Instance.GetDateTimeString()}] " : "";
        var text = w == WeatherType.Clear
            ? $"{time}날씨: 맑음 — {Describe(w)}"
            : $"{time}날씨: {Name(w)} — {Describe(w)}";
        NotificationUI.Instance?.Show(text, w == WeatherType.Clear ? null : NotificationUI.GoodNewsColor);
        if (w == WeatherType.Aurora || w == WeatherType.MeteorShower) Sfx.Play(SfxId.Boost);
    }

    // 월드 시드 기반이라 항상 같은 순서
    private static WeatherType Roll(int p)
    {
        if (p <= 2) return WeatherType.Clear;
        var rng = new System.Random(unchecked(GameLaunchContext.PendingWorldSeed * 486187739 + p * 16777619 + 7));
        int hourStart = (p % PeriodsPerDay) * PeriodHours;
        bool night = hourStart == 0 || hourStart == 18;
        int level  = CenterCore.CurrentLevel;
        float obs  = CivFacility.CountPowered(CivEffect.Observatory) > 0 ? 2f : 1f;

        float clear  = 50f;
        float rain   = 25f;
        float storm  = level >= 3 ? 9f : 0f;
        float aurora = night ? 9f * obs : 0f;
        float meteor = night && level >= 2 ? 7f * obs : 0f;
        float r = (float)rng.NextDouble() * (clear + rain + storm + aurora + meteor);

        if ((r -= clear)  < 0f) return WeatherType.Clear;
        if ((r -= rain)   < 0f) return WeatherType.StarRain;
        if ((r -= storm)  < 0f) return WeatherType.Storm;
        if ((r -= aurora) < 0f) return WeatherType.Aurora;
        return WeatherType.MeteorShower;
    }

    private static void Raise() => OnWeatherChanged?.Invoke();

    private bool TrySpawnMeteor()
    {
        var gm  = GridManager.Instance;
        var ptc = PlayerTileController.Instance;
        if (gm == null || ptc == null) return false;

        Vector2 center = ptc.transform.position;
        for (int attempt = 0; attempt < 14; attempt++)
        {
            Vector2 pos  = center + Random.insideUnitCircle.normalized * Random.Range(4f, 11f);
            var     cell = gm.WorldToGrid(pos);
            if (!gm.CanPlaceAt(cell, 1, 1)) continue;
            Vector2 at = gm.GridToWorld(cell);
            if (!ResourceBlock.FitsAt(at)) continue;

            var ore = PickMeteorOre();
            if (ore == null) return false;
            int amount = Random.Range(18, 33);
            MeteorFall.Play(at, () =>
            {
                if (!ResourceBlock.FitsAt(at) && !ResourceBlock.FindSpotNear(at, 3f, out at)) return;
                var player = PlayerTileController.Instance != null ? PlayerTileController.Instance.transform : null;
                ResourceBlock.CreateSaved(ore, at, player, amount);
                NotificationUI.Instance?.Show($"운석이 떨어졌어요! {ore.resourceName} 덩어리를 캐 보세요", NotificationUI.GoodNewsColor);
            });
            return true;
        }
        return false;
    }

    private ResourceData PickMeteorOre()
    {
        if (meteorOres.Count == 0)
            foreach (var r in GameDataRegistry.FindAll<ResourceData>())
                if (r != null && r.dropItem != null && r.rarity != ResourceRarity.Common) meteorOres.Add(r);
        if (meteorOres.Count == 0) return null;

        float total = 0f;
        foreach (var r in meteorOres) total += Weight(r);
        float roll = Random.value * total;
        foreach (var r in meteorOres)
            if ((roll -= Weight(r)) < 0f) return r;
        return meteorOres[meteorOres.Count - 1];
    }

    private static float Weight(ResourceData r) => r.rarity switch
    {
        ResourceRarity.Uncommon => 50f,
        ResourceRarity.Rare     => 30f,
        _                       => 20f,
    };

    public void Restore(int cur, int nxt, int nxt2, int savedPeriod, int struckPeriod)
    {
        if (savedPeriod < 0) return;
        current = Clamp(cur);
        next    = Clamp(nxt);
        next2   = Clamp(nxt2);
        period  = savedPeriod;
        stormStruckPeriod = struckPeriod;
        Raise();
    }

    public void SyncFromHost(int cur, int nxt, int nxt2)
    {
        var c = Clamp(cur);
        bool changed = c != current;
        if (!changed && Clamp(nxt) == next && Clamp(nxt2) == next2) return;
        current = c;
        next    = Clamp(nxt);
        next2   = Clamp(nxt2);
        if (changed && period >= 0) Announce(current);
        period = Mathf.Max(period, 0);
        Raise();
    }

    public void ForceWeather(WeatherType w)
    {
        if (NetSession.IsReplica) return;
        current = w;
        if (period < 0) period = PeriodNow();
        stormStruckPeriod = -1;
        Enter(announce: true);
        if (w == WeatherType.Storm) stormAt = Time.time + 2f;
        Raise();
    }

    private static WeatherType Clamp(int v)
        => v >= 0 && v <= (int)WeatherType.Storm ? (WeatherType)v : WeatherType.Clear;
}
