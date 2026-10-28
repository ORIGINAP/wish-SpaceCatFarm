using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ItemTooltipUI : MonoBehaviour
{
    private static ItemTooltipUI _instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _instance = null;

    public static ItemTooltipUI Instance
    {
        get
        {
            if (_instance == null) _instance = TryCreate();
            return _instance;
        }
    }

    public static void Create(Canvas canvas)
    {
        if (_instance != null || canvas == null) return;
        _instance = Spawn(canvas);
    }

    private static ItemTooltipUI TryCreate()
    {
        var canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return null;
        return Spawn(canvas);
    }

    private static ItemTooltipUI Spawn(Canvas canvas)
    {
        var go = new GameObject("ItemTooltipUI", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);

        var ui = go.AddComponent<ItemTooltipUI>();
        ui.rootCanvas = canvas;
        ui.Build();
        return ui;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

    private RectTransform    rt;
    private CanvasGroup      cg;
    private TextMeshProUGUI  nameText;
    private TextMeshProUGUI  descText;
    private TextMeshProUGUI  abilityText;
    private GameObject       tierPill;
    private Image            tierPillImage;
    private TextMeshProUGUI  tierText;
    private GameObject       divider;
    private Canvas           rootCanvas;
    private FarmingItemData  shownItem;
    private Coroutine        popCo;

    private const float OffsetX = 14f;
    private const float OffsetY = -14f;

    private void Build()
    {
        rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();
        rt.pivot = new Vector2(0f, 1f);

        CuteUIKit.DecorateWindow(rt, catEars: false, radius: 12, border: 3);

        cg = gameObject.AddComponent<CanvasGroup>();
        cg.alpha          = 0f;
        cg.blocksRaycasts = false;
        cg.interactable   = false;

        var csf = gameObject.AddComponent<ContentSizeFitter>();
        csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding            = new RectOffset(12, 12, 9, 10);
        vlg.spacing            = 4f;
        vlg.childForceExpandWidth  = false;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth  = true;
        vlg.childControlHeight = true;

        var nameRow = new GameObject("NameRow", typeof(RectTransform));
        nameRow.transform.SetParent(transform, false);
        var hlg = nameRow.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = 5f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;

        var pawGO = new GameObject("Paw", typeof(RectTransform));
        pawGO.transform.SetParent(nameRow.transform, false);
        var paw = pawGO.AddComponent<Image>();
        paw.sprite        = CuteUIKit.Paw();
        paw.color         = CuteUIKit.Strawberry;
        paw.raycastTarget = false;
        var pawLE = pawGO.AddComponent<LayoutElement>();
        pawLE.preferredWidth  = 14f;
        pawLE.preferredHeight = 14f;

        nameText = CuteUIKit.Text(nameRow.transform, "", 15f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        nameText.name = "Name";

        var pill = CuteUIKit.Pill(nameRow.transform, "", CuteUIKit.SlotFill, CuteUIKit.Cocoa, 10f, 7, new RectOffset(5, 5, 1, 1), out tierText);
        tierPill      = pill.gameObject;
        tierPillImage = pill.GetComponent<Image>();
        tierPill.SetActive(false);

        divider = CuteUIKit.DottedDivider(transform).gameObject;

        descText = CuteUIKit.Text(transform, "", 13f, CuteUIKit.Cocoa, align: TextAlignmentOptions.TopLeft);
        descText.name             = "Desc";
        descText.textWrappingMode = TextWrappingModes.Normal;
        descText.gameObject.AddComponent<LayoutElement>().preferredWidth = 250f;

        abilityText = CuteUIKit.Text(transform, "", 12f, CuteUIKit.CocoaSoft, align: TextAlignmentOptions.TopLeft);
        abilityText.name             = "Ability";
        abilityText.textWrappingMode = TextWrappingModes.Normal;
        abilityText.richText         = true;
        abilityText.gameObject.AddComponent<LayoutElement>().preferredWidth = 250f;

        transform.SetAsLastSibling();
    }

    public void Show(FarmingItemData item)
    {
        if (item == null) { Hide(); return; }

        bool popIn = cg.alpha <= 0f || item != shownItem;
        shownItem = item;

        nameText.text = item.itemName;

        int tier = item.Tier;
        tierPill.SetActive(tier > 1);
        if (tier > 1)
        {
            tierText.text       = $"{tier}티어";
            tierText.color      = CuteUIKit.TierText(tier);
            tierPillImage.color = CuteUIKit.TierFill(tier, CuteUIKit.SlotFill);
        }

        bool hasDesc = !string.IsNullOrEmpty(item.description);
        descText.text = hasDesc ? item.description : "";
        descText.gameObject.SetActive(hasDesc);

        string ability = ItemInfo.Describe(item);
        bool hasAbility = !string.IsNullOrEmpty(ability);
        abilityText.text = ability;
        abilityText.gameObject.SetActive(hasAbility);
        divider.SetActive(hasDesc || hasAbility);

        cg.alpha = 1f;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

        UpdatePosition();
        if (popIn) PopIn();
    }

    public void Hide()
    {
        if (cg != null) cg.alpha = 0f;
    }

    private void PopIn()
    {
        if (!isActiveAndEnabled) return;
        if (popCo != null) StopCoroutine(popCo);
        transform.localScale = Vector3.one * 0.9f;
        popCo = StartCoroutine(CuteUIKit.ScaleTo(transform, 1f, 0.14f));
    }

    private void Update()
    {
        if (cg != null && cg.alpha > 0f) UpdatePosition();
    }

    private void UpdatePosition()
    {
        if (rootCanvas == null) return;

        Camera cam = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : rootCanvas.worldCamera;

        // 패드는 칸 모서리에 붙임
        Vector3 cursor = GamepadPointer.Active ? (Vector3)GamepadPointer.TooltipAnchor
                       : InputManager.Instance != null ? InputManager.Instance.CursorScreenPos : GameInput.MousePosition;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rootCanvas.GetComponent<RectTransform>(),
            cursor, cam, out Vector2 local);

        var canvasRT = rootCanvas.GetComponent<RectTransform>();
        float halfW  = canvasRT.rect.width  * 0.5f;
        float halfH  = canvasRT.rect.height * 0.5f;
        float tipW   = rt.rect.width;
        float tipH   = rt.rect.height;

        float x = Mathf.Clamp(local.x + OffsetX, -halfW, halfW - tipW);
        float y = Mathf.Clamp(local.y + OffsetY, -halfH + tipH, halfH);

        rt.anchoredPosition = new Vector2(x, y);
    }
}
