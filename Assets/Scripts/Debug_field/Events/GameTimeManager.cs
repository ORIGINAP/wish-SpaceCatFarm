using UnityEngine;

public class GameTimeManager : MonoBehaviour
{
    public static GameTimeManager Instance => Services.Get<GameTimeManager>();

    [Header("시간 설정")]
    [Tooltip("실시간 몇 초가 게임 내 1분에 해당하는지")]
    [SerializeField] private float secondsPerGameMinute = 2f;
    [Tooltip("게임 시작 시각 (시)")]
    [SerializeField] private int   startHour = 6;
    [Tooltip("게임 시작 일차")]
    [SerializeField] private int   startDay  = 1;

    [Header("실시간 상태")]
    [SerializeField] private int currentDay;
    [SerializeField] private int currentHour;
    [SerializeField] private int currentMinute;

    private float minuteTimer;

    public int CurrentDay    => currentDay;
    public int CurrentHour   => currentHour;
    public int CurrentMinute => currentMinute;

    public float TotalElapsedSeconds { get; private set; }

    public event System.Action OnMinuteChanged;
    public event System.Action OnDayChanged;

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }

        currentDay    = Mathf.Max(1, startDay);
        currentHour   = Mathf.Clamp(startHour, 0, 23);
        currentMinute = 0;
    }

    private void OnDestroy() => Services.Unregister(this);

    private void Update()
    {
        TotalElapsedSeconds += Time.deltaTime;

        minuteTimer += Time.deltaTime;
        while (minuteTimer >= secondsPerGameMinute)
        {
            minuteTimer -= secondsPerGameMinute;
            AdvanceMinute();
        }
    }

    private void AdvanceMinute()
    {
        currentMinute++;
        if (currentMinute >= 60)
        {
            currentMinute = 0;
            currentHour++;
            if (currentHour >= 24)
            {
                currentHour = 0;
                currentDay++;
                OnDayChanged?.Invoke();
            }
        }
        OnMinuteChanged?.Invoke();
    }

    // 구버전 세이브 -1이면 기본 시각
    public void RestoreTime(int day, int hour, int minute)
    {
        if (day < 1 || hour < 0 || hour > 23 || minute < 0 || minute > 59) return;
        currentDay    = day;
        currentHour   = hour;
        currentMinute = minute;
        minuteTimer   = 0f;
        OnMinuteChanged?.Invoke();
    }

    public string GetTimeString() => $"{currentHour:00}:{currentMinute:00}";

    public string GetDateTimeString() => $"Day {currentDay}   {GetTimeString()}";
}
