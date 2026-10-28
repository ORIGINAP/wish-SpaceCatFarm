using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

// 왼쪽 위 퀘스트 패널 + 카드
[RequireComponent(typeof(RectTransform))]
public class QuestUI : MonoBehaviour
{
    public static QuestUI Instance => Services.Get<QuestUI>();

    [Header("폰트")]
    [SerializeField] private TMP_FontAsset fontAsset;

    private const float PanelWidth = 400f;
    private const int   CardRadius = 18;
    private static readonly Color TextColor         = CuteUIKit.Cocoa;
    private static readonly Color DoneTextColor     = CuteUIKit.MintText;
    private static readonly Color ProgressColor     = CuteUIKit.Lighter(CuteUIKit.MintText, 0.3f);
    private static readonly Color ProgressDoneColor = CuteUIKit.MintText;
    private static readonly Color BlinkFillColor    = CuteUIKit.MintTag;
    private static readonly Color BlinkBorderColor  = CuteUIKit.ButtonMint;
    private static readonly Color CompleteFlashColor = CuteUIKit.Strawberry;
    private static readonly Color CurrentRowFill    = CuteUIKit.Lighter(CuteUIKit.PinkSoft, 0.35f);
    private static readonly Color DoneRowFill       = CuteUIKit.Lighter(CuteUIKit.MintTag, 0.45f);

    private static readonly Color QuestNameBaseColor = CuteUIKit.Cocoa;
    private const string AllDoneText = "모든 퀘스트 완료!";

    private RectTransform   panelRT;
    private CanvasGroup     panelCG;
    private Image           cardFill;
    private Image           cardBorder;
    private TextMeshProUGUI chapterText;
    private TextMeshProUGUI stepText;
    private TextMeshProUGUI goalText;
    private TextMeshProUGUI questNameText;
    private Transform       objectiveContainer;
    private GameObject      hintRow;
    private TextMeshProUGUI hintText;
    private TextMeshProUGUI whyText;
    private Button          shortcutBtn;
    private TextMeshProUGUI shortcutText;
    private System.Action   shortcutAction;
    private TextMeshProUGUI toggleBtnText;

    private Coroutine completionCo;
    private Coroutine popInCo;
    private Coroutine blinkCo;

    private readonly List<ObjectiveRow> rows = new();
    private readonly List<bool> rowDone = new();
    private QuestDefinition rowsQuest;

    private class ObjectiveRow
    {
        public GameObject      root;
        public Image           bg;
        public Image           icon;
        public TextMeshProUGUI label;
        public TextMeshProUGUI count;
        public Image           fill;
    }

    private GameObject logSectionGO;
    private Transform  logContentRT;
    private bool       logOpen = false;

    private RectTransform cardRT;
    private CanvasGroup   cardCG;
    private Transform     cardBody;
    private enum CardKind { Started, Completed, Chapter }
    private readonly Queue<(CardKind kind, QuestDefinition def)> cardQueue = new();
    private Coroutine     cardCo;
    private bool          skipCard;

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }

        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
        foreach (var fx in GetComponents<BaseMeshEffect>()) DestroyImmediate(fx);
        { var c = GetComponent<Image>();               if (c) DestroyImmediate(c); }
        { var c = GetComponent<VerticalLayoutGroup>(); if (c) DestroyImmediate(c); }
        { var c = GetComponent<CanvasGroup>();         if (c) DestroyImmediate(c); }
        { var c = GetComponent<ContentSizeFitter>();   if (c) DestroyImmediate(c); }

        BuildPanel();

        if (transform.parent != null)
            CurrencyHUD.Create(transform.parent,
                panelRT.anchoredPosition + new Vector2(panelRT.sizeDelta.x + 10f, 0f),
                fontAsset);
    }

    private void Start()
    {
        if (QuestManager.Instance == null) return;

        QuestManager.Instance.OnQuestUpdated   += HandleQuestUpdated;
        QuestManager.Instance.OnQuestStarted   += OnQuestStarted;
        QuestManager.Instance.OnQuestCompleted += OnQuestCompleted;
        QuestManager.Instance.OnAllQuestsDone  += OnAllDone;
        QuestGuide.OnChanged += RefreshHint;

        RefreshUI();

        var qm = QuestManager.Instance;
        if (GameLaunchContext.IsNewGame && qm.CurrentQuest != null && qm.CurrentQuestIndex == 0 && cardQueue.Count == 0)
            EnqueueStart(qm.CurrentQuest);
    }

    private void OnDestroy()
    {
        Services.Unregister(this);
        QuestGuide.OnChanged -= RefreshHint;
        if (cardRT != null) Destroy(cardRT.root.gameObject);
        if (QuestManager.Instance == null) return;
        QuestManager.Instance.OnQuestUpdated   -= HandleQuestUpdated;
        QuestManager.Instance.OnQuestStarted   -= OnQuestStarted;
        QuestManager.Instance.OnQuestCompleted -= OnQuestCompleted;
        QuestManager.Instance.OnAllQuestsDone  -= OnAllDone;
    }

    private void Update()
    {
        if (cardCo != null && cardCG != null && cardCG.alpha > 0.9f &&
            (GameInput.GetMouseButtonDown(0) || GameInput.GetMouseButtonDown(1) || (Pad.Active && Pad.Down(PadButton.A))))
            skipCard = true;
    }

    private void OnQuestStarted(QuestDefinition def)
    {
        if (popInCo != null) StopCoroutine(popInCo);
        popInCo = StartCoroutine(PopIn());
        RefreshUI();
        if (logOpen) RefreshLog();
        EnqueueStart(def);
    }

    private void OnQuestCompleted(QuestDefinition def)
    {
        if (completionCo != null)
        {
            StopCoroutine(completionCo);
            questNameText.color = QuestNameBaseColor;
        }
        completionCo = StartCoroutine(CompletionFlash());
        EnqueueCard(CardKind.Completed, def);
    }

    private void OnAllDone()
    {
        questNameText.text = AllDoneText;
        ClearRows();
        if (logOpen) RefreshLog();
    }

    private void HandleQuestUpdated()
    {
        RefreshUI();
        if (logOpen) RefreshLog();

        if (blinkCo != null) StopCoroutine(blinkCo);
        blinkCo = StartCoroutine(UpdateBlink());
    }

    private void RefreshUI()
    {
        var quest      = QuestManager.Instance?.CurrentQuest;
        var objectives = QuestManager.Instance?.ActiveObjectives;

        RefreshChapter();
        if (quest == null)
        {
            bool allDone = QuestManager.Instance != null && QuestManager.Instance.AllQuestsDone;
            questNameText.text = allDone ? AllDoneText : "진행 중인 퀘스트 없음";
            ClearRows();
            RefreshHint();
            return;
        }

        questNameText.text = quest.questName;

        int count = objectives?.Count ?? 0;
        bool sameQuest = rowsQuest == quest;
        rowsQuest = quest;
        if (!sameQuest) rowDone.Clear();
        while (rows.Count < count) AddRow();
        while (rows.Count > count) RemoveLastRow();

        bool currentMarked = false;
        for (int i = 0; i < count; i++)
        {
            var obj = objectives[i];
            var row = rows[i];
            bool current = !obj.IsComplete && !currentMarked;
            if (current) currentMarked = true;

            float ratio = obj.targetAmount > 0 ? (float)obj.currentAmount / obj.targetAmount : 1f;
            row.label.text  = obj.IsComplete ? $"<s>{obj.GetLabel()}</s>" : current ? $"<b>{obj.GetLabel()}</b>" : obj.GetLabel();
            row.count.text  = $"{obj.currentAmount}/{obj.targetAmount}";
            row.label.color = row.count.color = obj.IsComplete ? DoneTextColor : TextColor;
            row.bg.color    = obj.IsComplete ? DoneRowFill : current ? CurrentRowFill : Color.clear;
            var sprite = QuestIcons.For(obj);
            row.icon.sprite  = sprite;
            row.icon.enabled = sprite != null;
            row.icon.color   = obj.IsComplete ? new Color(1f, 1f, 1f, 0.55f) : Color.white;
            CuteUIKit.SetBar(row.fill.rectTransform, ratio);
            row.fill.color = obj.IsComplete ? ProgressDoneColor : ProgressColor;

            bool was = i < rowDone.Count && rowDone[i];
            if (sameQuest && obj.IsComplete && !was && isActiveAndEnabled)
            {
                CuteUIKit.SparkleBurst(this, (RectTransform)row.root.transform, CuteUIKit.MintText, 10, 70f);
                StartCoroutine(CuteUIKit.ScaleTo(row.root.transform, 1f, 0.25f));
                row.root.transform.localScale = Vector3.one * 1.08f;
            }
            while (rowDone.Count <= i) rowDone.Add(false);
            rowDone[i] = obj.IsComplete;
        }

        RefreshHint();
    }

    private void RefreshChapter()
    {
        var qm = QuestManager.Instance;
        var chapter = qm != null ? qm.CurrentChapter : null;
        if (chapter == null)
        {
            chapterText.text = qm != null && qm.AllQuestsDone ? "모든 장 완료" : "진행 중인 퀘스트";
            stepText.transform.parent.gameObject.SetActive(false);
            goalText.gameObject.SetActive(false);
            return;
        }
        chapterText.text = $"{qm.CurrentChapterIndex + 1}장 · {chapter.title}";
        stepText.transform.parent.gameObject.SetActive(true);
        stepText.text = $"{qm.StepInChapter}/{qm.StepsInChapter}";
        goalText.gameObject.SetActive(!string.IsNullOrEmpty(chapter.goal));
        goalText.text = $"목표 · {chapter.goal}";
    }

    private void RefreshHint()
    {
        if (hintText == null) return;
        var quest = QuestManager.Instance?.CurrentQuest;
        var guide = QuestGuide.Current;
        string text = !string.IsNullOrEmpty(guide.action) ? guide.action : quest != null ? quest.HintText : "";
        hintRow.SetActive(quest != null && !string.IsNullOrEmpty(text));
        hintText.text = text;
        bool why = quest != null && !string.IsNullOrEmpty(guide.why);
        whyText.gameObject.SetActive(why);
        if (why) whyText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(CuteUIKit.Strawberry)}>다음:</color> {guide.why}";

        shortcutAction = quest != null ? guide.shortcut : null;
        shortcutBtn.gameObject.SetActive(shortcutAction != null);
        if (shortcutAction != null) shortcutText.text = guide.shortcutLabel;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRT);
    }

    private void OnShortcut() => shortcutAction?.Invoke();

    public void ToggleLog() => SetLogOpen(!logOpen);

    private void SetLogOpen(bool open)
    {
        logOpen = open;
        logSectionGO.SetActive(open);
        toggleBtnText.text = ToggleLabel(open);
        if (open) RefreshLog();

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRT);
    }

    private static string ToggleLabel(bool open) => open ? "자세히 접기" : "자세히 · 보상 보기";

    private void RefreshLog()
    {
        for (int i = logContentRT.childCount - 1; i >= 0; i--)
            Destroy(logContentRT.GetChild(i).gameObject);

        var quest = QuestManager.Instance?.CurrentQuest;
        if (quest == null) return;

        var entryGO = new GameObject("LogEntry", typeof(RectTransform));
        entryGO.transform.SetParent(logContentRT, false);
        var vlg = entryGO.AddComponent<VerticalLayoutGroup>();
        vlg.padding               = new RectOffset(12, 12, 10, 11);
        vlg.spacing               = 6f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight= false;
        vlg.childControlWidth     = true;
        vlg.childControlHeight    = true;
        entryGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        if (!string.IsNullOrEmpty(quest.talk))
            NewText(entryGO.transform, $"<color=#{ColorUtility.ToHtmlStringRGB(CuteUIKit.SkyText)}><b>{AINpc.NpcName}</b></color>  {quest.talk}", 13f,
                    TextColor, FontStyles.Normal, wrap: true).name = "Talk";
        if (!string.IsNullOrEmpty(quest.description))
            NewText(entryGO.transform, quest.description, 13f, CuteUIKit.CocoaSoft, FontStyles.Normal, wrap: true).name = "Desc";
        if (!string.IsNullOrEmpty(quest.tutorialTip))
            NewText(entryGO.transform, quest.tutorialTip, 13f, TextColor, wrap: true).name = "Tip";

        if (quest.rewards.Count > 0 || quest.rewardCoins > 0)
            BuildRewardRow(entryGO.transform, quest);
    }

    private void BuildRewardRow(Transform parent, QuestDefinition quest, float iconSize = 22f)
        => BuildRewardRow(parent, quest.rewards, quest.rewardCoins, iconSize);

    private void BuildRewardRow(Transform parent, List<QuestReward> rewards, int rewardCoins, float iconSize)
    {
        var rowGO = new GameObject("Rewards", typeof(RectTransform));
        rowGO.transform.SetParent(parent, false);
        var hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing               = 5f;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight= false;
        hlg.childControlWidth     = true;
        hlg.childControlHeight    = true;
        hlg.childAlignment        = TextAnchor.MiddleLeft;
        rowGO.AddComponent<LayoutElement>().preferredHeight = iconSize + 8f;

        var tag = CuteUIKit.Pill(rowGO.transform, "보상", CuteUIKit.ButtonButter,
                                 CuteUIKit.TextOn(CuteUIKit.ButtonButter), 11f, 9, new RectOffset(8, 8, 2, 2), out _);
        ApplyFont(tag);

        foreach (var reward in rewards)
            if (reward.item != null) BuildRewardChip(rowGO.transform, reward.item, reward.amount, iconSize);
        if (rewardCoins > 0) BuildRewardChip(rowGO.transform, null, rewardCoins, iconSize);
    }

    private void BuildRewardChip(Transform parent, FarmingItemData item, int amount, float iconSize)
    {
        var chip = CuteUIKit.Card(parent, CuteUIKit.Cream, 11, "Chip");
        chip.raycastTarget = false;
        var chipHLG = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
        chipHLG.padding               = new RectOffset(4, 8, 2, 2);
        chipHLG.spacing               = 3f;
        chipHLG.childForceExpandWidth = false;
        chipHLG.childForceExpandHeight= false;
        chipHLG.childControlWidth     = true;
        chipHLG.childControlHeight    = true;
        chipHLG.childAlignment        = TextAnchor.MiddleLeft;

        var icon = NewIcon(chip.transform, item != null ? item.icon : CuteUIKit.Coin(), item != null ? iconSize : iconSize * 0.8f);
        icon.enabled = icon.sprite != null;

        NewText(chip.transform, item != null ? $"×{amount}" : CurrencyManager.Format(amount), 12f,
                item != null ? TextColor : CuteUIKit.AmberText, FontStyles.Bold).name = "Amount";
    }

    private void EnqueueStart(QuestDefinition def)
    {
        var chain = QuestChain.Load();
        if (chain != null && chain.ChapterOf(def, out int step) >= 0 && step == 0) EnqueueCard(CardKind.Chapter, def);
        EnqueueCard(CardKind.Started, def);
    }

    private void EnqueueCard(CardKind kind, QuestDefinition def)
    {
        if (def == null) return;
        if (cardRT == null) BuildCard();
        cardQueue.Enqueue((kind, def));
        if (cardCo == null) cardCo = StartCoroutine(CardLoop());
    }

    private IEnumerator CardLoop()
    {
        yield return null;
        while (cardQueue.Count > 0)
        {
            while (CardBlocked) yield return null;

            var (kind, def) = cardQueue.Dequeue();
            bool started = kind == CardKind.Started;
            // 이미 지나간 카드는 건너뜀
            if (kind != CardKind.Completed && QuestManager.Instance != null && QuestManager.Instance.CurrentQuest != def) continue;
            // 연속 완료 카드는 한 장으로
            List<QuestDefinition> batch = null;
            if (kind == CardKind.Completed)
            {
                var rest = new List<(CardKind kind, QuestDefinition def)>();
                while (cardQueue.Count > 0)
                {
                    var next = cardQueue.Dequeue();
                    if (next.kind == CardKind.Completed) { batch ??= new List<QuestDefinition> { def }; batch.Add(next.def); }
                    else if (QuestManager.Instance == null || QuestManager.Instance.CurrentQuest == next.def) rest.Add(next);
                }
                foreach (var r in rest) cardQueue.Enqueue(r);
            }
            bool hurry = kind == CardKind.Completed && cardQueue.Count > 0;
            cardRT.sizeDelta = new Vector2(kind == CardKind.Chapter ? 600f : 520f, 0f);
            if (kind == CardKind.Chapter) FillChapterCard(def);
            else if (batch != null) FillCompletedBatch(batch);
            else FillCard(started, def);

            cardRT.gameObject.SetActive(true);
            cardRT.anchoredPosition = CardHome;
            cardRT.localScale = Vector3.one;
            skipCard = false;
            if (kind == CardKind.Completed) CuteUIKit.SparkleBurst(this, cardRT, CuteUIKit.Butter, 16, 140f);
            if (kind == CardKind.Chapter)   CuteUIKit.SparkleBurst(this, cardRT, CuteUIKit.Strawberry, 22, 220f);
            Sfx.Play(kind == CardKind.Completed ? SfxId.QuestComplete : SfxId.UiOpen, kind == CardKind.Completed ? 0.5f : 0.8f);
            yield return CuteUIKit.PopIn(cardRT, cardCG, 0.3f);

            float hold = kind == CardKind.Chapter ? 5.5f : started ? 4.2f : batch != null ? 2.8f : hurry ? 1.4f : 2.2f;
            for (float t = 0f; t < hold && !(skipCard && t > 0.35f); t += Time.unscaledDeltaTime)
            {
                if (LevelUpUI.IsOpen || PlayerTileController.IsAnyUIOpen()) break;
                yield return null;
            }

            if (started) yield return FlyToPanel();
            else yield return CuteUIKit.PopOut(cardRT, cardCG, 0.18f);
            cardRT.gameObject.SetActive(false);
        }
        cardCo = null;
    }

    private static readonly Vector2 CardHome = new(0f, -118f);

    private static bool CardBlocked => TutorialUI.IsActive || LevelUpUI.IsOpen || PlayerTileController.IsAnyUIOpen();

    private IEnumerator FlyToPanel()
    {
        var canvasRT = (RectTransform)cardRT.parent;
        Vector3[] corners = new Vector3[4];
        panelRT.GetWorldCorners(corners);
        Vector2 target = CardHome;
        Vector2 screen = (corners[0] + corners[2]) * 0.5f;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screen, null, out var local))
            target = local + new Vector2(0f, canvasRT.rect.height * 0.5f);

        Vector2 from = cardRT.anchoredPosition;
        const float dur = 0.42f;
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / dur);
            cardRT.anchoredPosition = Vector2.Lerp(from, target, k);
            cardRT.localScale = Vector3.one * Mathf.Lerp(1f, 0.3f, k);
            cardCG.alpha = 1f - k * k;
            yield return null;
        }
        cardCG.alpha = 0f;
        if (popInCo != null) StopCoroutine(popInCo);
        popInCo = StartCoroutine(PopIn());
    }

    private void FillCard(bool started, QuestDefinition def)
    {
        ClearCardBody();
        if (!started) lastPraise = Praises[Random.Range(0, Praises.Length)];

        var qm = QuestManager.Instance;
        var head = Row(cardBody, TextAnchor.MiddleCenter, 8f);
        var ribbon = CuteUIKit.TitleRibbon(head, started ? "새 퀘스트" : "퀘스트 완료!", null, 15f);
        ApplyFont(ribbon);
        if (started && qm != null && qm.CurrentChapter != null && qm.CurrentQuest == def)
        {
            var step = CuteUIKit.Pill(head, $"{qm.CurrentChapterIndex + 1}장 · {qm.StepInChapter}/{qm.StepsInChapter}",
                                      CuteUIKit.LatteLight, CuteUIKit.Cocoa, 12f, 10, new RectOffset(9, 9, 3, 3), out _);
            ApplyFont(step);
        }

        var title = NewText(cardBody, def.questName, started ? 24f : 20f, started ? CuteUIKit.Cocoa : CuteUIKit.Strawberry, FontStyles.Bold);
        title.alignment = TextAlignmentOptions.Center;

        if (!started)
        {
            CocoRow(cardBody, lastPraise, 52f, 15f, stretch: false);
            if (def.rewards.Count > 0 || def.rewardCoins > 0)
            {
                var rewards = Row(cardBody, TextAnchor.MiddleCenter, 0f);
                BuildRewardRow(rewards, def, 26f);
            }
            return;
        }

        string talk = !string.IsNullOrEmpty(def.talk) ? def.talk : def.description;
        if (!string.IsNullOrEmpty(talk)) CocoRow(cardBody, talk, 66f, 16f);

        var objs = qm != null && qm.CurrentQuest == def ? qm.ActiveObjectives : null;
        for (int i = 0; i < def.objectives.Count; i++)
        {
            var src = def.objectives[i];
            int cur = objs != null && i < objs.Count ? objs[i].currentAmount : 0;
            var line = Row(cardBody, TextAnchor.MiddleCenter, 8f);
            var bg = line.gameObject.AddComponent<Image>();
            CuteUIKit.SetRounded(bg, 12, CurrentRowFill);
            bg.raycastTarget = false;
            var hl = line.GetComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(10, 12, 5, 5);
            NewIcon(line, QuestIcons.For(src), 30f);
            NewText(line, src.GetLabel(), 16f, TextColor, FontStyles.Bold);
            NewText(line, $"{cur}/{src.targetAmount}", 15f, CuteUIKit.CocoaSoft, FontStyles.Bold);
        }

        string hint = def.HintText;
        if (!string.IsNullOrEmpty(hint))
        {
            var hintCard = CuteUIKit.Card(cardBody, CuteUIKit.ButterSoft, 12, "HowTo");
            hintCard.raycastTarget = false;
            var hv = hintCard.gameObject.AddComponent<HorizontalLayoutGroup>();
            hv.padding = new RectOffset(12, 12, 7, 7);
            hv.spacing = 8f;
            hv.childAlignment = TextAnchor.MiddleLeft;
            hv.childControlWidth = hv.childControlHeight = true;
            hv.childForceExpandWidth = hv.childForceExpandHeight = false;
            var tag = CuteUIKit.Pill(hintCard.transform, "방법", CuteUIKit.Strawberry, Color.white, 12f, 9, new RectOffset(8, 8, 2, 2), out _);
            ApplyFont(tag);
            var ht = NewText(hintCard.transform, hint, 15f, TextColor, FontStyles.Bold, wrap: true);
            ht.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        var skip = NewText(cardBody, Pad.Active ? "A: 넘기기" : "클릭하면 넘겨요", 11f, CuteUIKit.CocoaMuted);
        skip.alignment = TextAlignmentOptions.Center;
    }

    private void FillChapterCard(QuestDefinition def)
    {
        ClearCardBody();
        var chain = QuestChain.Load();
        int ch = chain != null ? chain.ChapterOf(def, out _) : -1;
        if (ch < 0) return;
        var chapter = chain.chapters[ch];

        var head = Row(cardBody, TextAnchor.MiddleCenter, 8f);
        var ribbon = CuteUIKit.TitleRibbon(head, ch == 0 ? "모험 시작!" : "새로운 장", null, 15f);
        ApplyFont(ribbon);
        var step = CuteUIKit.Pill(head, $"{ch + 1}장 / {chain.chapters.Count}장", CuteUIKit.LatteLight, CuteUIKit.Cocoa, 12f, 10,
                                  new RectOffset(9, 9, 3, 3), out _);
        ApplyFont(step);

        var title = NewText(cardBody, $"{ch + 1}장 · {chapter.title}", 30f, CuteUIKit.Strawberry, FontStyles.Bold);
        title.alignment = TextAlignmentOptions.Center;

        var stages = CoreStageTable.Load();
        if (stages != null && ch + 2 <= stages.MaxLevel)
        {
            var row = Row(cardBody, TextAnchor.LowerCenter, 18f);
            StageFigure(row, stages.Stage(ch + 1), $"지금 · Lv.{ch + 1}", 1.6f, false);
            var arrow = NewText(row, ">>", 22f, CuteUIKit.Strawberry, FontStyles.Bold);
            arrow.alignment = TextAlignmentOptions.Center;
            arrow.gameObject.AddComponent<LayoutElement>().minHeight = 60f;
            StageFigure(row, stages.Stage(ch + 2), $"목표 · Lv.{ch + 2}", 1.6f, true);
        }

        if (!string.IsNullOrEmpty(chapter.goal))
        {
            var goal = NewText(cardBody, $"목표 · {chapter.goal}", 16f, CuteUIKit.Cocoa, FontStyles.Bold, wrap: true);
            goal.alignment = TextAlignmentOptions.Center;
        }
        if (!string.IsNullOrEmpty(chapter.intro)) CocoRow(cardBody, chapter.intro, 66f, 16f);

        var skip = NewText(cardBody, Pad.Active ? "A: 넘기기" : "클릭하면 넘겨요 · 여정(L)에서 다시 볼 수 있어요", 11f, CuteUIKit.CocoaMuted);
        skip.alignment = TextAlignmentOptions.Center;
    }

    private void StageFigure(Transform parent, CoreStage stage, string caption, float scale, bool goal)
    {
        var col = new GameObject("Stage", typeof(RectTransform));
        col.transform.SetParent(parent, false);
        var vl = col.AddComponent<VerticalLayoutGroup>();
        vl.spacing = 4f;
        vl.childAlignment = TextAnchor.LowerCenter;
        vl.childControlWidth = vl.childControlHeight = true;
        vl.childForceExpandWidth = vl.childForceExpandHeight = false;

        var sprite = stage != null ? stage.spriteOn : null;
        if (sprite != null) NewIcon(col.transform, sprite, sprite.rect.height * scale).GetComponent<LayoutElement>().preferredWidth = sprite.rect.width * scale;
        var cap = NewText(col.transform, caption, 12f, CuteUIKit.CocoaSoft, FontStyles.Bold);
        cap.alignment = TextAlignmentOptions.Center;
        var name = NewText(col.transform, stage != null ? stage.title : "", 16f, goal ? CuteUIKit.Strawberry : CuteUIKit.Cocoa, FontStyles.Bold);
        name.alignment = TextAlignmentOptions.Center;
    }

    private void FillCompletedBatch(List<QuestDefinition> defs)
    {
        ClearCardBody();
        lastPraise = Praises[Random.Range(0, Praises.Length)];

        var head = Row(cardBody, TextAnchor.MiddleCenter, 8f);
        var ribbon = CuteUIKit.TitleRibbon(head, $"퀘스트 {defs.Count}개 완료!", null, 15f);
        ApplyFont(ribbon);

        var names = new List<string>();
        foreach (var d in defs) names.Add(d.questName);
        var title = NewText(cardBody, string.Join("  ·  ", names), 17f, CuteUIKit.Strawberry, FontStyles.Bold, wrap: true);
        title.alignment = TextAlignmentOptions.Center;

        CocoRow(cardBody, lastPraise, 52f, 15f, stretch: false);

        var items = new List<QuestReward>();
        int coins = 0;
        foreach (var d in defs)
        {
            coins += d.rewardCoins;
            foreach (var r in d.rewards)
            {
                if (r.item == null) continue;
                var same = items.Find(x => x.item == r.item);
                if (same != null) same.amount += r.amount;
                else items.Add(new QuestReward { item = r.item, amount = r.amount });
            }
        }
        if (items.Count > 0 || coins > 0)
            BuildRewardRow(Row(cardBody, TextAnchor.MiddleCenter, 0f), items, coins, 26f);
    }

    private static readonly string[] Praises =
    {
        "잘했어요, 대원님!", "역시 대원님이에요!", "최고예요!", "우와, 벌써 해냈어요!",
        "고향별 고양이들이 기뻐할 거예요!", "척척 해내네요!", "이 기세로 계속 가요!",
    };
    private string lastPraise = "잘했어요, 대원님!";

    private void CocoRow(Transform parent, string line, float portrait, float fontSize, bool stretch = true)
        => CocoUI.Row(parent, line, portrait, fontSize, stretch, fontAsset);

    private void ClearCardBody()
    {
        for (int i = cardBody.childCount - 1; i >= 0; i--)
        {
            var c = cardBody.GetChild(i).gameObject;
            c.SetActive(false);
            Destroy(c);
        }
    }

    private void BuildCard()
    {
        var root = new GameObject("QuestCardCanvas", typeof(RectTransform));
        UIStyleKit.CreateOverlayCanvas(root, 440).GetComponent<GraphicRaycaster>().enabled = false;

        var go = new GameObject("QuestCard", typeof(RectTransform));
        go.transform.SetParent(root.transform, false);
        cardRT = (RectTransform)go.transform;
        cardRT.anchorMin = cardRT.anchorMax = cardRT.pivot = new Vector2(0.5f, 1f);
        cardRT.anchoredPosition = CardHome;
        cardRT.sizeDelta = new Vector2(520f, 0f);
        go.AddComponent<Image>().raycastTarget = false;
        CuteUIKit.DecorateWindow(cardRT, catEars: true, earInset: 90f);
        cardCG = go.AddComponent<CanvasGroup>();
        cardCG.blocksRaycasts = false;
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(22, 22, 16, 14);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        go.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var body = new GameObject("Body", typeof(RectTransform));
        body.transform.SetParent(go.transform, false);
        var bvl = body.AddComponent<VerticalLayoutGroup>();
        bvl.spacing = 8f;
        bvl.childControlWidth = bvl.childControlHeight = true;
        bvl.childForceExpandWidth = true;
        bvl.childForceExpandHeight = false;
        cardBody = body.transform;
        foreach (var g in go.GetComponentsInChildren<Graphic>()) g.raycastTarget = false;
        cardRT.gameObject.SetActive(false);
    }

    private IEnumerator PopIn()
    {
        const float duration = 0.3f;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float ease = CuteUIKit.EaseOutBack(t / duration, 2.2f);
            panelRT.localScale = Vector3.one * Mathf.LerpUnclamped(0.85f, 1f, ease);
            yield return null;
        }
        panelRT.localScale = Vector3.one;
    }

    private IEnumerator UpdateBlink()
    {
        float t = 0f;
        float duration = 0.35f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float p     = Mathf.Clamp01(t / duration);
            float pulse = 1f - Mathf.Abs(p * 2f - 1f);
            SetCardColors(Color.Lerp(CuteUIKit.Cream, BlinkFillColor, pulse),
                          Color.Lerp(CuteUIKit.Latte, BlinkBorderColor, pulse));
            yield return null;
        }
        SetCardColors(CuteUIKit.Cream, CuteUIKit.Latte);
    }

    private IEnumerator CompletionFlash()
    {
        float t = 0f;
        while (t < 0.6f)
        {
            t += Time.unscaledDeltaTime;
            float pulse = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 5f));
            questNameText.color = Color.Lerp(QuestNameBaseColor, CompleteFlashColor, pulse);
            yield return null;
        }
        questNameText.color = QuestNameBaseColor;
    }

    private void SetCardColors(Color fill, Color border)
    {
        if (cardFill   != null) cardFill.color   = fill;
        if (cardBorder != null) cardBorder.color = border;
    }

    private void BuildPanel()
    {
        panelRT = GetComponent<RectTransform>();
        panelRT.anchorMin        = new Vector2(0f, 1f);
        panelRT.anchorMax        = new Vector2(0f, 1f);
        panelRT.pivot            = new Vector2(0f, 1f);
        panelRT.anchoredPosition = new Vector2(20f, -20f);
        panelRT.sizeDelta        = new Vector2(PanelWidth, 0f);

        CuteUIKit.DecorateWindow(panelRT, catEars: false, radius: CardRadius, border: 4);
        cardBorder = FindLayerImage("Cute_Border");
        cardFill   = FindLayerImage("Cute_Fill");

        panelCG = gameObject.AddComponent<CanvasGroup>();

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding               = new RectOffset(16, 16, 14, 14);
        vlg.spacing               = 7f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight= false;
        vlg.childControlWidth     = true;
        vlg.childControlHeight    = true;

        var csf = gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildHeader();
        goalText = NewText(transform, "", 13f, CuteUIKit.CocoaSoft, FontStyles.Normal, wrap: true);
        goalText.name = "ChapterGoal";
        CuteUIKit.DottedDivider(transform);

        questNameText = NewText(transform, "퀘스트 로딩 중", 20f, QuestNameBaseColor, FontStyles.Bold, wrap: true);
        questNameText.name = "QuestName";

        var containerGO = new GameObject("Objectives", typeof(RectTransform));
        containerGO.transform.SetParent(transform, false);
        var cvlg = containerGO.AddComponent<VerticalLayoutGroup>();
        cvlg.spacing               = 4f;
        cvlg.childForceExpandWidth = true;
        cvlg.childForceExpandHeight= false;
        cvlg.childControlWidth     = true;
        cvlg.childControlHeight    = true;
        containerGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        objectiveContainer = containerGO.transform;

        BuildHintRow();

        var toggleBtn = CuteUIKit.Button(transform, ToggleLabel(false), CuteUIKit.ButtonSky, ToggleLog, -1f, 24f, 12f);
        toggleBtn.name = "ToggleBtn";
        toggleBtnText = toggleBtn.GetComponentInChildren<TextMeshProUGUI>(true);
        if (fontAsset != null) toggleBtnText.font = fontAsset;

        BuildLogSection();
    }

    private void BuildHeader()
    {
        var headerGO = new GameObject("Header", typeof(RectTransform));
        headerGO.transform.SetParent(transform, false);
        var hlg = headerGO.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        var ribbon = CuteUIKit.TitleRibbon(headerGO.transform, "진행 중인 퀘스트", null, 14f);
        ApplyFont(ribbon);
        chapterText = ribbon.GetComponentInChildren<TextMeshProUGUI>(true);

        var spacer = new GameObject("Spacer", typeof(RectTransform));
        spacer.transform.SetParent(headerGO.transform, false);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var step = CuteUIKit.Pill(headerGO.transform, "1/1", CuteUIKit.LatteLight, CuteUIKit.Cocoa, 13f, 10,
                                  new RectOffset(9, 9, 3, 3), out stepText);
        ApplyFont(step);
        step.gameObject.SetActive(false);

        var journey = CuteUIKit.Pill(headerGO.transform, MobileMode.Active ? "여정" : "여정 (L)", CuteUIKit.ButtonSky,
                                     CuteUIKit.TextOn(CuteUIKit.ButtonSky), 12f, 10, new RectOffset(8, 8, 3, 3), out _);
        ApplyFont(journey);
        journey.gameObject.AddComponent<CutePop>();
        var hit = headerGO.AddComponent<Image>();
        hit.color = Color.clear;
        var btn = headerGO.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(JourneyUI.Toggle);
        headerGO.AddComponent<UiButtonSfx>();
        hlg.spacing = 6f;
    }

    private void BuildHintRow()
    {
        var border = CuteUIKit.Card(transform, CuteUIKit.Latte, 13, "Hint");
        border.raycastTarget = false;
        hintRow = border.gameObject;
        CuteUIKit.Layer(border.transform, "Fill", CuteUIKit.Rounded(11), CuteUIKit.ButterSoft, 2f);
        var vlg = hintRow.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(11, 11, 8, 9);
        vlg.spacing = 6f;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        var tagRow = Row(hintRow.transform, TextAnchor.MiddleLeft, 6f);
        var tag = CuteUIKit.Pill(tagRow, "지금 할 일", CuteUIKit.PinkSoft, CuteUIKit.Cocoa, 12f, 9, new RectOffset(8, 9, 2, 3), out _);
        ApplyFont(tag);

        hintText = NewText(hintRow.transform, "", 15f, CuteUIKit.Cocoa, FontStyles.Bold, wrap: true);
        hintText.name = "HintText";
        hintText.richText = true;
        hintText.lineSpacing = 4f;

        whyText = NewText(hintRow.transform, "", 12f, CuteUIKit.CocoaSoft, FontStyles.Normal, wrap: true);
        whyText.name = "WhyText";
        whyText.richText = true;

        shortcutBtn = CuteUIKit.Button(hintRow.transform, "바로가기", CuteUIKit.ButtonMint, OnShortcut, -1f, 30f, 13f);
        shortcutBtn.name = "Shortcut";
        shortcutText = shortcutBtn.GetComponentInChildren<TextMeshProUGUI>(true);
        if (fontAsset != null) shortcutText.font = fontAsset;
        shortcutBtn.gameObject.SetActive(false);
        hintRow.SetActive(false);
    }

    private void BuildLogSection()
    {
        logSectionGO = new GameObject("QuestLog", typeof(RectTransform));
        logSectionGO.transform.SetParent(transform, false);
        logSectionGO.SetActive(false);

        var logBg = logSectionGO.AddComponent<Image>();
        CuteUIKit.SetRounded(logBg, 12, CuteUIKit.LatteLight);
        logBg.raycastTarget = false;

        var vlg = logSectionGO.AddComponent<VerticalLayoutGroup>();
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight= false;
        vlg.childControlWidth     = true;
        vlg.childControlHeight    = true;
        logSectionGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        logContentRT = logSectionGO.transform;
    }

    private void AddRow()
    {
        var row = new ObjectiveRow();

        row.bg = CuteUIKit.Card(objectiveContainer, Color.clear, 10, "ObjRow");
        row.bg.raycastTarget = false;
        row.root = row.bg.gameObject;
        var hlg = row.root.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(6, 8, 4, 5);
        hlg.spacing                = 8f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        row.icon = NewIcon(row.root.transform, null, 28f);

        var col = new GameObject("Col", typeof(RectTransform));
        col.transform.SetParent(row.root.transform, false);
        col.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var cvl = col.AddComponent<VerticalLayoutGroup>();
        cvl.spacing = 3f;
        cvl.childControlWidth = cvl.childControlHeight = true;
        cvl.childForceExpandWidth = true;
        cvl.childForceExpandHeight = false;

        var line = Row(col.transform, TextAnchor.MiddleLeft, 6f);
        row.label = NewText(line, "", 15f, TextColor);
        row.label.name         = "Label";
        row.label.overflowMode = TextOverflowModes.Ellipsis;
        row.label.richText     = true;
        row.label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        row.count = NewText(line, "", 14f, TextColor, FontStyles.Bold);
        row.count.name      = "Count";
        row.count.alignment = TextAlignmentOptions.Right;
        row.count.gameObject.AddComponent<LayoutElement>().minWidth = 44f;

        var barFill = CuteUIKit.Bar(col.transform, 9f, CuteUIKit.LatteLight, ProgressColor);
        row.fill = barFill.GetComponent<Image>();
        CuteUIKit.SetBar(barFill, 0f);

        rows.Add(row);
    }

    private void RemoveLastRow()
    {
        if (rows.Count == 0) return;
        var last = rows[rows.Count - 1];
        Destroy(last.root);
        rows.RemoveAt(rows.Count - 1);
    }

    private void ClearRows()
    {
        while (rows.Count > 0) RemoveLastRow();
    }

    private TextMeshProUGUI NewText(Transform parent, string text, float size, Color color,
                                    FontStyles style = FontStyles.Normal, bool wrap = false)
    {
        var t = CuteUIKit.Text(parent, text, size, color, style, TextAlignmentOptions.Left);
        if (wrap) t.textWrappingMode = TextWrappingModes.Normal;
        if (fontAsset != null) t.font = fontAsset;
        return t;
    }

    private static Image NewIcon(Transform parent, Sprite sprite, float size)
    {
        var go = new GameObject("Icon", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite         = sprite;
        img.preserveAspect = true;
        img.raycastTarget  = false;
        img.enabled        = sprite != null;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = size;
        le.minWidth = le.minHeight = size;
        return img;
    }

    private static Transform Row(Transform parent, TextAnchor align, float spacing)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = spacing;
        hlg.childAlignment         = align;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        return go.transform;
    }

    private void ApplyFont(Component root)
    {
        if (fontAsset == null) return;
        foreach (var t in root.GetComponentsInChildren<TextMeshProUGUI>(true)) t.font = fontAsset;
    }

    private Image FindLayerImage(string layerName)
    {
        var t = transform.Find(layerName);
        return t != null ? t.GetComponent<Image>() : null;
    }

    [ContextMenu("Rebuild UI")]
    public void RebuildUI()
    {
        rows.Clear();
        objectiveContainer = null;
        questNameText = null;
        panelCG = null;
        cardFill = cardBorder = null;
        logSectionGO = null;
        logContentRT = null;
        logOpen = false;
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
        foreach (var fx in GetComponents<BaseMeshEffect>()) DestroyImmediate(fx);
        var img = GetComponent<Image>();              if (img) DestroyImmediate(img);
        var vlg = GetComponent<VerticalLayoutGroup>();if (vlg) DestroyImmediate(vlg);
        var cg  = GetComponent<CanvasGroup>();        if (cg)  DestroyImmediate(cg);
        var csf = GetComponent<ContentSizeFitter>(); if (csf) DestroyImmediate(csf);
        BuildPanel();
    }
}
