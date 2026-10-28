using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;
using System.Collections.Generic;

// 왼쪽 씨앗 가게, 오른쪽 가방
[RequireComponent(typeof(RectTransform))]
public class ShopUI : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    public static ShopUI Instance => Services.Get<ShopUI>();

    [Header("설정")]
    [SerializeField] private KeyCode toggleKey = KeyCode.B;
    [Header("폰트")]
    [SerializeField] private TMP_FontAsset fontAsset;

    private const float TileW = 100f, TileH = 128f, TileGap = 8f, TileSlot = 56f;
    private const int   TileCols = 5, TileRows = 3;
    private const float SlotSize = 62f, SlotGap = 6f;
    private const int   BagRows = 4;
    private const int   TileRadius = 14;

    private const float ShopW = TileCols * TileW + (TileCols - 1) * TileGap;
    private static float BagW => BagView.Width(SlotSize, SlotGap);
    private static float BodyW => ShopW + BagW + 12f * 4 + 12f * 2 + 14f;

    private static readonly Color CardFill = CuteUIKit.Lighter(CuteUIKit.Cream, 0.5f);
    private static readonly Color DimFill  = CuteUIKit.Hex(0xEFE6DC);

    private static readonly int[] BuyAmounts  = { 1, 10, 50 };
    private static readonly int[] SellAmounts = { 1, 10, 0 };
    private static int buyChoice, sellChoice = 2;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        buyChoice = 0;
        sellChoice = 2;
    }

    private static readonly string[] Thanks = { "고마워요!", "싱싱해요!", "좋은 작물이에요!", "잘 팔았어요!" };

    public bool IsOpen { get; private set; }

    private RectTransform panelRT;
    private CanvasGroup   panelCG;
    private Coroutine     animCo;
    private Vector2       dragOffset;

    private RectTransform   coinPill;
    private TextMeshProUGUI coinText;
    private double          coinShown;
    private long            coinWritten = long.MinValue;
    private Coroutine       coinPunchCo;

    private TextMeshProUGUI cocoText;
    private float           holdLineUntil;
    private object          holdOwner;

    private Transform  tileGrid;
    private ScrollRect tileScroll;
    private BagView    bagView;
    private Button[]   buyAmountBtns, sellAmountBtns;
    private Button     undoBtn;
    private TextMeshProUGUI undoText;
    private GameObject footNote;

    private bool padOnBag;
    private bool inventoryDirty, tilesDirty;
    private Tile     hoveredTile;
    private ItemCell hoveredCell;

    private readonly List<Tile> tiles = new();
    private readonly List<Sale> sales = new();

    private sealed class Tile
    {
        public FarmingItemData item;
        public RectTransform   rt, slot;
        public CanvasGroup     cg;
        public Image           fill;
        public GameObject      ownedBadge, pricePill, lockPill, newPill;
        public TextMeshProUGUI owned, price, lockText;
        public int             look = -1;
    }

    private readonly struct Sale
    {
        public readonly FarmingItemData item;
        public readonly int             count;
        public readonly long            coins;
        public Sale(FarmingItemData item, int count, long coins) { this.item = item; this.count = count; this.coins = coins; }
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
            panelRT.anchoredPosition = lp - dragOffset * panelRT.localScale.x;
    }

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }

        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
        { var c = GetComponent<Outline>();             if (c) DestroyImmediate(c); }
        { var c = GetComponent<VerticalLayoutGroup>(); if (c) DestroyImmediate(c); }
        { var c = GetComponent<ContentSizeFitter>();   if (c) DestroyImmediate(c); }
        { var c = GetComponent<CanvasGroup>();         if (c) DestroyImmediate(c); }

        BuildWindow();
        panelRT.localScale = Vector3.zero;
        panelCG.alpha      = 0f;
        panelCG.blocksRaycasts = panelCG.interactable = false;
        UIStyleKit.SetScrolling(panelRT, false);
    }

    private void Start()
    {
        if (ShopManager.Instance != null)     ShopManager.Instance.OnStockChanged += RebuildTiles;
        if (CurrencyManager.Instance != null) CurrencyManager.Instance.OnCurrencyChanged += HandleCurrencyChanged;
        if (Inventory.Instance != null)       Inventory.Instance.OnInventoryChanged += MarkInventoryDirty;
        if (TechTreeManager.Instance != null) TechTreeManager.Instance.OnTechChanged += MarkTilesDirty;
        GameEventManager.OnBoostChanged += HandleBoostChanged;
        CenterCore.OnAnyLevelUp         += HandleCoreLevelUp;
        HarvestLog.OnChanged            += MarkTilesDirty;

        RebuildTiles();
        bagView.Refresh();
    }

    private void OnDestroy()
    {
        if (ShopManager.Instance != null)     ShopManager.Instance.OnStockChanged -= RebuildTiles;
        if (CurrencyManager.Instance != null) CurrencyManager.Instance.OnCurrencyChanged -= HandleCurrencyChanged;
        if (Inventory.Instance != null)       Inventory.Instance.OnInventoryChanged -= MarkInventoryDirty;
        if (TechTreeManager.Instance != null) TechTreeManager.Instance.OnTechChanged -= MarkTilesDirty;
        GameEventManager.OnBoostChanged -= HandleBoostChanged;
        CenterCore.OnAnyLevelUp         -= HandleCoreLevelUp;
        HarvestLog.OnChanged            -= MarkTilesDirty;
        Services.Unregister(this);
    }

    private void HandleCurrencyChanged(long _) => tilesDirty = true;
    private void HandleCoreLevelUp(CenterCore core, int level) => tilesDirty = true;
    private void MarkInventoryDirty() => inventoryDirty = true;
    private void MarkTilesDirty() => tilesDirty = true;

    private void HandleBoostChanged(bool _)
    {
        if (!IsOpen) return;
        bagView.Repaint();
        if (Time.unscaledTime >= holdLineUntil) RefreshHoverLine();
    }

    private void Update()
    {
        if (!TextInputFocus.Active)
        {
            if (GameInput.GetKeyDown(toggleKey) || (InputManager.Instance != null && InputManager.Instance.ShopButton)) Toggle();
            // Esc 중복 처리 방지
            if (IsOpen && EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) Toggle();
        }
        if (!IsOpen) return;

        TickCoins();
        if (holdLineUntil > 0f && Time.unscaledTime >= holdLineUntil)
        {
            holdLineUntil = 0f;
            RefreshHoverLine();
        }
    }

    private void LateUpdate()
    {
        if (!IsOpen) return;
        // 한 프레임에 한 번만 갱신
        if (inventoryDirty)
        {
            inventoryDirty = false;
            tilesDirty     = true;
            bagView.Refresh();
            if (hoveredCell != null && Time.unscaledTime >= holdLineUntil) RefreshHoverLine();
        }
        if (tilesDirty)
        {
            tilesDirty = false;
            RefreshTiles();
        }
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
            if (CraftingTableUI.Instance != null && CraftingTableUI.Instance.IsOpen) CraftingTableUI.Instance.Toggle();
            transform.SetAsLastSibling();
            sales.Clear();
            UpdateUndo();
            bagView.Repaint();
            RefreshTiles();
            inventoryDirty = tilesDirty = false;
            hoveredTile = null;
            hoveredCell = null;
            holdLineUntil = 0f;
            SayIdle();
            SnapCoins();
            PlaceGamepadDefault();
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRT);
            ItemCellUI.UpdateScrollable(tileScroll);
            ItemCellUI.UpdateScrollable(bagView.Scroll);
            ItemCellUI.ResetScroll(tileScroll);
            ItemCellUI.ResetScroll(bagView.Scroll);
        }
        else
        {
            ItemTooltipUI.Instance?.Hide();
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
        padOnBag = !padOnBag;
        PlaceGamepadDefault();
        Sfx.Play(SfxId.UiClick);
    }

    private void PlaceGamepadDefault()
    {
        foreach (var t in tiles) RemoveDefault(t.rt);
        foreach (var c in bagView.Cells) RemoveDefault(c.rt);

        RectTransform target = null;
        if (padOnBag)
        {
            foreach (var c in bagView.Cells) if (c.item != null && c.usable) { target = c.rt; break; }
            if (target == null && bagView.Cells.Count > 0) target = bagView.Cells[0].rt;
        }
        else
        {
            foreach (var t in tiles) if (t.look != 2) { target = t.rt; break; }
            if (target == null && tiles.Count > 0) target = tiles[0].rt;
        }
        if (target != null) target.gameObject.AddComponent<GamepadDefault>();
    }

    private static void RemoveDefault(RectTransform rt)
    {
        if (rt != null && rt.TryGetComponent(out GamepadDefault d)) DestroyImmediate(d);
    }

    public RectTransform TileRect(FarmingItemData item)
    {
        foreach (var t in tiles) if (t.item == item) return t.rt;
        return null;
    }

    public RectTransform BagSlotRect(FarmingItemData item) => bagView?.CellOf(item)?.rt;

    public static FarmingItemData FirstNewSeed()
    {
        var shop = ShopManager.Instance;
        if (shop == null) return null;
        FarmingItemData best = null;
        foreach (var item in shop.CurrentStock)
        {
            if (!IsNew(item) || shop.LockReason(item) != null) continue;
            if (best == null || item.basePrice < best.basePrice) best = item;
        }
        return best;
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
        BuildShopPane(body);
        BuildBagPane(body);
    }

    private void BuildTitleBar()
    {
        var bar = new GameObject("TitleBar", typeof(RectTransform));
        bar.transform.SetParent(transform, false);
        var le = bar.AddComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = 34f;
        le.flexibleWidth   = 1f;

        var ribbon = CuteUIKit.TitleRibbon(bar.transform, "상점", toggleKey.ToString());
        ribbon.anchorMin = ribbon.anchorMax = ribbon.pivot = new Vector2(0.5f, 0.5f);
        ribbon.anchoredPosition = Vector2.zero;
        FitToContent(ribbon.gameObject);

        var right = ItemCellUI.HRow("Right", bar.transform, 8f, TextAnchor.MiddleRight);
        var rightRT = (RectTransform)right;
        rightRT.anchorMin = rightRT.anchorMax = rightRT.pivot = new Vector2(1f, 0.5f);
        rightRT.anchoredPosition = Vector2.zero;
        FitToContent(right.gameObject);

        coinPill = ItemCellUI.CoinPill(right, "0", CuteUIKit.ButtonButter, 15f, 18f, fontAsset, out coinText);
        CuteUIKit.CloseButton(right, Toggle, 28f);
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

    private void BuildShopPane(Transform body)
    {
        var pane = ItemCellUI.Pane("ShopPane", body);
        var header = ItemCellUI.PaneHeader(pane, "씨앗 가게", CuteUIKit.ButterSoft, CuteUIKit.AmberText, "누르면 사요", ShopW + 12f, fontAsset, out _);
        buyAmountBtns = ItemCellUI.AmountPicker(header, BuyAmounts, buyChoice, fontAsset, k => PickAmount(false, k));

        float h = TileRows * TileH + (TileRows - 1) * TileGap;
        tileGrid = ItemCellUI.ScrollGrid(pane, "Seeds", ShopW, h, new Vector2(TileW, TileH), TileGap, TileCols, out tileScroll);
    }

    private void RebuildTiles()
    {
        if (tileGrid == null) return;
        for (int i = tileGrid.childCount - 1; i >= 0; i--) DestroyImmediate(tileGrid.GetChild(i).gameObject);
        tiles.Clear();
        hoveredTile = null;

        var shop = ShopManager.Instance;
        if (shop == null) return;

        var stock = new List<FarmingItemData>();
        foreach (var item in shop.CurrentStock) if (item != null) stock.Add(item);
        stock.Sort((a, b) => a.unlockCoreLevel != b.unlockCoreLevel ? a.unlockCoreLevel.CompareTo(b.unlockCoreLevel)
                           : a.basePrice != b.basePrice ? a.basePrice.CompareTo(b.basePrice)
                           : string.CompareOrdinal(a.itemName, b.itemName));
        foreach (var item in stock) tiles.Add(BuildTile(item, tileGrid));

        RefreshTiles();
        ItemCellUI.UpdateScrollable(tileScroll);
        if (IsOpen) PlaceGamepadDefault();
    }

    private Tile BuildTile(FarmingItemData item, Transform parent)
    {
        var t  = new Tile { item = item };
        var go = new GameObject($"Seed_{item.name}", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        t.rt   = (RectTransform)go.transform;
        CuteUIKit.SetRounded(go.AddComponent<Image>(), TileRadius, CuteUIKit.SlotBorder);
        t.fill = CuteUIKit.Layer(go.transform, "Fill", CuteUIKit.Rounded(TileRadius - 2), CardFill, 2.5f);
        t.cg   = go.AddComponent<CanvasGroup>();

        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => Buy(t));
        go.AddComponent<CutePop>().hoverScale = 1.06f;
        go.AddComponent<Hover>().on = on => HoverTile(t, on);

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(6, 6, 8, 7);
        vlg.spacing                = 4f;
        vlg.childAlignment         = TextAnchor.UpperCenter;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = false;
        vlg.childForceExpandHeight = false;

        var slot = CuteUIKit.Slot(go.transform, TileSlot, CuteUIKit.TierBorder(item, CuteUIKit.SlotBorder), CuteUIKit.TierFill(item, CuteUIKit.SlotFill));
        slot.GetComponent<Image>().raycastTarget = false;
        t.slot = (RectTransform)slot.transform;
        ItemCellUI.Icon(slot.transform, item.icon);

        var badge = CuteUIKit.Pill(slot.transform, "", CuteUIKit.Cocoa, Color.white, 9f, 7, new RectOffset(4, 4, 0, 1), out t.owned, fitSelf: true);
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1f, 0f);
        badge.anchoredPosition = new Vector2(-2f, 2f);
        t.ownedBadge = badge.gameObject;

        var name = Txt(go.transform, ShortName(item), 12f, CuteUIKit.Cocoa, FontStyles.Bold);
        name.overflowMode = TextOverflowModes.Ellipsis;
        var nameLE = name.gameObject.AddComponent<LayoutElement>();
        nameLE.preferredWidth = TileW - 12f;
        nameLE.minHeight      = 15f;

        t.pricePill = ItemCellUI.CoinPill(go.transform, "", CuteUIKit.ButterSoft, 12f, 13f, fontAsset, out t.price).gameObject;
        t.lockPill  = CuteUIKit.Pill(go.transform, "", CuteUIKit.LavenderTag, CuteUIKit.LavenderText, 11f, 10,
                                     new RectOffset(8, 8, 2, 3), out t.lockText).gameObject;
        if (fontAsset != null) t.lockText.font = fontAsset;

        var fresh = CuteUIKit.Pill(go.transform, "처음!", CuteUIKit.Strawberry, Color.white, 9f, 8, new RectOffset(5, 5, 1, 1), out var freshText, fitSelf: true);
        if (fontAsset != null) freshText.font = fontAsset;
        fresh.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        fresh.anchorMin = fresh.anchorMax = fresh.pivot = new Vector2(0f, 1f);
        fresh.anchoredPosition = new Vector2(3f, -3f);
        fresh.localRotation    = Quaternion.Euler(0f, 0f, 8f);
        t.newPill = fresh.gameObject;
        return t;
    }

    private void RefreshTiles()
    {
        var shop = ShopManager.Instance;
        if (shop == null) return;
        long coins = CurrencyManager.Instance != null ? CurrencyManager.Instance.Currency : 0;
        foreach (var t in tiles)
        {
            int owned = Inventory.Instance != null ? Inventory.Instance.GetCount(t.item) : 0;
            t.ownedBadge.SetActive(owned > 0);
            if (owned > 0) t.owned.text = owned.ToString();

            string lockReason = shop.LockReason(t.item);
            int price = shop.BuyPrice(t.item);
            int look  = lockReason != null ? 2 : coins < price ? 1 : 0;
            t.newPill.SetActive(look != 2 && IsNew(t.item));
            if (look == t.look) continue;
            t.look = look;

            t.pricePill.SetActive(look != 2);
            t.lockPill.SetActive(look == 2);
            t.cg.alpha    = look == 2 ? 0.6f : 1f;
            t.fill.color  = look == 2 ? DimFill : CardFill;
            t.price.text  = $"{price:N0}";
            t.price.color = look == 1 ? CuteUIKit.CoralText : CuteUIKit.Cocoa;
            if (look == 2) t.lockText.text = LockLabel(t.item, lockReason);
        }
        if (hoveredTile != null && Time.unscaledTime >= holdLineUntil) RefreshHoverLine();
    }

    private void BuildBagPane(Transform body)
    {
        var pane = ItemCellUI.Pane("BagPane", body);
        var header = ItemCellUI.PaneHeader(pane, "내 가방", CuteUIKit.MintTag, CuteUIKit.MintText, "누르면 팔아요", BagW + 12f, fontAsset, out _);
        sellAmountBtns = ItemCellUI.AmountPicker(header, SellAmounts, sellChoice, fontAsset, k => PickAmount(true, k));

        bagView = new BagView(pane, SlotSize, SlotGap, BagRows, fontAsset)
        {
            Usable  = ShopManager.IsSellable,
            Tag     = item => ShopManager.IsSellable(item) && ShopManager.Instance != null ? $"{ShopManager.Instance.SellPrice(item):N0}" : null,
            Clicked = Sell,
            Hovered = HoverCell,
        };

        var foot = ItemCellUI.HRow("Foot", pane, 6f, TextAnchor.MiddleCenter);
        var fle = foot.gameObject.AddComponent<LayoutElement>();
        fle.preferredWidth  = BagW;
        fle.preferredHeight = fle.minHeight = 28f;
        var note = Txt(foot, "흐린 칸은 팔 수 없는 물건이에요 · 팔고 나면 여기서 되찾을 수 있어요", 11f, CuteUIKit.CocoaMuted, FontStyles.Normal);
        note.overflowMode = TextOverflowModes.Ellipsis;
        footNote = note.gameObject;
        undoBtn = CuteUIKit.Button(foot, "", CuteUIKit.ButtonLatte, Undo, -1f, 26f, 12f);
        undoText = undoBtn.GetComponentInChildren<TextMeshProUGUI>(true);
        undoText.overflowMode = TextOverflowModes.Ellipsis;
        if (fontAsset != null) undoText.font = fontAsset;
        undoBtn.gameObject.SetActive(false);
    }

    private void Buy(Tile t)
    {
        var shop = ShopManager.Instance;
        var cur  = CurrencyManager.Instance;
        var inv  = Inventory.Instance;
        if (shop == null || cur == null || inv == null) return;
        string name = t.item.itemName;

        string lockReason = shop.LockReason(t.item);
        if (lockReason != null)
        {
            Fail(t.rt, LockedLine(t.item, lockReason));
            return;
        }

        int price = shop.BuyPrice(t.item);
        int want  = BuyAmounts[buyChoice];
        int n     = (int)System.Math.Min(want, cur.Currency / Mathf.Max(1, price));
        if (n <= 0)
        {
            Fail(coinPill, $"코인이 모자라요 — {name} 1개에 {price:N0}코인이에요");
            return;
        }
        if (!inv.CanAddItem(t.item))
        {
            Fail(t.rt, "가방이 꽉 찼어요! 안 쓰는 물건을 팔거나 창고에 넣어 자리를 만들어요");
            return;
        }
        if (!shop.TryBuy(t.item, n)) { Fail(t.rt, "지금은 살 수 없어요"); return; }

        Sfx.Play(SfxId.Buy);
        ItemCellUI.FloatText(this, CoinDropPoint, $"-{CurrencyManager.Format((long)price * n)}", CuteUIKit.CoralText, 16f, fontAsset);

        bagView.Refresh();
        var cell = bagView.CellOf(t.item);
        if (cell != null) ItemCellUI.FlyIcon(this, t.item.icon, t.slot.position, cell.rt);
        string where = cell == null ? "" : cell.index < BagView.Cols ? $" 핫바 {cell.index + 1}번 칸에 있어요" : " 가방에 넣었어요";
        Say($"{name} {n}개 샀어요!{where}" + (n < want ? $" (코인이 모자라 {n}개만)" : ""), 2.6f);
    }

    private void Sell(ItemCell c)
    {
        var shop = ShopManager.Instance;
        var inv  = Inventory.Instance;
        if (shop == null || inv == null) return;
        var slot = inv.GetSlot(c.index);
        if (slot == null || slot.IsEmpty) return;
        var item = slot.Item;

        if (!ShopManager.IsSellable(item))
        {
            Fail(c.rt, $"{ItemCellUI.Eun(item.itemName)} 팔 수 없어요 — {WhyNotSellable(item)}");
            return;
        }

        int want = SellAmounts[sellChoice];
        int n    = want <= 0 ? slot.Count : Mathf.Min(want, slot.Count);
        long gain = (long)shop.SellPrice(item) * n;
        Vector3 from = c.rt.position;
        if (!shop.TrySell(item, n)) { Fail(c.rt, "지금은 팔 수 없어요"); return; }

        Sfx.Play(SfxId.Sell);
        if (!NetSession.IsReplica)
        {
            sales.Add(new Sale(item, n, gain));
            UpdateUndo();
        }
        bool big = gain >= 1000;
        ItemCellUI.CoinShower(this, from, coinPill, gain, last =>
        {
            PunchCoins(last ? 1.16f : 1.07f);
            if (last && big) CuteUIKit.SparkleBurst(this, coinPill, CuteUIKit.Butter, 10, 60f);
        });
        ItemCellUI.FloatText(this, from + Vector3.up * 30f * Scale, $"+{CurrencyManager.Format(gain)}", CuteUIKit.AmberText, 18f, fontAsset);
        int need = ShuttleService.NeededBy(item);
        bool short_ = need > 0 && inv.GetCount(item) < need && !NetSession.IsReplica;
        Say($"{Thanks[Random.Range(0, Thanks.Length)]} {item.itemName} {n}개 → +{CurrencyManager.Format(gain)}코인"
            + (short_ ? $" <color=#C9485B>· 셔틀 짐에 {need}개 필요했어요 — [되찾기]로 되살 수 있어요</color>" : ""), short_ ? 4f : 2.6f);
    }

    private void Undo()
    {
        if (sales.Count == 0) return;
        var last = sales[sales.Count - 1];
        var cur  = CurrencyManager.Instance;
        var inv  = Inventory.Instance;
        if (cur == null || inv == null) return;

        if (!inv.CanAddItem(last.item)) { Fail(undoBtn.transform as RectTransform, "가방에 자리가 없어 되찾을 수 없어요"); return; }
        if (!cur.TrySpend(last.coins))  { Fail(coinPill, $"코인이 모자라 되찾을 수 없어요 ({CurrencyManager.Format(last.coins)}코인)"); return; }
        inv.AddItem(last.item, last.count, countAsNew: false);
        sales.RemoveAt(sales.Count - 1);
        UpdateUndo();

        Sfx.Play(SfxId.Pickup);
        bagView.Refresh();
        var cell = bagView.CellOf(last.item);
        if (cell != null) ItemCellUI.FlyIcon(this, last.item.icon, undoBtn.transform.position, cell.rt);
        ItemCellUI.FloatText(this, CoinDropPoint, $"-{CurrencyManager.Format(last.coins)}", CuteUIKit.CoralText, 16f, fontAsset);
        Say($"{last.item.itemName} {last.count}개를 되찾았어요", 2.2f);
    }

    private void UpdateUndo()
    {
        if (undoBtn == null) return;
        bool any = sales.Count > 0;
        undoBtn.gameObject.SetActive(any);
        footNote.SetActive(!any);
        if (!any) return;
        var last = sales[sales.Count - 1];
        undoText.text = $"되찾기 · 방금 판 {last.item.itemName} {last.count}개 ({CurrencyManager.Format(last.coins)}코인)";
    }

    private void Fail(RectTransform at, string line)
    {
        Sfx.Play(SfxId.UiError);
        if (at != null && at.gameObject.activeInHierarchy) StartCoroutine(CuteUIKit.Wiggle(at, 7f, 0.3f, 2f));
        Say(line, 3f);
    }

    private string PressWord => Pad.Active ? "A로" : MobileMode.Active ? "누르면" : "클릭하면";

    private void SayIdle()
    {
        float boost = GameEventManager.CropSellMultiplier;
        string line = Pad.Active ? "A로 씨앗을 사고, 가방의 작물을 팔아요 · LT/RT로 가게 / 가방"
                                 : "씨앗을 누르면 사고, 가방의 작물 · 요리를 누르면 팔아요!";
        if (boost > 1.01f) line = $"<color=#A8741A>지금은 풍족기간!</color> 작물을 {boost:0.#}배 값에 팔 수 있어요 · " + line;
        SetLine(line);
    }

    private void Say(string line, float hold)
    {
        holdLineUntil = Time.unscaledTime + hold;
        holdOwner     = (object)hoveredTile ?? hoveredCell;
        SetLine(line);
    }

    private void SetLine(string line)
    {
        if (cocoText != null) cocoText.text = line;
    }

    private void HoverTile(Tile t, bool on)
    {
        if (on) { hoveredTile = t; hoveredCell = null; if (holdOwner != t) holdLineUntil = 0f; }
        else if (hoveredTile == t) hoveredTile = null;
        ShowTooltip(on ? t.item : null);
        if (Time.unscaledTime >= holdLineUntil) RefreshHoverLine();
    }

    private void HoverCell(ItemCell c, bool on)
    {
        if (on) { hoveredCell = c; hoveredTile = null; if (holdOwner != c) holdLineUntil = 0f; }
        else if (hoveredCell == c) hoveredCell = null;
        ShowTooltip(on ? c.item : null);
        if (Time.unscaledTime >= holdLineUntil) RefreshHoverLine();
    }

    private static void ShowTooltip(FarmingItemData item)
    {
        var tip = ItemTooltipUI.Instance;
        if (tip == null) return;
        if (item == null) { tip.Hide(); return; }
        tip.Show(item);
        tip.transform.SetAsLastSibling();
    }

    private void RefreshHoverLine()
    {
        if (!IsOpen) return;
        var shop = ShopManager.Instance;
        if (hoveredTile != null && shop != null)
        {
            var item  = hoveredTile.item;
            string lockReason = shop.LockReason(item);
            int price = shop.BuyPrice(item);
            if (lockReason != null) { SetLine(LockedLine(item, lockReason)); return; }
            long coins = CurrencyManager.Instance != null ? CurrencyManager.Instance.Currency : 0;
            int n = (int)System.Math.Min(BuyAmounts[buyChoice], coins / Mathf.Max(1, price));
            string fresh = IsNew(item) ? " · <color=#F7879F>아직 안 길러 본 작물!</color>" : "";
            SetLine(n <= 0 ? $"{item.itemName} — 1개 {price:N0}코인 <color=#C9485B>(코인이 모자라요)</color>"
                           : $"{item.itemName} — 1개 {price:N0}코인 · {PressWord} {n}개 사요 ({(long)price * n:N0}코인){fresh}");
            return;
        }
        if (hoveredCell != null && hoveredCell.item != null && shop != null)
        {
            var item = hoveredCell.item;
            if (!hoveredCell.usable) { SetLine($"{ItemCellUI.Eun(item.itemName)} 팔 수 없어요 — {WhyNotSellable(item)}"); return; }
            int price = shop.SellPrice(item);
            int want  = SellAmounts[sellChoice];
            int n     = want <= 0 ? hoveredCell.shownCount : Mathf.Min(want, hoveredCell.shownCount);
            string boost = item.isCropProduct && GameEventManager.CropSellMultiplier > 1.01f ? " <color=#A8741A>풍족기간!</color>" : "";
            int order = ShuttleService.NeededBy(item);
            string warn = order > 0 ? $" · <color=#C9485B>셔틀 짐에 {order}개 필요해요!</color>" : "";
            SetLine($"{item.itemName} {hoveredCell.shownCount}개 — 1개 {price:N0}코인{boost} · {PressWord} {n}개 팔아요 (+{(long)price * n:N0}코인){warn}");
            return;
        }
        SayIdle();
    }

    private void PickAmount(bool sell, int k)
    {
        if (sell) sellChoice = k; else buyChoice = k;
        ItemCellUI.PaintAmounts(sell ? sellAmountBtns : buyAmountBtns, k);
        if (Time.unscaledTime >= holdLineUntil) RefreshHoverLine();
    }

    private static string LockedLine(FarmingItemData item, string lockReason)
    {
        if (item.unlockCoreLevel > CenterCore.CurrentLevel)
            return $"{ItemCellUI.Eun(item.itemName)} 문명 Lv.{item.unlockCoreLevel}에 열려요 — 다음 단계는 여정(L)에서 볼 수 있어요";
        return $"{ItemCellUI.Eun(item.itemName)} 아직 살 수 없어요 ({lockReason})";
    }

    private static string LockLabel(FarmingItemData item, string lockReason)
        => item.unlockCoreLevel > CenterCore.CurrentLevel ? $"문명 Lv.{item.unlockCoreLevel}" : lockReason.Replace(" 필요", "");

    private static string WhyNotSellable(FarmingItemData item)
    {
        if (item.hideFromShop) return "소중한 물건이에요";
        if (item.IsFertilizer) return "퇴비는 밭에 뿌려요";
        switch (item.itemType)
        {
            case FarmingItemType.Tool:      return "도구예요";
            case FarmingItemType.Equipment: return "장비예요";
            case FarmingItemType.Tile:      return "설치하는 물건이에요";
        }
        return "제작 재료예요";
    }

    private static bool IsNew(FarmingItemData seed)
    {
        if (seed == null || seed.seedData == null || seed.seedData.drops == null) return false;
        foreach (var d in seed.seedData.drops)
            if (d.item != null && d.item.isCropProduct) return !HarvestLog.Contains(d.item);
        return false;
    }

    private static string ShortName(FarmingItemData item)
    {
        const string suffix = " 씨앗";
        string n = item.itemName ?? "";
        return n.EndsWith(suffix) && n.Length > suffix.Length ? n.Substring(0, n.Length - suffix.Length) : n;
    }

    private void SnapCoins()
    {
        coinShown   = CurrencyManager.Instance != null ? CurrencyManager.Instance.Currency : 0;
        coinWritten = long.MinValue;
        WriteCoins();
    }

    private void TickCoins()
    {
        if (CurrencyManager.Instance == null) return;
        double target = CurrencyManager.Instance.Currency;
        if (System.Math.Abs(target - coinShown) < 0.5) coinShown = target;
        else coinShown += (target - coinShown) * Mathf.Min(1f, Time.unscaledDeltaTime * 9f);
        WriteCoins();
    }

    private void WriteCoins()
    {
        if (coinText == null) return;
        long v = (long)System.Math.Round(coinShown);
        if (v == coinWritten) return;
        coinWritten = v;
        coinText.text = CurrencyManager.Format(v);
    }

    private void PunchCoins(float scale)
    {
        if (coinPunchCo != null) StopCoroutine(coinPunchCo);
        coinPill.localScale = Vector3.one;
        coinPunchCo = StartCoroutine(ItemCellUI.Punch(coinPill, scale));
    }

    private Vector3 CoinDropPoint => coinPill.position + Vector3.down * (coinPill.rect.height * 0.5f + 16f) * Scale;

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
}
