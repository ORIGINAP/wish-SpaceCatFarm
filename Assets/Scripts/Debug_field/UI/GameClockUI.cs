using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameClockUI : MonoBehaviour
{
    private TextMeshProUGUI dayText;
    private TextMeshProUGUI clockText;
    private int             shownDay = -1;

    public static GameClockUI Instance => Services.Get<GameClockUI>();

    private void Awake()
    {
        Services.Register(this);
        var selfRT = GetComponent<RectTransform>();
        if (selfRT == null) selfRT = gameObject.AddComponent<RectTransform>();

        selfRT.anchorMin = new Vector2(1f, 1f);
        selfRT.anchorMax = new Vector2(1f, 1f);
        selfRT.pivot     = new Vector2(1f, 1f);
        // 도움말 버튼 피해서
        selfRT.anchoredPosition = new Vector2(-95f, -20f);
        selfRT.sizeDelta        = new Vector2(150f, 40f);

        CuteUIKit.DecorateWindow(selfRT, catEars: false, radius: 20, border: 3);

        var hlg = gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(9, 15, 0, 0);
        hlg.spacing                = 8f;
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        var dayPill = CuteUIKit.Pill(transform, "Day", CuteUIKit.SkyTag, CuteUIKit.SkyText, 12f, 11,
                                     new RectOffset(9, 9, 3, 3), out dayText);
        dayPill.name = "DayPill";

        clockText = CuteUIKit.Text(transform, "--:--", 16f, CuteUIKit.Cocoa, FontStyles.Bold);
        clockText.name = "ClockText";
        clockText.gameObject.AddComponent<LayoutElement>().preferredWidth = 50f;
    }

    private void Start()
    {
        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.OnMinuteChanged += Refresh;
        Refresh();
    }

    private void OnDestroy()
    {
        Services.Unregister(this);
        if (GameTimeManager.Instance != null)
            GameTimeManager.Instance.OnMinuteChanged -= Refresh;
    }

    private void Refresh()
    {
        if (clockText == null) return;

        var gtm = GameTimeManager.Instance;
        if (gtm == null)
        {
            clockText.text = "--:--";
            return;
        }

        if (gtm.CurrentDay != shownDay)
        {
            shownDay     = gtm.CurrentDay;
            dayText.text = $"Day {shownDay}";
        }
        clockText.text = gtm.GetTimeString();
    }
}
