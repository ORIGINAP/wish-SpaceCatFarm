using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;
using System.Collections.Generic;

// 제작 창, 왼쪽 목록 오른쪽 상세
[RequireComponent(typeof(RectTransform))]
public class CraftingTableUI : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    public static CraftingTableUI Instance => Services.Get<CraftingTableUI>();

    [Header("설정")]
    [SerializeField] private KeyCode toggleKey = KeyCode.C;
    [Header("폰트")]
    [SerializeField] private TMP_FontAsset fontAsset;

    private const float TileW = 100f, TileH = 118f, TileGap = 8f, TileSlot = 52f;
    private const int   TileCols = 4, TileRows = 3;
    private const int   TileRadius = 14;
    private const float ListW   = TileCols * TileW + (TileCols - 1) * TileGap;
    private const float DetailW = 300f;
    private const int   MaxIngredients = 6;
    private const float IngRowH = 32f, IngGap = 2f;
    private static float BodyW => (ListW + 12f + 24f) + 14f + (DetailW + 24f);

    private static readonly Color CardFill    = CuteUIKit.Lighter(CuteUIKit.Cream, 0.5f);
    private static readonly Color DimFill     = CuteUIKit.Hex(0xEFE6DC);
    private static readonly Color ReadyBorder = CuteUIKit.Darker(CuteUIKit.ButtonMint, 0.9f);
    private static readonly Color RowFill     = CuteUIKit.Hex(0xFFF9F1);

    private const string EnoughHex  = "#3F8A66";
    private const string MissingHex = "#C9485B";

    private static readonly int[] Amounts = { 1, 5, 0 };
    private static int amountChoice;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => amountChoice = 0;

    private enum Tab { Material, Farming, Machine, Storage, Decoration, Civilization, Gear }
    private static readonly string[] TabLabels = { "재료", "농사", "설비", "창고", "꾸미기", "문명", "도구" };

    private const int Ready = 0, Missing = 1, NoStation = 2, Locked = 3;

    public bool IsOpen { get; private set; }

    private RectTransform panelRT;
    private CanvasGroup   panelCG;
    private Coroutine     animCo;
    private Vector2       dragOffset;
    private bool          dragged;

    private TextMeshProUGUI cocoText;
    private float           holdLineUntil;
    private object          holdOwner;

    private Tab currentTab = Tab.Material;
    private readonly List<Button>     tabButtons = new();
    private readonly List<GameObject> tabBadges  = new();
    private readonly List<TextMeshProUGUI> tabBadgeTexts = new();
    private readonly List<Image>      tabBadgeFills = new();

    private Transform       tileGrid;
    private ScrollRect      tileScroll;
    private TextMeshProUGUI lockedNote;
    private readonly List<Tile> tiles = new();
    private CraftingRecipe  selected;

    private RectTransform   detailSlot;
    private Image           detailFrame, detailFill, detailIcon;
    private GameObject      outBadge;
    private TextMeshProUGUI outText, detailName, detailOwned, detailDesc, tierText, kindText;
    private Image           tierFill, kindFill;
    private readonly List<IngRow> ingRows = new();
    private Button[]        amountBtns;
    private Button          craftBtn;
    private TextMeshProUGUI craftLabel;

    private bool  inventoryDirty, rebuildPending;
    private float nextStationCheck;
    private bool  stationReady, stationNear;
    private Tile   hoveredTile;
    private IngRow hoveredRow;

    private sealed class Tile
    {
        public CraftingRecipe  recipe;
        public RectTransform   rt;
        public Image           frame, fill, statusFill;
        public CanvasGroup     cg;
        public GameObject      ownedBadge, questPill;
        public TextMeshProUGUI owned, status;
    }

    private sealed class IngRow
    {
        public GameObject      go;
        public RectTransform   rt;
        public Image           frame, fill, icon;
        public TextMeshProUGUI name, hint, count;
        public CraftingRecipe.Ingredient ing;
        public CraftingRecipe  source;
    }

    private sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action<bool> on;
        public void OnPointerEnter(PointerEventData e) => on?.Invoke(true);
        public void OnPointerExit(PointerEventData e)  => on?.Invoke(false);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(panelRT, e.position, e.pressEventCamera, out dragOffset);
    }
    public void OnDrag(PointerEventData e)
    {
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(transform.parent as RectTransform, e.position, e.pressEventCamera, out Vector2 lp))
        {
            panelRT.anchoredPosition = lp - dragOffset * panelRT.localScale.x;
            dragged = true;
        }
    }

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }

        ClearBuilt();
        BuildWindow();
        panelRT.localScale = Vector3.zero;
        panelCG.alpha      = 0f;
        panelCG.blocksRaycasts = panelCG.interactable = false;
        UIStyleKit.SetScrolling(panelRT, false);
    }

    private void Start()
    {
        if (Inventory.Instance != null)       Inventory.Instance.OnInventoryChanged += MarkInventoryDirty;
        if (TechTreeManager.Instance != null) TechTreeManager.Instance.OnTechChanged += MarkRebuild;
        CenterCore.OnAnyLevelUp += HandleCoreLevelUp;
        QuestGuide.OnChanged    += MarkInventoryDirty;
    }

    private void OnDestroy()
    {
        if (Inventory.Instance != null)       Inventory.Instance.OnInventoryChanged -= MarkInventoryDirty;
        if (TechTreeManager.Instance != null) TechTreeManager.Instance.OnTechChanged -= MarkRebuild;
        CenterCore.OnAnyLevelUp -= HandleCoreLevelUp;
        QuestGuide.OnChanged    -= MarkInventoryDirty;
        Services.Unregister(this);
    }

    private void HandleCoreLevelUp(CenterCore core, int level) => rebuildPending = true;
    // 한 프레임에 한 번만 갱신
    private void MarkInventoryDirty() => inventoryDirty = true;
    private void MarkRebuild() => rebuildPending = true;

    private void Update()
    {
        if (!TextInputFocus.Active)
        {
            if (GameInput.GetKeyDown(toggleKey) || (InputManager.Instance != null && InputManager.Instance.CraftingButton)) Toggle();
            if (IsOpen && EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) Toggle();
        }
        if (!IsOpen) return;

        // 제련소 거리는 가끔만 체크
        if (Time.unscaledTime >= nextStationCheck)
        {
            nextStationCheck = Time.unscaledTime + 0.5f;
            if (CheckStation()) inventoryDirty = true;
        }
        if (holdLineUntil > 0f && Time.unscaledTime >= holdLineUntil)
        {
            holdLineUntil = 0f;
            RefreshLine();
        }
    }

    private void LateUpdate()
    {
        if (!IsOpen) return;
        if (rebuildPending)
        {
            rebuildPending = inventoryDirty = false;
            RebuildTiles();
            EnsureSelection(null);
            RefreshAll();
            PlaceGamepadDefault();
        }
        else if (inventoryDirty)
        {
            inventoryDirty = false;
            RefreshAll();
        }
    }

    public void Open()
    {
        if (!IsOpen) Toggle();
    }

    public void Toggle()
    {
        IsOpen = !IsOpen;
        Sfx.Play(IsOpen ? SfxId.UiOpen : SfxId.UiClose);

        panelCG.blocksRaycasts = IsOpen;
        panelCG.interactable   = IsOpen;

        if (IsOpen)
        {
            if (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen) InventoryUI.Instance.Toggle();
            if (ShopUI.Instance != null && ShopUI.Instance.IsOpen) ShopUI.Instance.Toggle();
            transform.SetAsLastSibling();
            CheckStation();
            hoveredTile   = null;
            hoveredRow    = null;
            holdLineUntil = 0f;
            rebuildPending = inventoryDirty = false;

            var quest = QuestRecipe();
            if (quest != null && TabOf(quest) != currentTab) currentTab = TabOf(quest);
            PaintTabs();
            RebuildTiles();
            EnsureSelection(quest);
            RefreshAll();
            PlaceGamepadDefault();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRT);
            if (!dragged) panelRT.anchoredPosition = new Vector2(SideShift(), 0f);
            ItemCellUI.UpdateScrollable(tileScroll);
            ItemCellUI.ResetScroll(tileScroll);
            ScrollToTile(TileOf(selected));
        }
        else
        {
            UIStyleKit.ClearSelectionIn(transform);
        }
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(IsOpen));
    }

    private IEnumerator Animate(bool open)
    {
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

    public void CycleTab(int delta)
    {
        int count = TabLabels.Length;
        SelectTab((Tab)((((int)currentTab + delta) % count + count) % count));
        Sfx.Play(SfxId.UiClick);
    }

    public void Focus(CraftingRecipe recipe)
    {
        if (recipe == null) return;
        if (!IsOpen) Toggle();
        var tab = TabOf(recipe);
        if (tab != currentTab) SelectTab(tab);
        if (TileOf(recipe) != null) Select(recipe);
        ScrollToTile(TileOf(recipe));
    }

    public CraftingRecipe SelectedRecipe => selected;

    public RectTransform CardRect(CraftingRecipe recipe)
    {
        if (!IsOpen) return null;
        var t = TileOf(recipe);
        return t != null ? t.rt : null;
    }

    public RectTransform TabRect(CraftingRecipe recipe)
    {
        if (!IsOpen || recipe == null) return null;
        int i = (int)TabOf(recipe);
        return i < tabButtons.Count && tabButtons[i] != null ? (RectTransform)tabButtons[i].transform : null;
    }

    public bool IsInCurrentTab(CraftingRecipe recipe) => recipe != null && TabOf(recipe) == currentTab;

    public RectTransform CraftButtonRect => IsOpen && craftBtn != null && craftBtn.gameObject.activeInHierarchy ? (RectTransform)craftBtn.transform : null;

    public static string TabName(CraftingRecipe recipe) => recipe == null ? "" : TabLabels[(int)TabOf(recipe)];

    // 퀘스트 레시피 자동 포커스
    private static CraftingRecipe QuestRecipe()
    {
        var c = QuestGuide.Current;
        var r = c.recipe != null ? c.recipe : c.goal;
        return r != null && !r.IsMachineRecipe && r.outputItem != null ? r : null;
    }

    private void ClearBuilt()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
        { var c = GetComponent<Outline>();             if (c) DestroyImmediate(c); }
        { var c = GetComponent<VerticalLayoutGroup>(); if (c) DestroyImmediate(c); }
        { var c = GetComponent<ContentSizeFitter>();   if (c) DestroyImmediate(c); }
        { var c = GetComponent<CanvasGroup>();         if (c) DestroyImmediate(c); }
        tabButtons.Clear();
        tabBadges.Clear();
        tabBadgeTexts.Clear();
        tabBadgeFills.Clear();
        tiles.Clear();
        ingRows.Clear();
    }

    private void BuildWindow()
    {
        panelRT = GetComponent<RectTransform>();
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = Vector2.one * 0.5f;
        panelRT.anchoredPosition = Vector2.zero;
        panelRT.localPosition    = Vector3.zero;

        panelCG = gameObject.AddComponent<CanvasGroup>();

        if (GetComponent<Image>() == null) gameObject.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT);

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(18, 18, 14, 18);
        vlg.spacing                = 10f;
        vlg.childAlignment         = TextAnchor.UpperCenter;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = false;
        vlg.childForceExpandHeight = false;
        var fit = gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildTitleBar();
        BuildCocoLine();

        var body = ItemCellUI.HRow("Body", transform, 14f, TextAnchor.UpperLeft);
        body.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
        BuildListPane(body);
        BuildDetailPane(body);
    }

    private void BuildTitleBar()
    {
        var bar = new GameObject("TitleBar", typeof(RectTransform));
        bar.transform.SetParent(transform, false);
        var le = bar.AddComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = 34f;
        le.flexibleWidth   = 1f;

        var ribbon = CuteUIKit.TitleRibbon(bar.transform, "제작", toggleKey.ToString());
        ribbon.anchorMin = ribbon.anchorMax = ribbon.pivot = new Vector2(0.5f, 0.5f);
        ribbon.anchoredPosition = Vector2.zero;
        FitToContent(ribbon.gameObject);

        var close = CuteUIKit.CloseButton(bar.transform, Toggle, 28f);
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 0.5f);
        crt.anchoredPosition = Vector2.zero;
        crt.sizeDelta = new Vector2(28f, 28f);
    }

    private void BuildCocoLine()
    {
        var row = CocoUI.Row(transform, "", 46f, 14f, stretch: true, font: fontAsset);
        var le = row.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth  = BodyW;
        le.flexibleWidth   = 1f;
        le.preferredHeight = le.minHeight = 50f;
        var bubble = row.Find("Bubble");
        cocoText = bubble != null ? bubble.GetComponentInChildren<TextMeshProUGUI>(true) : null;
        if (cocoText != null)
        {
            cocoText.textWrappingMode = TextWrappingModes.NoWrap;
            cocoText.overflowMode     = TextOverflowModes.Ellipsis;
            cocoText.richText         = true;
            cocoText.GetComponent<LayoutElement>().minWidth = 10f;
            bubble.GetComponent<LayoutElement>().minWidth   = 10f;
        }
    }

    private void BuildListPane(Transform body)
    {
        var pane = ItemCellUI.Pane("RecipePane", body);

        var tabsRow = ItemCellUI.HRow("Tabs", pane, 4f, TextAnchor.MiddleCenter);
        var tle = tabsRow.gameObject.AddComponent<LayoutElement>();
        tle.preferredWidth  = ListW + 12f;
        tle.preferredHeight = tle.minHeight = 30f;
        var thlg = tabsRow.GetComponent<HorizontalLayoutGroup>();
        thlg.childForceExpandWidth = true;
        thlg.padding = new RectOffset(0, 0, 3, 0);
        for (int i = 0; i < TabLabels.Length; i++)
        {
            var tab = (Tab)i;
            var b = CuteUIKit.Button(tabsRow, TabLabels[i], CuteUIKit.ButtonLatte, () => { SelectTab(tab); Sfx.Play(SfxId.UiClick); }, -1f, 26f, 12f);
            var label = b.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null && fontAsset != null) label.font = fontAsset;
            tabButtons.Add(b);

            var badge = CuteUIKit.Pill(b.transform, "", CuteUIKit.MintTag, CuteUIKit.MintText, 9f, 7, new RectOffset(4, 4, 0, 1), out var bt, fitSelf: true);
            if (fontAsset != null) bt.font = fontAsset;
            badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1f, 1f);
            badge.anchoredPosition = new Vector2(3f, 6f);
            badge.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            tabBadges.Add(badge.gameObject);
            tabBadgeTexts.Add(bt);
            tabBadgeFills.Add(badge.GetComponent<Image>());
            badge.gameObject.SetActive(false);
        }
        PaintTabs();

        float h = TileRows * TileH + (TileRows - 1) * TileGap;
        tileGrid = ItemCellUI.ScrollGrid(pane, "Recipes", ListW, h, new Vector2(TileW, TileH), TileGap, TileCols, out tileScroll);
        tileScroll.GetComponent<LayoutElement>().flexibleHeight = 1f;

        lockedNote = Txt(pane, "", 11f, CuteUIKit.CocoaSoft, FontStyles.Normal);
        lockedNote.overflowMode = TextOverflowModes.Ellipsis;
        lockedNote.richText     = true;
        var nle = lockedNote.gameObject.AddComponent<LayoutElement>();
        nle.preferredWidth  = ListW + 12f;
        nle.preferredHeight = nle.minHeight = 16f;
    }

    private void PaintTabs()
    {
        for (int i = 0; i < tabButtons.Count; i++)
            CuteUIKit.ApplyButtonColors(tabButtons[i], i == (int)currentTab ? CuteUIKit.Strawberry : CuteUIKit.ButtonLatte);
    }

    private void SelectTab(Tab tab)
    {
        if (tab == currentTab && tiles.Count > 0) return;
        currentTab = tab;
        PaintTabs();
        RebuildTiles();
        EnsureSelection(QuestRecipe());
        hoveredTile = null;
        RefreshAll();
        PlaceGamepadDefault();
        ItemCellUI.ResetScroll(tileScroll);
        ScrollToTile(TileOf(selected));
    }

    private static Tab TabOf(CraftingRecipe r)
    {
        var item = r.outputItem;
        if (item == null || r.station == CraftingStation.Smelter || item.itemType == FarmingItemType.Resource) return Tab.Material;
        if (CivFacility.IsFacilityItem(item)) return Tab.Civilization;
        if (item.itemType == FarmingItemType.Tool || item.itemType == FarmingItemType.Equipment) return Tab.Gear;
        if (item.itemType == FarmingItemType.Seed) return Tab.Farming;

        var tile = item.tilePrefab != null ? item.tilePrefab.GetComponent<TileItem>() : null;
        return tile == null ? Tab.Machine : tile.TileType switch
        {
            TileType.Plant or TileType.AutoSeeder or TileType.AutoHarvester or TileType.AutoSeller
                or TileType.GrowthBooster or TileType.StormShield
                or TileType.Kitchen or TileType.Composter => Tab.Farming,
            TileType.Storage    => Tab.Storage,
            TileType.Decoration => Tab.Decoration,
            TileType.ShuttlePad => Tab.Civilization,
            _                   => Tab.Machine,
        };
    }

    private static int NeedLevel(CraftingRecipe r)
        => Mathf.Max(1, Mathf.Max(r.requiredCoreLevel, CraftingManager.TierCoreLevel(r.tier)));

    private void RebuildTiles()
    {
        if (tileGrid == null) return;
        for (int i = tileGrid.childCount - 1; i >= 0; i--) DestroyImmediate(tileGrid.GetChild(i).gameObject);
        tiles.Clear();
        hoveredTile = null;

        var cm = CraftingManager.Instance;
        if (cm == null) return;

        int level = CenterCore.CurrentLevel;
        var shown = new List<CraftingRecipe>();
        var later = new List<CraftingRecipe>();
        for (int i = 0; i < cm.Recipes.Count; i++)
        {
            var r = cm.Recipes[i];
            if (r == null || r.outputItem == null || r.IsMachineRecipe || TabOf(r) != currentTab) continue;
            if (cm.IsRecipeUnlocked(r) || NeedLevel(r) <= level + 1) shown.Add(r);
            else later.Add(r);
        }
        var order = new Dictionary<CraftingRecipe, int>();
        for (int i = 0; i < cm.Recipes.Count; i++) if (cm.Recipes[i] != null && !order.ContainsKey(cm.Recipes[i])) order[cm.Recipes[i]] = i;
        shown.Sort((a, b) =>
        {
            int la = cm.IsRecipeUnlocked(a) ? 0 : NeedLevel(a), lb = cm.IsRecipeUnlocked(b) ? 0 : NeedLevel(b);
            return la != lb ? la.CompareTo(lb) : order[a].CompareTo(order[b]);
        });
        foreach (var r in shown) tiles.Add(BuildTile(r));

        if (later.Count > 0)
        {
            int next = int.MaxValue;
            foreach (var r in later) next = Mathf.Min(next, NeedLevel(r));
            lockedNote.text = $"<color=#6E57B5>문명 Lv.{next}</color>부터 {later.Count}개 더 열려요 · 흐린 칸은 다음 단계에 열려요";
        }
        else lockedNote.text = tiles.Exists(t => !cm.IsRecipeUnlocked(t.recipe)) ? "흐린 칸은 다음 문명 단계에 열려요" : "";

        ItemCellUI.UpdateScrollable(tileScroll);
    }

    private Tile BuildTile(CraftingRecipe recipe)
    {
        var t  = new Tile { recipe = recipe };
        var go = new GameObject($"Recipe_{recipe.name}", typeof(RectTransform));
        go.transform.SetParent(tileGrid, false);
        t.rt    = (RectTransform)go.transform;
        t.frame = CuteUIKit.SetRounded(go.AddComponent<Image>(), TileRadius, CuteUIKit.SlotBorder);
        t.fill  = CuteUIKit.Layer(go.transform, "Fill", CuteUIKit.Rounded(TileRadius - 2), CardFill, 2.5f);
        t.cg    = go.AddComponent<CanvasGroup>();

        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => ClickTile(t));
        go.AddComponent<CutePop>().hoverScale = 1.06f;
        go.AddComponent<Hover>().on = on => HoverTile(t, on);

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(5, 5, 8, 7);
        vlg.spacing                = 4f;
        vlg.childAlignment         = TextAnchor.UpperCenter;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = false;
        vlg.childForceExpandHeight = false;

        var item = recipe.outputItem;
        var slot = CuteUIKit.Slot(go.transform, TileSlot, CuteUIKit.TierBorder(item, CuteUIKit.SlotBorder), CuteUIKit.TierFill(item, CuteUIKit.SlotFill));
        slot.GetComponent<Image>().raycastTarget = false;
        ItemCellUI.Icon(slot.transform, item.icon);

        var badge = CuteUIKit.Pill(slot.transform, "", CuteUIKit.Cocoa, Color.white, 9f, 7, new RectOffset(4, 4, 0, 1), out t.owned, fitSelf: true);
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1f, 0f);
        badge.anchoredPosition = new Vector2(-2f, 2f);
        t.ownedBadge = badge.gameObject;

        var name = Txt(go.transform, DisplayName(recipe), 12f, CuteUIKit.Cocoa, FontStyles.Bold);
        name.enableAutoSizing = true;
        name.fontSizeMin      = 9f;
        name.fontSizeMax      = 12f;
        name.overflowMode     = TextOverflowModes.Ellipsis;
        var nameLE = name.gameObject.AddComponent<LayoutElement>();
        nameLE.preferredWidth = TileW - 10f;
        nameLE.minHeight      = 15f;

        var status = CuteUIKit.Pill(go.transform, "", CuteUIKit.ButtonLatte, CuteUIKit.Cocoa, 10f, 9, new RectOffset(7, 7, 2, 3), out t.status);
        if (fontAsset != null) t.status.font = fontAsset;
        t.statusFill = status.GetComponent<Image>();

        var quest = CuteUIKit.Pill(go.transform, "퀘스트", CuteUIKit.Strawberry, Color.white, 9f, 8, new RectOffset(5, 5, 1, 1), out var questText, fitSelf: true);
        if (fontAsset != null) questText.font = fontAsset;
        quest.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        quest.anchorMin = quest.anchorMax = quest.pivot = new Vector2(0f, 1f);
        quest.anchoredPosition = new Vector2(3f, -3f);
        quest.localRotation    = Quaternion.Euler(0f, 0f, 8f);
        t.questPill = quest.gameObject;
        t.questPill.SetActive(false);
        return t;
    }

    private void BuildDetailPane(Transform body)
    {
        var pane = ItemCellUI.Pane("DetailPane", body);
        pane.GetComponent<VerticalLayoutGroup>().spacing = 7f;

        var head = ItemCellUI.HRow("Head", pane, 10f, TextAnchor.MiddleLeft);
        var hle = head.gameObject.AddComponent<LayoutElement>();
        hle.preferredWidth  = DetailW;
        hle.preferredHeight = hle.minHeight = 70f;

        var slot = CuteUIKit.Slot(head, 66f, CuteUIKit.SlotBorder, CuteUIKit.SlotFill, 16);
        detailSlot  = (RectTransform)slot.transform;
        detailFrame = slot.GetComponent<Image>();
        detailFrame.raycastTarget = false;
        detailFill  = slot.transform.Find("Fill").GetComponent<Image>();
        detailIcon  = ItemCellUI.Icon(slot.transform, null);
        var ob = CuteUIKit.Pill(slot.transform, "", CuteUIKit.Cocoa, Color.white, 11f, 8, new RectOffset(5, 5, 0, 1), out outText, fitSelf: true);
        if (fontAsset != null) outText.font = fontAsset;
        ob.anchorMin = ob.anchorMax = ob.pivot = new Vector2(1f, 0f);
        ob.anchoredPosition = new Vector2(-3f, 3f);
        outBadge = ob.gameObject;

        var info = new GameObject("Info", typeof(RectTransform));
        info.transform.SetParent(head, false);
        var ivlg = info.AddComponent<VerticalLayoutGroup>();
        ivlg.spacing                = 4f;
        ivlg.childAlignment         = TextAnchor.MiddleLeft;
        ivlg.childControlWidth      = true;
        ivlg.childControlHeight     = true;
        ivlg.childForceExpandWidth  = false;
        ivlg.childForceExpandHeight = false;
        var ile = info.AddComponent<LayoutElement>();
        ile.preferredWidth = DetailW - 76f;
        ile.flexibleWidth  = 1f;

        detailName = Txt(info.transform, "", 17f, CuteUIKit.Cocoa, FontStyles.Bold);
        detailName.alignment    = TextAlignmentOptions.MidlineLeft;
        detailName.overflowMode = TextOverflowModes.Ellipsis;
        var dnle = detailName.gameObject.AddComponent<LayoutElement>();
        dnle.preferredWidth = DetailW - 76f;
        dnle.minHeight      = 21f;

        var pills = ItemCellUI.HRow("Pills", info.transform, 4f, TextAnchor.MiddleLeft);
        tierFill = CuteUIKit.Pill(pills, "", CuteUIKit.ButtonLatte, CuteUIKit.Cocoa, 10f, 8, new RectOffset(6, 6, 1, 2), out tierText).GetComponent<Image>();
        kindFill = CuteUIKit.Pill(pills, "", CuteUIKit.SkyTag, CuteUIKit.SkyText, 10f, 8, new RectOffset(6, 6, 1, 2), out kindText).GetComponent<Image>();
        if (fontAsset != null) { tierText.font = fontAsset; kindText.font = fontAsset; }

        detailOwned = Txt(info.transform, "", 11f, CuteUIKit.CocoaSoft, FontStyles.Normal);
        detailOwned.alignment = TextAlignmentOptions.MidlineLeft;
        detailOwned.gameObject.AddComponent<LayoutElement>().minHeight = 14f;

        detailDesc = Txt(pane, "", 12f, CuteUIKit.CocoaSoft, FontStyles.Normal);
        detailDesc.alignment        = TextAlignmentOptions.TopLeft;
        detailDesc.textWrappingMode = TextWrappingModes.Normal;
        detailDesc.overflowMode     = TextOverflowModes.Ellipsis;
        detailDesc.richText         = true;
        detailDesc.lineSpacing      = 2f;
        var dle = detailDesc.gameObject.AddComponent<LayoutElement>();
        dle.preferredWidth  = DetailW;
        dle.preferredHeight = dle.minHeight = 48f;

        CuteUIKit.DottedDivider(pane, DetailW);

        var ingHead = ItemCellUI.HRow("IngHeader", pane, 6f, TextAnchor.MiddleLeft);
        var ihle = ingHead.gameObject.AddComponent<LayoutElement>();
        ihle.preferredWidth  = DetailW;
        ihle.preferredHeight = ihle.minHeight = 16f;
        Txt(ingHead, "필요한 재료", 12f, CuteUIKit.Cocoa, FontStyles.Bold);
        var spacer = new GameObject("Spacer", typeof(RectTransform));
        spacer.transform.SetParent(ingHead, false);
        spacer.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Txt(ingHead, "가진 수 / 필요", 11f, CuteUIKit.CocoaMuted, FontStyles.Normal);

        var list = new GameObject("Ingredients", typeof(RectTransform));
        list.transform.SetParent(pane, false);
        var lvlg = list.AddComponent<VerticalLayoutGroup>();
        lvlg.spacing                = IngGap;
        lvlg.childAlignment         = TextAnchor.UpperLeft;
        lvlg.childControlWidth      = true;
        lvlg.childControlHeight     = true;
        lvlg.childForceExpandWidth  = true;
        lvlg.childForceExpandHeight = false;
        var lle = list.AddComponent<LayoutElement>();
        lle.preferredWidth  = DetailW;
        lle.preferredHeight = lle.minHeight = MaxIngredients * IngRowH + (MaxIngredients - 1) * IngGap;
        for (int i = 0; i < MaxIngredients; i++) ingRows.Add(BuildIngRow(list.transform));

        var fill = new GameObject("Spacer", typeof(RectTransform));
        fill.transform.SetParent(pane, false);
        fill.AddComponent<LayoutElement>().flexibleHeight = 1f;

        craftBtn = CuteUIKit.Button(pane, "만들기", CuteUIKit.ButtonMint, () => Craft(selected, false), DetailW, 44f, 16f);
        craftBtn.name = "Btn_Craft";
        craftLabel = craftBtn.GetComponentInChildren<TextMeshProUGUI>(true);
        if (fontAsset != null) craftLabel.font = fontAsset;
        craftLabel.overflowMode     = TextOverflowModes.Ellipsis;
        craftLabel.enableAutoSizing = true;
        craftLabel.fontSizeMin      = 11f;
        craftLabel.fontSizeMax      = 16f;

        var amountRow = ItemCellUI.HRow("Amount", pane, 6f, TextAnchor.MiddleCenter);
        var arle = amountRow.gameObject.AddComponent<LayoutElement>();
        arle.preferredWidth  = DetailW;
        arle.preferredHeight = arle.minHeight = 26f;
        amountBtns = ItemCellUI.AmountPicker(amountRow, Amounts, amountChoice, fontAsset, PickAmount);
    }

    private IngRow BuildIngRow(Transform parent)
    {
        var r = new IngRow();
        var card = CuteUIKit.Card(parent, CuteUIKit.SlotBorder, 10, "Ing");
        r.go    = card.gameObject;
        r.rt    = (RectTransform)card.transform;
        r.frame = card;
        r.fill  = CuteUIKit.Layer(card.transform, "Fill", CuteUIKit.Rounded(8), RowFill, 1.5f);
        var le = r.go.AddComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = IngRowH;

        var btn = r.go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => ClickIngredient(r));
        r.go.AddComponent<Hover>().on = on => HoverRow(r, on);

        var hlg = r.go.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(4, 9, 2, 2);
        hlg.spacing                = 7f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        var slot = CuteUIKit.Slot(r.go.transform, IngRowH - 4f, CuteUIKit.SlotBorder, CuteUIKit.SlotFill, 8);
        slot.GetComponent<Image>().raycastTarget = false;
        r.icon = ItemCellUI.Icon(slot.transform, null);

        var texts = new GameObject("Texts", typeof(RectTransform));
        texts.transform.SetParent(r.go.transform, false);
        var tv = texts.AddComponent<VerticalLayoutGroup>();
        tv.spacing                = 0f;
        tv.childAlignment         = TextAnchor.MiddleLeft;
        tv.childControlWidth      = true;
        tv.childControlHeight     = true;
        tv.childForceExpandWidth  = false;
        tv.childForceExpandHeight = false;
        var tle = texts.AddComponent<LayoutElement>();
        tle.flexibleWidth  = 1f;
        tle.preferredWidth = 0f;

        r.name = Txt(texts.transform, "", 13f, CuteUIKit.Cocoa, FontStyles.Bold);
        r.name.alignment    = TextAlignmentOptions.MidlineLeft;
        r.name.overflowMode = TextOverflowModes.Ellipsis;
        r.name.gameObject.AddComponent<LayoutElement>().minHeight = 15f;
        r.hint = Txt(texts.transform, "", 10f, CuteUIKit.CocoaSoft, FontStyles.Normal);
        r.hint.alignment    = TextAlignmentOptions.MidlineLeft;
        r.hint.overflowMode = TextOverflowModes.Ellipsis;
        r.hint.gameObject.AddComponent<LayoutElement>().minHeight = 12f;

        r.count = Txt(r.go.transform, "", 14f, CuteUIKit.Cocoa, FontStyles.Bold);
        r.count.alignment = TextAlignmentOptions.MidlineRight;
        r.count.richText  = true;
        var cle = r.count.gameObject.AddComponent<LayoutElement>();
        cle.preferredWidth = cle.minWidth = 62f;

        r.go.SetActive(false);
        return r;
    }

    private void RefreshAll()
    {
        CheckStation();
        var quest = QuestRecipe();
        foreach (var t in tiles) PaintTile(t, quest);
        PaintTabBadges(quest);
        RefreshDetail();
        if (Time.unscaledTime >= holdLineUntil) RefreshLine();
    }

    private bool CheckStation()
    {
        var cm = CraftingManager.Instance;
        if (cm == null) return false;
        var s = cm.GetStationStatus(CraftingStation.Smelter);
        bool ready = s == CraftingManager.StationStatus.Ready;
        bool near  = s != CraftingManager.StationStatus.Missing;
        bool changed = ready != stationReady || near != stationNear;
        stationReady = ready;
        stationNear  = near;
        return changed;
    }

    private int StateOf(CraftingRecipe r, out int have, out int total)
    {
        have = total = 0;
        var cm = CraftingManager.Instance;
        if (r == null || cm == null || !cm.IsRecipeUnlocked(r)) return Locked;
        var inv = Inventory.Instance;
        foreach (var ing in r.ingredients)
        {
            if (ing.item == null) continue;
            total++;
            if (inv != null && inv.GetCount(ing.item) >= ing.amount) have++;
        }
        if (have < total) return Missing;
        return r.station == CraftingStation.None || stationReady ? Ready : NoStation;
    }

    private int StateOf(CraftingRecipe r) => StateOf(r, out _, out _);

    private void PaintTile(Tile t, CraftingRecipe quest)
    {
        var item  = t.recipe.outputItem;
        int owned = Inventory.Instance != null ? Inventory.Instance.GetCount(item) : 0;
        t.ownedBadge.SetActive(owned > 0);
        if (owned > 0) t.owned.text = owned > 999 ? "999+" : owned.ToString();
        t.questPill.SetActive(t.recipe == quest);

        int state = StateOf(t.recipe, out int have, out int total);
        bool sel  = t.recipe == selected;
        switch (state)
        {
            case Ready:
            {
                int n = MaxCraftable(t.recipe) * t.recipe.outputAmount;
                t.status.text = $"{(n > 99 ? "99+" : n.ToString())}개 가능";
                t.statusFill.color = CuteUIKit.MintTag;
                t.status.color     = CuteUIKit.MintText;
                break;
            }
            case NoStation:
                t.status.text = "제련소 곁";
                t.statusFill.color = CuteUIKit.SkyTag;
                t.status.color     = CuteUIKit.SkyText;
                break;
            case Locked:
                t.status.text = $"문명 Lv.{NeedLevel(t.recipe)}";
                t.statusFill.color = CuteUIKit.LavenderTag;
                t.status.color     = CuteUIKit.LavenderText;
                break;
            default:
                t.status.text = $"재료 {have}/{total}";
                t.statusFill.color = CuteUIKit.ButtonLatte;
                t.status.color     = CuteUIKit.CocoaSoft;
                break;
        }
        t.cg.alpha    = state == Locked && !sel ? 0.6f : 1f;
        t.frame.color = sel ? CuteUIKit.Strawberry : state == Ready ? ReadyBorder : CuteUIKit.SlotBorder;
        t.fill.color  = sel ? CuteUIKit.PinkSoft : state == Locked ? DimFill : CardFill;
    }

    private void PaintTabBadges(CraftingRecipe quest)
    {
        var cm = CraftingManager.Instance;
        if (cm == null) return;
        var counts = new int[TabLabels.Length];
        foreach (var r in cm.Recipes)
            if (r != null && r.outputItem != null && !r.IsMachineRecipe && StateOf(r) == Ready) counts[(int)TabOf(r)]++;
        int questTab = quest != null ? (int)TabOf(quest) : -1;
        for (int i = 0; i < tabBadges.Count; i++)
        {
            bool q = i == questTab && i != (int)currentTab;
            bool show = q || counts[i] > 0;
            tabBadges[i].SetActive(show);
            if (!show) continue;
            tabBadgeTexts[i].text  = q ? "!" : counts[i].ToString();
            tabBadgeFills[i].color = q ? CuteUIKit.Strawberry : CuteUIKit.MintTag;
            tabBadgeTexts[i].color = q ? Color.white : CuteUIKit.MintText;
        }
    }

    private void RefreshDetail()
    {
        var r = selected;
        var item = r != null ? r.outputItem : null;
        detailIcon.sprite  = item != null ? item.icon : null;
        detailIcon.enabled = item != null && item.icon != null;
        detailFrame.color  = CuteUIKit.TierBorder(item, CuteUIKit.SlotBorder);
        detailFill.color   = CuteUIKit.TierFill(item, CuteUIKit.SlotFill);
        outBadge.SetActive(r != null && r.outputAmount > 1);
        if (r != null && r.outputAmount > 1) outText.text = $"×{r.outputAmount}";

        if (r == null)
        {
            detailName.text  = "만들 물건을 골라요";
            detailOwned.text = "";
            detailDesc.text  = "왼쪽 칸을 누르면 여기에 크게 보여요.";
            tierFill.gameObject.SetActive(false);
            kindFill.gameObject.SetActive(false);
            foreach (var row in ingRows) row.go.SetActive(false);
            craftLabel.text = "만들기";
            CuteUIKit.ApplyButtonColors(craftBtn, CuteUIKit.ButtonLatte);
            return;
        }

        detailName.text = DisplayName(r);
        int owned = Inventory.Instance != null ? Inventory.Instance.GetCount(item) : 0;
        detailOwned.text = owned > 0 ? $"가방에 {owned}개 있어요" : "아직 없어요";

        tierFill.gameObject.SetActive(true);
        var (tf, tt) = TierColors(r.tier);
        tierFill.color = tf;
        tierText.color = tt;
        tierText.text  = CraftingManager.TierDisplayName(r.tier);
        kindFill.gameObject.SetActive(true);
        var (kf, kt, kn) = KindOf(item);
        kindFill.color = kf;
        kindText.color = kt;
        kindText.text  = kn;

        string desc = ItemInfo.Describe(item);
        if (string.IsNullOrEmpty(desc)) desc = item.description;
        if (r.station == CraftingStation.Smelter) desc = "<color=#3F78AE>전기가 들어오는 제련소 곁(5칸)에서 만들어요</color>\n" + desc;
        detailDesc.text = desc;

        var inv = Inventory.Instance;
        int k = 0;
        foreach (var ing in r.ingredients)
        {
            if (ing.item == null || k >= ingRows.Count) continue;
            var row = ingRows[k++];
            row.ing    = ing;
            row.source = CraftRecipeFor(ing.item);
            row.go.SetActive(true);
            int have = inv != null ? inv.GetCount(ing.item) : 0;
            bool enough = have >= ing.amount;
            row.icon.sprite  = ing.item.icon;
            row.icon.enabled = ing.item.icon != null;
            row.name.text    = ing.item.itemName;
            row.count.text   = $"<color={(enough ? EnoughHex : MissingHex)}>{have}</color><size=11><color=#C4B0A5> / </color></size>{ing.amount}";
            row.frame.color  = enough ? ReadyBorder : CuteUIKit.Hex(0xF4C3CB);
            if (enough) row.hint.text = "충분해요";
            else
            {
                string how = row.source != null ? "누르면 레시피로 이동" : QuestGuide.HowToGet(ing.item, brief: true);
                row.hint.text = $"<color=#C9485B>{ing.amount - have}개 더</color>" + (string.IsNullOrEmpty(how) ? "" : $" · {how}");
            }
            row.hint.richText = true;
        }
        for (; k < ingRows.Count; k++) { ingRows[k].go.SetActive(false); ingRows[k].ing = null; }

        int state = StateOf(r);
        if (state == Ready)
        {
            int times = Times(r);
            craftLabel.text = times > 1 ? $"만들기 ×{times}" : "만들기";
            CuteUIKit.ApplyButtonColors(craftBtn, CuteUIKit.ButtonMint);
        }
        else
        {
            craftLabel.text = state switch
            {
                Locked    => $"문명 Lv.{NeedLevel(r)}에 열려요",
                NoStation => stationNear ? "제련소에 전기가 없어요" : "제련소 곁에서 만들어요",
                _         => "재료가 모자라요",
            };
            CuteUIKit.ApplyButtonColors(craftBtn, CuteUIKit.ButtonLatte);
        }
    }

    private void PlaceGamepadDefault()
    {
        foreach (var t in tiles) RemoveDefault(t.rt);
        if (craftBtn != null) RemoveDefault((RectTransform)craftBtn.transform);
        RectTransform target = null;
        var sel = TileOf(selected);
        if (sel != null && StateOf(selected) == Ready) target = (RectTransform)craftBtn.transform;
        else if (sel != null) target = sel.rt;
        else if (tiles.Count > 0) target = tiles[0].rt;
        if (target != null) target.gameObject.AddComponent<GamepadDefault>();
    }

    private static void RemoveDefault(RectTransform rt)
    {
        if (rt != null && rt.TryGetComponent(out GamepadDefault d)) DestroyImmediate(d);
    }

    private void EnsureSelection(CraftingRecipe prefer)
    {
        if (prefer != null && TileOf(prefer) != null) { selected = prefer; return; }
        if (selected != null && TileOf(selected) != null) return;
        selected = null;
        foreach (var t in tiles) if (StateOf(t.recipe) == Ready) { selected = t.recipe; return; }
        var cm = CraftingManager.Instance;
        foreach (var t in tiles) if (cm != null && cm.IsRecipeUnlocked(t.recipe)) { selected = t.recipe; return; }
        if (tiles.Count > 0) selected = tiles[0].recipe;
    }

    private void Select(CraftingRecipe r)
    {
        if (r == null || selected == r) return;
        selected = r;
        holdLineUntil = 0f;
        RefreshAll();
        PlaceGamepadDefault();
        StartCoroutine(ItemCellUI.Punch(detailSlot, 1.1f));
    }

    private void ClickTile(Tile t)
    {
        if (t.recipe != selected)
        {
            Sfx.Play(SfxId.UiClick);
            Select(t.recipe);
            return;
        }
        Craft(t.recipe, true);
    }

    private void ClickIngredient(IngRow row)
    {
        if (row.ing == null || row.ing.item == null) return;
        if (row.source != null)
        {
            Sfx.Play(SfxId.UiClick);
            Focus(row.source);
            Say($"{DisplayName(row.source)} 레시피예요 — 이걸로 {row.ing.item.itemName}{(HasBatchim(row.ing.item.itemName) ? "을" : "를")} 만들어요", 3f, row.source);
            return;
        }
        string how = QuestGuide.HowToGet(row.ing.item);
        Fail(row.rt, string.IsNullOrEmpty(how) ? $"{row.ing.item.itemName}: 가진 {Count(row.ing.item)} / 필요 {row.ing.amount}"
                                               : $"{row.ing.item.itemName}{(HasBatchim(row.ing.item.itemName) ? "은" : "는")} {how}", quiet: true);
    }

    private void PickAmount(int k)
    {
        amountChoice = k;
        ItemCellUI.PaintAmounts(amountBtns, k);
        RefreshDetail();
        if (Time.unscaledTime >= holdLineUntil) RefreshLine();
    }

    private static int Times(CraftingRecipe r)
    {
        int want = Amounts[amountChoice];
        int max  = MaxCraftable(r);
        return Mathf.Max(1, want <= 0 ? max : Mathf.Min(want, max));
    }

    private void Craft(CraftingRecipe r, bool fromTile)
    {
        var cm  = CraftingManager.Instance;
        var inv = Inventory.Instance;
        if (r == null || cm == null || inv == null) return;
        var at = fromTile && TileOf(r) != null ? TileOf(r).rt : (RectTransform)craftBtn.transform;

        int state = StateOf(r);
        if (state != Ready) { Fail(at, WhyNot(r, state)); return; }

        int times = Times(r);
        int made  = 0;
        while (made < times && cm.CanCraftNow(r) && cm.Craft(r)) made++;
        if (made == 0)
        {
            Fail(at, inv.CanAddItem(r.outputItem) ? "지금은 만들 수 없어요" : "가방이 꽉 찼어요 — 안 쓰는 물건을 팔거나 창고에 넣어요");
            return;
        }

        var item  = r.outputItem;
        int count = made * r.outputAmount;
        int tier  = item.Tier;
        if (tier >= 3) CelebrateHighTier(r, count, tier);
        else Sfx.Play(SfxId.Craft);

        RectTransform landing = null;
        int slot = SlotOf(item);
        if (slot >= 0 && slot < Inventory.HotbarSize && TilePlacementUI.Instance != null) landing = TilePlacementUI.Instance.SlotRect(slot);
        if (landing == null) landing = HudDock.ButtonRect(HudDock.Slot.Bag);
        if (landing != null && item.icon != null) ItemCellUI.FlyIcon(this, item.icon, detailSlot.position, landing);
        StartCoroutine(ItemCellUI.Punch(detailSlot, 1.15f));
        ItemCellUI.FloatText(this, detailSlot.position + Vector3.up * 40f * Scale, $"+{count}", CuteUIKit.MintText, 18f, fontAsset);

        bool place = item.itemType == FarmingItemType.Tile;
        if (place) TilePlacementUI.Instance?.SelectItem(item);
        string where = slot >= 0 && slot < Inventory.HotbarSize ? $"핫바 {slot + 1}번 칸" : "가방";
        string next  = place ? (Pad.Active ? " — 창을 닫고 RT로 설치해요" : " — 손에 들었어요! 창 밖 땅을 클릭해 설치해요")
                     : item.itemType == FarmingItemType.Equipment || IsEquipment(item) ? " — 가방(Tab)에서 장착해요"
                     : $" — {where}에 넣었어요";
        Say($"{DisplayName(r)} {count}개 완성!{next}", 3.2f, r);
        RefreshAll();
    }

    private void CelebrateHighTier(CraftingRecipe recipe, int count, int tier)
    {
        Color color = CuteUIKit.TierBorder(tier, CuteUIKit.Butter);
        NotificationUI.Instance?.Show($"{tier}티어 {DisplayName(recipe)} ×{count} 완성!", color);
        Sfx.Play(tier >= 4 ? SfxId.QuestComplete : SfxId.Upgrade);
        CuteUIKit.SparkleBurst(this, detailSlot, color, tier >= 4 ? 20 : 12, tier >= 4 ? 120f : 80f);
    }

    private string WhyNot(CraftingRecipe r, int state)
    {
        string name = DisplayName(r);
        switch (state)
        {
            case Locked:
                return $"{Eun(name)} 문명 Lv.{NeedLevel(r)}에 열려요 — 다음 단계는 여정(L)에서 볼 수 있어요";
            case NoStation:
                return stationNear ? "제련소에 전기가 없어요 — 센터코어 · 전력 중계탑 범위 안에 두면 켜져요"
                                   : $"{Eun(name)} 전기가 들어오는 제련소 곁(5칸)에서 만들어요 — 제련소 가까이 가요";
            default:
            {
                foreach (var ing in r.ingredients)
                {
                    if (ing.item == null) continue;
                    int lack = ing.amount - Count(ing.item);
                    if (lack <= 0) continue;
                    string how = QuestGuide.HowToGet(ing.item);
                    return $"{ing.item.itemName} {lack}개가 더 필요해요" + (string.IsNullOrEmpty(how) ? "" : $" — {how}");
                }
                return "재료가 모자라요";
            }
        }
    }

    private string PressWord => Pad.Active ? "A로" : MobileMode.Active ? "누르면" : "클릭하면";

    private void Say(string line, float hold, object owner = null)
    {
        holdLineUntil = Time.unscaledTime + hold;
        holdOwner     = owner ?? (object)hoveredTile ?? hoveredRow;
        SetLine(line);
    }

    private void SetLine(string line)
    {
        if (cocoText != null) cocoText.text = line;
    }

    private void Fail(RectTransform at, string line, bool quiet = false)
    {
        Sfx.Play(quiet ? SfxId.UiClick : SfxId.UiError);
        if (at != null && at.gameObject.activeInHierarchy) StartCoroutine(CuteUIKit.Wiggle(at, 7f, 0.3f, 2f));
        Say(line, 3.2f);
    }

    private void HoverTile(Tile t, bool on)
    {
        if (on) { hoveredTile = t; hoveredRow = null; if (holdOwner != (object)t && holdOwner != (object)t.recipe) holdLineUntil = 0f; }
        else if (hoveredTile == t) hoveredTile = null;
        if (Time.unscaledTime >= holdLineUntil) RefreshLine();
    }

    private void HoverRow(IngRow r, bool on)
    {
        if (on) { hoveredRow = r; hoveredTile = null; if (holdOwner != (object)r) holdLineUntil = 0f; }
        else if (hoveredRow == r) hoveredRow = null;
        if (Time.unscaledTime >= holdLineUntil) RefreshLine();
    }

    private void RefreshLine()
    {
        if (!IsOpen) return;
        if (hoveredRow != null && hoveredRow.ing != null && hoveredRow.ing.item != null && hoveredRow.go.activeInHierarchy)
        {
            var ing  = hoveredRow.ing;
            int have = Count(ing.item);
            string how = QuestGuide.HowToGet(ing.item);
            string tail = hoveredRow.source != null ? $" · {PressWord} 레시피로 가요"
                        : have < ing.amount && !string.IsNullOrEmpty(how) ? $" · {how}" : "";
            SetLine($"{ing.item.itemName} — 가진 {have}개 / 필요 {ing.amount}개{tail}");
            return;
        }
        var r = hoveredTile != null ? hoveredTile.recipe : selected;
        if (r == null) { SetLine("만들 물건을 고르고 [만들기]! 민트 칸은 지금 바로 만들 수 있어요"); return; }

        string name  = DisplayName(r);
        string quest = r == QuestRecipe() ? "<color=#F7879F>퀘스트</color> · " : "";
        int state = StateOf(r);
        if (state != Ready) { SetLine(quest + WhyNot(r, state)); return; }
        bool isSel = r == selected;
        SetLine(isSel ? $"{quest}{name} — 재료가 다 있어요! [만들기]를 누르거나 칸을 한 번 더 누르면 만들어요"
                      : $"{quest}{name} — 재료가 다 있어요 · {PressWord} 골라요");
    }

    private Tile TileOf(CraftingRecipe r)
    {
        if (r == null) return null;
        foreach (var t in tiles) if (t.recipe == r) return t;
        return null;
    }

    private float SideShift()
    {
        var parent = panelRT.parent as RectTransform;
        if (parent == null) return 0f;
        float room = (parent.rect.width - panelRT.rect.width * CuteUIKit.FitScale(panelRT)) * 0.5f - 12f;
        return Mathf.Clamp(room, 0f, 56f);
    }

    private void ScrollToTile(Tile t)
    {
        if (t == null || tileScroll == null || !tileScroll.vertical) return;
        Canvas.ForceUpdateCanvases();
        int row = tiles.IndexOf(t) / TileCols;
        var content = tileScroll.content;
        var vp = tileScroll.viewport != null ? tileScroll.viewport : (RectTransform)tileScroll.transform;
        float y    = 6f + row * (TileH + TileGap);
        float maxY = Mathf.Max(0f, content.rect.height - vp.rect.height);
        float to   = Mathf.Clamp(y - (vp.rect.height - TileH) * 0.5f, 0f, maxY);
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, to);
        tileScroll.StopMovement();
    }

    private static CraftingRecipe CraftRecipeFor(FarmingItemData item)
    {
        var cm = CraftingManager.Instance;
        if (cm == null || item == null) return null;
        foreach (var r in cm.Recipes)
            if (r != null && r.outputItem == item && !r.IsMachineRecipe) return r;
        return null;
    }

    private static int MaxCraftable(CraftingRecipe recipe)
    {
        var inv = Inventory.Instance;
        int max = 999;
        if (inv != null)
            foreach (var ing in recipe.ingredients)
                if (ing.item != null && ing.amount > 0) max = Mathf.Min(max, inv.GetCount(ing.item) / ing.amount);
        return Mathf.Max(1, max);
    }

    private static int Count(FarmingItemData item) => item != null && Inventory.Instance != null ? Inventory.Instance.GetCount(item) : 0;

    private static int SlotOf(FarmingItemData item)
    {
        var inv = Inventory.Instance;
        if (inv == null || item == null) return -1;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            var s = inv.GetSlot(i);
            if (s != null && !s.IsEmpty && s.Item == item) return i;
        }
        return -1;
    }

    private static bool IsEquipment(FarmingItemData item) => item != null && item.itemType == FarmingItemType.Equipment;

    private static (Color fill, Color text) TierColors(CraftingTier tier) => tier switch
    {
        CraftingTier.Intermediate => (CuteUIKit.SkyTag,      CuteUIKit.SkyText),
        CraftingTier.Advanced     => (CuteUIKit.LavenderTag, CuteUIKit.LavenderText),
        _                         => (CuteUIKit.ButtonLatte, CuteUIKit.Cocoa),
    };

    private static (Color fill, Color text, string name) KindOf(FarmingItemData item)
    {
        if (item == null) return (CuteUIKit.ButtonLatte, CuteUIKit.Cocoa, "");
        if (CivFacility.IsFacilityItem(item)) return (CuteUIKit.LavenderTag, CuteUIKit.LavenderText, "문명 시설");
        return item.itemType switch
        {
            FarmingItemType.Tile      => (CuteUIKit.SkyTag,     CuteUIKit.SkyText,  "설치물"),
            FarmingItemType.Tool      => (CuteUIKit.PinkSoft,   CuteUIKit.CoralText, item.IsDrill ? "드릴" : "도구"),
            FarmingItemType.Equipment => (CuteUIKit.LavenderTag, CuteUIKit.LavenderText, "장비"),
            FarmingItemType.Seed      => (CuteUIKit.MintTag,    CuteUIKit.MintText, "씨앗"),
            _                         => (CuteUIKit.ButterSoft, CuteUIKit.AmberText, "재료"),
        };
    }

    private static string DisplayName(CraftingRecipe recipe)
    {
        if (recipe == null) return "";
        if (!string.IsNullOrEmpty(recipe.recipeName)) return recipe.recipeName;
        return recipe.outputItem != null ? recipe.outputItem.itemName : "?";
    }

    private static bool HasBatchim(string w) => ItemCellUI.HasBatchim(w);
    private static string Eun(string w) => ItemCellUI.Eun(w);

    private float Scale
    {
        get
        {
            var canvas = GetComponentInParent<Canvas>();
            return canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
        }
    }

    private TextMeshProUGUI Txt(Transform parent, string text, float size, Color color, FontStyles style)
    {
        var t = CuteUIKit.Text(parent, text, size, color, style);
        if (fontAsset != null) t.font = fontAsset;
        return t;
    }

    private static void FitToContent(GameObject go)
    {
        var fit = go.GetComponent<ContentSizeFitter>();
        if (fit == null) fit = go.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    [ContextMenu("Rebuild UI")]
    public void RebuildUI()
    {
        bool open = IsOpen;
        ClearBuilt();
        BuildWindow();
        panelRT.localScale = open ? Vector3.one : Vector3.zero;
        panelCG.alpha      = open ? 1f : 0f;
        panelCG.blocksRaycasts = panelCG.interactable = open;
        UIStyleKit.SetScrolling(panelRT, open);
        if (open) { RebuildTiles(); EnsureSelection(QuestRecipe()); RefreshAll(); }
    }
}
