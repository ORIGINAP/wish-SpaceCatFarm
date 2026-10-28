using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(RectTransform))]
public class ComposterUI : MonoBehaviour
{
    public static ComposterUI Instance => Services.Get<ComposterUI>();
    public static bool IsOpen => Instance != null && Instance.isOpen;

    private const float AutoCloseDist = 5f;
    private static readonly Color PointsFill  = CuteUIKit.Hex(0xB98A5E);
    private static readonly Color ProgressFill = CuteUIKit.Hex(0x8BC97A);

    private Composter     bin;
    private bool          isOpen;
    private RectTransform panelRT;
    private CanvasGroup   panelCG;
    private Coroutine     animCo;

    private RectTransform   pointsFill, progressFill;
    private TextMeshProUGUI pointsText, stateText, outputCount;
    private Image           outputIcon;
    private Button          takeBtn;
    private Transform       rows;
    private bool            rowsQueued;
    private int             rowsSignature;

    public static void Open(Composter target)
    {
        if (target == null) return;
        if (Instance == null) Create();
        if (Instance != null) Instance.Show(target);
    }

    private static void Create()
    {
        Transform parent = GeneratorFuelUI.Instance != null ? GeneratorFuelUI.Instance.transform.parent : null;
        var go = new GameObject("ComposterUI", typeof(RectTransform));
        if (parent != null) go.transform.SetParent(parent, false);
        else UIStyleKit.CreateOverlayCanvas(go, 300);
        go.AddComponent<ComposterUI>();
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

    private void Show(Composter target)
    {
        bool wasOpen = isOpen;
        Unsubscribe();
        bin    = target;
        isOpen = true;
        bin.OnChanged += Refresh;
        if (Inventory.Instance != null) Inventory.Instance.OnInventoryChanged += QueueRows;
        panelCG.blocksRaycasts = true;
        panelCG.interactable   = true;
        Refresh();
        RebuildRows();
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(true));
        if (!wasOpen) Sfx.Play(SfxId.UiOpen);
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        Unsubscribe();
        bin = null;
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;
        UIStyleKit.ClearSelectionIn(transform);
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(false));
        Sfx.Play(SfxId.UiClose);
    }

    private void Unsubscribe()
    {
        if (!(bin is null)) bin.OnChanged -= Refresh;
        if (Inventory.Instance != null) Inventory.Instance.OnInventoryChanged -= QueueRows;
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
        if (bin == null) { Close(); return; }
        if (EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) { Close(); return; }
        var player = PlayerTileController.Instance;
        float cs = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
        if (player != null && bin.DistanceFrom(player.transform.position) > AutoCloseDist * cs) { Close(); return; }
        CuteUIKit.SetBar(progressFill, bin.IsWorking ? bin.Progress01 : 0f);
        if (takeBtn != null) UIStyleKit.KeepNavigable(takeBtn.gameObject, transform);
    }

    private void QueueRows() => rowsQueued = true;

    private void LateUpdate()
    {
        if (!rowsQueued) return;
        rowsQueued = false;
        if (isOpen && Inventory.Instance != null && Inventory.Instance.Signature(IsCompostable) != rowsSignature)
            RebuildRows();
    }

    private static bool IsCompostable(FarmingItemData item) => Composter.PointsOf(item) > 0;

    private void Refresh()
    {
        if (bin == null) return;
        CuteUIKit.SetBar(pointsFill, bin.Points / (float)Mathf.Max(1, bin.PointCapacity));
        pointsText.text = $"모인 점수 {bin.Points}/{bin.PointCapacity}  <color=#A88E82>(퇴비 1개 = {bin.PointsPerCompost}점 · 씨앗 1점 · 작물 2~12점)</color>";
        stateText.text  = bin.IsWorking ? $"퇴비가 되는 중 · {bin.SecondsPerCompost:0.#}초마다 1개"
                        : bin.OutputCount >= bin.OutputCapacity ? "퇴비 칸이 가득 찼어요 — 받아 주세요"
                        : "씨앗 · 작물을 넣어 주세요";
        stateText.color = bin.IsWorking ? CuteUIKit.MintText : CuteUIKit.CocoaSoft;

        outputIcon.sprite  = bin.CompostItem != null ? bin.CompostItem.icon : null;
        outputIcon.enabled = outputIcon.sprite != null;
        outputCount.text   = bin.OutputCount > 0 ? bin.OutputCount.ToString() : "";
        takeBtn.interactable = bin.OutputCount > 0;
    }

    private void RebuildRows()
    {
        rowsSignature = Inventory.Instance != null ? Inventory.Instance.Signature(IsCompostable) : 0;
        for (int i = rows.childCount - 1; i >= 0; i--) DestroyImmediate(rows.GetChild(i).gameObject);
        var inv = Inventory.Instance;
        if (inv == null || bin == null) return;

        var shown = new HashSet<FarmingItemData>();
        for (int i = 0; i < inv.SlotCount; i++)
        {
            var slot = inv.GetSlot(i);
            if (slot == null || slot.IsEmpty || !shown.Add(slot.Item)) continue;
            int p = Composter.PointsOf(slot.Item);
            if (p <= 0) continue;
            BuildRow(slot.Item, p, inv.GetCount(slot.Item));
            if (rows.childCount >= 6) break;
        }
        if (rows.childCount == 0)
        {
            var t = CuteUIKit.Text(rows, "넣을 씨앗 · 작물이 가방에 없어요", 11f, CuteUIKit.CocoaSoft);
            t.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f;
        }
    }

    private void BuildRow(FarmingItemData item, int points, int have)
    {
        var card = CuteUIKit.Card(rows, CuteUIKit.LatteLight, 12, $"Row_{item.itemName}");
        card.raycastTarget = false;
        var hlg = card.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(6, 6, 3, 3);
        hlg.spacing = 6f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;

        var iconGO = new GameObject("Icon", typeof(RectTransform));
        iconGO.transform.SetParent(card.transform, false);
        var icon = iconGO.AddComponent<Image>();
        icon.sprite = item.icon;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        var ile = iconGO.AddComponent<LayoutElement>();
        ile.preferredWidth = ile.preferredHeight = 26f;

        var t = CuteUIKit.Text(card.transform, $"{item.itemName}  <color=#A88E82>보유 {have} · 1개 {points}점</color>", 11f, CuteUIKit.Cocoa,
                               FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        var tle = t.gameObject.AddComponent<LayoutElement>();
        tle.flexibleWidth = 1f;
        tle.preferredWidth = 0f;

        var captured = item;
        CuteUIKit.Button(card.transform, "1",    CuteUIKit.ButtonMint, () => Insert(captured, 1),  32f, 24f, 11f);
        CuteUIKit.Button(card.transform, "10",   CuteUIKit.ButtonMint, () => Insert(captured, 10), 36f, 24f, 11f);
        CuteUIKit.Button(card.transform, "전부", CuteUIKit.ButtonMint, () => Insert(captured, -1), 44f, 24f, 11f);
    }

    private void Insert(FarmingItemData item, int want)
    {
        var inv = Inventory.Instance;
        if (bin == null || inv == null) return;
        int n = Mathf.Min(want < 0 ? inv.GetCount(item) : want, inv.GetCount(item), bin.RoomFor(item));
        if (n <= 0 || !inv.ConsumeItem(item, n))
        {
            Sfx.Play(SfxId.UiError);
            NotificationUI.Instance?.Show("퇴비통이 가득 찼어요 — 퇴비가 될 때까지 기다려 주세요");
            return;
        }
        int put = bin.Insert(item, n);
        if (put < n) inv.AddItem(item, n - put);
        Sfx.Play(SfxId.StoreItem);
    }

    private void OnTake()
    {
        var inv = Inventory.Instance;
        if (bin == null || inv == null || bin.CompostItem == null) return;
        if (!inv.CanAddItem(bin.CompostItem)) { Sfx.Play(SfxId.UiError); return; }
        int n = bin.TakeOutput(bin.OutputCount);
        if (n > 0) inv.AddItem(bin.CompostItem, n);
        Sfx.Play(SfxId.Pickup);
    }

    private void BuildPanel()
    {
        panelRT = (RectTransform)transform;
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = new Vector2(0f, 40f);
        panelRT.sizeDelta        = new Vector2(440f, 0f);
        if (GetComponent<Image>() == null) gameObject.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT);

        panelCG = gameObject.AddComponent<CanvasGroup>();
        panelCG.alpha = 0f;
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(18, 18, 16, 18);
        vlg.spacing = 7f;
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
        CuteUIKit.TitleRibbon(header.transform, "퇴비통");
        var close = CuteUIKit.CloseButton(header.transform, Close, 26f);
        close.GetComponent<LayoutElement>().ignoreLayout = true;
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
        crt.sizeDelta = new Vector2(26f, 26f);
        crt.anchoredPosition = new Vector2(-13f, 1f);

        var info = CuteUIKit.Text(transform, "퇴비를 들고 밭을 클릭하면 수확 3번 동안 작물이 1개씩 더 열리고 황금 작물 확률이 2배가 돼요. 자동파종기는 이어 준 창고의 퇴비를 알아서 뿌려요.",
                                  11f, CuteUIKit.CocoaSoft, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        info.textWrappingMode = TextWrappingModes.Normal;

        pointsText = CuteUIKit.Text(transform, "", 11f, CuteUIKit.Cocoa, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        pointsFill = CuteUIKit.Bar(transform, 12f, CuteUIKit.LatteLight, PointsFill);

        var row = new GameObject("Make", typeof(RectTransform));
        row.transform.SetParent(transform, false);
        var rhlg = row.AddComponent<HorizontalLayoutGroup>();
        rhlg.spacing = 10f;
        rhlg.childAlignment = TextAnchor.MiddleCenter;
        rhlg.childControlWidth = rhlg.childControlHeight = true;
        rhlg.childForceExpandWidth = rhlg.childForceExpandHeight = false;

        var mid = new GameObject("Progress", typeof(RectTransform));
        mid.transform.SetParent(row.transform, false);
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

        var col = new GameObject("Output", typeof(RectTransform));
        col.transform.SetParent(row.transform, false);
        var cvlg = col.AddComponent<VerticalLayoutGroup>();
        cvlg.spacing = 4f;
        cvlg.childAlignment = TextAnchor.MiddleCenter;
        cvlg.childControlWidth = cvlg.childControlHeight = true;
        cvlg.childForceExpandWidth = cvlg.childForceExpandHeight = false;
        CuteUIKit.Text(col.transform, "퇴비", 10f, CuteUIKit.CocoaSoft, FontStyles.Bold);
        var slot = CuteUIKit.Slot(col.transform, 56f, CuteUIKit.SlotBorder, CuteUIKit.SlotFill, 14);
        slot.GetComponent<Image>().raycastTarget = false;
        var iconGO = new GameObject("Icon", typeof(RectTransform));
        iconGO.transform.SetParent(slot.transform, false);
        outputIcon = iconGO.AddComponent<Image>();
        outputIcon.preserveAspect = true;
        outputIcon.raycastTarget  = false;
        var irt = outputIcon.rectTransform;
        irt.anchorMin = new Vector2(0.15f, 0.15f);
        irt.anchorMax = new Vector2(0.85f, 0.85f);
        irt.offsetMin = irt.offsetMax = Vector2.zero;
        outputCount = CuteUIKit.Text(slot.transform, "", 15f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.BottomRight);
        var ocrt = outputCount.rectTransform;
        ocrt.anchorMin = Vector2.zero;
        ocrt.anchorMax = Vector2.one;
        ocrt.offsetMin = new Vector2(4f, 4f);
        ocrt.offsetMax = new Vector2(-6f, -2f);
        takeBtn = CuteUIKit.Button(col.transform, "받기", CuteUIKit.ButtonMint, OnTake, 70f, 24f, 11f);

        CuteUIKit.DottedDivider(transform);
        CuteUIKit.SectionHeader(transform, "넣기", CuteUIKit.ButtonButter, CuteUIKit.TextOn(CuteUIKit.ButtonButter), "가방의 씨앗 · 작물");
        var rowsGO = new GameObject("Rows", typeof(RectTransform));
        rowsGO.transform.SetParent(transform, false);
        var rvlg = rowsGO.AddComponent<VerticalLayoutGroup>();
        rvlg.spacing = 4f;
        rvlg.childForceExpandWidth = true;
        rvlg.childForceExpandHeight = false;
        rvlg.childControlWidth = rvlg.childControlHeight = true;
        rows = rowsGO.transform;

        CuteUIKit.DottedDivider(transform);
        LinkRowView.Build(transform, () => bin, Close,
                          n => n > 0 ? $"창고 {n}개와 연결됨 — 종류마다 20개 넘는 씨앗만 가져와 퇴비로, 퇴비는 창고로"
                                     : "창고와 이으면 넘치는 씨앗을 알아서 퇴비로 만들어요");
    }
}
