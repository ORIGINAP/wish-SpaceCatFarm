using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(RectTransform))]
public class GeneratorFuelUI : MonoBehaviour
{
    public static GeneratorFuelUI Instance => Services.Get<GeneratorFuelUI>();

    [SerializeField] private float animDuration    = 0.18f;
    [SerializeField] [Range(0f, 1f)]
    private float lowFuelThreshold = 0.3f;

    private static readonly Color LowFuelText = CuteUIKit.Hex(0xC9485B);

    private RectTransform   panelRT;
    private CanvasGroup     panelCG;
    private Generator       currentGen;
    private bool            isOpen = false;
    public  bool            IsOpen => isOpen;
    public  Generator       Current => currentGen;

    private TextMeshProUGUI titleText;
    private static readonly Vector2 DefaultPosition = new Vector2(0f, 60f);
    private Coroutine       animCo;

    private RectTransform   fuelFill;
    private Image           fuelFillImg;
    private TextMeshProUGUI fuelText;
    private Transform       fuelButtonContainer;
    private TextMeshProUGUI linkText;
    private int             shownLinks = -1;

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }

        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        BuildPanel();
        panelRT.localScale = Vector3.zero;
        panelCG.alpha      = 0f;
    }

    private void OnDestroy()
    {
        if (Inventory.Instance != null) Inventory.Instance.OnInventoryChanged -= QueueFuelButtons;
        Services.Unregister(this);
    }

    private static readonly float AUTO_CLOSE_DIST = 5f;
    private Transform playerTransform;

    private void Update()
    {
        if (!isOpen) return;

        if (currentGen == null) { Close(); return; }

        if (EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) { Close(); return; }

        if (playerTransform == null)
        {
            var ptc = PlayerTileController.Instance;
            if (ptc != null) playerTransform = ptc.transform;
        }

        if (playerTransform != null)
        {
            float cellSize = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
            if (currentGen.DistanceFrom(playerTransform.position) > AUTO_CLOSE_DIST * cellSize) { Close(); return; }
        }

        RefreshFuelBar();
        fuelText.text = $"연료: {currentGen.CurrentFuel:F0} / {currentGen.MaxFuel:F0}";
        if (currentGen.Links.Count != shownLinks) RefreshLinkText();
    }

    private void RefreshLinkText()
    {
        if (linkText == null || currentGen == null) return;
        shownLinks = currentGen.Links.Count;
        int storages = 0;
        foreach (var t in currentGen.Links) if (t is ItemStorage) storages++;
        linkText.text = storages > 0
            ? $"창고 {storages}개와 연결됨 — 연료가 다 떨어지면 창고의 연료로 채워요"
            : "창고와 이으면 연료가 다 떨어질 때 창고의 연료(광석 · 에너지 셀)로 채워요";
    }

    private void OnStartLink()
    {
        var gen = currentGen;
        if (gen == null) return;
        Close();
        CenterCoreUI.Instance?.Close();
        LinkModeController.Begin(gen);
    }

    private void RefreshFuelBar()
    {
        if (fuelFill == null || currentGen == null) return;

        float ratio = currentGen.FuelRatio;

        CuteUIKit.SetBar(fuelFill, ratio);

        bool low = ratio <= lowFuelThreshold;
        fuelFillImg.color = low ? CuteUIKit.Strawberry : CuteUIKit.Butter;
        fuelText.color    = low ? LowFuelText : CuteUIKit.Cocoa;
    }

    public void Open(Generator gen, Vector2? topCenter = null)
    {
        bool wasOpen = isOpen;
        currentGen = gen;
        isOpen = true;
        panelRT.pivot            = topCenter.HasValue ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = topCenter ?? DefaultPosition;
        if (titleText != null) titleText.text = gen is CenterCore ? "발전소 연료" : "발전기 연료";

        panelCG.blocksRaycasts = true;
        panelCG.interactable   = true;

        RefreshFuelBar();
        RefreshLinkText();
        if (fuelText != null)
            fuelText.text = $"연료: {currentGen.CurrentFuel:F0} / {currentGen.MaxFuel:F0}";
        RefreshFuelButtons();

        if (!wasOpen && Inventory.Instance != null) Inventory.Instance.OnInventoryChanged += QueueFuelButtons;

        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(true));

        if (!wasOpen) Sfx.Play(SfxId.UiOpen);
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        if (Inventory.Instance != null) Inventory.Instance.OnInventoryChanged -= QueueFuelButtons;
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;
        currentGen = null;
        if (animCo != null) StopCoroutine(animCo);
        animCo = StartCoroutine(Animate(false));
        Sfx.Play(SfxId.UiClose);
    }

    private IEnumerator Animate(bool open)
    {
        if (open)
        {
            CuteUIKit.WiggleEars(this, panelRT);
            yield return CuteUIKit.PopIn(panelRT, panelCG, animDuration * 1.8f);
        }
        else
        {
            yield return CuteUIKit.PopOut(panelRT, panelCG, animDuration);
            panelCG.alpha = 0f;
        }
    }

    private bool fuelButtonsQueued;
    private int  fuelButtonsSignature;

    private void QueueFuelButtons() => fuelButtonsQueued = true;

    private void LateUpdate()
    {
        if (!fuelButtonsQueued) return;
        fuelButtonsQueued = false;
        if (isOpen && Inventory.Instance != null && Inventory.Instance.Signature(IsFuel) != fuelButtonsSignature)
            RefreshFuelButtons();
    }

    private static bool IsFuel(FarmingItemData item) => item.canUseFuel;

    private void RefreshFuelButtons()
    {
        fuelButtonsSignature = Inventory.Instance != null ? Inventory.Instance.Signature(IsFuel) : 0;
        for (int i = fuelButtonContainer.childCount - 1; i >= 0; i--)
            DestroyImmediate(fuelButtonContainer.GetChild(i).gameObject);

        if (Inventory.Instance == null || currentGen == null) return;

        var shown = new HashSet<FarmingItemData>();
        bool anyFuel = false;
        for (int i = 0; i < Inventory.Instance.SlotCount; i++)
        {
            var slot = Inventory.Instance.GetSlot(i);
            if (slot == null || slot.IsEmpty) continue;
            if (!slot.Item.canUseFuel) continue;
            if (Inventory.Instance.GetCount(slot.Item) <= 0) continue;
            if (!shown.Add(slot.Item)) continue;

            anyFuel = true;
            BuildFuelButton(slot.Item);
        }

        if (!anyFuel)
        {
            var tmp = CuteUIKit.Text(fuelButtonContainer, "넣을 수 있는 연료가 없습니다.", 11f, CuteUIKit.CocoaSoft);
            tmp.name = "NoFuel";
            var le = tmp.gameObject.AddComponent<LayoutElement>();
            le.preferredHeight = 28f;
        }
    }

    private void BuildFuelButton(FarmingItemData fuelItem)
    {
        var row = CardRow(fuelButtonContainer, $"Row_{fuelItem.itemName}");
        IconSlot(row, fuelItem.icon, 30f);

        int have = Inventory.Instance.GetCount(fuelItem);
        var info = TextColumn(row);
        RowText(info, fuelItem.itemName, 12f, CuteUIKit.Cocoa, FontStyles.Bold);
        RowText(info, $"보유 {have} · 개당 +{fuelItem.fuelValue:0.#}", 10f, CuteUIKit.CocoaSoft);

        var captured = fuelItem;
        CuteUIKit.Button(row, "넣기", CuteUIKit.ButtonMint, () => AddFuel(captured), 64f, 26f, 12f);
    }

    private void AddFuel(FarmingItemData fuelItem)
    {
        if (currentGen == null || Inventory.Instance == null) return;

        int stock  = Inventory.Instance.GetCount(fuelItem);
        if (stock <= 0) { Sfx.Play(SfxId.UiError); RefreshFuelButtons(); return; }

        int needed = Mathf.CeilToInt((currentGen.MaxFuel - currentGen.CurrentFuel) / fuelItem.fuelValue);
        int use    = Mathf.Min(stock, Mathf.Max(needed, 1));

        int used = currentGen.AddFuel(fuelItem, use);
        if (used > 0)
        {
            // 들어간 만큼만 한 번에 소비
            Inventory.Instance.ConsumeItem(fuelItem, used);
            Sfx.Play(SfxId.FuelInsert);
        }
        else
        {
            NotificationUI.Instance?.Show("연료가 가득 찼어요.");
            Sfx.Play(SfxId.UiError);
        }

        RefreshFuelButtons();
    }

    private void BuildPanel()
    {
        panelRT = GetComponent<RectTransform>();
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = DefaultPosition;
        panelRT.sizeDelta        = new Vector2(400f, 0f);

        if (GetComponent<Image>() == null) gameObject.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT);

        panelCG = gameObject.AddComponent<CanvasGroup>();
        panelCG.alpha          = 0f;
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding               = new RectOffset(18, 18, 16, 18);
        vlg.spacing               = 8f;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight= false;
        vlg.childControlWidth     = true;
        vlg.childControlHeight    = true;

        var csf = gameObject.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildHeader("발전기 연료");

        BuildFuelBar();

        fuelText = CuteUIKit.Text(transform, "연료: 0 / 0", 14f, CuteUIKit.Cocoa, FontStyles.Bold);
        fuelText.name = "FuelTxt";
        var fle = fuelText.gameObject.AddComponent<LayoutElement>();
        fle.preferredHeight = 20f;

        CuteUIKit.DottedDivider(transform);

        CuteUIKit.SectionHeader(transform, "보유 연료", CuteUIKit.ButtonButter,
                                CuteUIKit.TextOn(CuteUIKit.ButtonButter), "가득 찰 만큼 넣어요");

        var containerGO = new GameObject("FuelButtons");
        containerGO.transform.SetParent(transform, false);

        var cvlg = containerGO.AddComponent<VerticalLayoutGroup>();
        cvlg.spacing               = 4f;
        cvlg.childForceExpandWidth = true;
        cvlg.childForceExpandHeight= false;
        cvlg.childControlWidth     = true;
        cvlg.childControlHeight    = true;

        var ccsf = containerGO.AddComponent<ContentSizeFitter>();
        ccsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        fuelButtonContainer = containerGO.transform;

        CuteUIKit.DottedDivider(transform);
        var linkRow = CardRow(transform, "LinkRow");
        linkText = CuteUIKit.Text(linkRow, "", 10f, CuteUIKit.Cocoa, align: TextAlignmentOptions.MidlineLeft);
        linkText.textWrappingMode = TextWrappingModes.Normal;
        var lle = linkText.gameObject.AddComponent<LayoutElement>();
        lle.flexibleWidth  = 1f;
        lle.preferredWidth = 0f;
        CuteUIKit.Button(linkRow, "연결하기", CuteUIKit.ButtonSky, OnStartLink, 80f, 26f, 11f);
    }

    private void BuildFuelBar()
    {
        fuelFill    = CuteUIKit.Bar(transform, 20f, CuteUIKit.LatteLight, CuteUIKit.Butter);
        fuelFillImg = fuelFill.GetComponent<Image>();
        fuelFill.parent.name = "FuelBar";
        CuteUIKit.SetBar(fuelFill, 0f);
    }

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
        go.AddComponent<LayoutElement>().minHeight = 30f;

        titleText = CuteUIKit.TitleRibbon(go.transform, title).GetComponentInChildren<TextMeshProUGUI>();

        var close = CuteUIKit.CloseButton(go.transform, Close, 26f);
        close.GetComponent<LayoutElement>().ignoreLayout = true;
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
        crt.sizeDelta        = new Vector2(26f, 26f);
        crt.anchoredPosition = new Vector2(-13f, 1f);
    }

    private static Transform CardRow(Transform parent, string name)
    {
        var card = CuteUIKit.Card(parent, CuteUIKit.LatteLight, 12, name);
        card.raycastTarget = false;

        var hlg = card.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(4, 6, 4, 4);
        hlg.spacing                = 8f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        return card.transform;
    }

    private static void IconSlot(Transform parent, Sprite sprite, float size)
    {
        var slot = CuteUIKit.Slot(parent, size, CuteUIKit.SlotBorder, CuteUIKit.SlotFill, 10);
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

    private static TextMeshProUGUI RowText(Transform parent, string text, float size, Color color,
                                           FontStyles style = FontStyles.Normal)
    {
        var t = CuteUIKit.Text(parent, text, size, color, style, TextAlignmentOptions.MidlineLeft);
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }
}
