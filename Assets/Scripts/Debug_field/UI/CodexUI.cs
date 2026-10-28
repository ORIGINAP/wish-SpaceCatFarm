using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(RectTransform))]
public class CodexUI : MonoBehaviour
{
    public static CodexUI Instance => Services.Get<CodexUI>();
    public static bool IsOpen => Instance != null && Instance.isOpen;

    private const float CellW = 74f, CellH = 78f;

    private bool          isOpen;
    private RectTransform panelRT;
    private CanvasGroup   panelCG;
    private Coroutine     animCo;
    private Transform     body;
    private TextMeshProUGUI summary;
    private Button        closeBtn;
    private bool          dirty;

    public static void Toggle()
    {
        if (Instance == null) Create();
        if (Instance == null) return;
        if (Instance.isOpen) Instance.Close();
        else Instance.Open();
    }

    private static void Create()
    {
        Transform parent = GeneratorFuelUI.Instance != null ? GeneratorFuelUI.Instance.transform.parent : null;
        var go = new GameObject("CodexUI", typeof(RectTransform));
        if (parent != null) go.transform.SetParent(parent, false);
        else UIStyleKit.CreateOverlayCanvas(go, 300);
        go.AddComponent<CodexUI>();
    }

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }
        BuildPanel();
        panelRT.localScale = Vector3.zero;
        Codex.OnChanged += MarkDirty;
    }

    private void OnDestroy()
    {
        Codex.OnChanged -= MarkDirty;
        Services.Unregister(this);
    }

    private void MarkDirty() => dirty = true;

    public void Open()
    {
        if (isOpen) return;
        if (ShuttleUI.IsOpen) ShuttleUI.Instance.Close();
        isOpen = true;
        panelCG.blocksRaycasts = true;
        panelCG.interactable   = true;
        Rebuild();
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(true));
        Sfx.Play(SfxId.UiOpen);
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;
        UIStyleKit.ClearSelectionIn(transform);
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(false));
        Sfx.Play(SfxId.UiClose);
    }

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
        if (EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) { Close(); return; }
        if (closeBtn != null) UIStyleKit.KeepNavigable(closeBtn.gameObject, transform);
    }

    private void LateUpdate()
    {
        if (!dirty) return;
        dirty = false;
        if (isOpen) Rebuild();
    }

    private void Rebuild()
    {
        for (int i = body.childCount - 1; i >= 0; i--) DestroyImmediate(body.GetChild(i).gameObject);

        var crops  = Crops();
        var dishes = new List<FarmingItemData>();
        foreach (var r in CatKitchen.Menu()) if (r.outputItem != null && !dishes.Contains(r.outputItem)) dishes.Add(r.outputItem);

        int cropN = 0, goldenN = 0, dishN = 0;
        foreach (var c in crops) { if (HarvestLog.Contains(c)) cropN++; if (Codex.HasGolden(c.goldenVariant)) goldenN++; }
        foreach (var d in dishes) if (Codex.HasDish(d)) dishN++;
        int total = crops.Count * 2 + dishes.Count;

        string next = "모든 보상을 받았어요!";
        if (Codex.MilestonesPaid < Codex.Milestones.Length)
        {
            var (count, coins) = Codex.Milestones[Codex.MilestonesPaid];
            next = $"다음 보상: {count}종 → {CurrencyManager.Format(coins)}코인";
        }
        summary.text = $"모은 수 <b>{Codex.Total}/{total}</b>   <color=#A8741A>{next}</color>";

        Section($"작물  {cropN}/{crops.Count}", CuteUIKit.MintTag, CuteUIKit.MintText, "수확해 보면 기록돼요 · 칸 아래는 작물 성향");
        var grid = Grid();
        var traits = Traits();
        foreach (var c in crops) Cell(grid, c, HarvestLog.Contains(c), traits.TryGetValue(c, out var t) ? t : "");

        Section($"황금 작물  {goldenN}/{crops.Count}", CuteUIKit.ButterSoft, CuteUIKit.AmberText, "아주 드물게 함께 열려요 · 퇴비 · 오로라 때 잘 나와요");
        grid = Grid();
        foreach (var c in crops) if (c.goldenVariant != null) Cell(grid, c.goldenVariant, Codex.HasGolden(c.goldenVariant), "");

        Section($"요리  {dishN}/{dishes.Count}", CuteUIKit.PinkSoft, CuteUIKit.CoralText, "고양이 주방에서 처음 만들면 기록돼요");
        grid = Grid();
        foreach (var d in dishes) Cell(grid, d, Codex.HasDish(d), "");
    }

    private static List<FarmingItemData> Crops()
    {
        var list = new List<(int level, FarmingItemData crop)>();
        foreach (var item in GameDataRegistry.FindAll<FarmingItemData>())
        {
            if (item == null || item.itemType != FarmingItemType.Seed || item.seedData?.drops == null) continue;
            foreach (var d in item.seedData.drops)
                if (d.item != null && d.item.isCropProduct && !d.item.isGolden && !list.Exists(x => x.crop == d.item))
                    list.Add((item.unlockCoreLevel, d.item));
        }
        list.Sort((a, b) => a.level != b.level ? a.level.CompareTo(b.level) : a.crop.basePrice.CompareTo(b.crop.basePrice));
        return list.ConvertAll(x => x.crop);
    }

    private static Dictionary<FarmingItemData, string> Traits()
    {
        var map = new Dictionary<FarmingItemData, string>();
        foreach (var item in GameDataRegistry.FindAll<FarmingItemData>())
            if (item != null && item.seedData?.drops != null)
                foreach (var d in item.seedData.drops)
                    if (d.item != null && d.item.isCropProduct && !map.ContainsKey(d.item)) map[d.item] = CropTraits.Name(item.seedData.trait);
        return map;
    }

    private void Section(string label, Color tag, Color text, string note)
        => CuteUIKit.SectionHeader(body, label, tag, text, note);

    private Transform Grid()
    {
        var go = new GameObject("Grid", typeof(RectTransform));
        go.transform.SetParent(body, false);
        var grid = go.AddComponent<GridLayoutGroup>();
        grid.cellSize        = new Vector2(CellW, CellH);
        grid.spacing         = new Vector2(5f, 5f);
        grid.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 8;
        grid.childAlignment  = TextAnchor.UpperLeft;
        return go.transform;
    }

    private static void Cell(Transform grid, FarmingItemData item, bool found, string note)
    {
        var cell = new GameObject(item.name, typeof(RectTransform));
        cell.transform.SetParent(grid, false);
        var vlg = cell.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 1f;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = vlg.childForceExpandHeight = false;

        var slot = CuteUIKit.Slot(cell.transform, 46f,
            found ? CuteUIKit.TierBorder(item, CuteUIKit.SlotBorder) : CuteUIKit.SlotBorder,
            found ? CuteUIKit.TierFill(item, CuteUIKit.SlotFill) : CuteUIKit.EmptiedFill, 12);
        slot.GetComponent<Image>().raycastTarget = false;
        var iconGO = new GameObject("Icon", typeof(RectTransform));
        iconGO.transform.SetParent(slot.transform, false);
        var icon = iconGO.AddComponent<Image>();
        icon.sprite = item.icon;
        icon.preserveAspect = true;
        icon.raycastTarget  = false;
        icon.color = found ? Color.white : new Color(0.25f, 0.18f, 0.15f, 0.35f);
        var irt = icon.rectTransform;
        irt.anchorMin = new Vector2(0.14f, 0.14f);
        irt.anchorMax = new Vector2(0.86f, 0.86f);
        irt.offsetMin = irt.offsetMax = Vector2.zero;

        var name = CuteUIKit.Text(cell.transform, found ? item.itemName : "???", 9.5f, found ? CuteUIKit.Cocoa : CuteUIKit.CocoaMuted, FontStyles.Bold);
        name.overflowMode = TextOverflowModes.Ellipsis;
        if (!string.IsNullOrEmpty(note))
        {
            var t = CuteUIKit.Text(cell.transform, note, 8.5f, CuteUIKit.LavenderText);
            t.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    private void BuildPanel()
    {
        panelRT = (RectTransform)transform;
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = new Vector2(0f, 10f);
        panelRT.sizeDelta        = new Vector2(CellW * 8 + 5f * 7 + 40f, 0f);
        if (GetComponent<Image>() == null) gameObject.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT);

        panelCG = gameObject.AddComponent<CanvasGroup>();
        panelCG.alpha = 0f;
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(18, 18, 16, 18);
        vlg.spacing = 6f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = vlg.childControlHeight = true;
        gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(transform, false);
        var hlg = header.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
        header.AddComponent<LayoutElement>().minHeight = 30f;
        CuteUIKit.TitleRibbon(header.transform, "농사 도감");
        closeBtn = CuteUIKit.CloseButton(header.transform, Close, 26f);
        closeBtn.GetComponent<LayoutElement>().ignoreLayout = true;
        var crt = (RectTransform)closeBtn.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
        crt.sizeDelta = new Vector2(26f, 26f);
        crt.anchoredPosition = new Vector2(-13f, 1f);

        summary = CuteUIKit.Text(transform, "", 12f, CuteUIKit.Cocoa, FontStyles.Normal, TextAlignmentOptions.Center);
        CuteUIKit.DottedDivider(transform);

        var bodyGO = new GameObject("Body", typeof(RectTransform));
        bodyGO.transform.SetParent(transform, false);
        var bvlg = bodyGO.AddComponent<VerticalLayoutGroup>();
        bvlg.spacing = 5f;
        bvlg.childForceExpandWidth = true;
        bvlg.childForceExpandHeight = false;
        bvlg.childControlWidth = bvlg.childControlHeight = true;
        body = bodyGO.transform;
    }
}
