using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class WeatherHUD : MonoBehaviour
{
    private Image           icon;
    private TextMeshProUGUI nowText;
    private TextMeshProUGUI forecastText;

    public static WeatherHUD Instance => Services.Get<WeatherHUD>();

    public static WeatherHUD Create()
    {
        var clock = GameClockUI.Instance;
        if (clock == null) return null;

        var go = new GameObject("WeatherHUD", typeof(RectTransform));
        go.transform.SetParent(clock.transform.parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        var clockRT = (RectTransform)clock.transform;
        rt.anchoredPosition = clockRT.anchoredPosition + new Vector2(0f, -44f);
        rt.sizeDelta = new Vector2(150f, 30f);

        CuteUIKit.DecorateWindow(rt, catEars: false, radius: 15, border: 3);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(8, 12, 3, 3);
        hlg.spacing                = 6f;
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        go.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        var hit = go.AddComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0f);
        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;

        var hud = go.AddComponent<WeatherHUD>();
        btn.onClick.AddListener(hud.Explain);

        var iconGO = new GameObject("Icon", typeof(RectTransform));
        iconGO.transform.SetParent(go.transform, false);
        hud.icon = iconGO.AddComponent<Image>();
        hud.icon.preserveAspect = true;
        hud.icon.raycastTarget  = false;
        var le = iconGO.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = 22f;

        hud.nowText = CuteUIKit.Text(go.transform, "", 13f, CuteUIKit.Cocoa, FontStyles.Bold);
        hud.nowText.raycastTarget = false;
        hud.forecastText = CuteUIKit.Text(go.transform, "", 11f, CuteUIKit.CocoaSoft);
        hud.forecastText.raycastTarget = false;
        hud.Refresh();
        return hud;
    }

    private void Awake()     => Services.Register(this);
    private void OnDestroy() => Services.Unregister(this);

    private void OnEnable()  => WeatherManager.OnWeatherChanged += Refresh;
    private void OnDisable() => WeatherManager.OnWeatherChanged -= Refresh;

    private float nextRefresh;

    private void Update()
    {
        // 천문대 상태 따라 가끔 갱신
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 2f;
        Refresh();
    }

    private void Refresh()
    {
        if (nowText == null) return;
        var wm  = WeatherManager.Instance;
        var now = WeatherManager.Current;
        icon.sprite  = WeatherIcons.Get(now);
        icon.enabled = icon.sprite != null;
        nowText.text = WeatherManager.Name(now);
        if (wm == null) { forecastText.text = ""; return; }

        string f = $"다음 → {WeatherManager.Name(wm.Next)}";
        if (WeatherManager.ForecastDepth >= 2) f += $" → {WeatherManager.Name(wm.Next2)}";
        forecastText.text = f;
    }

    private void Explain()
    {
        var now = WeatherManager.Current;
        NotificationUI.Instance?.Show($"날씨: {WeatherManager.Name(now)} — {WeatherManager.Describe(now)} (6시간마다 바뀌어요 · 천문대가 있으면 더 멀리 내다봐요)");
    }
}
