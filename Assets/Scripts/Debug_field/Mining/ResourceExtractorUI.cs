using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(RectTransform))]
public class ResourceExtractorUI : MonoBehaviour
{
    public static ResourceExtractorUI Instance => Services.Get<ResourceExtractorUI>();

    [SerializeField] private float animDuration = 0.18f;

    private static readonly Color SocketEquippedFill = CuteUIKit.Hex(0xEDE5FF);
    private static readonly Color SocketEmptyPaw     = CuteUIKit.Hex(0xCFC0F4, 0.8f);

    private RectTransform    panelRT;
    private CanvasGroup      panelCG;
    private ResourceExtractor currentExtractor;
    private bool              isOpen;
    public  bool               IsOpen => isOpen;
    private Coroutine          animCo;

    private TextMeshProUGUI equippedLabel;
    private Transform       equippedRow;
    private Transform       insertContainer;

    private Transform  playerTransform;
    private const float AUTO_CLOSE_DIST = 5f;

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
        if (Inventory.Instance != null)
            Inventory.Instance.OnInventoryChanged -= QueueInsertList;
        Services.Unregister(this);
    }

    private void Update()
    {
        if (!isOpen) return;

        if (currentExtractor == null) { Close(); return; }

        if (EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) { Close(); return; }

        if (playerTransform == null)
        {
            var ptc = PlayerTileController.Instance;
            if (ptc != null) playerTransform = ptc.transform;
        }

        if (playerTransform != null)
        {
            float cellSize = GridManager.Instance != null ? GridManager.Instance.CellSize : 1f;
            float dist = Vector2.Distance(playerTransform.position, currentExtractor.transform.position);
            if (dist > AUTO_CLOSE_DIST * cellSize) { Close(); return; }
        }
    }

    public void Open(ResourceExtractor extractor)
    {
        bool wasOpen = isOpen;
        currentExtractor = extractor;
        isOpen = true;

        panelCG.blocksRaycasts = true;
        panelCG.interactable   = true;

        if (Inventory.Instance != null)
        {
            Inventory.Instance.OnInventoryChanged -= QueueInsertList;
            Inventory.Instance.OnInventoryChanged += QueueInsertList;
        }

        RefreshAll();

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
        if (Inventory.Instance != null)
            Inventory.Instance.OnInventoryChanged -= QueueInsertList;
        currentExtractor = null;
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

    private void RefreshAll()
    {
        RefreshEquippedRow();
        RefreshInsertList();
    }

    private void RefreshEquippedRow()
    {
        for (int i = equippedRow.childCount - 1; i >= 0; i--)
            DestroyImmediate(equippedRow.GetChild(i).gameObject);

        if (currentExtractor == null) return;

        var drill = currentExtractor.EquippedDrill;
        BuildDrillSocket(drill);
        if (drill == null)
        {
            RowText(equippedRow, $"장착된 드릴 없음 (현재 등급 {currentExtractor.EffectiveToolGrade})", 13f, CuteUIKit.CocoaSoft);
            return;
        }

        RowText(equippedRow, $"{drill.itemName}  (등급 {drill.toolGrade})", 13f, CuteUIKit.Cocoa, FontStyles.Bold);

        UIStyleKit.CreateButton(equippedRow, "해제", UIStyleKit.ButtonDanger, OnClickUnequip,
            preferredWidth: 60f, preferredHeight: 28f);
    }

    private void BuildDrillSocket(FarmingItemData drill)
    {
        var slot = CuteUIKit.Slot(equippedRow, 44f, CuteUIKit.EquipBorder,
                                  drill != null ? SocketEquippedFill : CuteUIKit.EquipFill);
        slot.name = "DrillSocket";
        slot.GetComponent<Image>().raycastTarget = false;

        if (drill == null)
        {
            var hint = CuteUIKit.Layer(slot.transform, "EmptyHint", CuteUIKit.Paw(), SocketEmptyPaw).rectTransform;
            hint.anchorMin = new Vector2(0.27f, 0.27f);
            hint.anchorMax = new Vector2(0.73f, 0.73f);
            hint.offsetMin = hint.offsetMax = Vector2.zero;
            return;
        }

        AddIcon(slot.transform, drill.icon);
    }

    private bool insertListQueued;
    private int  insertListSignature;

    private void QueueInsertList() => insertListQueued = true;

    private void LateUpdate()
    {
        if (!insertListQueued) return;
        insertListQueued = false;
        if (isOpen && Inventory.Instance != null && Inventory.Instance.Signature(IsDrill) != insertListSignature)
            RefreshInsertList();
    }

    private static bool IsDrill(FarmingItemData item) => item.IsDrill;

    private void RefreshInsertList()
    {
        insertListSignature = Inventory.Instance != null ? Inventory.Instance.Signature(IsDrill) : 0;
        for (int i = insertContainer.childCount - 1; i >= 0; i--)
            DestroyImmediate(insertContainer.GetChild(i).gameObject);

        if (currentExtractor == null || Inventory.Instance == null) return;

        var shown = new HashSet<FarmingItemData>();
        bool any = false;

        for (int i = 0; i < Inventory.Instance.SlotCount; i++)
        {
            var slot = Inventory.Instance.GetSlot(i);
            if (slot == null || slot.IsEmpty) continue;
            if (!slot.Item.IsDrill) continue;
            if (!shown.Add(slot.Item)) continue;

            any = true;
            BuildEquipRow(slot.Item);
        }

        if (!any)
        {
            var empty = CuteUIKit.Text(insertContainer, "장착 가능한 드릴이 없음", 12f, CuteUIKit.CocoaSoft);
            empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 24f;
        }
    }

    private void BuildEquipRow(FarmingItemData drill)
    {
        var row = CardRow(insertContainer, "Row");

        var slot = CuteUIKit.Slot(row, 28f, CuteUIKit.TierBorder(drill, CuteUIKit.SlotBorder), CuteUIKit.TierFill(drill, CuteUIKit.SlotFill), 10);
        slot.GetComponent<Image>().raycastTarget = false;
        AddIcon(slot.transform, drill.icon);

        int have = Inventory.Instance.GetCount(drill);
        RowText(row, $"{drill.itemName}  (등급 {drill.toolGrade}, 보유 {have})", 12f, CuteUIKit.Cocoa);

        UIStyleKit.CreateButton(row, "장착", UIStyleKit.ButtonConfirm, () => OnClickEquip(drill),
            preferredWidth: 60f, preferredHeight: 26f);
    }

    private void OnClickUnequip()
    {
        if (currentExtractor == null || Inventory.Instance == null) return;

        var drill = currentExtractor.EquippedDrill;
        if (drill == null) return;

        if (!Inventory.Instance.CanAddItem(drill))
        {
            NotificationUI.Instance?.Show("인벤토리가 가득 찼습니다.");
            Sfx.Play(SfxId.UiError);
            return;
        }

        var removed = currentExtractor.Unequip();
        if (removed != null) Inventory.Instance.AddItemOrDrop(removed, 1, DropPosition());
        Sfx.Play(SfxId.Equip);
        RefreshAll();
    }

    private void OnClickEquip(FarmingItemData drill)
    {
        if (currentExtractor == null || Inventory.Instance == null) return;

        if (!Inventory.Instance.ConsumeItem(drill, 1))
        {
            Sfx.Play(SfxId.UiError);
            RefreshAll();
            return;
        }

        var prev = currentExtractor.EquippedDrill;
        if (!currentExtractor.TryEquip(drill))
        {
            Inventory.Instance.AddItemOrDrop(drill, 1, DropPosition());
            Sfx.Play(SfxId.UiError);
            RefreshAll();
            return;
        }

        if (prev != null) Inventory.Instance.AddItemOrDrop(prev, 1, DropPosition());
        Sfx.Play(SfxId.Equip);
        RefreshAll();
    }

    private Vector3 DropPosition()
        => PlayerTileController.Instance != null ? PlayerTileController.Instance.transform.position
                                                  : currentExtractor.transform.position;

    private void BuildPanel()
    {
        panelRT = GetComponent<RectTransform>();
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = new Vector2(0f, 60f);

        panelCG = UIStyleKit.CreatePanelRoot(gameObject, 380f);

        BuildHeader("자동채굴기 장비");

        CuteUIKit.SectionHeader(transform, "장착 중", CuteUIKit.LavenderTag, CuteUIKit.LavenderText);
        equippedRow = CardRow(transform, "EquippedRow");

        UIStyleKit.CreateDivider(transform);

        CuteUIKit.SectionHeader(transform, "장착 가능한 드릴", CuteUIKit.MintTag, CuteUIKit.MintText);
        var listGO = new GameObject("InsertList", typeof(RectTransform));
        listGO.transform.SetParent(transform, false);
        var lvlg = listGO.AddComponent<VerticalLayoutGroup>();
        lvlg.spacing = 4f; lvlg.childForceExpandWidth = true; lvlg.childControlWidth = true; lvlg.childControlHeight = true;
        listGO.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        insertContainer = listGO.transform;

        UIStyleKit.CreateDivider(transform);
        LinkRowView.Build(transform, () => currentExtractor, Close,
                          n => n > 0 ? $"창고 {n}개와 연결됨 — 캔 자원을 창고에 넣어요"
                                     : "창고와 이으면 캔 자원을 창고에 넣어요 (안 이으면 가방으로)");
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

        CuteUIKit.TitleRibbon(go.transform, title);

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

    private static void AddIcon(Transform slot, Sprite sprite)
    {
        var go = new GameObject("Icon", typeof(RectTransform));
        go.transform.SetParent(slot, false);
        var img = go.AddComponent<Image>();
        img.sprite         = sprite;
        img.preserveAspect = true;
        img.raycastTarget  = false;
        img.enabled        = sprite != null;
        var rt = img.rectTransform;
        rt.anchorMin = new Vector2(0.16f, 0.16f);
        rt.anchorMax = new Vector2(0.84f, 0.84f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private static TextMeshProUGUI RowText(Transform parent, string text, float size, Color color,
                                           FontStyles style = FontStyles.Normal)
    {
        var t = CuteUIKit.Text(parent, text, size, color, style, TextAlignmentOptions.MidlineLeft);
        t.overflowMode = TextOverflowModes.Ellipsis;
        var le = t.gameObject.AddComponent<LayoutElement>();
        le.flexibleWidth  = 1f;
        le.preferredWidth = 0f;
        return t;
    }
}
