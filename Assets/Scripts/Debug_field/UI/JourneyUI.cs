using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class JourneyUI : MonoBehaviour
{
    public const KeyCode ToggleKey = KeyCode.L;

    private const float WinW       = 1120f;
    private const float WinH       = 700f;
    private const float ColW       = 148f;
    private const float Baseline   = -250f;
    private const float RoadY      = -268f;
    private const float CoreScale  = 1.4f;
    private const float DetailTop  = -336f;
    private const float DetailW    = 512f;
    private const float DetailH    = 342f;
    private const int   StageCount = 7;

    private static readonly Color Dim       = new(1f, 1f, 1f, 0.38f);
    private static readonly Color RowNow    = CuteUIKit.Lighter(CuteUIKit.PinkSoft, 0.2f);
    private static readonly Color RowDone   = CuteUIKit.Lighter(CuteUIKit.MintTag, 0.35f);

    private static JourneyUI instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    public static bool IsOpen => instance != null && instance.isOpen;

    public static Transform Root => instance != null ? instance.windowRT : null;

    private Canvas          canvas;
    private RectTransform   windowRT;
    private CanvasGroup     group;
    private bool            isOpen;
    private Coroutine       popCo;
    private TextMeshProUGUI subtitle;
    private TextMeshProUGUI footer;

    private class StageCol
    {
        public RectTransform core;
        public Image         coreImg;
        public Image         node, nodeFill;
        public RectTransform here;
        public Image         frame;
        public TextMeshProUGUI title;
        public float         coreY;
    }
    private readonly List<StageCol>      cols     = new();
    private readonly List<RectTransform> segFills = new();
    private readonly List<Image>         segPills = new();
    private readonly List<TextMeshProUGUI> segPillTexts = new();

    private Transform leftBody, rightBody;
    private TextMeshProUGUI hoverText;
    private int       selected = -1;
    private int       selectedStage = -1;

    public static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("JourneyUI", typeof(RectTransform));
        // 창 위, 길잡이 아래
        UIStyleKit.CreateOverlayCanvas(go, 300);
        instance = go.AddComponent<JourneyUI>();
        instance.canvas = go.GetComponent<Canvas>();
        instance.Build();
        instance.canvas.enabled = false;
    }

    public static void Toggle()
    {
        if (instance == null) Create();
        if (instance.isOpen) instance.Close();
        else instance.Open();
    }

    public static void CloseIfOpen()
    {
        if (IsOpen) instance.Close();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        bool key = !TextInputFocus.Active && !Lab.Active && GameInput.GetKeyDown(ToggleKey);
        if (!isOpen)
        {
            if (key && PlayerTileController.Instance != null && !PlayerTileController.IsPauseMenuOpen()) Open();
            return;
        }
        if (key || EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) { Close(); return; }
        Animate();
    }

    private void Open()
    {
        isOpen = true;
        canvas.enabled = true;
        group.blocksRaycasts = true;
        group.interactable   = true;
        footer.text = Pad.Active ? "단계를 고르면 그 장을 자세히 봐요 · B: 닫기" : "단계나 장을 누르면 자세히 봐요 · Esc / L: 닫기";
        RefreshPath();
        Select(DefaultChapter());
        if (popCo != null) StopCoroutine(popCo);
        popCo = StartCoroutine(CuteUIKit.PopIn(windowRT, group, 0.3f));
        CuteUIKit.WiggleEars(this, windowRT);
        Sfx.Play(SfxId.UiOpen);
    }

    private void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        group.blocksRaycasts = false;
        group.interactable   = false;
        UIStyleKit.ClearSelectionIn(windowRT);
        if (popCo != null) StopCoroutine(popCo);
        popCo = StartCoroutine(CloseRoutine());
        Sfx.Play(SfxId.UiClose);
    }

    private IEnumerator CloseRoutine()
    {
        yield return CuteUIKit.PopOut(windowRT, group, 0.16f, hideAtEnd: false);
        if (!isOpen) canvas.enabled = false;
    }

    private static int DefaultChapter()
    {
        var qm = QuestManager.Instance;
        int n  = ChapterCount;
        if (qm != null && qm.CurrentChapterIndex >= 0) return qm.CurrentChapterIndex;
        if (qm != null && qm.AllQuestsDone) return n - 1;
        return Mathf.Clamp(CenterCore.CurrentLevel - 1, 0, Mathf.Max(0, n - 1));
    }

    private static int ChapterCount => QuestChain.Load() != null ? QuestChain.Load().chapters.Count : 0;

    private static (int done, int total) ChapterProgress(int ch)
    {
        var chain = QuestChain.Load();
        var qm    = QuestManager.Instance;
        if (chain == null || ch < 0 || ch >= chain.chapters.Count) return (0, 0);
        int total = 0, done = 0;
        foreach (var q in chain.chapters[ch].quests)
        {
            if (q == null) continue;
            total++;
            if (QuestState(q) == State.Done) done++;
        }
        return (done, total);
    }

    private enum State { Done, Now, Later }

    private static State QuestState(QuestDefinition q)
    {
        var qm = QuestManager.Instance;
        if (qm == null) return State.Later;
        if (qm.AllQuestsDone) return State.Done;
        if (qm.CurrentQuest == q) return State.Now;
        var all = qm.AllQuests;
        int idx = -1;
        for (int i = 0; i < all.Count; i++) if (all[i] == q) { idx = i; break; }
        return idx >= 0 && idx < qm.CurrentQuestIndex ? State.Done : State.Later;
    }

    private void RefreshPath()
    {
        int level = CenterCore.CurrentLevel;
        var stages = CoreStageTable.Load();
        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            int lv = i + 1;
            var st = stages != null ? stages.Stage(lv) : null;
            bool reached = lv <= level;
            c.coreImg.color = reached ? Color.white : Dim;
            c.title.text    = st != null ? st.title : $"Lv.{lv}";
            c.title.color   = lv == level ? CuteUIKit.Strawberry : reached ? CuteUIKit.Cocoa : CuteUIKit.CocoaMuted;
            c.node.color     = reached ? CuteUIKit.MintText : CuteUIKit.Latte;
            c.nodeFill.color = lv == level ? CuteUIKit.Strawberry : reached ? CuteUIKit.ButtonMint : CuteUIKit.Cream;
            c.here.gameObject.SetActive(lv == level);
            var pad = c.core.GetComponent<GamepadDefault>();
            if (lv == level && pad == null) c.core.gameObject.AddComponent<GamepadDefault>();
            else if (lv != level && pad != null) Destroy(pad);
        }
        for (int ch = 0; ch < segFills.Count; ch++)
        {
            var (done, total) = ChapterProgress(ch);
            CuteUIKit.SetBar(segFills[ch], total > 0 ? (float)done / total : 0f);
        }
        var qm = QuestManager.Instance;
        int cur = qm != null ? qm.CurrentChapterIndex : -1;
        var chain = QuestChain.Load();
        subtitle.text = qm != null && qm.AllQuestsDone
            ? "모든 장을 마쳤어요! 큰 이주선이 고향별에 남은 고양이들을 모두 데려왔어요."
            : cur >= 0 && chain != null
                ? $"지금 <color=#{Hex(CuteUIKit.Strawberry)}><b>{cur + 1}장 · {chain.chapters[cur].title}</b></color> — {chain.chapters[cur].goal}"
                : "셔틀로 고향별 고양이들을 데려오며 문명을 키워요";
    }

    private void Animate()
    {
        int level = CenterCore.CurrentLevel;
        float t = Time.unscaledTime;
        for (int i = 0; i < cols.Count; i++)
        {
            var c = cols[i];
            bool now = i + 1 == level;
            c.core.anchoredPosition = new Vector2(c.core.anchoredPosition.x, c.coreY + (now ? Mathf.Abs(Mathf.Sin(t * 2.4f)) * 5f : 0f));
            if (now) c.here.localScale = Vector3.one * (1f + Mathf.Sin(t * 4f) * 0.05f);
        }
    }

    private void Select(int ch, int stage = -1)
    {
        int n = ChapterCount;
        if (n == 0) return;
        selected      = Mathf.Clamp(ch, 0, n - 1);
        selectedStage = stage >= 0 ? stage : selected;

        for (int i = 0; i < segPills.Count; i++)
        {
            bool on = i == selected;
            segPills[i].color     = on ? CuteUIKit.Strawberry : CuteUIKit.LatteLight;
            segPillTexts[i].color = on ? Color.white : CuteUIKit.CocoaSoft;
        }
        for (int i = 0; i < cols.Count; i++)
            cols[i].frame.enabled = i == selectedStage;

        Clear(leftBody);
        Clear(rightBody);
        BuildChapter(selected);
        BuildNextStage(selected);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)leftBody);
        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)rightBody);
    }

    private void BuildChapter(int ch)
    {
        var chain = QuestChain.Load();
        var chapter = chain.chapters[ch];
        var (done, total) = ChapterProgress(ch);

        var head = Row(leftBody, 8f);
        Pill(head, $"{ch + 1}장 · {chapter.title}", CuteUIKit.Strawberry, Color.white, 15f);
        Spacer(head);
        Pill(head, done >= total && total > 0 ? "다 마쳤어요!" : $"{done}/{total}",
             done >= total && total > 0 ? CuteUIKit.MintTag : CuteUIKit.Cream,
             done >= total && total > 0 ? CuteUIKit.MintText : CuteUIKit.Cocoa, 13f);

        var goal = CuteUIKit.Text(leftBody, $"목표 · {chapter.goal}", 13f, CuteUIKit.CocoaSoft, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        goal.textWrappingMode = TextWrappingModes.Normal;
        CuteUIKit.DottedDivider(leftBody);

        int step = 0;
        foreach (var q in chapter.quests)
        {
            if (q == null) continue;
            step++;
            var state = QuestState(q);
            var row = CuteUIKit.Card(leftBody, state == State.Now ? RowNow : state == State.Done ? RowDone : Color.clear, 9, "Quest");
            var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(5, 8, 2, 2);
            hl.spacing = 8f;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = hl.childForceExpandHeight = false;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;

            var dot = CuteUIKit.Card(row.transform, state == State.Done ? CuteUIKit.MintText : state == State.Now ? CuteUIKit.Strawberry : CuteUIKit.Latte, 10, "Dot");
            Size(dot.gameObject, 19f, 19f);
            if (state == State.Later) CuteUIKit.Layer(dot.transform, "In", CuteUIKit.Rounded(8), CuteUIKit.Cream, 2f);
            var mark = CuteUIKit.Text(dot.transform, state == State.Done ? "완" : step.ToString(), 11f,
                                      state == State.Later ? CuteUIKit.CocoaSoft : Color.white, FontStyles.Bold);
            Stretch(mark.rectTransform);

            var name = CuteUIKit.Text(row.transform, q.questName, 14f,
                                      state == State.Done ? CuteUIKit.MintText : state == State.Now ? CuteUIKit.Cocoa : CuteUIKit.CocoaSoft,
                                      state == State.Now ? FontStyles.Bold : FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            if (state == State.Now) Pill(row.transform, "지금", CuteUIKit.Strawberry, Color.white, 10f);

            if (q.rewards.Count > 0 && q.rewards[0].item != null) RewardChip(row.transform, q.rewards[0].item.icon, $"×{q.rewards[0].amount}");
            if (q.rewardCoins > 0) RewardChip(row.transform, CuteUIKit.Coin(), CurrencyManager.Format(q.rewardCoins));
        }
    }

    private void BuildNextStage(int ch)
    {
        int nextLv = ch + 2;
        var stages = CoreStageTable.Load();
        var stage  = stages != null && nextLv <= stages.MaxLevel ? stages.Stage(nextLv) : null;
        if (stage == null) return;
        int level = CenterCore.CurrentLevel;
        bool reached = level >= nextLv;
        bool live    = level == nextLv - 1;

        var head = Row(rightBody, 8f);
        Pill(head, $"Lv.{nextLv} {stage.title}", CuteUIKit.MintText, Color.white, 15f);
        CuteUIKit.Text(head, reached ? "에 올랐어요!" : (HasBatchim(stage.title) ? "이" : "가") + " 되려면", 13f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        Spacer(head);
        if (live && stage.requirements.Count > 0)
            Pill(head, $"{CoreProgress.MetCount(stage)}/{stage.requirements.Count}", CuteUIKit.Cream, CuteUIKit.Cocoa, 13f);

        if (!string.IsNullOrEmpty(stage.description))
        {
            var desc = CuteUIKit.Text(rightBody, stage.description, 12f, CuteUIKit.CocoaSoft, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            desc.textWrappingMode = TextWrappingModes.Normal;
        }

        var grid = Grid(rightBody, new Vector2(236f, 30f), 2, new Vector2(8f, 6f));
        foreach (var r in stage.requirements)
        {
            if (r == null) continue;
            bool met = reached || (live && CoreProgress.IsMet(r));
            var chip = CuteUIKit.Card(grid, met ? CuteUIKit.MintTag : CuteUIKit.Cream, 9, "Req");
            var hl = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(5, 8, 2, 2);
            hl.spacing = 5f;
            hl.childAlignment = TextAnchor.MiddleLeft;
            hl.childControlWidth = hl.childControlHeight = true;
            hl.childForceExpandWidth = hl.childForceExpandHeight = false;
            Icon(chip.transform, CoreProgressHUD.ChipIcon(r), 22f);
            var label = CuteUIKit.Text(chip.transform, CoreProgressHUD.ShortLabel(r), 13f, met ? CuteUIKit.MintText : CuteUIKit.Cocoa,
                                       FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            string amount = met ? "완료" : live ? CoreProgress.ProgressText(r)
                          : r.type == CoreRequirementType.Coins ? CurrencyManager.Format(CoreProgress.Target(r))
                          : $"×{CoreProgress.Target(r)}";
            CuteUIKit.Text(chip.transform, amount, 12f, met ? CuteUIKit.MintText : CuteUIKit.CocoaSoft, FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        }

        var unlocks = new List<FarmingItemData>();
        foreach (var rcp in CenterCore.RecipesUnlockedAt(nextLv))
            if (rcp.outputItem != null && !unlocks.Contains(rcp.outputItem)) unlocks.Add(rcp.outputItem);
        foreach (var it in CenterCore.ItemsUnlockedAt(nextLv))
            if (it != null && !unlocks.Contains(it)) unlocks.Add(it);
        if (nextLv == 2 || nextLv == 4)
            CuteUIKit.Text(rightBody, nextLv == 2 ? "중급 제작이 열려요" : "고급 제작이 열려요", 12f, CuteUIKit.SkyText, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);

        if (unlocks.Count > 0)
        {
            CuteUIKit.SectionHeader(rightBody, reached ? "열린 것" : "새로 열려요", CuteUIKit.SkyTag, CuteUIKit.SkyText, $"{unlocks.Count}가지");
            const int Max = 20;
            var icons = Grid(rightBody, new Vector2(40f, 40f), 10, new Vector2(4f, 4f));
            for (int i = 0; i < unlocks.Count && i < Max; i++)
            {
                var item = unlocks[i];
                var slot = CuteUIKit.Slot(icons, 40f, CuteUIKit.TierBorder(item, CuteUIKit.SlotBorder), CuteUIKit.TierFill(item, CuteUIKit.SlotFill), 10);
                var sb = slot.AddComponent<Button>();
                sb.transition = Selectable.Transition.None;
                slot.AddComponent<HoverHook>().onHover = on => ShowItem(on ? item : null);
                slot.AddComponent<CutePop>();
                var img = Icon(slot.transform, item.icon, 28f);
                var irt = img.rectTransform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
                irt.anchoredPosition = Vector2.zero;
                irt.sizeDelta = new Vector2(28f, 28f);
                img.GetComponent<LayoutElement>().ignoreLayout = true;
                if (!reached) img.color = new Color(1f, 1f, 1f, 0.9f);
            }
            if (unlocks.Count > Max)
                CuteUIKit.Text(rightBody, $"…그 밖에 {unlocks.Count - Max}가지 더", 11f, CuteUIKit.CocoaSoft, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            hoverText = CuteUIKit.Text(rightBody, "", 12f, CuteUIKit.Cocoa, FontStyles.Normal, TextAlignmentOptions.TopLeft);
            hoverText.textWrappingMode = TextWrappingModes.Normal;
            hoverText.richText = true;
            hoverText.overflowMode = TextOverflowModes.Ellipsis;
            hoverText.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
            ShowItem(null);
        }
        else if (stages != null && nextLv >= stages.MaxLevel)
        {
            var end = CuteUIKit.Text(rightBody, "이 단계에 오르면 큰 이주선이 남은 고양이들을 모두 태우고 내려앉아요!", 14f, CuteUIKit.Strawberry, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            end.textWrappingMode = TextWrappingModes.Normal;
        }
    }

    private void ShowItem(FarmingItemData item)
    {
        if (hoverText == null) return;
        if (item == null)
        {
            hoverText.text  = Pad.Active ? "아이콘을 고르면 이름이 보여요" : "아이콘에 마우스를 올리면 이름이 보여요";
            hoverText.color = CuteUIKit.CocoaMuted;
            return;
        }
        string desc = item.description ?? "";
        int cut = desc.IndexOfAny(new[] { '\n', '.' });
        if (cut > 0) desc = desc.Substring(0, cut + (desc[cut] == '.' ? 1 : 0));
        hoverText.text  = string.IsNullOrEmpty(desc) ? $"<b>{item.itemName}</b>" : $"<b>{item.itemName}</b> — {desc}";
        hoverText.color = CuteUIKit.Cocoa;
    }

    private sealed class HoverHook : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public System.Action<bool> onHover;
        public void OnPointerEnter(PointerEventData e) => onHover?.Invoke(true);
        public void OnPointerExit(PointerEventData e)  => onHover?.Invoke(false);
        public void OnSelect(BaseEventData e)          => onHover?.Invoke(true);
        public void OnDeselect(BaseEventData e)        => onHover?.Invoke(false);
    }

    private void Build()
    {
        var rootRT = (RectTransform)transform;

        var dim = CuteUIKit.Layer(rootRT, "Backdrop", null, CuteUIKit.Overlay);
        dim.raycastTarget = true;
        var dimBtn = dim.gameObject.AddComponent<Button>();
        dimBtn.transition = Selectable.Transition.None;
        dimBtn.navigation = new Navigation { mode = Navigation.Mode.None };
        dimBtn.onClick.AddListener(Close);

        var win = new GameObject("Window", typeof(RectTransform));
        win.transform.SetParent(rootRT, false);
        windowRT = (RectTransform)win.transform;
        windowRT.anchorMin = windowRT.anchorMax = windowRT.pivot = new Vector2(0.5f, 0.5f);
        windowRT.sizeDelta = new Vector2(WinW, WinH);
        windowRT.anchoredPosition = new Vector2(0f, -14f);
        win.AddComponent<Image>();
        CuteUIKit.DecorateWindow(windowRT, catEars: true, earInset: 150f);
        group = win.AddComponent<CanvasGroup>();

        var ribbon = CuteUIKit.TitleRibbon(windowRT, "문명 여정", ToggleKey.ToString(), 18f);
        Pin(ribbon, new Vector2(0f, -16f));
        FitSelf(ribbon.gameObject);

        var close = CuteUIKit.CloseButton(windowRT, Close, 30f);
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 1f);
        crt.anchoredPosition = new Vector2(-18f, -16f);
        crt.sizeDelta = new Vector2(30f, 30f);

        subtitle = CuteUIKit.Text(windowRT, "", 14f, CuteUIKit.Cocoa, FontStyles.Normal);
        subtitle.richText = true;
        Pin(subtitle.rectTransform, new Vector2(0f, -62f));
        subtitle.rectTransform.sizeDelta = new Vector2(WinW - 120f, 22f);

        BuildPath();

        leftBody  = DetailCard("Chapter", new Vector2(-(DetailW + 16f) * 0.5f, DetailTop));
        rightBody = DetailCard("NextStage", new Vector2((DetailW + 16f) * 0.5f, DetailTop));

        footer = CuteUIKit.Text(windowRT, "", 11f, CuteUIKit.CocoaMuted);
        var hrt = footer.rectTransform;
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0.5f, 0f);
        hrt.anchoredPosition = new Vector2(0f, 6f);
        hrt.sizeDelta = new Vector2(600f, 16f);

        windowRT.localScale = Vector3.zero;
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable   = false;
    }

    private void BuildPath()
    {
        var stages = CoreStageTable.Load();
        float X(int i) => (i - (StageCount - 1) * 0.5f) * ColW;

        var frames = new List<Image>();
        for (int i = 0; i < StageCount; i++)
        {
            var frame = CuteUIKit.Card(windowRT, CuteUIKit.Lighter(CuteUIKit.PinkSoft, 0.3f), 18, "Frame");
            frame.raycastTarget = false;
            var frt = frame.rectTransform;
            frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 1f);
            frt.pivot = new Vector2(0.5f, 1f);
            frt.anchoredPosition = new Vector2(X(i), -86f);
            frt.sizeDelta = new Vector2(ColW - 10f, -86f - (RoadY - 56f));
            frame.enabled = false;
            frames.Add(frame);
        }

        for (int ch = 0; ch < StageCount - 1; ch++)
        {
            var fill = CuteUIKit.Bar(windowRT, 10f, CuteUIKit.LatteLight, CuteUIKit.ButtonMint, ColW);
            var bar  = (RectTransform)fill.parent.parent;
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 1f);
            bar.pivot     = new Vector2(0.5f, 0.5f);
            bar.sizeDelta = new Vector2(ColW, 10f);
            bar.anchoredPosition = new Vector2((X(ch) + X(ch + 1)) * 0.5f, RoadY);
            segFills.Add(fill);

            int c = ch;
            var pill = CuteUIKit.Pill(windowRT, $"{ch + 1}장", CuteUIKit.LatteLight, CuteUIKit.CocoaSoft, 12f, 9, new RectOffset(8, 8, 2, 2), out var pt, fitSelf: true);
            pill.anchorMin = pill.anchorMax = new Vector2(0.5f, 1f);
            pill.pivot = new Vector2(0.5f, 0.5f);
            pill.anchoredPosition = new Vector2((X(ch) + X(ch + 1)) * 0.5f, RoadY + 20f);
            var pimg = pill.GetComponent<Image>();
            pimg.raycastTarget = true;
            var pb = pill.gameObject.AddComponent<Button>();
            pb.transition = Selectable.Transition.None;
            pb.onClick.AddListener(() => Select(c));
            pill.gameObject.AddComponent<CutePop>();
            pill.gameObject.AddComponent<HoverHook>().onHover = on => { if (on && Pad.Active && (selected != c || selectedStage != c)) Select(c); };
            segPills.Add(pimg);
            segPillTexts.Add(pt);
        }

        for (int i = 0; i < StageCount; i++)
        {
            int lv = i + 1;
            var st = stages != null ? stages.Stage(lv) : null;
            var col = new StageCol();
            float x = X(i);

            col.frame = frames[i];

            var coreGO = new GameObject("Core", typeof(RectTransform));
            coreGO.transform.SetParent(windowRT, false);
            col.core = (RectTransform)coreGO.transform;
            col.coreImg = coreGO.AddComponent<Image>();
            col.coreImg.sprite = st != null ? st.spriteOn : null;
            col.coreImg.enabled = col.coreImg.sprite != null;
            col.coreImg.raycastTarget = true;
            col.core.anchorMin = col.core.anchorMax = new Vector2(0.5f, 1f);
            col.core.pivot = new Vector2(0.5f, 0f);
            col.coreY = Baseline;
            col.core.anchoredPosition = new Vector2(x, Baseline);
            col.core.sizeDelta = col.coreImg.sprite != null ? col.coreImg.sprite.rect.size * CoreScale : new Vector2(90f, 90f);
            int pick = Mathf.Min(i, StageCount - 2);
            int stageIndex = i;
            var cb = coreGO.AddComponent<Button>();
            cb.transition = Selectable.Transition.None;
            cb.onClick.AddListener(() => Select(pick, stageIndex));
            coreGO.AddComponent<CutePop>();
            coreGO.AddComponent<HoverHook>().onHover = on => { if (on && Pad.Active && selectedStage != stageIndex) Select(pick, stageIndex); };

            col.node = CuteUIKit.Card(windowRT, CuteUIKit.Latte, 10, "Node");
            var nrt = col.node.rectTransform;
            nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 1f);
            nrt.pivot = new Vector2(0.5f, 0.5f);
            nrt.anchoredPosition = new Vector2(x, RoadY);
            nrt.sizeDelta = new Vector2(22f, 22f);
            col.nodeFill = CuteUIKit.Layer(col.node.transform, "Fill", CuteUIKit.Rounded(8), CuteUIKit.Cream, 3f);

            var lvText = CuteUIKit.Text(windowRT, $"Lv.{lv}", 11f, CuteUIKit.CocoaSoft, FontStyles.Bold);
            Pin(lvText.rectTransform, new Vector2(x, RoadY - 18f));
            lvText.rectTransform.sizeDelta = new Vector2(ColW, 14f);
            col.title = CuteUIKit.Text(windowRT, st != null ? st.title : "", 14f, CuteUIKit.Cocoa, FontStyles.Bold);
            Pin(col.title.rectTransform, new Vector2(x, RoadY - 33f));
            col.title.rectTransform.sizeDelta = new Vector2(ColW, 18f);

            col.here = CuteUIKit.Pill(windowRT, "지금 여기!", CuteUIKit.Strawberry, Color.white, 12f, 10, new RectOffset(9, 9, 2, 3), out _, fitSelf: true);
            col.here.anchorMin = col.here.anchorMax = new Vector2(0.5f, 1f);
            col.here.pivot = new Vector2(0.5f, 0f);
            col.here.anchoredPosition = new Vector2(x, Baseline + col.core.sizeDelta.y + 6f);
            col.here.gameObject.SetActive(false);

            cols.Add(col);
        }
    }

    private Transform DetailCard(string name, Vector2 topCenter)
    {
        var card = CuteUIKit.Card(windowRT, CuteUIKit.LatteLight, 16, name);
        card.raycastTarget = false;
        var rt = card.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = topCenter;
        rt.sizeDelta = new Vector2(DetailW, DetailH);

        var body = new GameObject("Body", typeof(RectTransform));
        body.transform.SetParent(rt, false);
        var brt = (RectTransform)body.transform;
        brt.anchorMin = new Vector2(0f, 1f);
        brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.offsetMin = new Vector2(14f, 0f);
        brt.offsetMax = new Vector2(-14f, -12f);
        var vlg = body.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 5f;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        body.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return body.transform;
    }

    private static void Clear(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--)
        {
            var c = t.GetChild(i).gameObject;
            c.SetActive(false);
            Destroy(c);
        }
    }

    private static Transform Row(Transform parent, float spacing)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = spacing;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
        return go.transform;
    }

    private static void Spacer(Transform row)
    {
        var go = new GameObject("Spacer", typeof(RectTransform));
        go.transform.SetParent(row, false);
        go.AddComponent<LayoutElement>().flexibleWidth = 1f;
    }

    private static RectTransform Pill(Transform parent, string text, Color fill, Color color, float size)
        => CuteUIKit.Pill(parent, text, fill, color, size, 10, new RectOffset(9, 9, 2, 3), out _);

    private static Transform Grid(Transform parent, Vector2 cell, int columns, Vector2 spacing)
    {
        var go = new GameObject("Grid", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var grid = go.AddComponent<GridLayoutGroup>();
        grid.cellSize = cell;
        grid.spacing = spacing;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.childAlignment = TextAnchor.UpperLeft;
        return go.transform;
    }

    private static Image Icon(Transform parent, Sprite sprite, float size)
    {
        var go = new GameObject("Icon", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;
        img.enabled = sprite != null;
        Size(go, size, size);
        return img;
    }

    private static void RewardChip(Transform parent, Sprite icon, string amount)
    {
        var chip = CuteUIKit.Card(parent, CuteUIKit.Cream, 8, "Reward");
        var hl = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(3, 6, 1, 1);
        hl.spacing = 2f;
        hl.childAlignment = TextAnchor.MiddleLeft;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = hl.childForceExpandHeight = false;
        Icon(chip.transform, icon, 16f);
        CuteUIKit.Text(chip.transform, amount, 11f, CuteUIKit.AmberText, FontStyles.Bold);
    }

    private static void Size(GameObject go, float w, float h)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = w;
        le.preferredHeight = le.minHeight = h;
        ((RectTransform)go.transform).sizeDelta = new Vector2(w, h);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void Pin(RectTransform rt, Vector2 pos)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = pos;
    }

    private static void FitSelf(GameObject go)
    {
        var fit = go.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

    private static bool HasBatchim(string word)
    {
        if (string.IsNullOrEmpty(word)) return false;
        char c = word[word.Length - 1];
        return c >= 0xAC00 && c <= 0xD7A3 && (c - 0xAC00) % 28 != 0;
    }
}
