using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CoreProgressHUD : MonoBehaviour
{
    private const float RefreshInterval = 0.5f;
    private const float BannerTime      = 4f;
    private const int   ChipsPerRow     = 4;

    private static CoreProgressHUD instance;

    private RectTransform   cardRT;
    private TextMeshProUGUI levelText;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI nextText;
    private Transform       chipArea;
    private float           nextRefresh;
    private int             builtForLevel = -1;
    private readonly List<Chip> chips = new();

    private class Chip
    {
        public CoreRequirement req;
        public Image           bg;
        public TextMeshProUGUI text;
    }

    private RectTransform   coinsRT, ordersRT, clockRT, weatherRT;
    private Canvas          canvas;
    private float           nextHudLookup;
    private readonly Vector3[] corners = new Vector3[4];

    private RectTransform   bannerRT;
    private CanvasGroup     bannerCG;
    private TextMeshProUGUI bannerTitle;
    private TextMeshProUGUI bannerBody;
    private Coroutine       bannerCo;

    public static void Ensure()
    {
        if (instance != null) return;
        var go = new GameObject("CoreProgressHUD", typeof(RectTransform));
        // 창 아래, 날씨 효과 위
        UIStyleKit.CreateOverlayCanvas(go, -1);
        instance = go.AddComponent<CoreProgressHUD>();
        instance.Build();
    }

    public static RectTransform CardRect => instance != null && instance.cardRT.gameObject.activeInHierarchy ? instance.cardRT : null;

    public static RectTransform ChipRect(CoreRequirement req)
    {
        if (instance == null || req == null || !instance.cardRT.gameObject.activeInHierarchy) return null;
        foreach (var c in instance.chips) if (c.req == req) return c.bg.rectTransform;
        return null;
    }

    private void OnEnable()  => CenterCore.OnAnyLevelUp += HandleLevelUp;
    private void OnDisable() => CenterCore.OnAnyLevelUp -= HandleLevelUp;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        if (bannerRT != null) Destroy(bannerRT.root.gameObject);
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + RefreshInterval;
        Refresh();
    }

    private void LateUpdate() => AvoidOtherHud();

    // 폰에서 위쪽 HUD랑 겹침 방지
    private void AvoidOtherHud()
    {
        if (!cardRT.gameObject.activeSelf) return;
        if ((coinsRT == null || ordersRT == null || clockRT == null || weatherRT == null) && Time.unscaledTime >= nextHudLookup)
        {
            nextHudLookup = Time.unscaledTime + 1f;
            if (coinsRT   == null) coinsRT   = CurrencyHUD.Instance?.transform as RectTransform;
            if (ordersRT  == null && MobileMode.Active) ordersRT = ShuttleUI.Instance?.HudPill;
            if (clockRT   == null) clockRT   = GameClockUI.Instance?.transform as RectTransform;
            if (weatherRT == null) weatherRT = WeatherHUD.Instance?.transform as RectTransform;
        }

        const float Gap = 10f;
        if (canvas == null) canvas = GetComponent<Canvas>();
        float scale  = canvas.scaleFactor;
        float half   = cardRT.rect.width * 0.5f * scale;
        float center = Screen.width * 0.5f;
        float left   = Mathf.Max(Edge(coinsRT, right: true), Edge(ordersRT, right: true)) + Gap;
        float right  = Mathf.Min(Edge(clockRT, right: false), Edge(weatherRT, right: false)) - Gap;

        float x = Mathf.Max(center, left + half);
        if (x + half > right) x = (left + right) * 0.5f;
        cardRT.anchoredPosition = new Vector2((x - center) / scale, cardRT.anchoredPosition.y);
    }

    private float Edge(RectTransform rt, bool right)
    {
        if (rt == null || !rt.gameObject.activeInHierarchy) return right ? 0f : Screen.width;
        rt.GetWorldCorners(corners);
        return right ? corners[2].x : corners[0].x;
    }

    private void OnCardClicked()
    {
        var core = CenterCore.Current;
        var ui   = CenterCoreUI.Instance;
        if (core == null || ui == null) return;
        if (ui.IsOpen) ui.Close();
        else ui.Open(core, remote: true);
    }

    private void Refresh()
    {
        var core = CenterCore.Current;
        cardRT.gameObject.SetActive(core != null);
        if (core == null) return;

        levelText.text = $"문명 Lv.{core.Level}";
        int pop = ShuttleService.Population;
        titleText.text = (core.CurrentStage?.title ?? "") +
                         (pop > 0 ? $"  <size=80%><color=#{Hex(CuteUIKit.CocoaSoft)}>주민 {pop}</color></size>" : "");

        if (builtForLevel != core.Level) BuildChips(core);

        var next = core.NextStage;
        if (next == null) return;
        int met = 0;
        foreach (var c in chips)
        {
            bool ok = CoreProgress.IsMet(c.req);
            if (ok) met++;
            c.bg.color   = ok ? CuteUIKit.MintTag : CuteUIKit.LatteLight;
            c.text.color = ok ? CuteUIKit.MintText : CuteUIKit.Cocoa;
            c.text.text  = $"{ShortLabel(c.req)} {CoreProgress.ProgressText(c.req)}";
        }
        nextText.text = $"다음: Lv.{core.Level + 1} {next.title}  <color=#{Hex(met == chips.Count ? CuteUIKit.MintText : CuteUIKit.CocoaSoft)}>{met}/{chips.Count}</color>";
    }

    private void BuildChips(CenterCore core)
    {
        builtForLevel = core.Level;
        chips.Clear();
        for (int i = chipArea.childCount - 1; i >= 0; i--)
        {
            var c = chipArea.GetChild(i).gameObject;
            c.SetActive(false);
            Destroy(c);
        }

        var next = core.NextStage;
        if (next == null)
        {
            nextText.text = $"<color=#{Hex(CuteUIKit.MintText)}>새 고향 완성!</color>";
            return;
        }

        int n = next.requirements.Count;
        int perRow = n > ChipsPerRow ? Mathf.CeilToInt(n / 2f) : ChipsPerRow;
        Transform row = null;
        for (int i = 0; i < n; i++)
        {
            if (i % perRow == 0) row = Row(chipArea, 5f);
            chips.Add(MakeChip(row, next.requirements[i]));
        }
    }

    private Chip MakeChip(Transform parent, CoreRequirement req)
    {
        var bg = CuteUIKit.Card(parent, CuteUIKit.LatteLight, 9, "Chip");
        bg.raycastTarget = false;
        var hlg = bg.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(4, 8, 2, 2);
        hlg.spacing                = 3f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        var iconGO = new GameObject("Icon", typeof(RectTransform));
        iconGO.transform.SetParent(bg.transform, false);
        var icon = iconGO.AddComponent<Image>();
        icon.sprite         = ChipIcon(req);
        icon.preserveAspect = true;
        icon.raycastTarget  = false;
        icon.enabled        = icon.sprite != null;
        var le = iconGO.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = le.minWidth = le.minHeight = 20f;

        var text = CuteUIKit.Text(bg.transform, "", 12f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        text.raycastTarget = false;
        return new Chip { req = req, bg = bg, text = text };
    }

    public static Sprite ChipIcon(CoreRequirement r) => r.type switch
    {
        CoreRequirementType.Facility      => r.item != null ? r.item.icon : null,
        CoreRequirementType.CropVarieties => NetWorld.Item("RainbowCarrot")?.icon ?? NetWorld.Item("BlueStar")?.icon,
        CoreRequirementType.Orders        => HudDock.RocketIcon,
        CoreRequirementType.Residents     => HudDock.CatIcon,
        CoreRequirementType.Coins         => CuteUIKit.Coin(),
        CoreRequirementType.Research      => HudDock.FlaskIcon,
        _                                 => null,
    };

    public static string ShortLabel(CoreRequirement r) => r.type switch
    {
        CoreRequirementType.Facility      => r.item != null ? r.item.itemName : "?",
        CoreRequirementType.CropVarieties => "작물 종류",
        CoreRequirementType.Research      => TechTreeManager.Instance?.GetNode(r.techNodeId)?.displayName ?? r.techNodeId,
        CoreRequirementType.Coins         => "발전 자금",
        CoreRequirementType.Orders        => "셔틀",
        CoreRequirementType.Residents     => "주민",
        _                                 => "?",
    };

    private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

    private void HandleLevelUp(CenterCore core, int level)
    {
        Refresh();
        if (core.IsMaxLevel) return;
        if (LevelUpUI.WillShow) return;
        var stage = core.CurrentStage;
        bannerTitle.text = "문명 발전!";
        bannerBody.text  = $"Lv.{level} {stage?.title}\n<size=12><color=#{Hex(CuteUIKit.CocoaSoft)}>{stage?.description}</color></size>";
        if (bannerCo != null) StopCoroutine(bannerCo);
        bannerCo = StartCoroutine(ShowBanner());
    }

    private IEnumerator ShowBanner()
    {
        bannerRT.gameObject.SetActive(true);
        CuteUIKit.WiggleEars(this, bannerRT);
        yield return CuteUIKit.PopIn(bannerRT, bannerCG, 0.35f);
        yield return new WaitForSecondsRealtime(BannerTime);
        yield return CuteUIKit.PopOut(bannerRT, bannerCG, 0.25f);
        bannerRT.gameObject.SetActive(false);
        bannerCo = null;
    }

    private static Transform Row(Transform parent, float spacing)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = spacing;
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        return go.transform;
    }

    private void Build()
    {
        var card = CuteUIKit.Card(transform, CuteUIKit.Latte, 14, "Card");
        cardRT = card.rectTransform;
        cardRT.anchorMin = cardRT.anchorMax = cardRT.pivot = new Vector2(0.5f, 1f);
        cardRT.anchoredPosition = new Vector2(0f, -12f);
        CuteUIKit.Layer(card.transform, "Fill", CuteUIKit.Rounded(12), CuteUIKit.Cream, 2f);

        var vlg = card.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(12, 12, 7, 8);
        vlg.spacing                = 5f;
        vlg.childAlignment         = TextAnchor.UpperCenter;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        var fit = card.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        var top = Row(card.transform, 8f);
        top.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
        CuteUIKit.Pill(top, "문명 Lv.1", CuteUIKit.LavenderTag, CuteUIKit.LavenderText, 13f, 10,
                       new RectOffset(9, 9, 2, 2), out levelText);
        titleText = CuteUIKit.Text(top, "", 16f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        titleText.richText = true;
        var spacer = new GameObject("Spacer", typeof(RectTransform));
        spacer.transform.SetParent(top, false);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;
        nextText = CuteUIKit.Text(top, "", 12f, CuteUIKit.CocoaSoft, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        nextText.richText = true;

        var area = new GameObject("Chips", typeof(RectTransform));
        area.transform.SetParent(card.transform, false);
        var avl = area.AddComponent<VerticalLayoutGroup>();
        avl.spacing                = 4f;
        avl.childAlignment         = TextAnchor.UpperCenter;
        avl.childControlWidth      = true;
        avl.childControlHeight     = true;
        avl.childForceExpandWidth  = false;
        avl.childForceExpandHeight = false;
        chipArea = area.transform;

        card.raycastTarget = true;
        foreach (var g in card.GetComponentsInChildren<Graphic>()) if (g != card) g.raycastTarget = false;
        var button = card.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(OnCardClicked);
        card.gameObject.AddComponent<UiButtonSfx>();
        card.gameObject.AddComponent<CutePop>();

        var bannerRoot = new GameObject("CoreLevelUpBanner", typeof(RectTransform));
        UIStyleKit.CreateOverlayCanvas(bannerRoot, 450);
        bannerRoot.GetComponent<GraphicRaycaster>().enabled = false;

        var bannerGO = new GameObject("Banner", typeof(RectTransform));
        bannerGO.transform.SetParent(bannerRoot.transform, false);
        bannerRT = (RectTransform)bannerGO.transform;
        bannerRT.anchorMin = bannerRT.anchorMax = bannerRT.pivot = new Vector2(0.5f, 0f);
        bannerRT.anchoredPosition = new Vector2(0f, 150f);
        bannerRT.sizeDelta = new Vector2(420f, 0f);
        bannerGO.AddComponent<Image>();
        CuteUIKit.DecorateWindow(bannerRT, catEars: true, earInset: 70f);
        bannerCG = bannerGO.AddComponent<CanvasGroup>();
        bannerCG.blocksRaycasts = false;

        var bvlg = bannerGO.AddComponent<VerticalLayoutGroup>();
        bvlg.padding                = new RectOffset(20, 20, 14, 16);
        bvlg.spacing                = 4f;
        bvlg.childControlWidth      = true;
        bvlg.childControlHeight     = true;
        bvlg.childForceExpandWidth  = true;
        bvlg.childForceExpandHeight = false;
        bannerGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        bannerTitle = CuteUIKit.Text(bannerGO.transform, "", 18f, CuteUIKit.Strawberry, FontStyles.Bold);
        bannerBody  = CuteUIKit.Text(bannerGO.transform, "", 15f, CuteUIKit.Cocoa, FontStyles.Bold);
        bannerBody.textWrappingMode = TextWrappingModes.Normal;
        bannerBody.richText = true;
        foreach (var g in bannerGO.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
        bannerRT.gameObject.SetActive(false);

        Refresh();
    }
}
