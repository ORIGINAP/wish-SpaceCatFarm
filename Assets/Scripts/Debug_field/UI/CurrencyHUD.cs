using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CurrencyHUD : MonoBehaviour
{
    private const float CountUpDuration = 0.6f;
    private const float PulseDuration   = 0.25f;

    private static readonly Color BaseTextColor  = CuteUIKit.Cocoa;
    private static readonly Color FlashTextColor = CuteUIKit.TextOn(CuteUIKit.ButtonButter);
    private static readonly Color FlashFillColor = CuteUIKit.ButterSoft;

    private TextMeshProUGUI amountText;
    private RectTransform   amountRT;
    private RectTransform   coinRT;
    private Image           cardFill;

    private double displayed = -1;
    private double rate;
    private long   lastTarget = -1;
    private float  pulseTime;

    // double 정수 정밀도 한계
    private const long ExactDoubleLimit = 1L << 53;

    public static CurrencyHUD Instance => Services.Get<CurrencyHUD>();

    private void Awake() => Services.Register(this);

    private void OnDestroy() => Services.Unregister(this);

    public static CurrencyHUD Create(Transform parent, Vector2 anchoredPos, TMP_FontAsset font)
    {
        var go = new GameObject("CurrencyHUD", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = anchoredPos;

        CuteUIKit.DecorateWindow(rt, catEars: false, radius: 18, border: 3);

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.padding              = new RectOffset(9, 16, 7, 7);
        hlg.spacing              = 7f;
        hlg.childAlignment       = TextAnchor.MiddleCenter;
        hlg.childControlWidth    = true;
        hlg.childControlHeight   = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        var csf = go.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        var hud = go.AddComponent<CurrencyHUD>();
        var fillTf = go.transform.Find("Cute_Fill");
        if (fillTf != null) hud.cardFill = fillTf.GetComponent<Image>();

        var coinGO = new GameObject("Coin", typeof(RectTransform));
        coinGO.transform.SetParent(go.transform, false);
        var coin = coinGO.AddComponent<Image>();
        coin.sprite         = CuteUIKit.Coin();
        coin.preserveAspect = true;
        coin.raycastTarget  = false;
        var coinLE = coinGO.AddComponent<LayoutElement>();
        coinLE.preferredWidth  = 22f;
        coinLE.preferredHeight = 22f;
        hud.coinRT = (RectTransform)coinGO.transform;

        hud.amountText = CuteUIKit.Text(go.transform, "", 18f, BaseTextColor, FontStyles.Bold);
        hud.amountText.name = "Amount";
        if (font != null) hud.amountText.font = font;
        hud.amountRT = hud.amountText.rectTransform;
        hud.SetText(0);

        return hud;
    }

    private void Update()
    {
        var cm = CurrencyManager.Instance;
        if (cm == null || amountText == null) return;

        long target = cm.Currency;
        float dt = Time.unscaledDeltaTime;

        if (target >= ExactDoubleLimit)
        {
            if (target != lastTarget)
            {
                if (lastTarget >= 0 && target > lastTarget) pulseTime = PulseDuration;
                SetText(target);
            }
            displayed  = target;
            lastTarget = target;
        }
        else if (displayed < 0 || target < displayed)
        {
            displayed  = target;
            lastTarget = target;
            SetText(target);
        }
        else if (target != lastTarget)
        {
            lastTarget = target;
            rate       = (target - displayed) / CountUpDuration;
            pulseTime  = PulseDuration;
        }

        if (displayed < target && target < ExactDoubleLimit)
        {
            displayed = System.Math.Min(target, displayed + System.Math.Max(rate, 1.0) * dt);
            SetText((long)displayed);
        }

        if (pulseTime > 0f)
        {
            pulseTime = Mathf.Max(0f, pulseTime - dt);
            float p = pulseTime / PulseDuration;
            amountText.color    = Color.Lerp(BaseTextColor, FlashTextColor, p);
            amountRT.localScale = Vector3.one * (1f + 0.12f * p);
            if (cardFill != null) cardFill.color = Color.Lerp(CuteUIKit.Cream, FlashFillColor, p);
            if (coinRT != null)
            {
                coinRT.localScale    = Vector3.one * (1f + 0.3f * p);
                coinRT.localRotation = Quaternion.Euler(0f, 0f, 14f * Mathf.Sin(p * Mathf.PI * 3f) * p);
            }
        }
    }

    private void SetText(long value) => amountText.text = $"{CurrencyManager.Format(value)}<size=72%> 코인</size>";
}
