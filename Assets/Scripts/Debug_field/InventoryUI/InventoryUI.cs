using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

[RequireComponent(typeof(RectTransform))]
public class InventoryUI : MonoBehaviour
{
    public static InventoryUI Instance => Services.Get<InventoryUI>();

    [Header("조작")]
    [SerializeField] private KeyCode toggleKey    = KeyCode.Tab;

    [Header("레이아웃")]
    [SerializeField] private int   hotbarCount    = 7;
    [SerializeField] private float slotSize       = 80f;
    [SerializeField] private float slotSpacing    = 7f;

    [Header("애니메이션")]
    [SerializeField] private float animDuration   = 0.18f;
    [SerializeField] private float targetScale    = 1.3f;


    private Canvas           rootCanvas;
    private RectTransform    panelRT;
    private CanvasGroup      panelCG;
    private Image            dragIconImage;
    private InventorySlotUI[]slotUIs;
    private bool             isOpen           = false;
    public  bool             IsOpen           => isOpen;
    private Coroutine        animCoroutine;

    // 휴지통용 가짜 슬롯 번호
    public const int TrashIndex = -2;
    private const int NoDrag    = -1;

    private int  _dragSourceIndex = NoDrag;
    private bool _dragConfirmed   = false;
    private TrashSlotUI trashSlot;

    public bool IsDragging       => _dragSourceIndex != NoDrag;
    public int  DragSourceIndex  => _dragSourceIndex;

    private float ContentWidth => hotbarCount * slotSize + (hotbarCount - 1) * slotSpacing;

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }

        rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null) rootCanvas = FindFirstObjectByType<Canvas>();

        BuildPanel();
        BuildDragIcon();
        BuildSlots();

        // SetActive 대신 scale 0으로 숨김
        panelRT.localScale = Vector3.zero;
        panelCG.alpha      = 0f;
    }

    private void Start()
    {
        ItemTooltipUI.Create(rootCanvas);

        if (Inventory.Instance != null)
            Inventory.Instance.OnInventoryChanged += RefreshAll;
    }

    private void OnDestroy()
    {
        Services.Unregister(this);
        if (Inventory.Instance != null)
            Inventory.Instance.OnInventoryChanged -= RefreshAll;
    }

    private void Update()
    {
        if (TextInputFocus.Active) return;
        if (GameInput.GetKeyDown(toggleKey) || (InputManager.Instance != null && InputManager.Instance.InventoryButton)) Toggle();
        if (isOpen && EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) Toggle();
    }

    public void Toggle()
    {
        isOpen = !isOpen;
        Sfx.Play(isOpen ? SfxId.UiOpen : SfxId.UiClose);

        if (isOpen)
        {
            if (CraftingTableUI.Instance != null && CraftingTableUI.Instance.IsOpen) CraftingTableUI.Instance.Toggle();
            if (ShopUI.Instance != null && ShopUI.Instance.IsOpen) ShopUI.Instance.Toggle();
            RefreshAll();
            foreach (var s in slotUIs) s.ResetVisual();
        }

        // 열려 있는 동안 하단 핫바 숨김
        TilePlacementUI.Instance?.SetHudVisible(!isOpen);

        if (animCoroutine != null) StopCoroutine(animCoroutine);
        animCoroutine = StartCoroutine(PlayAnim(isOpen));
    }

    private IEnumerator PlayAnim(bool opening)
    {
        panelCG.blocksRaycasts = opening;
        panelCG.interactable   = opening;
        if (opening)
        {
            CuteUIKit.WiggleEars(this, transform);
            yield return CuteUIKit.PopIn(panelRT, panelCG, animDuration * 1.8f);
        }
        else
        {
            yield return CuteUIKit.PopOut(panelRT, panelCG, animDuration);
        }
    }

    public void BeginDrag(int sourceIndex)
    {
        _dragSourceIndex = sourceIndex;
        _dragConfirmed   = false;

        dragIconImage.sprite = sourceIndex == TrashIndex
            ? Inventory.Instance?.TrashItem?.icon
            : Inventory.Instance?.GetSlot(sourceIndex)?.Item?.icon;
        dragIconImage.enabled = dragIconImage.sprite != null;
    }

    public void MoveDragIcon(Vector2 screenPos)
    {
        if (!dragIconImage.enabled) return;

        Camera cam = (rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            ? null : rootCanvas.worldCamera;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rootCanvas.GetComponent<RectTransform>(), screenPos, cam, out Vector2 local);

        dragIconImage.rectTransform.anchoredPosition = local;
    }

    public void EndDrag(int targetIndex)
    {
        _dragConfirmed = true;
        dragIconImage.enabled = false;

        if (_dragSourceIndex == TrashIndex)
        {
            if (targetIndex >= 0 && Inventory.Instance != null && Inventory.Instance.RestoreTrash(targetIndex))
                Sfx.Play(SfxId.Pickup);
        }
        else if (targetIndex == TrashIndex)
            TrashFrom(_dragSourceIndex);
        else if (targetIndex != _dragSourceIndex)
            Inventory.Instance?.SwapSlots(_dragSourceIndex, targetIndex);

        _dragSourceIndex = NoDrag;
        RefreshAll();
    }

    public void TrashFrom(int slotIndex)
    {
        var inv  = Inventory.Instance;
        var slot = inv?.GetSlot(slotIndex);
        if (slot == null || slot.IsEmpty) return;

        string label = $"{slot.Item.itemName} ×{slot.Count}";
        if (!inv.TrashSlot(slotIndex)) return;
        Sfx.Play(SfxId.Dismantle);
        NotificationUI.Instance?.Show($"{label} 버림 — 휴지통에서 다시 꺼낼 수 있어요");
        RefreshAll();
    }

    public void CancelDragIfActive(bool droppedOnWorld = false, Vector2 screenPos = default)
    {
        if (_dragConfirmed) { _dragConfirmed = false; return; }
        dragIconImage.enabled = false;
        int source       = _dragSourceIndex;
        _dragSourceIndex = NoDrag;
        if (droppedOnWorld && source >= 0) ThrowToWorld(source, screenPos);
        RefreshAll();
    }

    private void ThrowToWorld(int slotIndex, Vector2 screenPos)
    {
        var inv    = Inventory.Instance;
        var player = PlayerTileController.Instance;
        if (inv == null || player == null || !inv.TakeSlot(slotIndex, out var item, out int count)) return;

        Vector3 from   = player.transform.position;
        Vector2 toward = from;
        var cam = Camera.main;
        if (cam != null) toward = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -cam.transform.position.z));

        DroppedItem.Throw(item, count, from, toward);
        Sfx.Play(SfxId.Dismantle);
        NotificationUI.Instance?.Show($"{item.itemName} ×{count} 버림 — 3초 뒤부터 다시 주울 수 있어요");
    }

    public void EndDragExternal()
    {
        _dragConfirmed        = true;
        dragIconImage.enabled = false;
        _dragSourceIndex      = NoDrag;
        RefreshAll();
    }

    private void RefreshAll()
    {
        if (slotUIs == null) return;
        foreach (var s in slotUIs) s.Refresh();
        if (trashSlot != null) trashSlot.Refresh();
    }

    private void BuildPanel()
    {
        panelRT = GetComponent<RectTransform>();
        if (panelRT == null) panelRT = gameObject.AddComponent<RectTransform>();

        if (gameObject.GetComponent<Image>() == null) gameObject.AddComponent<Image>();

        panelCG = gameObject.AddComponent<CanvasGroup>();
        panelCG.alpha          = 1f;
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;

        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = Vector2.zero;

        CuteUIKit.DecorateWindow(panelRT);
        BuildFootprints();

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding            = new RectOffset(22, 22, 18, 30);
        vlg.spacing            = 8f;
        vlg.childAlignment     = TextAnchor.UpperCenter;
        vlg.childForceExpandWidth  = false;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = true;

        var csf = gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void BuildFootprints()
    {
        Footprint(new Vector2(-26f, 14f), 14f,  78f);
        Footprint(new Vector2(-45f, 20f), 14f, 102f);
        Footprint(new Vector2(-64f, 14f), 14f,  78f);
    }

    private void Footprint(Vector2 pos, float size, float angle)
    {
        var rt = CuteUIKit.Layer(transform, "Footprint", CuteUIKit.Paw(), CuteUIKit.Hex(0xDDB892, 0.8f)).rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.sizeDelta        = new Vector2(size, size);
        rt.anchoredPosition = pos;
        rt.localRotation    = Quaternion.Euler(0f, 0f, angle);
    }

    private void BuildDragIcon()
    {
        var go = new GameObject("InventoryDragIcon");
        go.transform.SetParent(rootCanvas.transform, false);

        dragIconImage = go.AddComponent<Image>();
        dragIconImage.raycastTarget = false;
        dragIconImage.preserveAspect = true;
        dragIconImage.enabled = false;
        dragIconImage.color   = new Color(1f, 1f, 1f, 0.9f);
        dragIconImage.rectTransform.sizeDelta = new Vector2(slotSize, slotSize);

        go.transform.localScale    = Vector3.one * 1.12f;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, -8f);
        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor    = CuteUIKit.Shadow;
        shadow.effectDistance = new Vector2(3f, -5f);
    }

    private void BuildSlots()
    {
        int total     = Inventory.Instance?.SlotCount ?? Inventory.MinSlots;
        slotUIs       = new InventorySlotUI[total];
        int mainCount = total - hotbarCount;
        int cols      = hotbarCount;

        CuteUIKit.TitleRibbon(transform, "인벤토리", toggleKey.ToString());

        SectionHeader("장비", CuteUIKit.LavenderTag, CuteUIKit.LavenderText, "눌러서 해제");
        BuildEquipmentSlots();

        AddDivider();

        SectionHeader("핫바", CuteUIKit.SkyTag, CuteUIKit.SkyText, $"1~{hotbarCount} 키로 선택");
        var hotbarRow = CreateSlotRow(cols);
        for (int i = 0; i < hotbarCount; i++)
        {
            var slotGO = CreateSlotGO(hotbarRow, CuteUIKit.HotbarBorder, CuteUIKit.HotbarFill);
            var slotUI = slotGO.AddComponent<InventorySlotUI>();
            slotUI.Setup(i, this, hotbar: true);
            slotUIs[i] = slotUI;
        }

        AddDivider();

        SectionHeader("가방", CuteUIKit.MintTag, CuteUIKit.MintText, null);
        var grid = CreateGrid(cols);
        for (int i = 0; i < mainCount; i++)
        {
            int idx    = hotbarCount + i;
            var slotGO = CreateSlotGO(grid, CuteUIKit.SlotBorder, CuteUIKit.SlotFill);
            var slotUI = slotGO.AddComponent<InventorySlotUI>();
            slotUI.Setup(idx, this, hotbar: false);
            slotUIs[idx] = slotUI;
        }

        AddDivider();
        BuildTrashRow();
    }

    private void BuildTrashRow()
    {
        var row = CreateSlotRow(1);
        var hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.spacing        = 10f;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        row.gameObject.AddComponent<LayoutElement>().preferredWidth = ContentWidth;

        var slotGO = CreateSlotGO(row, TrashSlotUI.BorderColor, TrashSlotUI.FillColor);
        slotGO.name = "TrashSlot";
        trashSlot = slotGO.AddComponent<TrashSlotUI>();
        trashSlot.Setup(this);

        var note = CuteUIKit.Text(row, "<b>휴지통</b>  끌어다 놓거나 Ctrl+클릭하면 버려요\n마지막으로 버린 건 다시 꺼낼 수 있어요",
                                  12f, CuteUIKit.CocoaSoft, FontStyles.Normal);
        note.alignment        = TextAlignmentOptions.MidlineLeft;
        note.textWrappingMode = TextWrappingModes.Normal;
        note.rectTransform.sizeDelta = new Vector2(ContentWidth - slotSize - hlg.spacing, slotSize);
    }

    private void BuildEquipmentSlots()
    {
        var row = CreateSlotRow(EquipmentManager.SlotCount);
        for (int i = 0; i < EquipmentManager.SlotCount; i++)
        {
            var slotGO = CreateSlotGO(row, CuteUIKit.EquipBorder, CuteUIKit.EquipFill);
            slotGO.name = $"EquipSlot_{i}";
            slotGO.AddComponent<EquipmentSlotUI>().Setup(i);
        }
    }

    private void SectionHeader(string label, Color tagColor, Color textColor, string note)
        => CuteUIKit.SectionHeader(transform, label, tagColor, textColor, note, ContentWidth);

    private GameObject CreateSlotGO(Transform parent, Color border, Color fill)
        => CuteUIKit.Slot(parent, slotSize, border, fill);

    private Transform CreateSlotRow(int cols)
    {
        var go = new GameObject("HotbarRow");
        go.transform.SetParent(transform, false);

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing            = slotSpacing;
        hlg.childAlignment     = TextAnchor.MiddleCenter;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        hlg.childControlWidth  = false;
        hlg.childControlHeight = false;

        var csf = go.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        return go.transform;
    }

    private Transform CreateGrid(int cols)
    {
        var go = new GameObject("MainGrid");
        go.transform.SetParent(transform, false);

        var glg = go.AddComponent<GridLayoutGroup>();
        glg.cellSize        = new Vector2(slotSize, slotSize);
        glg.spacing         = new Vector2(slotSpacing, slotSpacing);
        glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = cols;
        glg.childAlignment  = TextAnchor.UpperLeft;

        var csf = go.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        return go.transform;
    }

    private void AddDivider() => CuteUIKit.DottedDivider(transform, ContentWidth);
}
