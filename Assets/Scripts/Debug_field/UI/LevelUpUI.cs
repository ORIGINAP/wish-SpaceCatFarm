using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LevelUpUI : MonoBehaviour
{
    private static LevelUpUI instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    public static bool IsOpen => instance != null && instance.isOpen;

    public static bool WillShow => true;

    public static Transform Root => instance != null ? instance.transform : null;

    private const float Width    = 470f;
    private const float CardW    = 50f;
    private const float CardH    = 62f;
    private const int   Columns  = 8;

    private RectTransform   panelRT;
    private CanvasGroup     group;
    private RectTransform   ribbonRT;
    private TextMeshProUGUI ribbonText, titleText, descText;
    private Transform       body;
    private bool            isOpen;
    private Coroutine       popCo;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        CenterCore.OnAnyLevelUp -= HandleLevelUp;
        CenterCore.OnAnyLevelUp += HandleLevelUp;
    }

    private static void HandleLevelUp(CenterCore core, int level)
    {
        // 마지막 단계는 엔딩이 대신
        if (core != null && core.IsMaxLevel) return;
        if (instance == null) instance = new GameObject("LevelUpUI").AddComponent<LevelUpUI>();
        instance.Show(core, level);
    }

    private void Awake() => Build();

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        if (isOpen && EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) Hide();
    }

    private void Show(CenterCore core, int level)
    {
        var stage = core != null ? core.CurrentStage : null;
        ribbonText.text = $"문명 Lv.{level}";
        titleText.text  = stage != null ? $"「{stage.title}」" : "";
        descText.text   = stage != null ? stage.description : "";
        descText.gameObject.SetActive(!string.IsNullOrEmpty(descText.text));

        for (int i = body.childCount - 1; i >= 0; i--)
        {
            var child = body.GetChild(i).gameObject;
            // Destroy는 프레임 끝이라 먼저 끔
            child.SetActive(false);
            Destroy(child);
        }
        BuildSections(level);

        isOpen = true;
        group.blocksRaycasts = true;
        group.interactable   = true;
        if (popCo != null) StopCoroutine(popCo);
        popCo = StartCoroutine(CuteUIKit.PopIn(panelRT, group, 0.4f));
        CuteUIKit.WiggleEars(this, panelRT);
        CuteUIKit.SparkleBurst(this, ribbonRT, CuteUIKit.Butter, 16, 120f);
    }

    private void Hide()
    {
        if (!isOpen) return;
        isOpen = false;
        group.blocksRaycasts = false;
        group.interactable   = false;
        UIStyleKit.ClearSelectionIn(panelRT);
        if (popCo != null) StopCoroutine(popCo);
        popCo = StartCoroutine(CuteUIKit.PopOut(panelRT, group, 0.16f));
        Sfx.Play(SfxId.UiClose);
    }

    private void BuildSections(int level)
    {
        var made   = new List<FarmingItemData>();
        var dishes = new List<FarmingItemData>();
        foreach (var r in CenterCore.RecipesUnlockedAt(level))
        {
            if (r.outputItem == null) continue;
            var target = r.IsKitchenRecipe ? dishes : made;
            if (!target.Contains(r.outputItem)) target.Add(r.outputItem);
        }
        made.Sort((a, b) => a.Tier != b.Tier ? b.Tier.CompareTo(a.Tier) : string.CompareOrdinal(a.itemName, b.itemName));
        if (made.Count > 0) Section("새로 만들 수 있어요", CuteUIKit.MintTag, CuteUIKit.MintText, "제작창(C)에서", made);
        if (dishes.Count > 0) Section("새 요리", CuteUIKit.PinkSoft, CuteUIKit.CoralText, "고양이 주방에서", dishes);

        var seeds = CenterCore.ItemsUnlockedAt(level);
        if (seeds.Count > 0) Section("새 씨앗", CuteUIKit.ButterSoft, CuteUIKit.AmberText, "상점(B)에서 · 조금은 선물로", seeds);

        var research = new List<string>();
        if (TechTreeManager.Instance != null)
            foreach (var n in TechTreeManager.Instance.Nodes)
            {
                if (n.IsMaxed) continue;
                for (int lv = n.currentLevel; lv < n.maxLevel; lv++)
                {
                    if (n.coreLevels[lv] != level) continue;
                    research.Add(lv == 0 ? $"{n.displayName} (새로)" : $"{n.displayName} {lv + 1}단계");
                    break;
                }
            }
        if (research.Count > 0)
        {
            CuteUIKit.SectionHeader(body, "새 연구", CuteUIKit.LavenderTag, CuteUIKit.LavenderText, "연구(T)에서");
            var t = CuteUIKit.Text(body, string.Join(" · ", research), 12f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            t.textWrappingMode = TextWrappingModes.Normal;
        }

        if (body.childCount == 0)
            CuteUIKit.Text(body, "센터코어가 한층 더 발전했어요!", 12f, CuteUIKit.CocoaSoft);
    }

    private void Section(string label, Color tag, Color text, string note, List<FarmingItemData> items)
    {
        CuteUIKit.SectionHeader(body, label, tag, text, note);

        var gridGO = new GameObject("Grid", typeof(RectTransform));
        gridGO.transform.SetParent(body, false);
        var grid = gridGO.AddComponent<GridLayoutGroup>();
        grid.cellSize        = new Vector2(CardW, CardH);
        grid.spacing         = new Vector2(3f, 4f);
        grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Columns;
        grid.childAlignment  = TextAnchor.UpperLeft;

        foreach (var item in items)
        {
            var card = new GameObject("Card", typeof(RectTransform));
            card.transform.SetParent(gridGO.transform, false);

            var slot = CuteUIKit.Slot(card.transform, 40f, CuteUIKit.TierBorder(item, CuteUIKit.SlotBorder),
                                      CuteUIKit.TierFill(item, CuteUIKit.SlotFill), 12);
            var srt = (RectTransform)slot.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 1f);
            srt.anchoredPosition = Vector2.zero;
            slot.GetComponent<Image>().raycastTarget = false;
            if (item.icon != null)
            {
                var icon = new GameObject("Icon", typeof(RectTransform)).AddComponent<Image>();
                icon.transform.SetParent(slot.transform, false);
                icon.sprite         = item.icon;
                icon.preserveAspect = true;
                icon.raycastTarget  = false;
                icon.rectTransform.anchorMin = new Vector2(0.15f, 0.15f);
                icon.rectTransform.anchorMax = new Vector2(0.85f, 0.85f);
                icon.rectTransform.offsetMin = icon.rectTransform.offsetMax = Vector2.zero;
            }

            var itemName = CuteUIKit.Text(card.transform, item.itemName, 9f, CuteUIKit.Cocoa);
            itemName.textWrappingMode = TextWrappingModes.NoWrap;
            itemName.overflowMode     = TextOverflowModes.Ellipsis;
            var nrt = itemName.rectTransform;
            nrt.anchorMin = new Vector2(0f, 0f);
            nrt.anchorMax = new Vector2(1f, 0f);
            nrt.pivot     = new Vector2(0.5f, 0f);
            nrt.sizeDelta = new Vector2(0f, 16f);
            nrt.anchoredPosition = Vector2.zero;
        }
    }

    private void Build()
    {
        var canvasGO = new GameObject("LevelUpCanvas", typeof(RectTransform));
        canvasGO.transform.SetParent(transform, false);
        UIStyleKit.CreateOverlayCanvas(canvasGO, 440);

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(canvasGO.transform, false);
        panelRT = (RectTransform)panel.transform;
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = new Vector2(0f, 30f);
        group = UIStyleKit.CreatePanelRoot(panel, Width, new RectOffset(20, 20, 16, 18), 7f);

        var head = new GameObject("Head", typeof(RectTransform));
        head.transform.SetParent(panel.transform, false);
        var hlg = head.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        ribbonRT   = CuteUIKit.TitleRibbon(head.transform, "문명 Lv.1");
        ribbonText = ribbonRT.GetComponentInChildren<TextMeshProUGUI>();

        titleText = CuteUIKit.Text(panel.transform, "", 20f, CuteUIKit.Strawberry, FontStyles.Bold);
        descText  = CuteUIKit.Text(panel.transform, "", 11f, CuteUIKit.CocoaSoft);
        descText.textWrappingMode = TextWrappingModes.Normal;

        CuteUIKit.DottedDivider(panel.transform);

        var bodyGO = new GameObject("Body", typeof(RectTransform));
        bodyGO.transform.SetParent(panel.transform, false);
        var vlg = bodyGO.AddComponent<VerticalLayoutGroup>();
        vlg.spacing                = 6f;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        body = bodyGO.transform;

        var buttons = new GameObject("Buttons", typeof(RectTransform));
        buttons.transform.SetParent(panel.transform, false);
        var bl = buttons.AddComponent<HorizontalLayoutGroup>();
        bl.spacing = 8f;
        bl.childControlWidth = bl.childControlHeight = true;
        bl.childForceExpandWidth = bl.childForceExpandHeight = false;
        CuteUIKit.Button(buttons.transform, "여정 보기", CuteUIKit.ButtonSky, () => { Hide(); JourneyUI.Toggle(); }, 130f, 30f, 13f);
        var ok = CuteUIKit.Button(buttons.transform, "좋아요!", CuteUIKit.ButtonMint, Hide, -1f, 30f, 13f);
        ok.gameObject.AddComponent<GamepadDefault>();

        panelRT.localScale = Vector3.zero;
    }
}
