using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(RectTransform))]
public class ItemStorageUI : MonoBehaviour
{
    public static ItemStorageUI Instance => Services.Get<ItemStorageUI>();

    [SerializeField] private float animDuration = 0.18f;

    private static readonly Color StockFill     = CuteUIKit.HotbarBorder;
    private static readonly Color FullFill      = CuteUIKit.Strawberry;
    private static readonly Color UnlimitedFill = CuteUIKit.LavenderTag;

    private const float SlotSize = 62f, SlotGap = 6f;
    private const int   BoxCols = 6, Rows = 4;
    private static float BoxW => BoxCols * SlotSize + (BoxCols - 1) * SlotGap;
    private static float BagW => BagView.Width(SlotSize, SlotGap);

    private static readonly int[] Amounts = { 1, 10, 0 };
    private static int amountChoice = 2;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => amountChoice = 2;

    private RectTransform panelRT;
    private CanvasGroup   panelCG;
    private TileItem      currentTile;
    private IItemBox      currentStorage;
    private bool          isOpen;
    public  bool          IsOpen => isOpen;
    private Coroutine     animCo;
    private bool          refreshQueued;

    private RectTransform   fillRT;
    private Image           fillImg;
    private TextMeshProUGUI fillText;
    private TextMeshProUGUI titleText, boxTitle, boxNote, bagNote, statusText;
    private Transform       boxGrid;
    private ScrollRect      boxScroll;
    private BagView         bagView;
    private Button[]        amountBtns;
    private Button          depositAllButton;
    private bool            depositAllFullLook;
    private int             shownTotal = -1, shownCapacity = -1;
    private float           statusUntil;
    private ItemCell        hovered;

    private readonly List<ItemCell>        boxCells   = new();
    private readonly List<FarmingItemData> stockOrder = new();

    private bool IsFull => currentStorage != null && !currentStorage.HasSpace;

    private string BoxName => currentTile is AutoSeeder ? "씨앗통" : "창고";

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }

        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);
        { var c = GetComponent<VerticalLayoutGroup>(); if (c) DestroyImmediate(c); }
        { var c = GetComponent<ContentSizeFitter>();   if (c) DestroyImmediate(c); }
        { var c = GetComponent<CanvasGroup>();         if (c) DestroyImmediate(c); }

        BuildPanel();
        panelRT.localScale = Vector3.zero;
        panelCG.alpha      = 0f;
        UIStyleKit.SetScrolling(panelRT, false);
    }

    private void OnDestroy()
    {
        Unsubscribe();
        Services.Unregister(this);
    }

    private Transform playerTransform;
    private const float AUTO_CLOSE_DIST = 5f;

    private void Update()
    {
        if (!isOpen) return;

        if (currentTile == null) { Close(); return; }

        if (EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) { Close(); return; }

        if (playerTransform == null)
        {
            var ptc = PlayerTileController.Instance;
            if (ptc != null) playerTransform = ptc.transform;
        }

        if (playerTransform != null)
        {
            float cellSize = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
            float dist = Vector2.Distance(playerTransform.position, currentTile.transform.position);
            if (dist > AUTO_CLOSE_DIST * cellSize) { Close(); return; }
        }

        if (statusUntil > 0f && Time.unscaledTime >= statusUntil)
        {
            statusUntil = 0f;
            RefreshStatus();
        }
    }

    // 프레임당 한 번만 갱신
    private void LateUpdate()
    {
        if (!isOpen || !refreshQueued || currentTile == null) return;
        refreshQueued = false;
        RefreshAll();
    }

    private void QueueRefresh() => refreshQueued = true;

    private void RefreshAll()
    {
        RefreshFill();
        RefreshBox();
        bagView.Refresh();
        RefreshDepositAll();
        if (statusUntil <= 0f) RefreshStatus();
    }

    private void RefreshFill()
    {
        if (fillRT == null || currentStorage == null) return;
        int total = currentStorage.TotalCount, capacity = currentStorage.Capacity;
        if (total == shownTotal && capacity == shownCapacity) return;
        shownTotal    = total;
        shownCapacity = capacity;

        if (currentStorage.IsUnlimited)
        {
            CuteUIKit.SetBar(fillRT, 1f);
            fillImg.color  = UnlimitedFill;
            fillText.text  = $"{total} / ∞";
            fillText.color = CuteUIKit.LavenderText;
            return;
        }

        CuteUIKit.SetBar(fillRT, capacity > 0 ? (float)total / capacity : 0f);
        fillText.text = $"{total} / {capacity}";

        bool full = IsFull;
        fillImg.color  = full ? FullFill : StockFill;
        fillText.color = full ? Color.white : CuteUIKit.Cocoa;
    }

    private void RefreshBox()
    {
        stockOrder.Clear();
        if (currentStorage != null)
            foreach (var item in currentStorage.StockedItems)
                if (item != null && currentStorage.GetCount(item) > 0) stockOrder.Add(item);

        int need = Mathf.Max(BoxCols * Rows, Mathf.CeilToInt(stockOrder.Count / (float)BoxCols) * BoxCols);
        bool grew = false;
        while (boxCells.Count < need)
        {
            var c = ItemCell.Build(boxGrid, SlotSize, false, null, TakeFrom, HoverBox);
            c.index   = boxCells.Count;
            c.rt.name = $"Box_{c.index}";
            boxCells.Add(c);
            grew = true;
        }
        for (int i = 0; i < boxCells.Count; i++)
        {
            var c = boxCells[i];
            bool used = i < need;
            if (c.rt.gameObject.activeSelf != used) { c.rt.gameObject.SetActive(used); grew = true; }
            var item = i < stockOrder.Count ? stockOrder[i] : null;
            c.Set(item, item != null ? currentStorage.GetCount(item) : 0, true, null);
        }
        if (grew) ItemCellUI.UpdateScrollable(boxScroll);
    }

    private void RefreshDepositAll()
    {
        bool fullLook = IsFull;
        if (depositAllButton == null || fullLook == depositAllFullLook) return;
        depositAllFullLook = fullLook;
        CuteUIKit.ApplyButtonColors(depositAllButton, fullLook ? CuteUIKit.ButtonCoral : CuteUIKit.ButtonMint);
    }

    public void Open(TileItem tile)
    {
        if (tile is not IItemBox box) return;
        bool wasOpen = isOpen;

        Unsubscribe();

        currentTile    = tile;
        currentStorage = box;
        isOpen = true;

        bool seeder = tile is AutoSeeder;
        titleText.text = seeder ? LinkRules.NameOf(tile) : "창고";
        boxTitle.text  = seeder ? "씨앗통" : "창고 안";
        boxNote.text   = seeder ? "위 칸부터 차례로 심어요" : "누르면 꺼내요";
        bagNote.text   = seeder ? "씨앗을 누르면 넣어요" : "누르면 넣어요";

        panelCG.blocksRaycasts = true;
        panelCG.interactable   = true;
        transform.SetAsLastSibling();

        currentStorage.OnStockChanged += QueueRefresh;
        if (Inventory.Instance != null)
            Inventory.Instance.OnInventoryChanged += QueueRefresh;

        bagView.Usable = item => currentStorage != null && currentStorage.Accepts(item);
        shownTotal = shownCapacity = -1;
        refreshQueued = false;
        statusUntil   = 0f;
        hovered       = null;
        bagView.Repaint();
        RefreshAll();
        PlaceGamepadDefault();

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRT);
        ItemCellUI.UpdateScrollable(boxScroll);
        ItemCellUI.UpdateScrollable(bagView.Scroll);
        ItemCellUI.ResetScroll(boxScroll);
        ItemCellUI.ResetScroll(bagView.Scroll);

        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(true));

        if (!wasOpen) Sfx.Play(SfxId.UiOpen);
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;
        Unsubscribe();
        currentTile    = null;
        currentStorage = null;
        refreshQueued  = false;
        ItemTooltipUI.Instance?.Hide();
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(false));
        Sfx.Play(SfxId.UiClose);
    }

    private void Unsubscribe()
    {
        if (currentStorage is not null) currentStorage.OnStockChanged -= QueueRefresh;
        if (Inventory.Instance != null) Inventory.Instance.OnInventoryChanged -= QueueRefresh;
    }

    private IEnumerator Animate(bool open)
    {
        if (open)
        {
            UIStyleKit.SetScrolling(panelRT, true);
            CuteUIKit.WiggleEars(this, panelRT);
            yield return CuteUIKit.PopIn(panelRT, panelCG, animDuration * 1.8f);
        }
        else
        {
            yield return CuteUIKit.PopOut(panelRT, panelCG, animDuration);
            panelCG.alpha = 0f;
            UIStyleKit.SetScrolling(panelRT, false);
        }
    }

    private void PlaceGamepadDefault()
    {
        foreach (var c in boxCells) RemoveDefault(c.rt);
        foreach (var c in bagView.Cells) RemoveDefault(c.rt);
        RectTransform target = boxCells.Count > 0 && boxCells[0].item != null ? boxCells[0].rt : null;
        if (target == null)
            foreach (var c in bagView.Cells) if (c.item != null && c.usable) { target = c.rt; break; }
        if (target == null && boxCells.Count > 0) target = boxCells[0].rt;
        if (target != null) target.gameObject.AddComponent<GamepadDefault>();
    }

    private static void RemoveDefault(RectTransform rt)
    {
        if (rt != null && rt.TryGetComponent(out GamepadDefault d)) DestroyImmediate(d);
    }

    private int AmountFor(int have) => Amounts[amountChoice] <= 0 ? have : Mathf.Min(Amounts[amountChoice], have);

    private void PutFrom(ItemCell c)
    {
        var inv = Inventory.Instance;
        if (currentStorage == null || inv == null) return;
        var slot = inv.GetSlot(c.index);
        if (slot == null || slot.IsEmpty) return;
        var item = slot.Item;

        if (!currentStorage.Accepts(item))
        {
            Fail(c.rt, currentTile is AutoSeeder ? "씨앗통에는 씨앗만 넣을 수 있어요" : $"{ItemCellUI.Eun(item.itemName)} 이 창고에 넣을 수 없어요");
            return;
        }
        if (!currentStorage.HasSpace) { Fail(fillRT.parent.parent as RectTransform, $"{BoxName}가 가득 찼어요"); return; }

        int want  = AmountFor(slot.Count);
        int added = currentStorage.AddItem(item, want);
        if (added <= 0) { Fail(c.rt, $"{BoxName}가 가득 찼어요"); return; }

        if (!inv.ConsumeItem(item, added))
        {
            currentStorage.RemoveItem(item, added);
            Fail(c.rt, "지금은 넣을 수 없어요");
            return;
        }

        Sfx.Play(SfxId.StoreItem);
        Vector3 from = c.rt.position;
        RefreshAll();
        var to = CellOf(boxCells, item);
        if (to != null) ItemCellUI.FlyIcon(this, item.icon, from, to.rt);
        Status(added < want ? $"{item.itemName} {added}개를 넣었어요 ({BoxName}가 가득 차 나머지는 남겼어요)"
                            : $"{item.itemName} {added}개를 넣었어요");
    }

    private void TakeFrom(ItemCell c)
    {
        var inv = Inventory.Instance;
        if (currentStorage == null || inv == null || c.item == null) return;
        var item  = c.item;
        int stock = currentStorage.GetCount(item);
        if (stock <= 0) { QueueRefresh(); return; }
        if (!inv.CanAddItem(item)) { Fail(c.rt, "가방이 꽉 찼어요"); return; }

        int removed = currentStorage.RemoveItem(item, AmountFor(stock));
        if (removed <= 0) return;
        if (!inv.AddItem(item, removed))
        {
            currentStorage.AddItem(item, removed);
            Fail(c.rt, "가방이 꽉 찼어요");
            return;
        }

        Sfx.Play(SfxId.StoreItem);
        Vector3 from = c.rt.position;
        RefreshAll();
        var to = bagView.CellOf(item);
        if (to != null) ItemCellUI.FlyIcon(this, item.icon, from, to.rt);
        Status($"{item.itemName} {removed}개를 꺼냈어요" + (to != null && to.index < BagView.Cols ? $" — 핫바 {to.index + 1}번 칸" : ""));
    }

    private void DepositAll()
    {
        var inv = Inventory.Instance;
        if (currentStorage == null || inv == null) return;

        int total = 0;
        var kinds = new HashSet<FarmingItemData>();
        var moved = new List<(Sprite icon, Vector3 from, FarmingItemData item)>();
        bool stoppedFull = false;
        for (int i = 0; i < inv.SlotCount; i++)
        {
            var slot = inv.GetSlot(i);
            if (slot == null || slot.IsEmpty) continue;
            var item = slot.Item;
            if (item.itemType == FarmingItemType.Tool || item.itemType == FarmingItemType.Equipment) continue;
            if (!currentStorage.Accepts(item)) continue;
            if (!currentStorage.HasSpace) { stoppedFull = true; break; }

            int count = slot.Count;
            int added = currentStorage.AddItem(item, count);
            if (added <= 0) continue;
            if (!inv.ConsumeItem(item, added)) { currentStorage.RemoveItem(item, added); continue; }

            if (kinds.Add(item) && bagView.CellAt(i) != null) moved.Add((item.icon, bagView.CellAt(i).rt.position, item));
            total += added;
            if (added < count) { stoppedFull = true; break; }
        }

        if (total > 0)
        {
            Sfx.Play(SfxId.StoreItem);
            RefreshAll();
            foreach (var (icon, from, item) in moved)
            {
                var to = CellOf(boxCells, item);
                if (to != null) ItemCellUI.FlyIcon(this, icon, from, to.rt);
            }
            Status(stoppedFull ? $"{kinds.Count}종 {total}개를 넣었어요 — {BoxName}가 가득 차서 나머지는 남겼어요"
                               : $"{kinds.Count}종 {total}개를 넣었어요");
        }
        else Fail(depositAllButton.transform as RectTransform, stoppedFull ? $"{BoxName}가 가득 찼어요" : "넣을 수 있는 물건이 없어요");
    }

    private static ItemCell CellOf(List<ItemCell> cells, FarmingItemData item)
    {
        foreach (var c in cells) if (c.item == item && c.rt.gameObject.activeSelf) return c;
        return null;
    }

    private void Fail(RectTransform at, string line)
    {
        Sfx.Play(SfxId.UiError);
        if (at != null && at.gameObject.activeInHierarchy) StartCoroutine(CuteUIKit.Wiggle(at, 7f, 0.3f, 2f));
        Status(line, 3f);
    }

    private void Status(string line, float hold = 2.4f)
    {
        statusUntil = Time.unscaledTime + hold;
        if (statusText != null) statusText.text = line;
    }

    private void HoverBox(ItemCell c, bool on) => HoverAny(c, on);
    private void HoverBag(ItemCell c, bool on) => HoverAny(c, on);

    private void HoverAny(ItemCell c, bool on)
    {
        if (on) { hovered = c; statusUntil = 0f; }
        else if (hovered == c) hovered = null;
        var tip = ItemTooltipUI.Instance;
        if (tip != null)
        {
            if (on && c.item != null) { tip.Show(c.item); tip.transform.SetAsLastSibling(); }
            else tip.Hide();
        }
        if (statusUntil <= 0f) RefreshStatus();
    }

    private string PressWord => Pad.Active ? "A로" : MobileMode.Active ? "누르면" : "클릭하면";

    private void RefreshStatus()
    {
        if (statusText == null || !isOpen) return;
        var c = hovered;
        if (c != null && c.item != null)
        {
            bool inBox = boxCells.Contains(c);
            int n = AmountFor(c.shownCount);
            if (inBox)                 statusText.text = $"{c.item.itemName} {c.shownCount}개 — {PressWord} {n}개 꺼내요";
            else if (!c.usable)        statusText.text = currentTile is AutoSeeder ? "씨앗통에는 씨앗만 넣을 수 있어요" : $"{ItemCellUI.Eun(c.item.itemName)} 이 창고에 넣을 수 없어요";
            else                       statusText.text = $"{c.item.itemName} {c.shownCount}개 — {PressWord} {n}개 넣어요";
            return;
        }
        statusText.text = Pad.Active ? "A로 칸을 누르면 반대쪽으로 옮겨요" : "칸을 누르면 반대쪽으로 옮겨요 — 창고 / 가방";
    }

    private void PickAmount(int k)
    {
        amountChoice = k;
        ItemCellUI.PaintAmounts(amountBtns, k);
        if (statusUntil <= 0f) RefreshStatus();
    }

    private void BuildPanel()
    {
        panelRT = GetComponent<RectTransform>();
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = new Vector2(0f, 20f);

        if (GetComponent<Image>() == null) gameObject.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT);

        panelCG = gameObject.AddComponent<CanvasGroup>();
        panelCG.alpha          = 0f;
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(18, 18, 16, 18);
        vlg.spacing                = 8f;
        vlg.childAlignment         = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth  = false;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;

        var csf = gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildHeader("창고");
        BuildInfoRow();

        var body = ItemCellUI.HRow("Body", transform, 14f, TextAnchor.UpperLeft);
        body.GetComponent<HorizontalLayoutGroup>().childForceExpandHeight = true;
        BuildBoxPane(body);
        BuildBagPane(body);

        var link = LinkRowView.Build(transform, () => currentTile, Close, n =>
            currentTile is AutoSeeder
                ? (n > 0 ? $"창고 {n}개와 연결됨 — 씨앗통이 비면 창고의 씨앗을 꺼내 심고, 퇴비가 있으면 함께 뿌려요"
                         : "창고와 이으면 씨앗통이 빌 때 창고의 씨앗을 꺼내 써요")
                : (n > 0 ? $"설비 {n}개와 연결됨 — 이어진 설비가 이 창고에 넣고 꺼내 가요"
                         : "자동수확기 · 제련소 · 주방 · 착륙장 같은 설비와 이으면 이 창고로 알아서 넣고 꺼내 가요"));
        link.gameObject.AddComponent<LayoutElement>().preferredWidth = BodyW;
    }

    private static float BodyW => BoxW + BagW + 12f * 4 + 12f * 2 + 14f;

    private void BuildHeader(string title)
    {
        var go = new GameObject("Header", typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        var le = go.AddComponent<LayoutElement>();
        le.minHeight     = 30f;
        le.flexibleWidth = 1f;

        titleText = CuteUIKit.TitleRibbon(go.transform, title).GetComponentInChildren<TextMeshProUGUI>();

        var close = CuteUIKit.CloseButton(go.transform, Close, 26f);
        close.GetComponent<LayoutElement>().ignoreLayout = true;
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
        crt.sizeDelta        = new Vector2(26f, 26f);
        crt.anchoredPosition = new Vector2(-13f, 1f);
    }

    private void BuildInfoRow()
    {
        var row = ItemCellUI.HRow("Info", transform, 6f, TextAnchor.MiddleLeft);
        var le = row.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth  = BodyW;
        le.preferredHeight = le.minHeight = 28f;

        statusText = CuteUIKit.Text(row, "", 13f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        statusText.overflowMode = TextOverflowModes.Ellipsis;
        var sle = statusText.gameObject.AddComponent<LayoutElement>();
        sle.flexibleWidth = 1f;
        sle.minWidth      = 10f;
        amountBtns = ItemCellUI.AmountPicker(row, Amounts, amountChoice, null, PickAmount);
    }

    private void BuildBoxPane(Transform body)
    {
        var pane = ItemCellUI.Pane("BoxPane", body);
        var header = ItemCellUI.PaneHeader(pane, "창고 안", CuteUIKit.SkyTag, CuteUIKit.SkyText, "누르면 꺼내요", BoxW + 12f, null, out boxNote);
        boxTitle = header.GetChild(0).GetComponentInChildren<TextMeshProUGUI>();
        BuildFillBar(pane);

        float h = Rows * SlotSize + (Rows - 1) * SlotGap;
        boxGrid = ItemCellUI.ScrollGrid(pane, "Box", BoxW, h, new Vector2(SlotSize, SlotSize), SlotGap, BoxCols, out boxScroll);
    }

    private void BuildBagPane(Transform body)
    {
        var pane = ItemCellUI.Pane("BagPane", body);
        var header = ItemCellUI.PaneHeader(pane, "내 가방", CuteUIKit.MintTag, CuteUIKit.MintText, "누르면 넣어요", BagW + 12f, null, out bagNote);
        depositAllButton = CuteUIKit.Button(header, "모두 넣기", CuteUIKit.ButtonMint, DepositAll, 78f, 24f, 11f);

        bagView = new BagView(pane, SlotSize, SlotGap, Rows, null)
        {
            Clicked = PutFrom,
            Hovered = HoverBag,
        };
    }

    private void BuildFillBar(Transform pane)
    {
        fillRT  = CuteUIKit.Bar(pane, 20f, CuteUIKit.LatteLight, StockFill, BoxW + 12f);
        fillImg = fillRT.GetComponent<Image>();
        var bar = fillRT.parent.parent;
        bar.name = "FillBar";
        CuteUIKit.SetBar(fillRT, 0f);

        fillText = CuteUIKit.Text(bar, "0 / 0", 11f, CuteUIKit.Cocoa, FontStyles.Bold);
        fillText.rectTransform.anchorMin = Vector2.zero;
        fillText.rectTransform.anchorMax = Vector2.one;
        fillText.rectTransform.offsetMin = fillText.rectTransform.offsetMax = Vector2.zero;
        fillText.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
    }
}
