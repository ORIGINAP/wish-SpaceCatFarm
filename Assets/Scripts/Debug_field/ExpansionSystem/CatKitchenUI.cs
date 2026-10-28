using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(RectTransform))]
public class CatKitchenUI : MonoBehaviour
{
    public static CatKitchenUI Instance => Services.Get<CatKitchenUI>();
    public static bool IsOpen => Instance != null && Instance.isOpen;

    private const float AutoCloseDist = 5f;
    private const float SlotSize      = 52f;
    private static readonly Color ProgressFill = CuteUIKit.Hex(0xFFA54F);

    private CatKitchen    kitchen;
    private bool          isOpen;
    private RectTransform panelRT;
    private CanvasGroup   panelCG;
    private Coroutine     animCo;

    private Image           powerPill;
    private TextMeshProUGUI powerText;
    private Transform       menuGrid;
    private Transform       ingredientRow;
    private TextMeshProUGUI recipeTitle, stateText;
    private RectTransform   progressFill;
    private Image           outputIcon;
    private TextMeshProUGUI outputCount;
    private Button          takeOutputBtn, insertBtn, takeInputsBtn;
    private bool            dirty;
    private CraftingRecipe  shownMenuRecipe;
    private int             shownMenuLevel = -1;
    private CatKitchen      shownMenuKitchen;

    public static void Open(CatKitchen target)
    {
        if (target == null) return;
        if (Instance == null) Create();
        if (Instance != null) Instance.Show(target);
    }

    private static void Create()
    {
        Transform parent = GeneratorFuelUI.Instance != null ? GeneratorFuelUI.Instance.transform.parent : null;
        var go = new GameObject("CatKitchenUI", typeof(RectTransform));
        if (parent != null) go.transform.SetParent(parent, false);
        else UIStyleKit.CreateOverlayCanvas(go, 300);
        go.AddComponent<CatKitchenUI>();
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

    private void Show(CatKitchen target)
    {
        bool wasOpen = isOpen;
        Unsubscribe();
        kitchen = target;
        isOpen  = true;
        kitchen.OnChanged += MarkDirty;
        if (Inventory.Instance != null) Inventory.Instance.OnInventoryChanged += MarkDirty;
        panelCG.blocksRaycasts = true;
        panelCG.interactable   = true;
        Refresh();
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(true));
        if (!wasOpen) Sfx.Play(SfxId.UiOpen);
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        Unsubscribe();
        kitchen = null;
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;
        UIStyleKit.ClearSelectionIn(transform);
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(false));
        Sfx.Play(SfxId.UiClose);
    }

    private void Unsubscribe()
    {
        if (!(kitchen is null)) kitchen.OnChanged -= MarkDirty;
        if (Inventory.Instance != null) Inventory.Instance.OnInventoryChanged -= MarkDirty;
    }

    private void MarkDirty() => dirty = true;

    private IEnumerator Animate(bool open)
    {
        if (open)
        {
            CuteUIKit.WiggleEars(this, panelRT);
            yield return CuteUIKit.PopIn(panelRT, panelCG, 0.3f);
        }
        else
        {
            yield return CuteUIKit.PopOut(panelRT, panelCG, 0.16f);
            panelCG.alpha = 0f;
        }
    }

    private void Update()
    {
        if (!isOpen) return;
        if (kitchen == null) { Close(); return; }
        if (EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) { Close(); return; }

        var player = PlayerTileController.Instance;
        float cs = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
        if (player != null && kitchen.DistanceFrom(player.transform.position) > AutoCloseDist * cs) { Close(); return; }

        CuteUIKit.SetBar(progressFill, kitchen.Progress01);
        if (insertBtn != null) UIStyleKit.KeepNavigable(insertBtn.gameObject, transform);
    }

    private void LateUpdate()
    {
        if (!dirty || !isOpen) return;
        dirty = false;
        Refresh();
    }

    private void Refresh()
    {
        if (kitchen == null) return;

        bool powered = kitchen.IsPowered;
        powerPill.color = powered ? CuteUIKit.MintTag : CuteUIKit.ButtonCoral;
        powerText.color = powered ? CuteUIKit.MintText : CuteUIKit.CoralText;
        powerText.text  = powered ? "전기 들어옴" : "전기 없음 — 센터코어 · 전력 중계탑 범위 안으로 옮겨요";

        RebuildMenu();
        RebuildIngredients();

        var r = kitchen.Recipe;
        recipeTitle.text = r != null
            ? $"<b>{r.outputItem.itemName}</b> ×{r.outputAmount}  <color=#A88E82>· {kitchen.SecondsPerDish:0.#}초 · 판매가 {r.outputItem.basePrice:N0}코인</color>"
            : "메뉴판에서 요리를 골라 주세요";

        bool outputBlocked = r != null && kitchen.OutputItem != null && kitchen.OutputItem != r.outputItem;
        stateText.text = kitchen.IsWorking ? "보글보글 요리 중…"
                       : r == null         ? "메뉴를 골라 주세요"
                       : !powered          ? "전기가 없어 멈췄어요"
                       : outputBlocked     ? "다른 요리를 먼저 받아 주세요"
                       : kitchen.OutputCount >= kitchen.OutputCapacity ? "요리 칸이 가득 찼어요"
                       : MissingText(r);
        stateText.color = kitchen.IsWorking ? CuteUIKit.MintText : CuteUIKit.CocoaSoft;

        outputIcon.sprite  = kitchen.OutputItem != null ? kitchen.OutputItem.icon : null;
        outputIcon.enabled = outputIcon.sprite != null;
        outputCount.text   = kitchen.OutputCount > 0 ? kitchen.OutputCount.ToString() : "";
        takeOutputBtn.interactable = kitchen.OutputCount > 0;
        takeInputsBtn.interactable = kitchen.TotalInputs > 0;
        insertBtn.interactable     = r != null;
    }

    private string MissingText(CraftingRecipe r)
    {
        var inv = Inventory.Instance;
        foreach (var ing in r.ingredients)
        {
            if (ing.item == null) continue;
            int have = kitchen.GetInput(ing.item) + (inv != null ? inv.GetCount(ing.item) : 0);
            if (have < ing.amount) return $"{ItemCellUI.Ga(ing.item.itemName)} 모자라요 — 모아 와서 [재료 넣기]";
        }
        return "재료가 모자라요 — [재료 넣기]";
    }

    private void RebuildMenu()
    {
        int level = CenterCore.CurrentLevel;
        if (shownMenuKitchen == kitchen && shownMenuRecipe == kitchen.Recipe && shownMenuLevel == level && menuGrid.childCount > 0) return;
        shownMenuKitchen = kitchen;
        shownMenuRecipe  = kitchen.Recipe;
        shownMenuLevel   = level;
        for (int i = menuGrid.childCount - 1; i >= 0; i--) DestroyImmediate(menuGrid.GetChild(i).gameObject);
        foreach (var r in CatKitchen.Menu())
        {
            bool unlocked = CatKitchen.IsUnlocked(r);
            bool selected = kitchen.Recipe == r;
            Color fill = selected ? CuteUIKit.PinkSoft : unlocked ? CuteUIKit.LatteLight : CuteUIKit.Lighter(CuteUIKit.LatteLight, 0.5f);
            var captured = r;
            var btn = CuteUIKit.Button(menuGrid, "", fill, () => Choose(captured), 92f, 58f, 10f);
            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.text = unlocked ? r.outputItem.itemName : $"문명 Lv.{r.requiredCoreLevel}";
                label.fontSize = 10f;
                label.alignment = TextAlignmentOptions.Bottom;
                label.color = unlocked ? CuteUIKit.Cocoa : CuteUIKit.CocoaMuted;
                var lrt = label.rectTransform;
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.offsetMin = new Vector2(2f, 3f);
                lrt.offsetMax = new Vector2(-2f, -2f);
            }
            var icon = new GameObject("Icon", typeof(RectTransform));
            icon.transform.SetParent(btn.transform, false);
            var img = icon.AddComponent<Image>();
            img.sprite = r.outputItem.icon;
            img.preserveAspect = true;
            img.raycastTarget  = false;
            img.color = unlocked ? Color.white : new Color(0.3f, 0.25f, 0.25f, 0.45f);
            var irt = img.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
            irt.pivot = new Vector2(0.5f, 1f);
            irt.sizeDelta = new Vector2(30f, 30f);
            irt.anchoredPosition = new Vector2(0f, -4f);
        }
    }

    private void RebuildIngredients()
    {
        for (int i = ingredientRow.childCount - 1; i >= 0; i--) DestroyImmediate(ingredientRow.GetChild(i).gameObject);
        var r = kitchen.Recipe;
        if (r == null)
        {
            CuteUIKit.Text(ingredientRow, "고른 메뉴의 재료가 여기에 보여요", 11f, CuteUIKit.CocoaMuted);
            return;
        }
        var inv = Inventory.Instance;
        foreach (var ing in r.ingredients)
        {
            if (ing.item == null) continue;
            var col = new GameObject("Ing", typeof(RectTransform));
            col.transform.SetParent(ingredientRow, false);
            var vlg = col.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = vlg.childControlHeight = true;
            vlg.childForceExpandWidth = vlg.childForceExpandHeight = false;

            var slot = CuteUIKit.Slot(col.transform, SlotSize, CuteUIKit.TierBorder(ing.item, CuteUIKit.SlotBorder), CuteUIKit.TierFill(ing.item, CuteUIKit.SlotFill), 12);
            slot.GetComponent<Image>().raycastTarget = false;
            var iconGO = new GameObject("Icon", typeof(RectTransform));
            iconGO.transform.SetParent(slot.transform, false);
            var icon = iconGO.AddComponent<Image>();
            icon.sprite = ing.item.icon;
            icon.preserveAspect = true;
            icon.raycastTarget  = false;
            var irt = icon.rectTransform;
            irt.anchorMin = new Vector2(0.15f, 0.15f);
            irt.anchorMax = new Vector2(0.85f, 0.85f);
            irt.offsetMin = irt.offsetMax = Vector2.zero;

            int inKitchen = kitchen.GetInput(ing.item);
            int have = inv != null ? inv.GetCount(ing.item) : 0;
            string ok = inKitchen >= ing.amount ? "#3F8A66" : "#C9485B";
            CuteUIKit.Text(col.transform, $"{ing.item.itemName} ×{ing.amount}", 10f, CuteUIKit.Cocoa, FontStyles.Bold);
            CuteUIKit.Text(col.transform, $"<color={ok}>주방 {inKitchen}</color> · 가방 {have}", 10f, CuteUIKit.CocoaSoft);
        }
    }

    private void Choose(CraftingRecipe r)
    {
        if (kitchen == null) return;
        if (!CatKitchen.IsUnlocked(r))
        {
            Sfx.Play(SfxId.UiError);
            NotificationUI.Instance?.Show($"{ItemCellUI.Eun(r.outputItem.itemName)} 센터코어 문명 Lv.{r.requiredCoreLevel}에 열려요");
            return;
        }
        if (r != kitchen.Recipe && !kitchen.CanChangeRecipe) OnTakeInputs();
        if (!kitchen.SetRecipe(r))
        {
            Sfx.Play(SfxId.UiError);
            NotificationUI.Instance?.Show("가방이 가득 차서 넣어 둔 재료를 꺼내지 못했어요 — 가방을 비운 뒤 메뉴를 바꿔 주세요");
            return;
        }
        Refresh();
    }

    private void OnInsert()
    {
        var inv = Inventory.Instance;
        var r   = kitchen != null ? kitchen.Recipe : null;
        if (inv == null || r == null) return;

        int dishes = int.MaxValue;
        foreach (var ing in r.ingredients)
        {
            if (ing.item == null || ing.amount <= 0) continue;
            int could = kitchen.GetInput(ing.item) + Mathf.Min(inv.GetCount(ing.item), kitchen.RoomFor(ing.item));
            dishes = Mathf.Min(dishes, could / ing.amount);
        }
        bool fillAll = dishes <= 0 || dishes == int.MaxValue;

        int total = 0;
        foreach (var ing in r.ingredients)
        {
            if (ing.item == null) continue;
            int n = Mathf.Min(inv.GetCount(ing.item), kitchen.RoomFor(ing.item));
            if (!fillAll) n = Mathf.Min(n, dishes * ing.amount - kitchen.GetInput(ing.item));
            if (n <= 0 || !inv.ConsumeItem(ing.item, n)) continue;
            int put = kitchen.InsertIngredient(ing.item, n);
            if (put < n) inv.AddItem(ing.item, n - put);
            total += put;
        }
        if (total > 0) Sfx.Play(SfxId.StoreItem);
        else
        {
            Sfx.Play(SfxId.UiError);
            NotificationUI.Instance?.Show("넣을 재료가 가방에 없거나 재료 칸이 가득 찼어요");
        }
    }

    private void OnTakeInputs()
    {
        var inv = Inventory.Instance;
        if (kitchen == null || inv == null) return;
        foreach (var item in new List<FarmingItemData>(kitchen.InputItems))
        {
            int n = kitchen.GetInput(item);
            if (n <= 0 || !inv.CanAddItem(item)) continue;
            int taken = kitchen.TakeInput(item, n);
            if (taken > 0) inv.AddItem(item, taken, countAsNew: false);
        }
        Sfx.Play(SfxId.Pickup);
    }

    private void OnTakeOutput()
    {
        var inv = Inventory.Instance;
        if (kitchen == null || inv == null || kitchen.OutputItem == null) return;
        if (!inv.CanAddItem(kitchen.OutputItem))
        {
            Sfx.Play(SfxId.UiError);
            NotificationUI.Instance?.Show("인벤토리가 가득 찼어요.");
            return;
        }
        int n = kitchen.TakeOutput(kitchen.OutputCount, out var item);
        if (n > 0) inv.AddItem(item, n);
        Sfx.Play(SfxId.Pickup);
    }

    private void BuildPanel()
    {
        panelRT = (RectTransform)transform;
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = new Vector2(0f, 30f);
        panelRT.sizeDelta        = new Vector2(540f, 0f);

        if (GetComponent<Image>() == null) gameObject.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT);

        panelCG = gameObject.AddComponent<CanvasGroup>();
        panelCG.alpha          = 0f;
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(18, 18, 16, 18);
        vlg.spacing                = 7f;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(transform, false);
        var hlg = header.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
        header.AddComponent<LayoutElement>().minHeight = 30f;
        CuteUIKit.TitleRibbon(header.transform, "고양이 주방");
        var close = CuteUIKit.CloseButton(header.transform, Close, 26f);
        close.GetComponent<LayoutElement>().ignoreLayout = true;
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
        crt.sizeDelta = new Vector2(26f, 26f);
        crt.anchoredPosition = new Vector2(-13f, 1f);

        var pill = CuteUIKit.Pill(transform, "전기 없음", CuteUIKit.ButtonCoral, CuteUIKit.CoralText, 11f, 10, new RectOffset(10, 10, 3, 3), out powerText);
        pill.name = "Power";
        powerPill = pill.GetComponent<Image>();

        CuteUIKit.SectionHeader(transform, "메뉴판", CuteUIKit.ButtonButter, CuteUIKit.TextOn(CuteUIKit.ButtonButter), "문명이 발전할수록 늘어나요");
        var gridGO = new GameObject("Menu", typeof(RectTransform));
        gridGO.transform.SetParent(transform, false);
        var grid = gridGO.AddComponent<GridLayoutGroup>();
        grid.cellSize        = new Vector2(92f, 58f);
        grid.spacing         = new Vector2(6f, 6f);
        grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 5;
        grid.childAlignment  = TextAnchor.UpperCenter;
        menuGrid = gridGO.transform;

        CuteUIKit.DottedDivider(transform);
        recipeTitle = CuteUIKit.Text(transform, "", 12f, CuteUIKit.Cocoa, FontStyles.Normal, TextAlignmentOptions.Center);

        var ingGO = new GameObject("Ingredients", typeof(RectTransform));
        ingGO.transform.SetParent(transform, false);
        var ihlg = ingGO.AddComponent<HorizontalLayoutGroup>();
        ihlg.spacing = 14f;
        ihlg.childAlignment = TextAnchor.MiddleCenter;
        ihlg.childControlWidth = ihlg.childControlHeight = true;
        ihlg.childForceExpandWidth = ihlg.childForceExpandHeight = false;
        ingredientRow = ingGO.transform;

        var btnRow = new GameObject("Buttons", typeof(RectTransform));
        btnRow.transform.SetParent(transform, false);
        var bhlg = btnRow.AddComponent<HorizontalLayoutGroup>();
        bhlg.spacing = 8f;
        bhlg.childAlignment = TextAnchor.MiddleCenter;
        bhlg.childControlWidth = bhlg.childControlHeight = true;
        bhlg.childForceExpandWidth = bhlg.childForceExpandHeight = false;
        insertBtn     = CuteUIKit.Button(btnRow.transform, "재료 넣기 (가방에서)", CuteUIKit.ButtonMint, OnInsert, 170f, 28f, 12f);
        insertBtn.gameObject.AddComponent<GamepadDefault>();
        takeInputsBtn = CuteUIKit.Button(btnRow.transform, "재료 빼기", CuteUIKit.ButtonLatte, OnTakeInputs, 100f, 28f, 12f);

        var cookRow = new GameObject("Cook", typeof(RectTransform));
        cookRow.transform.SetParent(transform, false);
        var chlg = cookRow.AddComponent<HorizontalLayoutGroup>();
        chlg.spacing = 10f;
        chlg.childAlignment = TextAnchor.MiddleCenter;
        chlg.childControlWidth = chlg.childControlHeight = true;
        chlg.childForceExpandWidth = chlg.childForceExpandHeight = false;

        var mid = new GameObject("Progress", typeof(RectTransform));
        mid.transform.SetParent(cookRow.transform, false);
        var mvlg = mid.AddComponent<VerticalLayoutGroup>();
        mvlg.spacing = 5f;
        mvlg.childAlignment = TextAnchor.MiddleCenter;
        mvlg.childControlWidth = mvlg.childControlHeight = true;
        mvlg.childForceExpandWidth = true;
        mvlg.childForceExpandHeight = false;
        var mle = mid.AddComponent<LayoutElement>();
        mle.flexibleWidth = 1f;
        mle.preferredWidth = 0f;
        progressFill = CuteUIKit.Bar(mid.transform, 14f, CuteUIKit.LatteLight, ProgressFill);
        CuteUIKit.SetBar(progressFill, 0f);
        stateText = CuteUIKit.Text(mid.transform, "", 11f, CuteUIKit.CocoaSoft);

        var outCol = new GameObject("Output", typeof(RectTransform));
        outCol.transform.SetParent(cookRow.transform, false);
        var ovlg = outCol.AddComponent<VerticalLayoutGroup>();
        ovlg.spacing = 4f;
        ovlg.childAlignment = TextAnchor.MiddleCenter;
        ovlg.childControlWidth = ovlg.childControlHeight = true;
        ovlg.childForceExpandWidth = ovlg.childForceExpandHeight = false;
        CuteUIKit.Text(outCol.transform, "다 된 요리", 10f, CuteUIKit.CocoaSoft, FontStyles.Bold);
        var oslot = CuteUIKit.Slot(outCol.transform, SlotSize + 6f, CuteUIKit.SlotBorder, CuteUIKit.SlotFill, 14);
        oslot.GetComponent<Image>().raycastTarget = false;
        var oicon = new GameObject("Icon", typeof(RectTransform));
        oicon.transform.SetParent(oslot.transform, false);
        outputIcon = oicon.AddComponent<Image>();
        outputIcon.preserveAspect = true;
        outputIcon.raycastTarget  = false;
        outputIcon.enabled = false;
        var ort = outputIcon.rectTransform;
        ort.anchorMin = new Vector2(0.15f, 0.15f);
        ort.anchorMax = new Vector2(0.85f, 0.85f);
        ort.offsetMin = ort.offsetMax = Vector2.zero;
        outputCount = CuteUIKit.Text(oslot.transform, "", 15f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.BottomRight);
        var ocrt = outputCount.rectTransform;
        ocrt.anchorMin = Vector2.zero;
        ocrt.anchorMax = Vector2.one;
        ocrt.offsetMin = new Vector2(4f, 4f);
        ocrt.offsetMax = new Vector2(-6f, -2f);
        takeOutputBtn = CuteUIKit.Button(outCol.transform, "받기", CuteUIKit.ButtonMint, OnTakeOutput, SlotSize + 18f, 24f, 11f);

        CuteUIKit.DottedDivider(transform);
        LinkRowView.Build(transform, () => kitchen, Close,
                          n => n > 0 ? $"창고 {n}개와 연결됨 — 재료를 가져오고 요리를 돌려놔요"
                                     : "창고와 이으면 재료를 알아서 가져오고 요리를 돌려놔요");
    }
}
