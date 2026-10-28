using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

// 핫바와 설치 고스트 미리보기
public class TilePlacementUI : MonoBehaviour
{
    [Header("핫바")]
    [SerializeField] private Transform  hotbarContainer;
    [SerializeField] private GameObject slotPrefab;
    [SerializeField] private int        hotbarSlotCount = 7;

    private const float SelectedScale  = 1.08f;
    private const float EmptySlotAlpha = 0.55f;
    private const float HotbarBackPad  = 8f;

    [Header("고스트 프리뷰")]
    [SerializeField] private LayerMask groundLayer;

    [Header("References")]
    [SerializeField] private PlayerTileController controller;

    private static readonly Color GhostOkTint  = new(0.72f, 1f, 0.72f, 0.62f);
    private static readonly Color GhostBadTint = new(1f, 0.38f, 0.38f, 0.62f);
    private const int GhostSortBoost = 30;

    private readonly List<SlotUI> slots = new();
    private GameObject ghostObject;
    private readonly List<Renderer> ghostRenderers = new();
    private readonly List<SpriteRenderer> ghostSprites = new();
    private readonly List<Color>          ghostColors  = new();
    private bool ghostVisible = true;
    private Camera     mainCam;
    private int        currentSlotIndex = 0;

    public static TilePlacementUI Instance => Services.Get<TilePlacementUI>();

    public int SelectedIndex => currentSlotIndex;

    public RectTransform SlotRect(int index)
        => index >= 0 && index < slots.Count && slots[index].root != null ? (RectTransform)slots[index].root.transform : null;

    private void Awake() => Services.Replace(this);

    private CanvasGroup hudGroup;
    private Coroutine   hudFadeCo;

    public void SetHudVisible(bool visible)
    {
        if (hotbarContainer == null) return;
        if (hudGroup == null)
        {
            hudGroup = hotbarContainer.GetComponent<CanvasGroup>();
            if (hudGroup == null) hudGroup = hotbarContainer.gameObject.AddComponent<CanvasGroup>();
        }
        hudGroup.blocksRaycasts = visible;
        if (hudFadeCo != null) StopCoroutine(hudFadeCo);
        hudFadeCo = null;
        if (!isActiveAndEnabled) { hudGroup.alpha = visible ? 1f : 0f; return; }
        hudFadeCo = StartCoroutine(FadeHud(visible ? 1f : 0f));
    }

    private System.Collections.IEnumerator FadeHud(float to)
    {
        float from = hudGroup.alpha;
        const float Dur = 0.14f;
        for (float e = 0f; e < Dur; e += Time.unscaledDeltaTime)
        {
            hudGroup.alpha = Mathf.Lerp(from, to, e / Dur);
            yield return null;
        }
        hudGroup.alpha = to;
        hudFadeCo = null;
    }

    private void Start()
    {
        mainCam = Camera.main;
        BuildHotbar();
        if (Inventory.Instance != null)
            Inventory.Instance.OnInventoryChanged += RefreshSlots;
        SelectSlot(0);
    }

    private void OnDestroy()
    {
        Services.Unregister(this);
        if (Inventory.Instance != null)
            Inventory.Instance.OnInventoryChanged -= RefreshSlots;
    }

    private void Update()
    {
        HandleSlotNavigation();
        UpdateGhost();
    }

    private void LateUpdate()
    {
        if (controller == null || controller.SelectedItem == null) return;
        bool padDeselect = InputManager.Instance != null && InputManager.Instance.DeselectDown;
        if (EscapeRouter.TryConsume(EscapeRouter.EscPressed || padDeselect))
        {
            Deselect();
            GamepadRumble.Play(Haptic.UiClose);
        }
    }

    public void SelectSlot(int index)
    {
        if (index < 0 || index >= hotbarSlotCount) return;

        currentSlotIndex = index;

        var invSlot = Inventory.Instance?.GetSlot(index);
        FarmingItemData item = (invSlot != null && !invSlot.IsEmpty) ? invSlot.Item : null;

        controller.SelectItem(item);
        UpdateGhostPrefab();
        RefreshSlots();
    }

    public void SelectItem(FarmingItemData item)
    {
        if (item == null || Inventory.Instance == null) return;
        for (int i = 0; i < hotbarSlotCount; i++)
        {
            var slot = Inventory.Instance.GetSlot(i);
            if (slot != null && !slot.IsEmpty && slot.Item == item) { SelectSlot(i); return; }
        }
    }

    private void OnSlotClicked(int index)
    {
        // 모바일: 같은 슬롯 다시 누르면 해제
        if (MobileMode.Active && index == currentSlotIndex && controller.SelectedItem != null)
        {
            Deselect();
            return;
        }
        SelectSlot(index);
    }

    private void NavigateSlot(int delta)
    {
        int next = currentSlotIndex < 0
            ? (delta > 0 ? 0 : hotbarSlotCount - 1)
            : (currentSlotIndex + delta + hotbarSlotCount) % hotbarSlotCount;
        SelectSlot(next);
        GamepadRumble.Play(Haptic.UiMove);
    }

    public void Deselect()
    {
        currentSlotIndex = -1;
        controller.SelectItem(null);
        DestroyGhost();
        RefreshSlots();
    }

    private void BuildHotbar()
    {
        slots.Clear();
        foreach (Transform child in hotbarContainer) Destroy(child.gameObject);

        for (int i = 0; i < hotbarSlotCount; i++)
        {
            int idx = i;
            GameObject go = Instantiate(slotPrefab, hotbarContainer);

            var slotUI = new SlotUI
            {
                root      = go,
                icon      = FindIconImage(go),
                countText = go.GetComponentInChildren<TextMeshProUGUI>(true),
                highlight = go.transform.Find("highlight")?.gameObject
            };

            if (slotUI.icon != null)
                slotUI.icon.preserveAspect = true;

            SetIconSprite(slotUI.icon, null);
            slotUI.highlight?.SetActive(false);
            StyleSlot(slotUI, i);

            var button = go.GetComponent<Button>();
            if (button == null && MobileMode.Active)
            {
                button = go.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
            }
            button?.onClick.AddListener(() => OnSlotClicked(idx));
            slots.Add(slotUI);
        }

        BuildHotbarBack();
        RefreshSlots();
    }

    private void StyleSlot(SlotUI s, int index)
    {
        var t = s.root.transform;
        float size = ((RectTransform)t).rect.width;

        foreach (var old in s.root.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (old.gameObject != s.root) old.gameObject.SetActive(false);

        var frame = s.root.GetComponent<Image>();
        if (frame != null) CuteUIKit.SetRounded(frame, 12, CuteUIKit.HotbarBorder);
        var fill = CuteUIKit.Layer(t, "Fill", CuteUIKit.Rounded(10), CuteUIKit.HotbarFill, 2.5f);
        fill.transform.SetSiblingIndex(0);
        s.frame = frame;
        s.fill  = fill;

        if (s.highlight != null)
        {
            var hrt = (RectTransform)s.highlight.transform;
            hrt.anchorMin = Vector2.zero;
            hrt.anchorMax = Vector2.one;
            hrt.offsetMin = hrt.offsetMax = Vector2.zero;
            if (s.highlight.TryGetComponent<Image>(out var hl))
            {
                CuteUIKit.SetRounded(hl, 12, CuteUIKit.Strawberry);
                hl.raycastTarget = false;
            }
            s.highlightFill = CuteUIKit.Layer(hrt, "Fill", CuteUIKit.Rounded(9), CuteUIKit.PinkSoft, 3f);
            hrt.SetSiblingIndex(1);
        }

        var gloss = CuteUIKit.Layer(t, "Gloss", CuteUIKit.Rounded(4), CuteUIKit.Hex(0xFFFFFF, 0.6f)).rectTransform;
        gloss.anchorMin = gloss.anchorMax = gloss.pivot = new Vector2(0f, 1f);
        gloss.sizeDelta        = new Vector2(size * 0.32f, Mathf.Clamp(size * 0.11f, 4f, 8f));
        gloss.anchoredPosition = new Vector2(Mathf.Clamp(size * 0.11f, 4f, 8f), -Mathf.Clamp(size * 0.1f, 4f, 7f));
        gloss.SetSiblingIndex(s.highlight != null ? 2 : 1);

        if (s.icon != null)
        {
            var iconRT = s.icon.rectTransform;
            iconRT.anchorMin = new Vector2(0.14f, 0.16f);
            iconRT.anchorMax = new Vector2(0.86f, 0.86f);
            iconRT.offsetMin = iconRT.offsetMax = Vector2.zero;
            iconRT.SetSiblingIndex(gloss.GetSiblingIndex() + 1);
        }

        var dot = CuteUIKit.Layer(t, "HotbarNum", CuteUIKit.Rounded(9), CuteUIKit.SkyText);
        var dotRT = dot.rectTransform;
        dotRT.anchorMin = dotRT.anchorMax = dotRT.pivot = new Vector2(0f, 1f);
        dotRT.sizeDelta        = new Vector2(18f, 18f);
        dotRT.anchoredPosition = new Vector2(3f, -3f);
        var num = CuteUIKit.Text(dotRT, (index + 1).ToString(), 10f, Color.white, FontStyles.Bold);
        num.rectTransform.anchorMin = Vector2.zero;
        num.rectTransform.anchorMax = Vector2.one;
        num.rectTransform.offsetMin = num.rectTransform.offsetMax = Vector2.zero;
        s.numBadge = dot;

        var badge = CuteUIKit.Pill(t, "", CuteUIKit.Cocoa, Color.white, 10f, 8,
                                   new RectOffset(5, 5, 1, 1), out var countLabel, fitSelf: true);
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(1f, 0f);
        badge.anchoredPosition = new Vector2(-3f, 3f);
        badge.gameObject.SetActive(false);
        s.countText  = countLabel;
        s.countBadge = badge.gameObject;

        s.group = s.root.GetComponent<CanvasGroup>();
        if (s.group == null) s.group = s.root.AddComponent<CanvasGroup>();
    }

    private void BuildHotbarBack()
    {
        var container = hotbarContainer as RectTransform;
        if (container == null || slots.Count == 0) return;
        LayoutRebuilder.ForceRebuildLayoutImmediate(container);

        var min = new Vector2(float.MaxValue, float.MaxValue);
        var max = new Vector2(float.MinValue, float.MinValue);
        foreach (var s in slots)
        {
            var rt = (RectTransform)s.root.transform;
            Vector2 p = rt.localPosition;
            min = Vector2.Min(min, p + rt.rect.min);
            max = Vector2.Max(max, p + rt.rect.max);
        }

        var go = new GameObject("HotbarBack", typeof(RectTransform));
        go.transform.SetParent(container, false);
        go.transform.SetAsFirstSibling();
        go.AddComponent<LayoutElement>().ignoreLayout = true;

        var back = (RectTransform)go.transform;
        back.anchorMin = back.anchorMax = new Vector2(0.5f, 0.5f);
        back.sizeDelta        = max - min + new Vector2(HotbarBackPad * 2f, HotbarBackPad * 2f);
        back.anchoredPosition = (min + max) * 0.5f - container.rect.center;

        CuteUIKit.DecorateWindow(back, catEars: false, radius: 20, border: 4);
    }

    private void SetSelectedLook(SlotUI s, bool selected)
    {
        if (s.selectedLook == selected) return;
        s.selectedLook = selected;

        if (s.numBadge != null) s.numBadge.color = selected ? CuteUIKit.Strawberry : CuteUIKit.SkyText;

        float scale = selected ? SelectedScale : 1f;
        if (s.popCo != null) StopCoroutine(s.popCo);
        s.popCo = null;
        if (!isActiveAndEnabled) { s.root.transform.localScale = Vector3.one * scale; return; }
        s.popCo = StartCoroutine(CuteUIKit.ScaleTo(s.root.transform, scale, selected ? 0.18f : 0.12f));
    }

    private static void SetCount(SlotUI s, int count)
    {
        bool show = count > 1;
        if (s.countBadge != null) s.countBadge.SetActive(show);
        if (s.countText != null) s.countText.text = show ? count.ToString() : "";
    }

    private static void SetTierLook(SlotUI s, FarmingItemData item)
    {
        if (s.frame != null)         s.frame.color         = CuteUIKit.TierBorder(item, CuteUIKit.HotbarBorder);
        if (s.fill != null)          s.fill.color          = CuteUIKit.TierFill(item, CuteUIKit.HotbarFill);
        if (s.highlightFill != null) s.highlightFill.color = CuteUIKit.TierFill(item, CuteUIKit.PinkSoft);
    }

    private static void SetSlotAlpha(SlotUI s, float alpha)
    {
        if (s.group != null) s.group.alpha = alpha;
    }

    private void RefreshSlots()
    {
        var  currentInvSlot = currentSlotIndex >= 0 ? Inventory.Instance?.GetSlot(currentSlotIndex) : null;
        FarmingItemData slotItem = (currentInvSlot != null && !currentInvSlot.IsEmpty)
                                        ? currentInvSlot.Item : null;

        if (controller.SelectedItem != slotItem)
        {
            controller.SelectItem(slotItem);
            UpdateGhostPrefab();
        }

        for (int i = 0; i < slots.Count; i++)
        {
            var  slotUI  = slots[i];
            bool selected = (i == currentSlotIndex);

            slotUI.highlight?.SetActive(selected);
            SetSelectedLook(slotUI, selected);

            var invSlot = Inventory.Instance?.GetSlot(i);
            SetTierLook(slotUI, invSlot == null || invSlot.IsEmpty ? null : invSlot.Item);

            if (invSlot == null || invSlot.IsEmpty)
            {
                SetIconSprite(slotUI.icon, null);
                SetCount(slotUI, 0);
                SetSlotAlpha(slotUI, selected ? 1f : EmptySlotAlpha);
                continue;
            }

            SetIconSprite(slotUI.icon, invSlot.Item.icon);
            SetCount(slotUI, invSlot.Count);

            SetSlotAlpha(slotUI, 1f);
        }
    }

    private void UpdateGhostPrefab()
    {
        DestroyGhost();
        var item = controller.SelectedItem;

        if (item == null) return;
        if (item.itemType != FarmingItemType.Tile) return;
        if (item.tilePrefab == null) { Debug.LogWarning($"[Ghost] '{item.itemName}'의 tilePrefab이 NULL"); return; }

        ghostObject = Instantiate(item.tilePrefab);

        foreach (var col  in ghostObject.GetComponentsInChildren<Collider>())   col.enabled = false;
        foreach (var col2 in ghostObject.GetComponentsInChildren<Collider2D>()) col2.enabled = false;

        GetMostDerivedTileItem(ghostObject)?.SetGhostMode();

        ghostRenderers.Clear();
        ghostSprites.Clear();
        ghostColors.Clear();
        foreach (var r in ghostObject.GetComponentsInChildren<Renderer>())
        {
            if (r.enabled) ghostRenderers.Add(r);
            if (r is SpriteRenderer sr)
            {
                ghostSprites.Add(sr);
                ghostColors.Add(sr.color);
                sr.sortingOrder += GhostSortBoost;
            }
        }
        ghostVisible = true;

        SetGhostLook(true);
    }

    private void UpdateGhost()
    {
        if (ghostObject == null) return;

        bool hidden = PlayerTileController.BlocksPlacement()
                   || (Pad.Active && Pad.UIMode)
                   || GameInput.PointerOverUI;
        SetGhostVisible(!hidden);
        if (hidden) return;

        Vector3 mousePos = InputManager.Instance != null ? InputManager.Instance.CursorScreenPos : (Vector3)GameInput.MousePosition;
        mousePos.z = -mainCam.transform.position.z;
        Vector3 worldPos = mainCam.ScreenToWorldPoint(mousePos);
        worldPos.z = 0f;

        var item = controller.SelectedItem;
        Vector2Int gridPos = GridManager.Instance.WorldToGrid(worldPos);
        ghostObject.transform.position = GridManager.Instance.GridToWorldCenter(gridPos, item.tileWidth, item.tileHeight);

        bool canPlace = GridManager.Instance.CanPlaceAt(gridPos, item.tileWidth, item.tileHeight);
        SetGhostLook(canPlace);
    }

    private void SetGhostVisible(bool visible)
    {
        if (ghostVisible == visible) return;
        ghostVisible = visible;
        foreach (var r in ghostRenderers)
            if (r != null) r.enabled = visible;
    }

    private void DestroyGhost()
    {
        if (ghostObject != null) Destroy(ghostObject);
        ghostObject = null;
    }

    private void SetGhostLook(bool canPlace)
    {
        if (ghostObject == null) return;
        var tint = canPlace ? GhostOkTint : GhostBadTint;
        for (int i = 0; i < ghostSprites.Count; i++)
            if (ghostSprites[i] != null) ghostSprites[i].color = ghostColors[i] * tint;
    }

    private void HandleSlotNavigation()
    {
        for (int i = 0; i < hotbarSlotCount; i++)
        {
            if (!TextInputFocus.Active && GameInput.GetKeyDown(KeyCode.Alpha1 + i))
            {
                SelectSlot(i);
                return;
            }
        }

        bool prevSlot = InputManager.Instance != null ? InputManager.Instance.PrevSlot : GameInput.GetKeyDown(KeyCode.Q);
        bool nextSlot = InputManager.Instance != null ? InputManager.Instance.NextSlot : GameInput.GetKeyDown(KeyCode.E);
        if (prevSlot) { NavigateSlot(-1); return; }
        if (nextSlot) { NavigateSlot(+1); return; }

        bool isBlockingUIOpen = PlayerTileController.IsAnyUIOpen() || DevPanelUI.AnyOpen || TextInputFocus.Active ||
                                GameInput.PointerOverUI;

        // Ctrl+휠은 줌이라 제외
        if (!isBlockingUIOpen && !CameraZoom.IsCtrlHeld())
        {
            float scroll = GameInput.GetAxisRaw("Mouse ScrollWheel");
            if      (scroll > 0f) NavigateSlot(-1);
            else if (scroll < 0f) NavigateSlot(+1);
        }
    }

    private static TileItem GetMostDerivedTileItem(GameObject obj)
    {
        TileItem best = null;
        foreach (var comp in obj.GetComponentsInChildren<TileItem>())
        {
            if (best == null || comp.GetType().IsSubclassOf(best.GetType()))
                best = comp;
        }
        return best;
    }

    private static Image FindIconImage(GameObject slotRoot)
    {
        Transform named = slotRoot.transform.Find("icon")
                       ?? slotRoot.transform.Find("Icon")
                       ?? slotRoot.transform.Find("ItemIcon");
        if (named != null)
        {
            var img = named.GetComponent<Image>();
            if (img != null) return img;
        }

        Image rootImage = slotRoot.GetComponent<Image>();
        foreach (var img in slotRoot.GetComponentsInChildren<Image>(true))
        {
            if (img == rootImage) continue;
            if (img.gameObject == slotRoot) continue;
            if (img.transform.parent != slotRoot.transform) continue;
            string n = img.gameObject.name.ToLower();
            if (n.Contains("highlight") || n.Contains("select")) continue;
            return img;
        }
        return null;
    }

    private static void SetIconSprite(Image icon, Sprite sprite)
    {
        if (icon == null) return;
        icon.sprite  = sprite;
        icon.enabled = sprite != null;
    }

    private class SlotUI
    {
        public GameObject      root;
        public Image           icon;
             public TextMeshProUGUI countText;
        public GameObject      highlight;

        public GameObject      countBadge;
        public Image           numBadge;
        public CanvasGroup     group;
        public Coroutine       popCo;
        public bool            selectedLook;
        public Image           frame, fill;
        public Image           highlightFill;
    }
}
