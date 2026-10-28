using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GameEventManager : MonoBehaviour
{
    public static GameEventManager Instance => Services.Get<GameEventManager>();

    public static float CropSellMultiplier { get; private set; } = 1f;

    public static event System.Action<bool> OnBoostChanged;

    private Coroutine boostCo;

    [Header("작물 가치 부스트 이벤트")]
    [Tooltip("이벤트 발생 주기 (초)")]
    [SerializeField] private float boostInterval   = 300f;
    [Tooltip("부스트 지속 시간 (초)")]
    [SerializeField] private float boostDuration   = 60f;
    [Tooltip("판매가 배율. 2 = 2배, 3 = 3배")]
    [SerializeField] private float boostMultiplier = 2f;

    [Header("폭풍 이벤트")]
    [Tooltip("작물 날아갈 확률")]
    [SerializeField] [Range(0f, 1f)] private float destructionChance = 0.5f;

    [Header("폭풍 화면 연출 (흔들림 · 모래바람/눈보라)")]
    [Tooltip("폭풍 도달 몇 초 전부터 화면이 흔들리기 시작할지")]
    [SerializeField] private float preStormShakeLeadTime  = 10f;
    [Tooltip("도달 직전까지 점점 커지는 흔들림의 최대 강도")]
    [SerializeField] private float preStormShakeMagnitude = 0.5f;
    [Tooltip("도달 후 강하게 흔들리는 지속 시간(초)")]
    [SerializeField] private float stormImpactShakeDuration  = 2f;
    [Tooltip("최대 세기 지속 시간(초)")]
    [SerializeField] private float stormWeatherHold = 6f;
    [SerializeField] private float stormWeatherFade = 4f;
    [Tooltip("도달 순간 흔들림 강도")]
    [SerializeField] private float stormImpactShakeMagnitude = 1.5f;

    [Header("실시간 상태")]
    [SerializeField] private bool  boostActive;
    [SerializeField] private float boostTimeLeft;
    [SerializeField] private float nextBoostIn;
    [SerializeField] private bool  stormWarning;

    private void Awake()
    {
        // 같은 오브젝트의 GameTimeManager는 살림
        if (!Services.Register(this)) { Destroy(this); return; }

        CropSellMultiplier = 1f;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        Services.Unregister(this);
        CropSellMultiplier = 1f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        CropSellMultiplier = 1f;
        OnBoostChanged = null;
    }

    private void Start()
    {
        if (NetSession.IsReplica) return;
        boostCo = StartCoroutine(BoostLoop());
    }

    public void StartStorm(float warning) => StartCoroutine(StormSequence(Theme, warning));

    public void SyncBoost(bool active)
    {
        if (active == boostActive) return;
        if (active) StartBoost();
        else EndBoost();
    }

    public void PlayRemoteStorm(WorldTheme theme, float warning) => StartCoroutine(StormSequence(theme, warning));

    private IEnumerator BoostLoop()
    {
        while (true)
        {
            for (float t = 0f; t < boostInterval; t += Time.deltaTime)
            {
                nextBoostIn = boostInterval - t;
                yield return null;
            }

            StartBoost();

            for (float t = 0f; t < boostDuration; t += Time.deltaTime)
            {
                boostTimeLeft = boostDuration - t;
                yield return null;
            }

            EndBoost();
        }
    }

    private void StartBoost()
    {
        CropSellMultiplier = boostMultiplier;
        boostActive        = true;
        nextBoostIn        = 0f;
        NotificationUI.Instance?.Show(
            $"{CurrentTimeLabel()}에너지 풍족기간 시작! 작물 판매가 {boostMultiplier}배 상승 ({boostDuration}초 동안)", NotificationUI.GoodNewsColor);
        Sfx.Play(SfxId.Boost);
        OnBoostChanged?.Invoke(true);
    }

    private void EndBoost()
    {
        CropSellMultiplier = 1f;
        boostActive        = false;
        boostTimeLeft      = 0f;
        NotificationUI.Instance?.Show($"{CurrentTimeLabel()}에너지 풍족기간 종료.");
        OnBoostChanged?.Invoke(false);
    }

    private static WorldTheme Theme => GameLaunchContext.PendingWorldTheme;

    private IEnumerator StormSequence(WorldTheme theme, float warning)
    {
        NetHost.StormAll(theme, warning);
        stormWarning = true;
        NotificationUI.Instance?.Show(
            $"{CurrentTimeLabel()}{WorldThemeConfig.StormName(theme)} 접근 중! {warning}초 후 도달 — 다 자란 작물을 서둘러 거두세요!", NotificationUI.WarningColor);
        Sfx.Play(SfxId.StormWarning);

        float leadTime = Mathf.Min(preStormShakeLeadTime, warning);
        yield return new WaitForSeconds(warning - leadTime);

        CameraShake.GetOrCreate().Shake(leadTime, 0f, preStormShakeMagnitude);
        StormWeatherFx.Play(theme, leadTime, stormWeatherHold, stormWeatherFade);
        yield return new WaitForSeconds(leadTime);

        // 밭 피해는 호스트만
        stormWarning = false;
        if (!NetSession.IsReplica) ApplyStorm(theme);
        else Sfx.Play(SfxId.StormHit);

        CameraShake.GetOrCreate().Shake(stormImpactShakeDuration, stormImpactShakeMagnitude);
    }

    private void ApplyStorm(WorldTheme theme)
    {
        var shields = StormShield.All;

        var allTiles = FindObjectsByType<PlantTile>(FindObjectsSortMode.None);

        // 보호 없는 작물만 날림
        int blown = 0;
        foreach (var plant in allTiles)
        {
            if (plant.IsGhost || plant.CurrentSeed == null) continue;
            if (IsShielded(plant.transform.position, shields) || FarmConditions.InGreenhouse(plant.GridPosition)) continue;
            if (Random.value >= destructionChance)  continue;
            if (plant.StormBlowAway()) blown++;
        }

        string storm = WorldThemeConfig.StormSubject(theme);
        string result = blown > 0
            ? $"{CurrentTimeLabel()}{storm} 몰아쳤습니다. 작물 {blown}개가 날아갔어요 (밭은 무사해요)."
            : $"{CurrentTimeLabel()}{storm} 몰아쳤지만 날아간 작물은 없어요.";
        NotificationUI.Instance?.Show(result, NotificationUI.WarningColor);
        NetHost.NoticeAll(result);
        Sfx.Play(SfxId.StormHit);
    }

    private static bool IsShielded(Vector3 pos, IReadOnlyList<StormShield> shields)
    {
        foreach (var s in shields)
            if (s.Protects(pos)) return true;
        return false;
    }

    private static string CurrentTimeLabel()
        => GameTimeManager.Instance != null ? $"[{GameTimeManager.Instance.GetDateTimeString()}] " : "";

    public float NextStormIn => WeatherManager.Instance != null ? WeatherManager.Instance.SecondsUntilStorm : -1f;

    public bool BoostActive => boostActive;

    public float NextBoostIn => nextBoostIn;

    public float DestructionChance => destructionChance;

    public void TriggerStormNow(WorldTheme theme, float warning) => StartCoroutine(StormSequence(theme, warning));

    [ContextMenu("테스트 — 부스트 즉시 발동")]
    public void StartBoostNow()
    {
        if (boostCo != null) StopCoroutine(boostCo);
        boostCo = StartCoroutine(TestBoostRoutine());
    }

    private IEnumerator TestBoostRoutine()
    {
        StartBoost();
        for (float t = 0f; t < boostDuration; t += Time.deltaTime)
        {
            boostTimeLeft = boostDuration - t;
            yield return null;
        }
        EndBoost();
        boostCo = StartCoroutine(BoostLoop());
    }

    [ContextMenu("테스트 — 폭풍 즉시 발동")]
    private void TestStorm() => ApplyStorm(Theme);
}
