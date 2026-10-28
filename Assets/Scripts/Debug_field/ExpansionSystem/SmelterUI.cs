using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(RectTransform))]
public class SmelterUI : MonoBehaviour
{
    public static SmelterUI Instance => Services.Get<SmelterUI>();
    public static bool IsOpen => Instance != null && Instance.isOpen;

    private const float AnimDuration  = 0.18f;
    private const float AutoCloseDist = 5f;
    private const float SlotSize      = 58f;

    private static readonly Color ProgressFill = CuteUIKit.Hex(0xFFA54F);

    private Smelter       smelter;
    private bool          isOpen;
    private RectTransform panelRT;
    private CanvasGroup   panelCG;
    private Coroutine     animCo;

    private Image           powerPill;
    private TextMeshProUGUI powerText;
    private Image           inputIcon, outputIcon;
    private TextMeshProUGUI inputCount, outputCount;
    private TextMeshProUGUI recipeText, stateText;
    private RectTransform   progressFill;
    private Button          takeInputBtn, takeOutputBtn;
    private Transform       oreRows;

    public static void Open(Smelter target)
    {
        if (target == null) return;
        if (Instance == null) Create();
        if (Instance != null) Instance.Show(target);
    }

    private static void Create()
    {
        Transform parent = GeneratorFuelUI.Instance != null ? GeneratorFuelUI.Instance.transform.parent : null;
        var go = new GameObject("SmelterUI", typeof(RectTransform));
        if (parent != null) go.transform.SetParent(parent, false);
        else UIStyleKit.CreateOverlayCanvas(go, 300);
        go.AddComponent<SmelterUI>();
    }

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }
        BuildPanel();
        panelRT.localScale = Vector3.zero;
    }

    private void OnDestroy()
    {
        Unsubscribe();
        Services.Unregister(this);
    }

    private void Show(Smelter target)
    {
        bool wasOpen = isOpen;
        Unsubscribe();
        smelter = target;
        isOpen  = true;
        smelter.OnChanged += RefreshSlots;
        if (Inventory.Instance != null) Inventory.Instance.OnInventoryChanged += QueueOreRows;

        panelCG.blocksRaycasts = true;
        panelCG.interactable   = true;
        RefreshSlots();
        RebuildOreRows();

        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(true));
        if (!wasOpen) Sfx.Play(SfxId.UiOpen);
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        Unsubscribe();
        smelter = null;
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;
        UIStyleKit.ClearSelectionIn(transform);
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(false));
        Sfx.Play(SfxId.UiClose);
    }

    private void Unsubscribe()
    {
        if (!(smelter is null)) smelter.OnChanged -= RefreshSlots;
        if (Inventory.Instance != null) Inventory.Instance.OnInventoryChanged -= QueueOreRows;
    }

    private IEnumerator Animate(bool open)
    {
        if (open)
        {
            CuteUIKit.WiggleEars(this, panelRT);
            yield return CuteUIKit.PopIn(panelRT, panelCG, AnimDuration * 1.8f);
        }
        else
        {
            yield return CuteUIKit.PopOut(panelRT, panelCG, AnimDuration);
            panelCG.alpha = 0f;
        }
    }

    private void Update()
    {
        if (!isOpen) return;
        if (smelter == null) { Close(); return; }

        if (EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) { Close(); return; }

        var player = PlayerTileController.Instance;
        float cs = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
        if (player != null && smelter.DistanceFrom(player.transform.position) > AutoCloseDist * cs) { Close(); return; }

        CuteUIKit.SetBar(progressFill, smelter.Progress01);
    }

    private void RefreshSlots()
    {
        if (smelter == null) return;

        bool powered = smelter.IsPowered;
        powerPill.color = powered ? CuteUIKit.MintTag : CuteUIKit.ButtonCoral;
        powerText.color = powered ? CuteUIKit.MintText : CuteUIKit.CoralText;
        powerText.text  = powered ? "전기 들어옴" : "전기 없음 — 센터코어 · 전력 중계탑 범위 안으로 옮겨요";

        SetSlot(inputIcon,  inputCount,  smelter.InputItem,  smelter.InputCount);
        SetSlot(outputIcon, outputCount, smelter.OutputItem, smelter.OutputCount);
        takeInputBtn.interactable  = smelter.InputCount  > 0;
        takeOutputBtn.interactable = smelter.OutputCount > 0;

        var recipe = smelter.CurrentRecipe;
        recipeText.text = recipe != null
            ? $"{recipe.ingredients[0].item.itemName} {recipe.ingredients[0].amount}개 → {recipe.outputItem.itemName} {recipe.outputAmount}개"
            : "광석을 넣어 주세요";

        bool outputBlocked = recipe != null && smelter.OutputItem != null && smelter.OutputItem != recipe.outputItem;
        stateText.text = smelter.IsWorking ? $"녹이는 중 · {smelter.SecondsPerBatch:0.#}초마다 1번"
                       : recipe == null    ? "비어 있어요"
                       : !powered          ? "전기가 없어 멈췄어요"
                       : outputBlocked     ? "다른 주괴를 먼저 받아 주세요"
                       : smelter.OutputCount >= smelter.OutputCapacity ? "주괴 칸이 가득 찼어요"
                       : "광석이 모자라요";
        stateText.color = smelter.IsWorking ? CuteUIKit.MintText : CuteUIKit.CocoaSoft;
    }

    private static void SetSlot(Image icon, TextMeshProUGUI count, FarmingItemData item, int n)
    {
        icon.sprite  = item != null ? item.icon : null;
        icon.enabled = icon.sprite != null;
        count.text   = n > 0 ? n.ToString() : "";
    }

    private bool oreRowsQueued;
    private int  oreRowsSignature;

    private void QueueOreRows() => oreRowsQueued = true;

    private void LateUpdate()
    {
        if (!oreRowsQueued) return;
        oreRowsQueued = false;
        if (isOpen && Inventory.Instance != null && Inventory.Instance.Signature(IsSmeltable) != oreRowsSignature)
            RebuildOreRows();
    }

    private static bool IsSmeltable(FarmingItemData item) => Smelter.RecipeFor(item) != null;

    private void RebuildOreRows()
    {
        if (oreRows == null) return;
        oreRowsSignature = Inventory.Instance != null ? Inventory.Instance.Signature(IsSmeltable) : 0;
        for (int i = oreRows.childCount - 1; i >= 0; i--)
            DestroyImmediate(oreRows.GetChild(i).gameObject);

        var inv = Inventory.Instance;
        if (inv == null || smelter == null) return;

        var shown = new HashSet<FarmingItemData>();
        for (int i = 0; i < inv.SlotCount; i++)
        {
            var slot = inv.GetSlot(i);
            if (slot == null || slot.IsEmpty || !shown.Add(slot.Item)) continue;
            var recipe = Smelter.RecipeFor(slot.Item);
            if (recipe == null) continue;
            BuildOreRow(slot.Item, recipe, inv.GetCount(slot.Item));
        }

        if (oreRows.childCount == 0)
        {
            var t = CuteUIKit.Text(oreRows, "넣을 수 있는 광석이 없어요. 광석을 캐 오세요!", 11f, CuteUIKit.CocoaSoft);
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
        }
    }

    private void BuildOreRow(FarmingItemData ore, CraftingRecipe recipe, int have)
    {
        var row = CardRow(oreRows, $"Row_{ore.itemName}");
        IconSlot(row, ore, 30f);

        var info = TextColumn(row);
        RowText(info, ore.itemName, 12f, CuteUIKit.Cocoa, FontStyles.Bold);
        RowText(info, $"보유 {have} · {recipe.ingredients[0].amount}개 → {recipe.outputItem.itemName} {recipe.outputAmount}개",
                10f, CuteUIKit.CocoaSoft);

        var captured = ore;
        CuteUIKit.Button(row, "1",    CuteUIKit.ButtonMint, () => Insert(captured, 1),  34f, 26f, 12f);
        CuteUIKit.Button(row, "10",   CuteUIKit.ButtonMint, () => Insert(captured, 10), 38f, 26f, 12f);
        CuteUIKit.Button(row, "전부", CuteUIKit.ButtonMint, () => Insert(captured, -1), 46f, 26f, 12f);
    }

    private void Insert(FarmingItemData ore, int want)
    {
        var inv = Inventory.Instance;
        if (smelter == null || inv == null) return;

        if (!smelter.CanInsert(ore))
        {
            Sfx.Play(SfxId.UiError);
            NotificationUI.Instance?.Show(smelter.InputItem != null && smelter.InputItem != ore
                ? $"{ItemCellUI.Ga(smelter.InputItem.itemName)} 들어 있어요 — 다 녹이거나 꺼낸 뒤 넣어 주세요"
                : "넣는 칸이 가득 찼어요");
            return;
        }

        int have = inv.GetCount(ore);
        int room = smelter.InputCapacity - smelter.InputCount;
        int n = Mathf.Min(want < 0 ? have : want, have, room);
        if (n <= 0 || !inv.ConsumeItem(ore, n)) { Sfx.Play(SfxId.UiError); return; }

        int inserted = smelter.InsertOre(ore, n);
        if (inserted < n) inv.AddItem(ore, n - inserted);
        Sfx.Play(SfxId.StoreItem);
    }

    private void OnTakeInput()  => TakeToInventory(true);
    private void OnTakeOutput() => TakeToInventory(false);

    private void TakeToInventory(bool input)
    {
        var inv = Inventory.Instance;
        if (smelter == null || inv == null) return;
        var item = input ? smelter.InputItem : smelter.OutputItem;
        int n    = input ? smelter.InputCount : smelter.OutputCount;
        if (item == null || n <= 0) return;
        if (!inv.CanAddItem(item))
        {
            Sfx.Play(SfxId.UiError);
            NotificationUI.Instance?.Show("인벤토리가 가득 찼어요.");
            return;
        }

        if (input) smelter.TakeInput(n, out item);
        else       smelter.TakeOutput(n, out item);
        inv.AddItem(item, n);
        Sfx.Play(SfxId.Pickup);
    }

    private void BuildPanel()
    {
        panelRT = (RectTransform)transform;
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = new Vector2(0f, 40f);
        panelRT.sizeDelta        = new Vector2(420f, 0f);

        if (GetComponent<Image>() == null) gameObject.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT);

        panelCG = gameObject.AddComponent<CanvasGroup>();
        panelCG.alpha          = 0f;
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(18, 18, 16, 18);
        vlg.spacing                = 8f;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildHeader();

        var pill = CuteUIKit.Pill(transform, "전기 없음", CuteUIKit.ButtonCoral, CuteUIKit.CoralText, 11f, 10,
                                  new RectOffset(10, 10, 3, 3), out powerText);
        pill.name = "Power";
        powerPill = pill.GetComponent<Image>();

        BuildFurnaceRow();
        CuteUIKit.DottedDivider(transform);

        CuteUIKit.SectionHeader(transform, "광석 넣기", CuteUIKit.ButtonButter,
                                CuteUIKit.TextOn(CuteUIKit.ButtonButter), "한 종류씩 넣어요");
        var rowsGO = new GameObject("OreRows", typeof(RectTransform));
        rowsGO.transform.SetParent(transform, false);
        var rvlg = rowsGO.AddComponent<VerticalLayoutGroup>();
        rvlg.spacing                = 4f;
        rvlg.childForceExpandWidth  = true;
        rvlg.childForceExpandHeight = false;
        rvlg.childControlWidth      = true;
        rvlg.childControlHeight     = true;
        oreRows = rowsGO.transform;

        CuteUIKit.DottedDivider(transform);
        LinkRowView.Build(transform, () => smelter, Close,
                          n => n > 0 ? $"창고 {n}개와 연결됨 — 광석을 가져오고 주괴를 돌려놔요"
                                     : "창고와 이으면 광석을 알아서 가져오고 주괴를 돌려놔요");
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

        CuteUIKit.TitleRibbon(go.transform, "제련소");

        var close = CuteUIKit.CloseButton(go.transform, Close, 26f);
        close.GetComponent<LayoutElement>().ignoreLayout = true;
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
        crt.sizeDelta        = new Vector2(26f, 26f);
        crt.anchoredPosition = new Vector2(-13f, 1f);
    }

    private void BuildFurnaceRow()
    {
        var row = new GameObject("Furnace", typeof(RectTransform));
        row.transform.SetParent(transform, false);
        var hlg = row.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = 10f;
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        var inCol = SlotColumn(row.transform, "광석", out inputIcon, out inputCount);
        takeInputBtn = CuteUIKit.Button(inCol, "꺼내기", CuteUIKit.ButtonLatte, OnTakeInput, SlotSize + 12f, 24f, 11f);

        var mid = new GameObject("Progress", typeof(RectTransform));
        mid.transform.SetParent(row.transform, false);
        var mvlg = mid.AddComponent<VerticalLayoutGroup>();
        mvlg.spacing                = 6f;
        mvlg.childAlignment         = TextAnchor.MiddleCenter;
        mvlg.childControlWidth      = true;
        mvlg.childControlHeight     = true;
        mvlg.childForceExpandWidth  = true;
        mvlg.childForceExpandHeight = false;
        var mle = mid.AddComponent<LayoutElement>();
        mle.flexibleWidth  = 1f;
        mle.preferredWidth = 0f;

        recipeText = CuteUIKit.Text(mid.transform, "", 10f, CuteUIKit.Cocoa, FontStyles.Bold);
        recipeText.overflowMode = TextOverflowModes.Ellipsis;
        var arrow = CuteUIKit.Text(mid.transform, "→", 18f, CuteUIKit.Latte, FontStyles.Bold);
        arrow.name = "Arrow";
        progressFill = CuteUIKit.Bar(mid.transform, 14f, CuteUIKit.LatteLight, ProgressFill);
        CuteUIKit.SetBar(progressFill, 0f);
        stateText = CuteUIKit.Text(mid.transform, "", 10f, CuteUIKit.CocoaSoft);
        stateText.overflowMode = TextOverflowModes.Ellipsis;

        var outCol = SlotColumn(row.transform, "주괴", out outputIcon, out outputCount);
        takeOutputBtn = CuteUIKit.Button(outCol, "받기", CuteUIKit.ButtonMint, OnTakeOutput, SlotSize + 12f, 24f, 11f);
    }

    private static Transform SlotColumn(Transform parent, string label, out Image icon, out TextMeshProUGUI count)
    {
        var col = new GameObject($"Col_{label}", typeof(RectTransform));
        col.transform.SetParent(parent, false);
        var vlg = col.AddComponent<VerticalLayoutGroup>();
        vlg.spacing                = 4f;
        vlg.childAlignment         = TextAnchor.MiddleCenter;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = false;
        vlg.childForceExpandHeight = false;

        CuteUIKit.Text(col.transform, label, 10f, CuteUIKit.CocoaSoft, FontStyles.Bold);

        var slot = CuteUIKit.Slot(col.transform, SlotSize, CuteUIKit.SlotBorder, CuteUIKit.SlotFill, 14);
        slot.GetComponent<Image>().raycastTarget = false;

        var iconGO = new GameObject("Icon", typeof(RectTransform));
        iconGO.transform.SetParent(slot.transform, false);
        icon = iconGO.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget  = false;
        icon.enabled        = false;
        var irt = icon.rectTransform;
        irt.anchorMin = new Vector2(0.15f, 0.15f);
        irt.anchorMax = new Vector2(0.85f, 0.85f);
        irt.offsetMin = irt.offsetMax = Vector2.zero;

        count = CuteUIKit.Text(slot.transform, "", 15f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.BottomRight);
        var crt = count.rectTransform;
        crt.anchorMin = Vector2.zero;
        crt.anchorMax = Vector2.one;
        crt.offsetMin = new Vector2(4f, 4f);
        crt.offsetMax = new Vector2(-6f, -2f);
        return col.transform;
    }

    private static Transform CardRow(Transform parent, string name)
    {
        var card = CuteUIKit.Card(parent, CuteUIKit.LatteLight, 12, name);
        card.raycastTarget = false;
        var hlg = card.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(6, 6, 4, 4);
        hlg.spacing                = 6f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        return card.transform;
    }

    private static void IconSlot(Transform parent, FarmingItemData item, float size)
    {
        var sprite = item.icon;
        var slot = CuteUIKit.Slot(parent, size, CuteUIKit.TierBorder(item, CuteUIKit.SlotBorder), CuteUIKit.TierFill(item, CuteUIKit.SlotFill), 10);
        slot.GetComponent<Image>().raycastTarget = false;
        var go = new GameObject("Icon", typeof(RectTransform));
        go.transform.SetParent(slot.transform, false);
        var icon = go.AddComponent<Image>();
        icon.sprite         = sprite;
        icon.preserveAspect = true;
        icon.raycastTarget  = false;
        icon.enabled        = sprite != null;
        var rt = icon.rectTransform;
        rt.anchorMin = new Vector2(0.16f, 0.16f);
        rt.anchorMax = new Vector2(0.84f, 0.84f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static Transform TextColumn(Transform parent)
    {
        var go = new GameObject("Info", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.spacing                = 1f;
        vlg.childAlignment         = TextAnchor.MiddleLeft;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        var le = go.AddComponent<LayoutElement>();
        le.flexibleWidth  = 1f;
        le.preferredWidth = 0f;
        return go.transform;
    }

    private static void RowText(Transform parent, string text, float size, Color color, FontStyles style = FontStyles.Normal)
    {
        var t = CuteUIKit.Text(parent, text, size, color, style, TextAlignmentOptions.MidlineLeft);
        t.overflowMode = TextOverflowModes.Ellipsis;
    }
}
