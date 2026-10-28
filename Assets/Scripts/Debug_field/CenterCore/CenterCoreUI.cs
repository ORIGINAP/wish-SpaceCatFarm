using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using System.Text;

[RequireComponent(typeof(RectTransform))]
public class CenterCoreUI : MonoBehaviour
{
    public static CenterCoreUI Instance => Services.Get<CenterCoreUI>();

    private const float AnimDuration = 0.18f;
    private const float RefreshInterval = 0.5f;

    private static readonly Color  LevelTextColor = CuteUIKit.TextOn(CuteUIKit.Butter);
    private static readonly string StarOn  = $"<color=#{ColorUtility.ToHtmlStringRGB(CuteUIKit.Strawberry)}>■</color>";
    private static readonly string StarOff = $"<color=#{ColorUtility.ToHtmlStringRGB(CuteUIKit.CocoaMuted)}>□</color>";

    private RectTransform   panelRT;
    private CanvasGroup     panelCG;
    private CenterCore      currentCore;
    private bool            isOpen = false;
    public  bool            IsOpen => isOpen;
    private bool            remote;
    private Coroutine       animCo;
    private float           nextRefresh;

    private Image           levelPill;
    private TextMeshProUGUI levelText;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI starsText;
    private TextMeshProUGUI descText;
    private TextMeshProUGUI effectText;
    private TextMeshProUGUI nextHeaderText;
    private TextMeshProUGUI nextCountText;
    private RectTransform   reqContainer;
    private TextMeshProUGUI unlockText;
    private int             builtForLevel = -1;
    private readonly List<RequirementRow> rows = new();

    private class RequirementRow
    {
        public CoreRequirement req;
        public Image           fill;
        public TextMeshProUGUI mark;
        public TextMeshProUGUI label;
        public TextMeshProUGUI progress;
    }

    private Coroutine levelUpCo;

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }

        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        BuildPanel();
        panelRT.localScale         = Vector3.zero;
        panelCG.alpha              = 0f;
        panelCG.blocksRaycasts     = false;
        panelCG.interactable       = false;
    }

    private void OnDestroy()
    {
        UnsubscribeCore();
        Services.Unregister(this);
    }

    private static readonly float AUTO_CLOSE_DIST = 5f;
    private const float SideBySideOffset = 215f;
    private const float SideBySideTop    = 230f;
    private Transform playerTransform;

    private void Update()
    {
        if (!isOpen) return;

        if (currentCore == null) { Close(); return; }

        // 다른 창이 같이 닫히지 않게
        if (EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) { Close(); return; }

        if (!remote)
        {
            if (playerTransform == null)
            {
                var ptc = PlayerTileController.Instance;
                if (ptc != null) playerTransform = ptc.transform;
            }

            if (playerTransform != null)
            {
                float cellSize = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
                if (currentCore.DistanceFrom(playerTransform.position) > AUTO_CLOSE_DIST * cellSize) { Close(); return; }
            }
        }

        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + RefreshInterval;
            RefreshProgress();
        }
    }

    public void Open(CenterCore core) => Open(core, false);

    public void Open(CenterCore core, bool remote)
    {
        if (core == null) return;
        bool wasOpen = isOpen;

        UnsubscribeCore();

        currentCore = core;
        currentCore.OnChanged += RefreshAll;
        currentCore.OnLevelUp += OnLevelUp;
        this.remote = remote;
        isOpen = true;

        RefreshAll();

        panelRT.pivot = new Vector2(0.5f, 1f);
        if (remote || !core.UsesFuel)
        {
            panelRT.anchoredPosition = new Vector2(0f, SideBySideTop);
        }
        else
        {
            panelRT.anchoredPosition = new Vector2(-SideBySideOffset, SideBySideTop);
            GeneratorFuelUI.Instance?.Open(core, new Vector2(SideBySideOffset, SideBySideTop));
        }

        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(true));

        if (!wasOpen) Sfx.Play(SfxId.UiOpen);
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        var fuelUI = GeneratorFuelUI.Instance;
        if (fuelUI != null && fuelUI.IsOpen && !(currentCore is null) && fuelUI.Current == currentCore) fuelUI.Close();
        UnsubscribeCore();
        currentCore = null;
        StopLevelUpFlash();
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(false));
        Sfx.Play(SfxId.UiClose);
    }

    // 파괴된 코어도 구독은 남아 있음
    private void UnsubscribeCore()
    {
        if (currentCore is null) return;
        currentCore.OnChanged -= RefreshAll;
        currentCore.OnLevelUp -= OnLevelUp;
    }

    private IEnumerator Animate(bool open)
    {
        if (open)
        {
            panelCG.blocksRaycasts = true;
            panelCG.interactable   = true;
            CuteUIKit.WiggleEars(this, panelRT);
            yield return CuteUIKit.PopIn(panelRT, panelCG, AnimDuration * 1.8f);
        }
        else
        {
            yield return CuteUIKit.PopOut(panelRT, panelCG, AnimDuration);
            panelCG.alpha          = 0f;
            panelCG.blocksRaycasts = false;
            panelCG.interactable   = false;
        }
    }

    private void RefreshAll()
    {
        if (currentCore == null) return;
        RefreshStage();
        if (builtForLevel != currentCore.Level) BuildRequirementRows();
        RefreshProgress();
    }

    private void RefreshStage()
    {
        var core  = currentCore;
        var stage = core.CurrentStage;
        levelText.text = $"Lv.{core.Level}";
        titleText.text = stage != null ? stage.title : "";

        var sb = new StringBuilder();
        for (int i = 1; i <= core.MaxLevel; i++)
            sb.Append(i <= core.Level ? StarOn : StarOff);
        starsText.text = sb.ToString();

        descText.text   = stage != null ? stage.description : "";
        effectText.text = core.UsesFuel
            ? $"연료통 {core.MaxFuel:0} · 전기 범위 {core.DirectSupplyRadius}칸 (전력 중계탑으로 넓혀요)"
            : $"연료 없이 늘 가동 · 전기 범위 {core.DirectSupplyRadius}칸 (전력 중계탑으로 넓혀요)";
    }

    private void BuildRequirementRows()
    {
        builtForLevel = currentCore.Level;
        foreach (Transform child in reqContainer) Destroy(child.gameObject);
        rows.Clear();

        var next = currentCore.NextStage;
        if (next == null)
        {
            nextHeaderText.text = "새 고향 완성!";
            nextCountText.text  = "";
            var done = CuteUIKit.Text(reqContainer, "모든 문명 단계를 달성했어요. 큰 이주선이 고향별에 남은 고양이들을 모두 태우고 내려앉았어요!",
                                      12f, CuteUIKit.MintText, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            done.textWrappingMode = TextWrappingModes.Normal;
            unlockText.text = "";
            return;
        }

        int nextLevel = currentCore.Level + 1;
        nextHeaderText.text = $"Lv.{nextLevel} {next.title}";
        foreach (var req in next.requirements)
            rows.Add(BuildRow(req));

        var parts = new List<string>();
        var recipes    = CenterCore.RecipesUnlockedAt(nextLevel);
        var facilities = recipes.FindAll(r => CivFacility.IsFacilityItem(r.outputItem));
        var dishes     = recipes.FindAll(r => r.IsKitchenRecipe);
        var others     = recipes.FindAll(r => !CivFacility.IsFacilityItem(r.outputItem) && !r.IsKitchenRecipe);
        if (facilities.Count > 0) parts.Add("문명 시설 " + string.Join(", ", facilities.ConvertAll(r => r.outputItem.itemName)));
        if (others.Count > 0)     parts.Add("제작 " + string.Join(", ", others.ConvertAll(r => r.outputItem.itemName)));
        if (dishes.Count > 0)     parts.Add("요리 " + string.Join(", ", dishes.ConvertAll(r => r.outputItem.itemName)));
        var items = CenterCore.ItemsUnlockedAt(nextLevel);
        if (items.Count > 0) parts.Add("새 씨앗 " + string.Join(", ", items.ConvertAll(i => i.itemName)));
        unlockText.text = parts.Count > 0 ? $"해금 예정: {string.Join(" · ", parts)}" : "";
    }

    private RequirementRow BuildRow(CoreRequirement req)
    {
        var card = CuteUIKit.Card(reqContainer, CuteUIKit.LatteLight, 10, "Requirement");
        card.raycastTarget = false;
        var hlg = card.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(8, 10, 4, 4);
        hlg.spacing                = 6f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        card.gameObject.AddComponent<LayoutElement>().minHeight = 30f;

        var row = new RequirementRow { req = req, fill = card };
        row.mark = CuteUIKit.Text(card.transform, "-", 13f, CuteUIKit.CocoaMuted, FontStyles.Bold);
        row.mark.gameObject.AddComponent<LayoutElement>().preferredWidth = 14f;

        if (req.type == CoreRequirementType.Facility && req.item != null && req.item.icon != null)
        {
            var iconGO = new GameObject("Icon", typeof(RectTransform));
            iconGO.transform.SetParent(card.transform, false);
            var icon = iconGO.AddComponent<Image>();
            icon.sprite         = req.item.icon;
            icon.preserveAspect = true;
            icon.raycastTarget  = false;
            var le = iconGO.AddComponent<LayoutElement>();
            le.preferredWidth = le.preferredHeight = 22f;
        }

        row.label = CuteUIKit.Text(card.transform, CoreProgress.Label(req), 12f, CuteUIKit.Cocoa, FontStyles.Normal,
                                   TextAlignmentOptions.MidlineLeft);
        row.label.overflowMode = TextOverflowModes.Ellipsis;
        var lle = row.label.gameObject.AddComponent<LayoutElement>();
        lle.preferredWidth = 10f;
        lle.flexibleWidth  = 1f;

        row.progress = CuteUIKit.Text(card.transform, "", 12f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        return row;
    }

    private void RefreshProgress()
    {
        if (currentCore == null) return;
        int met = 0;
        foreach (var row in rows)
        {
            bool ok = CoreProgress.IsMet(row.req);
            if (ok) met++;
            row.mark.text      = ok ? "O" : "-";
            row.mark.color     = ok ? CuteUIKit.MintText : CuteUIKit.CocoaMuted;
            row.fill.color     = ok ? CuteUIKit.MintTag : CuteUIKit.LatteLight;
            row.progress.text  = CoreProgress.ProgressText(row.req);
            row.progress.color = ok ? CuteUIKit.MintText : CuteUIKit.Cocoa;
        }
        if (rows.Count > 0) nextCountText.text = $"{met}/{rows.Count}";
    }

    private void OnLevelUp(int newLevel)
    {
        if (levelUpCo != null) StopCoroutine(levelUpCo);
        levelUpCo = StartCoroutine(LevelUpFlash());
    }

    private IEnumerator LevelUpFlash()
    {
        float t = 0f;
        while (t < 0.7f)
        {
            t += Time.unscaledDeltaTime;
            float pulse = (Mathf.Sin(t * Mathf.PI * 6f) + 1f) * 0.5f;
            levelPill.color = Color.Lerp(CuteUIKit.Butter, CuteUIKit.Strawberry, pulse);
            levelText.color = Color.Lerp(LevelTextColor, Color.white, pulse);
            panelRT.localScale = Vector3.one * (1f + pulse * 0.05f);
            yield return null;
        }
        levelPill.color    = CuteUIKit.Butter;
        levelText.color    = LevelTextColor;
        panelRT.localScale = Vector3.one;
        levelUpCo = null;
    }

    private void StopLevelUpFlash()
    {
        if (levelUpCo == null) return;
        StopCoroutine(levelUpCo);
        levelUpCo = null;
        levelPill.color = CuteUIKit.Butter;
        levelText.color = LevelTextColor;
    }

    private void BuildPanel()
    {
        panelRT = GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0.5f, 0.5f);
        panelRT.anchorMax        = new Vector2(0.5f, 0.5f);
        panelRT.pivot            = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = new Vector2(-50f, 30f);
        panelRT.sizeDelta        = new Vector2(400f, 0f);

        if (GetComponent<Image>() == null) gameObject.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT);

        panelCG = gameObject.AddComponent<CanvasGroup>();
        panelCG.alpha          = 0f;
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding               = new RectOffset(20, 20, 16, 18);
        vlg.spacing               = 7f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight= false;
        vlg.childControlWidth     = true;
        vlg.childControlHeight    = true;

        gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildHeader();
        BuildStageSection();
        CuteUIKit.DottedDivider(transform);
        BuildNextSection();
    }

    private void BuildHeader()
    {
        var go = new GameObject("Header", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        go.AddComponent<LayoutElement>().minHeight = 30f;

        CuteUIKit.TitleRibbon(go.transform, "센터코어 · 문명 단계");

        var close = CuteUIKit.CloseButton(go.transform, Close, 26f);
        close.GetComponent<LayoutElement>().ignoreLayout = true;
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
        crt.sizeDelta        = new Vector2(26f, 26f);
        crt.anchoredPosition = new Vector2(-13f, 1f);
    }

    private void BuildStageSection()
    {
        var go = new GameObject("StageRow", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = 8f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;

        var pill = CuteUIKit.Pill(go.transform, "Lv.1", CuteUIKit.Butter, LevelTextColor, 13f, 11,
                                  new RectOffset(10, 10, 3, 3), out levelText);
        pill.name = "LevelPill";
        levelPill = pill.GetComponent<Image>();

        titleText = CuteUIKit.Text(go.transform, "", 16f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        titleText.name = "StageTitle";

        starsText = CuteUIKit.Text(go.transform, "", 12f, CuteUIKit.CocoaMuted, align: TextAlignmentOptions.MidlineRight);
        starsText.name = "Stars";
        starsText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        descText = CuteUIKit.Text(transform, "", 11f, CuteUIKit.CocoaSoft, align: TextAlignmentOptions.TopLeft);
        descText.name = "StageDesc";
        descText.textWrappingMode = TextWrappingModes.Normal;

        effectText = CuteUIKit.Text(transform, "", 10f, CuteUIKit.LavenderText, FontStyles.Bold);
        effectText.name = "StageEffects";
        effectText.textWrappingMode = TextWrappingModes.Normal;
    }

    private void BuildNextSection()
    {
        var header = CuteUIKit.SectionHeader(transform, "다음 단계", CuteUIKit.MintTag, CuteUIKit.MintText);
        nextHeaderText = CuteUIKit.Text(header, "", 12f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        nextCountText  = CuteUIKit.Text(header, "", 11f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        nextCountText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var list = new GameObject("Requirements", typeof(RectTransform));
        list.transform.SetParent(transform, false);
        reqContainer = (RectTransform)list.transform;
        var lvlg = list.AddComponent<VerticalLayoutGroup>();
        lvlg.spacing                = 4f;
        lvlg.childForceExpandWidth  = true;
        lvlg.childForceExpandHeight = false;
        lvlg.childControlWidth      = true;
        lvlg.childControlHeight     = true;

        var hint = CuteUIKit.Text(transform, "조건을 모두 채우면 센터코어가 저절로 발전해요. 문명 시설은 제작창(C)의 '문명 시설'에서 만들어요.",
                                  10f, CuteUIKit.CocoaSoft, align: TextAlignmentOptions.TopLeft);
        hint.textWrappingMode = TextWrappingModes.Normal;

        unlockText = CuteUIKit.Text(transform, "", 11f, CuteUIKit.AmberText, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        unlockText.name = "Unlocks";
        unlockText.textWrappingMode = TextWrappingModes.Normal;
    }
}
