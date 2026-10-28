using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class FacilityPopup : MonoBehaviour
{
    public static FacilityPopup Instance => Services.Get<FacilityPopup>();

    public static bool IsOpen => Instance != null && Instance.target != null;

    // 닫은 클릭이 월드로 안 새게
    public static bool BlocksWorldInput => IsOpen || (Instance != null && Instance.closedFrame == Time.frameCount);

    private const float Width         = 216f;
    private const float AutoCloseDist = 6f;

    private TileItem        target;
    private RectTransform   canvasRT;
    private RectTransform   panelRT;
    private CanvasGroup     group;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI roleText;
    private TextMeshProUGUI statusText;
    private Button          linkButton;
    private Button          unlinkButton;
    private TextMeshProUGUI unlinkLabel;
    private Camera          cam;
    private int             openedFrame = -1;
    private int             closedFrame = -1;
    private int             shownLinks  = -1;
    private Coroutine       popCo;

    public static void Open(TileItem tile)
    {
        if (tile == null || !LinkRules.IsLinkable(tile)) return;
        if (Instance == null) new GameObject("FacilityPopup").AddComponent<FacilityPopup>();
        Instance.Show(tile);
    }

    public static void Close()
    {
        if (IsOpen) Instance.Hide();
    }

    private void Awake()
    {
        Services.Replace(this);
        Build();
    }

    private void OnDestroy()
    {
        Services.Unregister(this);
    }

    private void Show(TileItem tile)
    {
        bool wasOpen = target != null;
        target      = tile;
        openedFrame = Time.frameCount;
        shownLinks  = -1;

        titleText.text = LinkRules.NameOf(tile);
        roleText.text  = LinkRules.RoleOf(tile);
        RefreshStatus();
        FollowTarget();

        group.alpha          = 1f;
        group.blocksRaycasts = true;
        group.interactable   = true;
        if (popCo != null) StopCoroutine(popCo);
        popCo = StartCoroutine(CuteUIKit.PopIn(panelRT, group, 0.22f));
        if (!wasOpen) Sfx.Play(SfxId.UiOpen, 0.6f);
    }

    private void Hide()
    {
        target      = null;
        closedFrame = Time.frameCount;
        group.blocksRaycasts = false;
        group.interactable   = false;
        UIStyleKit.ClearSelectionIn(panelRT);
        if (popCo != null) StopCoroutine(popCo);
        popCo = StartCoroutine(CuteUIKit.PopOut(panelRT, group, 0.12f));
    }

    private void Update()
    {
        if (target is null) return;
        if (target == null) { Hide(); return; }

        if (EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) { Hide(); return; }

        var player = PlayerTileController.Instance;
        if (player != null && target.DistanceFrom(player.transform.position) > AutoCloseDist) { Hide(); return; }

        var im = InputManager.Instance;
        bool press = im != null ? (im.ActionDown || im.RemoveDown) : (GameInput.GetMouseButtonDown(0) || GameInput.GetMouseButtonDown(1));
        if (press && Time.frameCount != openedFrame)
        {
            Vector3 cursor = im != null ? im.CursorScreenPos : GameInput.MousePosition;
            if (!RectTransformUtility.RectangleContainsScreenPoint(panelRT, cursor, null)) { Hide(); return; }
        }

        FollowTarget();
        if (target.Links.Count != shownLinks) RefreshStatus();
        UIStyleKit.KeepNavigable(linkButton.gameObject, panelRT);
    }

    private void FollowTarget()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null || target == null) return;

        var gm  = GridManager.Instance;
        float cs = gm != null ? gm.CellSize : 1f;
        float top = (target.FootprintMax.y - target.FootprintMin.y + 1) * cs * 0.5f;
        Vector3 screen = cam.WorldToScreenPoint(target.transform.position + Vector3.up * (top + 0.15f));
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screen, null, out var local)) return;

        Vector2 half = canvasRT.rect.size * 0.5f;
        Vector2 size = panelRT.rect.size;
        local.x = Mathf.Clamp(local.x, -half.x + size.x * 0.5f + 8f, half.x - size.x * 0.5f - 8f);
        local.y = Mathf.Clamp(local.y, -half.y + 8f, half.y - size.y - 8f);
        panelRT.anchoredPosition = local;
    }

    private void RefreshStatus()
    {
        shownLinks = target.Links.Count;
        string status = target switch
        {
            ItemStorage s => s.IsUnlimited ? $"보관 {s.TotalCount} / ∞" : $"보관 {s.TotalCount} / {s.Capacity}",
            AutoSeeder sd => $"씨앗 {sd.TotalCount} / {sd.Capacity}",
            CatKitchen k  => k.Recipe != null ? $"메뉴: {k.Recipe.outputItem.itemName}" : "메뉴를 골라 주세요",
            Composter c   => $"퇴비 {c.OutputCount}개",
            ShuttlePad p  => p.Shuttle == null ? "셔틀이 오는 중" : p.Shuttle.away ? "셔틀이 고향별에 가 있어요" : $"모자란 짐: {ShuttleUI.MissingText(p.Shuttle)}",
            _ => "",
        };
        string links = shownLinks > 0 ? $"연결 {shownLinks}개" : "아직 연결 없음";
        statusText.text = string.IsNullOrEmpty(status) ? links : $"{links} · {status}";

        unlinkButton.gameObject.SetActive(shownLinks > 0);
        unlinkLabel.text = $"연결 모두 끊기 ({shownLinks})";
    }

    private void OnStartLink()
    {
        var t = target;
        Hide();
        LinkModeController.Begin(t);
    }

    private void OnUnlinkAll()
    {
        if (target == null) return;
        int n = target.Links.Count;
        target.UnlinkAll();
        Sfx.Play(SfxId.UiClose);
        NotificationUI.Instance?.Show($"{LinkRules.NameOf(target)}: 연결 {n}개를 끊었어요");
        RefreshStatus();
    }

    private void Build()
    {
        var canvasGO = new GameObject("FacilityPopupCanvas", typeof(RectTransform));
        canvasGO.transform.SetParent(transform, false);
        UIStyleKit.CreateOverlayCanvas(canvasGO, 420);
        canvasRT = (RectTransform)canvasGO.transform;

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(canvasGO.transform, false);
        panelRT = (RectTransform)panel.transform;
        panelRT.anchorMin = panelRT.anchorMax = new Vector2(0.5f, 0.5f);
        panelRT.pivot     = new Vector2(0.5f, 0f);
        group = UIStyleKit.CreatePanelRoot(panel, Width, new RectOffset(14, 14, 12, 14), 6f);

        titleText = CuteUIKit.Text(panel.transform, "", 14f, CuteUIKit.Strawberry, FontStyles.Bold);
        titleText.name = "Title";

        roleText = CuteUIKit.Text(panel.transform, "", 10f, CuteUIKit.CocoaSoft);
        roleText.name = "Role";
        roleText.textWrappingMode = TextWrappingModes.Normal;

        statusText = CuteUIKit.Text(panel.transform, "", 10f, CuteUIKit.Cocoa, FontStyles.Bold);
        statusText.name = "Status";

        CuteUIKit.DottedDivider(panel.transform);

        linkButton   = CuteUIKit.Button(panel.transform, "연결하기", CuteUIKit.ButtonMint, OnStartLink, -1f, 28f, 12f);
        unlinkButton = CuteUIKit.Button(panel.transform, "연결 모두 끊기", CuteUIKit.ButtonCoral, OnUnlinkAll, -1f, 26f, 11f);
        unlinkLabel  = unlinkButton.GetComponentInChildren<TextMeshProUGUI>();

        panelRT.localScale = Vector3.zero;
    }
}
