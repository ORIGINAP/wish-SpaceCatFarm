using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(RectTransform))]
public class TechTreeUI : MonoBehaviour
{
    public static TechTreeUI Instance => Services.Get<TechTreeUI>();

    [Header("설정")]
    [SerializeField] private KeyCode toggleKey = KeyCode.T;

    private const float WinW  = 1040f;
    private const float WinH  = 640f;
    private const float ListW = 440f;
    private const float RowH  = 60f;

    private static readonly TechCategory[] Categories =
        { TechCategory.Farming, TechCategory.Automation, TechCategory.Shuttle, TechCategory.Pioneer };

    private static readonly Color PipDone = CuteUIKit.Darker(CuteUIKit.ButtonMint, 0.8f);
    private static readonly Color PipTodo = CuteUIKit.Latte;

    public bool IsOpen { get; private set; }

    private RectTransform   panelRT;
    private CanvasGroup     panelCG;
    private Coroutine       animCo;
    private TextMeshProUGUI currencyText, subtitleText, footerText;
    private long            shownCoins = long.MinValue;
    private bool            currencySubscribed, inventorySubscribed, techSubscribed;
    private ScrollRect      listScroll;
    private RectTransform   listContent;
    private RectTransform   detailPane;
    private TechNodeData    selected;
    private bool            dirty;

    private class Row
    {
        public TechNodeData    node;
        public RectTransform   rt;
        public Image           border;
        public Image           statusBg;
        public TextMeshProUGUI status;
        public readonly List<Image> pips = new();
    }
    private readonly List<Row> rows = new();

    private Button researchButton;

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }
        RebuildUI();
        panelRT.localScale     = Vector3.zero;
        panelCG.alpha          = 0f;
        panelCG.blocksRaycasts = panelCG.interactable = false;
        UIStyleKit.SetScrolling(panelRT, false);
    }

    private void Start()
    {
        // 매니저 Awake가 늦으면 다시 빌드
        if (rows.Count == 0 && TechTreeManager.Instance != null)
        {
            RebuildUI();
            panelRT.localScale = Vector3.zero;
            UIStyleKit.SetScrolling(panelRT, false);
        }
        Subscribe();
    }

    private void OnDestroy()
    {
        if (techSubscribed && TechTreeManager.Instance != null) TechTreeManager.Instance.OnTechChanged -= MarkDirty;
        if (currencySubscribed && CurrencyManager.Instance != null) CurrencyManager.Instance.OnCurrencyChanged -= HandleCurrencyChanged;
        if (inventorySubscribed && Inventory.Instance != null) Inventory.Instance.OnInventoryChanged -= MarkDirty;
        Services.Unregister(this);
    }

    private void Subscribe()
    {
        if (!techSubscribed && TechTreeManager.Instance != null)
        {
            TechTreeManager.Instance.OnTechChanged += MarkDirty;
            techSubscribed = true;
        }
        if (!currencySubscribed && CurrencyManager.Instance != null)
        {
            CurrencyManager.Instance.OnCurrencyChanged += HandleCurrencyChanged;
            currencySubscribed = true;
        }
        if (!inventorySubscribed && Inventory.Instance != null)
        {
            Inventory.Instance.OnInventoryChanged += MarkDirty;
            inventorySubscribed = true;
        }
    }

    private void MarkDirty() => dirty = true;
    private void HandleCurrencyChanged(long _) => dirty = true;

    private void Update()
    {
        if (!TextInputFocus.Active && (GameInput.GetKeyDown(toggleKey) || (InputManager.Instance != null && InputManager.Instance.TechButton))) Toggle();
        if (IsOpen && EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) Toggle();
        if (!IsOpen) return;

        UpdateCurrencyText();
        if (dirty)
        {
            dirty = false;
            RefreshAll();
        }
    }

    private void UpdateCurrencyText()
    {
        if (currencyText == null || CurrencyManager.Instance == null) return;
        long coins = CurrencyManager.Instance.Currency;
        if (coins == shownCoins) return;
        shownCoins = coins;
        currencyText.text = CurrencyManager.Format(coins);
    }

    public void Toggle()
    {
        IsOpen = !IsOpen;
        Sfx.Play(IsOpen ? SfxId.UiOpen : SfxId.UiClose);
        if (IsOpen)
        {
            Subscribe();
            transform.SetAsLastSibling();
            if (selected == null || !Visible(selected)) selected = DefaultPick();
            RefreshAll();
            footerText.text = Pad.Active ? "목록에 포인터를 올리면 골라요 · A: 연구하기 · LT / RT: 분야 넘기기 · B: 닫기"
                                         : "목록에서 연구를 고르고 [연구하기] · Esc / T: 닫기";
            StartCoroutine(ScrollToSelectedAfterLayout());
        }
        else UIStyleKit.ClearSelectionIn(transform);
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(IsOpen));
    }

    private IEnumerator Animate(bool open)
    {
        if (panelCG != null) panelCG.blocksRaycasts = panelCG.interactable = open;
        if (open)
        {
            UIStyleKit.SetScrolling(panelRT, true);
            CuteUIKit.WiggleEars(this, transform);
            yield return CuteUIKit.PopIn(panelRT, panelCG);
        }
        else
        {
            yield return CuteUIKit.PopOut(panelRT, panelCG);
            UIStyleKit.SetScrolling(panelRT, false);
        }
    }

    public void CycleCategory(int delta)
    {
        if (!IsOpen || rows.Count == 0) return;
        int cur = System.Array.IndexOf(Categories, selected != null ? selected.category : TechCategory.Farming);
        for (int step = 1; step <= Categories.Length; step++)
        {
            var cat = Categories[((cur + delta * step) % Categories.Length + Categories.Length) % Categories.Length];
            var row = rows.Find(r => r.node.category == cat);
            if (row == null) continue;
            Select(row.node);
            ScrollTo(row.rt);
            Sfx.Play(SfxId.UiHover, 0.6f);
            return;
        }
    }

    private enum State { Done, Ready, NeedCoins, NeedItems, Locked }

    private static State StateOf(TechNodeData n)
    {
        var tech = TechTreeManager.Instance;
        if (n.IsMaxed) return State.Done;
        if (tech == null || !tech.ArePrerequisitesMet(n)) return State.Locked;
        if (CurrencyManager.Instance == null || !CurrencyManager.Instance.CanAfford(tech.CostOf(n))) return State.NeedCoins;
        if (!TechTreeManager.HasItems(n)) return State.NeedItems;
        return State.Ready;
    }

    private static bool Visible(TechNodeData n) => n != null;

    private TechNodeData DefaultPick()
    {
        var tech = TechTreeManager.Instance;
        if (tech == null) return null;
        foreach (var n in tech.Nodes) if (StateOf(n) == State.Ready) return n;
        foreach (var n in tech.Nodes) if (StateOf(n) == State.NeedItems || StateOf(n) == State.NeedCoins) return n;
        return tech.Nodes.Length > 0 ? tech.Nodes[0] : null;
    }

    private void RefreshAll()
    {
        // 닫혀 있으면 갱신 안 함
        if (!IsOpen || TechTreeManager.Instance == null) return;
        UpdateCurrencyText();

        bool lab = CivFacility.CountPowered(CivEffect.Research) > 0;
        subtitleText.text = lab
            ? $"연구는 마을 전체에 효과가 있어요 · <color=#{Hex(CuteUIKit.MintText)}><b>기초 연구소 할인 중 (코인 −{TechTreeManager.LabDiscount * 100f:0}%)</b></color>"
            : "연구는 마을 전체에 효과가 있어요 · 기초 연구소에 전기가 들어오면 코인이 10% 싸져요";

        foreach (var r in rows) RefreshRow(r);
        BuildDetail();
    }

    private void RefreshRow(Row r)
    {
        var n = r.node;
        var state = StateOf(n);
        bool sel = n == selected;
        r.border.color = sel ? CuteUIKit.Strawberry : CuteUIKit.SlotBorder;

        for (int i = 0; i < r.pips.Count; i++)
            r.pips[i].color = i < n.currentLevel ? PipDone : PipTodo;

        (string text, Color bg, Color fg) = state switch
        {
            State.Done      => ("완료", CuteUIKit.MintTag, CuteUIKit.MintText),
            State.Ready     => ("연구 가능!", CuteUIKit.Strawberry, Color.white),
            State.NeedCoins => ("코인 부족", CuteUIKit.ButtonCoral, CuteUIKit.CoralText),
            State.NeedItems => ("재료 모으기", CuteUIKit.ButterSoft, CuteUIKit.AmberText),
            _               => ($"Lv.{n.NextCoreLevel}부터", CuteUIKit.LatteLight, CuteUIKit.CocoaSoft),
        };
        r.status.text   = text;
        r.status.color  = fg;
        r.statusBg.color = bg;
    }

    private void Select(TechNodeData n)
    {
        if (n == null || n == selected) return;
        selected = n;
        foreach (var r in rows) RefreshRow(r);
        BuildDetail();
    }

    [ContextMenu("Rebuild UI")]
    public void RebuildUI()
    {
        rows.Clear();
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
        BuildPanel();
    }

    private void BuildPanel()
    {
        panelRT = GetComponent<RectTransform>();
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = Vector2.one * 0.5f;
        panelRT.anchoredPosition = new Vector2(0f, -12f);
        panelRT.sizeDelta = new Vector2(WinW, WinH);

        if (GetComponent<Image>() == null) gameObject.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT, catEars: true, earInset: 140f);
        panelCG = GetComponent<CanvasGroup>();
        if (panelCG == null) panelCG = gameObject.AddComponent<CanvasGroup>();

        var vlg = GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(18, 18, 14, 12);
        vlg.spacing                = 8f;
        vlg.childAlignment         = TextAnchor.UpperCenter;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;

        BuildTitleBar();

        subtitleText = CuteUIKit.Text(transform, "", 12f, CuteUIKit.CocoaSoft);
        subtitleText.richText = true;
        subtitleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f;

        var body = MakeGO("Body", transform);
        body.AddComponent<LayoutElement>().flexibleHeight = 1f;
        var hlg = body.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = 14f;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = true;
        BuildListPane(body.transform);
        BuildDetailPane(body.transform);

        footerText = CuteUIKit.Text(transform, "", 11f, CuteUIKit.CocoaMuted);
        footerText.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;
    }

    private void BuildTitleBar()
    {
        var bar = MakeGO("TitleBar", transform);
        var le  = bar.AddComponent<LayoutElement>();
        le.preferredHeight = 40f;
        le.flexibleHeight  = 0f;

        var ribbon = CuteUIKit.TitleRibbon(bar.transform, "연구", toggleKey.ToString(), 18f);
        ribbon.anchorMin = ribbon.anchorMax = ribbon.pivot = new Vector2(0.5f, 0.5f);
        ribbon.anchoredPosition = Vector2.zero;
        FitToContent(ribbon.gameObject);

        var right   = MakeGO("Right", bar.transform);
        var rightRT = right.GetComponent<RectTransform>();
        rightRT.anchorMin = rightRT.anchorMax = rightRT.pivot = new Vector2(1f, 0.5f);
        rightRT.anchoredPosition = Vector2.zero;
        var rh = right.AddComponent<HorizontalLayoutGroup>();
        rh.spacing                = 8f;
        rh.childAlignment         = TextAnchor.MiddleRight;
        rh.childControlWidth      = true;
        rh.childControlHeight     = true;
        rh.childForceExpandWidth  = false;
        rh.childForceExpandHeight = false;
        FitToContent(right);

        CoinPill(right.transform, "--", CuteUIKit.ButtonButter, 16f, 16f, out currencyText);
        shownCoins = long.MinValue;
        CuteUIKit.CloseButton(right.transform, Toggle, 28f);
    }

    private void BuildListPane(Transform body)
    {
        var pane = MakeGO("ListPane", body);
        var le = pane.AddComponent<LayoutElement>();
        le.preferredWidth = le.minWidth = ListW;
        le.flexibleWidth  = 0f;
        Board(pane.transform);

        listContent = HelpUI.BuildScrollView((RectTransform)pane.transform, new Vector2(10f, 10f), new Vector2(-6f, -10f));
        listScroll  = listContent.GetComponentInParent<ScrollRect>();
        var vlg = listContent.GetComponent<VerticalLayoutGroup>();
        vlg.spacing = 6f;
        vlg.padding = new RectOffset(2, 4, 2, 8);

        var tech = TechTreeManager.Instance;
        if (tech == null) return;
        foreach (var cat in Categories)
        {
            var (tag, text) = CategoryColors(cat);
            CuteUIKit.SectionHeader(listContent, TechTreeManager.CategoryName(cat), tag, text, CategoryNote(cat));
            foreach (var n in tech.Nodes)
                if (n.category == cat) rows.Add(BuildRow(n));
        }
    }

    private Row BuildRow(TechNodeData n)
    {
        var r = new Row { node = n };
        r.border = CuteUIKit.Card(listContent, CuteUIKit.SlotBorder, 14, $"Row_{n.id}");
        r.border.raycastTarget = true;
        r.rt = r.border.rectTransform;
        CuteUIKit.Layer(r.rt, "Fill", CuteUIKit.Rounded(12), CuteUIKit.Lighter(CuteUIKit.Cream, 0.4f), 2.5f);
        var le = r.border.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = RowH;
        var hlg = r.border.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(10, 10, 7, 7);
        hlg.spacing                = 10f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        Icon(r.rt, n, 44f);

        var mid = MakeGO("Mid", r.rt);
        var mle = mid.AddComponent<LayoutElement>();
        mle.flexibleWidth  = 1f;
        mle.preferredWidth = 0f;
        var mv = mid.AddComponent<VerticalLayoutGroup>();
        mv.spacing                = 5f;
        mv.childAlignment         = TextAnchor.MiddleLeft;
        mv.childControlWidth      = true;
        mv.childControlHeight     = true;
        mv.childForceExpandWidth  = false;
        mv.childForceExpandHeight = false;
        CuteUIKit.Text(mid.transform, n.displayName, 15f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        var pips = MakeGO("Pips", mid.transform);
        var ph = pips.AddComponent<HorizontalLayoutGroup>();
        ph.spacing                = 4f;
        ph.childControlWidth      = true;
        ph.childControlHeight     = true;
        ph.childForceExpandWidth  = false;
        ph.childForceExpandHeight = false;
        for (int i = 0; i < n.maxLevel; i++)
        {
            var pip = CuteUIKit.Card(pips.transform, CuteUIKit.LatteLight, 4, "Pip");
            pip.raycastTarget = false;
            var ple = pip.gameObject.AddComponent<LayoutElement>();
            ple.preferredWidth = ple.minWidth = 18f;
            ple.preferredHeight = ple.minHeight = 8f;
            r.pips.Add(pip);
        }

        var pill = CuteUIKit.Pill(r.rt, "", CuteUIKit.LatteLight, CuteUIKit.CocoaSoft, 11f, 9, new RectOffset(9, 9, 3, 3), out r.status);
        r.statusBg = pill.GetComponent<Image>();

        var btn = r.border.gameObject.AddComponent<Button>();
        btn.transition    = Selectable.Transition.None;
        btn.targetGraphic = r.border;
        var cap = n;
        btn.onClick.AddListener(() => { Select(cap); Sfx.Play(SfxId.UiHover, 0.6f); });
        r.border.gameObject.AddComponent<CutePop>();
        r.border.gameObject.AddComponent<HoverHook>().onHover = on => { if (on && Pad.Active) Select(cap); };
        return r;
    }

    private void BuildDetailPane(Transform body)
    {
        var pane = MakeGO("DetailPane", body);
        var le = pane.AddComponent<LayoutElement>();
        le.flexibleWidth  = 1f;
        le.preferredWidth = 0f;
        Board(pane.transform);
        detailPane = (RectTransform)pane.transform;
        var vlg = pane.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(18, 18, 16, 16);
        vlg.spacing                = 10f;
        vlg.childAlignment         = TextAnchor.UpperLeft;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
    }

    private void BuildDetail()
    {
        if (detailPane == null) return;
        for (int i = detailPane.childCount - 1; i >= 0; i--)
        {
            var c = detailPane.GetChild(i);
            if (c.name == "BoardFill" || c.name == "BoardEdge") continue;
            c.gameObject.SetActive(false);
            Destroy(c.gameObject);
        }
        researchButton = null;
        var n = selected;
        var tech = TechTreeManager.Instance;
        if (n == null || tech == null) return;

        var head = HRow(detailPane, 12f, TextAnchor.MiddleLeft);
        Icon(head, n, 68f);
        var titleCol = MakeGO("Title", head);
        titleCol.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var tv = titleCol.AddComponent<VerticalLayoutGroup>();
        tv.spacing                = 4f;
        tv.childAlignment         = TextAnchor.MiddleLeft;
        tv.childControlWidth      = true;
        tv.childControlHeight     = true;
        tv.childForceExpandWidth  = false;
        tv.childForceExpandHeight = false;
        CuteUIKit.Text(titleCol.transform, n.displayName, 22f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        var tags = HRow(titleCol.transform, 6f, TextAnchor.MiddleLeft);
        var (tag, tagText) = CategoryColors(n.category);
        CuteUIKit.Pill(tags, TechTreeManager.CategoryName(n.category), tag, tagText, 11f, 9, new RectOffset(9, 9, 2, 2), out _);
        CuteUIKit.Pill(tags, n.IsMaxed ? $"{n.maxLevel}단계 모두 연구함" : $"{n.currentLevel} / {n.maxLevel}단계",
                       n.IsMaxed ? CuteUIKit.MintTag : CuteUIKit.LatteLight, n.IsMaxed ? CuteUIKit.MintText : CuteUIKit.Cocoa,
                       11f, 9, new RectOffset(9, 9, 2, 2), out _);

        var desc = CuteUIKit.Text(detailPane, n.description, 14f, CuteUIKit.CocoaSoft, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        desc.textWrappingMode = TextWrappingModes.Normal;

        var fx = CuteUIKit.Card(detailPane, CuteUIKit.Lighter(CuteUIKit.MintTag, 0.4f), 14, "Effect");
        fx.raycastTarget = false;
        var fh = fx.gameObject.AddComponent<HorizontalLayoutGroup>();
        fh.padding                = new RectOffset(14, 14, 10, 10);
        fh.spacing                = 10f;
        fh.childAlignment         = TextAnchor.MiddleCenter;
        fh.childControlWidth      = true;
        fh.childControlHeight     = true;
        fh.childForceExpandWidth  = false;
        fh.childForceExpandHeight = false;
        CuteUIKit.Text(fx.transform, "지금", 12f, CuteUIKit.CocoaSoft);
        CuteUIKit.Text(fx.transform, n.currentLevel > 0 ? Effect(n, n.currentLevel) : "없음", 20f, CuteUIKit.Cocoa, FontStyles.Bold);
        if (!n.IsMaxed)
        {
            CuteUIKit.Text(fx.transform, ">", 16f, CuteUIKit.Strawberry, FontStyles.Bold);
            CuteUIKit.Text(fx.transform, "연구하면", 12f, CuteUIKit.CocoaSoft);
            CuteUIKit.Text(fx.transform, Effect(n, n.currentLevel + 1), 22f, CuteUIKit.MintText, FontStyles.Bold);
        }
        else CuteUIKit.Text(fx.transform, "— 최고 단계예요!", 14f, CuteUIKit.MintText, FontStyles.Bold);

        CuteUIKit.SectionHeader(detailPane, "단계", CuteUIKit.LatteLight, CuteUIKit.Cocoa, "단계마다 문명 단계 · 코인 · 재료");
        for (int lv = 0; lv < n.maxLevel; lv++) BuildLevelRow(n, lv);

        if (n.IsMaxed) return;
        CuteUIKit.DottedDivider(detailPane);
        var foot = HRow(detailPane, 10f, TextAnchor.MiddleLeft);
        int coins = tech.CostOf(n);
        bool canCoins = CurrencyManager.Instance != null && CurrencyManager.Instance.CanAfford(coins);
        CoinPill(foot, coins.ToString("N0"), canCoins ? CuteUIKit.ButterSoft : CuteUIKit.ButtonCoral, 15f, 16f, out var coinText);
        if (!canCoins) coinText.color = CuteUIKit.CoralText;
        var (item, amount) = TechTreeManager.ItemCostOf(n);
        if (item != null)
        {
            int have = Inventory.Instance != null ? Inventory.Instance.GetCount(item) : 0;
            ItemNeed(foot, item, have, amount);
        }
        var spacer = MakeGO("Spacer", foot);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;

        var state = StateOf(n);
        researchButton = CuteUIKit.Button(foot, "연구하기", state == State.Ready ? CuteUIKit.ButtonMint : CuteUIKit.ButtonLatte, OnResearch, 150f, 40f, 16f);
        researchButton.interactable = state == State.Ready;
        researchButton.gameObject.AddComponent<GamepadDefault>();

        string why = state switch
        {
            State.Locked    => $"문명 Lv.{n.NextCoreLevel}이 되면 연구할 수 있어요 (지금 Lv.{CenterCore.CurrentLevel})",
            State.NeedCoins => $"코인이 {coins - (CurrencyManager.Instance != null ? CurrencyManager.Instance.Currency : 0):N0} 모자라요 — 셔틀 짐값 · 작물 판매로 모아요",
            State.NeedItems => item != null ? $"{item.itemName} {amount - (Inventory.Instance != null ? Inventory.Instance.GetCount(item) : 0)}개가 더 필요해요" : "",
            _               => "연구할 수 있어요!",
        };
        var whyText = CuteUIKit.Text(detailPane, why, 12f, state == State.Ready ? CuteUIKit.MintText : CuteUIKit.CoralText,
                                     FontStyles.Bold, TextAlignmentOptions.MidlineRight);
        whyText.textWrappingMode = TextWrappingModes.Normal;
    }

    private void BuildLevelRow(TechNodeData n, int lv)
    {
        bool done = lv < n.currentLevel, next = lv == n.currentLevel;
        var card = CuteUIKit.Card(detailPane, next ? CuteUIKit.Lighter(CuteUIKit.PinkSoft, 0.35f) : done ? CuteUIKit.Lighter(CuteUIKit.MintTag, 0.5f) : Color.clear,
                                  10, $"Lv{lv + 1}");
        card.raycastTarget = false;
        var h = card.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding                = new RectOffset(10, 10, 4, 4);
        h.spacing                = 10f;
        h.childAlignment         = TextAnchor.MiddleLeft;
        h.childControlWidth      = true;
        h.childControlHeight     = true;
        h.childForceExpandWidth  = false;
        h.childForceExpandHeight = false;

        Color ink = done ? CuteUIKit.MintText : next ? CuteUIKit.Cocoa : CuteUIKit.CocoaSoft;
        var num = CuteUIKit.Text(card.transform, done ? "완" : $"{lv + 1}", 13f, ink, FontStyles.Bold);
        num.gameObject.AddComponent<LayoutElement>().preferredWidth = 18f;
        var eff = CuteUIKit.Text(card.transform, Effect(n, lv + 1), 13f, ink, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        eff.gameObject.AddComponent<LayoutElement>().preferredWidth = 120f;
        bool reached = CenterCore.CurrentLevel >= n.coreLevels[lv];
        CuteUIKit.Text(card.transform, $"문명 Lv.{n.coreLevels[lv]}", 12f, done || reached ? ink : CuteUIKit.CoralText, FontStyles.Normal, TextAlignmentOptions.MidlineLeft)
                 .gameObject.AddComponent<LayoutElement>().preferredWidth = 74f;
        string cost = $"{n.coinCosts[lv]:N0}코인";
        var itemName = n.itemCosts != null && lv < n.itemCosts.Length ? n.itemCosts[lv] : null;
        var item = string.IsNullOrEmpty(itemName) ? null : NetWorld.Item(itemName);
        if (item != null) cost += $" + {item.itemName} {n.itemAmounts[lv]}";
        var ct = CuteUIKit.Text(card.transform, cost, 12f, ink, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        ct.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        ct.overflowMode = TextOverflowModes.Ellipsis;
    }

    private void OnResearch()
    {
        var n = selected;
        var tech = TechTreeManager.Instance;
        if (n == null || tech == null) return;
        if (tech.TryUpgrade(n))
        {
            Sfx.Play(SfxId.Upgrade);
            NotificationUI.Instance?.Show($"연구 완료: {n.displayName} {n.currentLevel}단계 — {Effect(n, n.currentLevel)}");
            RefreshAll();
            var row = rows.Find(r => r.node == n);
            if (row != null)
            {
                CuteUIKit.SparkleBurst(this, row.rt, CuteUIKit.Butter, 14, 120f);
                StartCoroutine(CuteUIKit.Wiggle(row.rt, 6f, 0.4f));
            }
        }
        else Sfx.Play(SfxId.UiError);
    }

    private static string Effect(TechNodeData n, int level)
    {
        float v = n.EffectAt(level) * 100f;
        return n.statType switch
        {
            TechStatType.ExtraYield   => $"하나 더 열릴 확률 +{v:0}%",
            TechStatType.ShuttleSpeed => $"왕복 {v:0}% 빨리",
            TechStatType.OrderReward  => $"짐값 +{v:0}%",
            TechStatType.PickupRange  => $"범위 +{v:0}%",
            TechStatType.MoveSpeed or TechStatType.PlacementSpeed or TechStatType.MiningSpeed or TechStatType.DismantleSpeed
                or TechStatType.CropGrowth or TechStatType.MachineSpeed or TechStatType.ProcessSpeed => $"{v:0}% 빨리",
            _ => $"+{v:0}%",
        };
    }

    private static (Color tag, Color text) CategoryColors(TechCategory c) => c switch
    {
        TechCategory.Farming    => (CuteUIKit.MintTag, CuteUIKit.MintText),
        TechCategory.Automation => (CuteUIKit.SkyTag, CuteUIKit.SkyText),
        TechCategory.Shuttle    => (CuteUIKit.PinkSoft, CuteUIKit.CoralText),
        _                       => (CuteUIKit.ButterSoft, CuteUIKit.AmberText),
    };

    private static string CategoryNote(TechCategory c) => c switch
    {
        TechCategory.Farming    => "밭 · 수확",
        TechCategory.Automation => "자동화 설비 · 제련 · 요리",
        TechCategory.Shuttle    => "이주 셔틀",
        _                       => "나 (대원)",
    };

    private static Sprite IconOf(TechNodeData n)
    {
        if (n.statType == TechStatType.OrderReward)  return CuteUIKit.Coin();
        if (n.statType == TechStatType.ShuttleSpeed) return HudDock.RocketIcon;
        var item = string.IsNullOrEmpty(n.iconItem) ? null : NetWorld.Item(n.iconItem);
        return item != null ? item.icon : null;
    }

    private static void Icon(Transform parent, TechNodeData n, float size)
    {
        var (tag, _) = CategoryColors(n.category);
        var slot = CuteUIKit.Slot(parent, size, CuteUIKit.Darker(tag, 0.92f), CuteUIKit.Lighter(tag, 0.35f), Mathf.RoundToInt(size * 0.28f));
        slot.GetComponent<Image>().raycastTarget = false;
        var go = MakeGO("Icon", slot.transform);
        go.AddComponent<LayoutElement>().ignoreLayout = true;
        var img = go.AddComponent<Image>();
        img.sprite         = IconOf(n);
        img.preserveAspect = true;
        img.raycastTarget  = false;
        img.enabled        = img.sprite != null;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0.14f, 0.14f);
        rt.anchorMax = new Vector2(0.86f, 0.86f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static void ItemNeed(Transform parent, FarmingItemData item, int have, int need)
    {
        bool ok = have >= need;
        var pill = CuteUIKit.Pill(parent, $"{item.itemName} {Mathf.Min(have, 9999)} / {need}", ok ? CuteUIKit.MintTag : CuteUIKit.ButtonCoral,
                                  ok ? CuteUIKit.MintText : CuteUIKit.CoralText, 14f, 12, new RectOffset(6, 10, 3, 3), out _);
        pill.GetComponent<HorizontalLayoutGroup>().spacing = 5f;
        var go = MakeGO("Icon", pill);
        go.transform.SetAsFirstSibling();
        var img = go.AddComponent<Image>();
        img.sprite         = item.icon;
        img.preserveAspect = true;
        img.raycastTarget  = false;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = le.minWidth = le.minHeight = 22f;
    }

    private static void Board(Transform pane)
    {
        CuteUIKit.Layer(pane, "BoardEdge", CuteUIKit.Rounded(16), CuteUIKit.SlotBorder);
        CuteUIKit.Layer(pane, "BoardFill", CuteUIKit.Rounded(13), CuteUIKit.Lighter(CuteUIKit.Cream, 0.5f), 3f);
    }

    private IEnumerator ScrollToSelectedAfterLayout()
    {
        if (listScroll == null) yield break;
        listScroll.verticalNormalizedPosition = 1f;
        yield return null;
        LayoutRebuilder.ForceRebuildLayoutImmediate(listContent);
        listScroll.StopMovement();
        listScroll.verticalNormalizedPosition = 1f;
        var row = rows.Find(r => r.node == selected);
        if (row != null) ScrollTo(row.rt);
    }

    private void ScrollTo(RectTransform row)
    {
        if (listScroll == null || row == null) return;
        Canvas.ForceUpdateCanvases();
        var content  = listScroll.content;
        var viewport = listScroll.viewport;
        float contentH = content.rect.height, viewH = viewport.rect.height;
        if (contentH <= viewH) return;
        float top    = -row.anchoredPosition.y - RowH * 0.5f;
        float scroll = content.anchoredPosition.y;
        if (top >= scroll && top + RowH <= scroll + viewH) return;
        float y = top - RowH * 0.6f;
        listScroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(y / (contentH - viewH));
    }

    private static Transform HRow(Transform parent, float spacing, TextAnchor align)
    {
        var go = MakeGO("Row", parent);
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing                = spacing;
        h.childAlignment         = align;
        h.childControlWidth      = true;
        h.childControlHeight     = true;
        h.childForceExpandWidth  = false;
        h.childForceExpandHeight = false;
        return go.transform;
    }

    private static RectTransform CoinPill(Transform parent, string text, Color fill, float fontSize, float iconSize, out TextMeshProUGUI label)
    {
        var pill = CuteUIKit.Pill(parent, text, fill, CuteUIKit.Cocoa, fontSize, Mathf.CeilToInt(fontSize),
                                  new RectOffset(5, 9, 3, 3), out label);
        pill.GetComponent<HorizontalLayoutGroup>().spacing = 3f;
        var coinGO = MakeGO("Coin", pill);
        coinGO.transform.SetAsFirstSibling();
        var coin = coinGO.AddComponent<Image>();
        coin.sprite         = CuteUIKit.Coin();
        coin.preserveAspect = true;
        coin.raycastTarget  = false;
        var le = coinGO.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = le.minWidth = le.minHeight = iconSize;
        return pill;
    }

    private static void FitToContent(GameObject go)
    {
        var fit = go.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private static GameObject MakeGO(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

    private sealed class HoverHook : MonoBehaviour, IPointerEnterHandler, ISelectHandler
    {
        public System.Action<bool> onHover;
        public void OnPointerEnter(PointerEventData e) => onHover?.Invoke(true);
        public void OnSelect(BaseEventData e)          => onHover?.Invoke(true);
    }
}
