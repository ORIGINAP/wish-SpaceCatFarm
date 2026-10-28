using UnityEngine;
using TMPro;

public class LinkModeController : MonoBehaviour
{
    public static LinkModeController Instance => Services.Get<LinkModeController>();

    public static bool IsActive => Instance != null && Instance.source != null;

    public static TileItem Source => IsActive ? Instance.source : null;

    // 끝낸 클릭이 월드로 안 새게
    public static bool BlocksWorldInput => IsActive || (Instance != null && Instance.endedFrame == Time.frameCount);

    private static readonly Color IdleColor    = new(1f, 1f, 1f, 0.5f);
    private static readonly Color ValidColor   = new(0.45f, 1f, 0.55f, 0.75f);
    private static readonly Color InvalidColor = new(1f, 0.45f, 0.45f, 0.7f);
    private static readonly Color UnlinkColor  = new(1f, 0.8f, 0.3f, 0.75f);
    private static readonly Color RangeColor   = new(1f, 1f, 1f, 0.18f);

    private TileItem        source;
    private int             changed;
    private LineRenderer    line;
    private Transform       rangeRoot;
    private RectTransform   hintRT;
    private RectTransform   canvasRT;
    private TextMeshProUGUI hintText;
    private Camera          cam;
    private int             endedFrame   = -1;
    // 시작 클릭 무시용
    private int             startedFrame = -1;

    public static void Begin(TileItem from)
    {
        if (from == null || !LinkRules.IsLinkable(from)) return;
        if (Instance == null) new GameObject("LinkModeController").AddComponent<LinkModeController>();
        Instance.StartMode(from);
    }

    public static void Cancel()
    {
        if (IsActive) Instance.End(null);
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

    private void StartMode(TileItem from)
    {
        source       = from;
        startedFrame = Time.frameCount;
        changed      = 0;
        line.enabled = true;
        hintRT.gameObject.SetActive(true);

        var gm = GridManager.Instance;
        float cs = gm != null ? gm.CellSize : 1f;
        var size = from.FootprintMax - from.FootprintMin + Vector2Int.one;
        float radius = (LinkRules.MaxDistanceCells + Mathf.Max(size.x, size.y) * 0.5f) * cs;
        if (rangeRoot != null) Destroy(rangeRoot.gameObject);
        rangeRoot = new GameObject("LinkRange").transform;
        rangeRoot.SetParent(transform, false);
        rangeRoot.position = from.transform.position;
        WorldCircleOutline.Create(rangeRoot, radius, RangeColor, 0.035f, 96, 52);

        Sfx.Play(SfxId.UiClick);
        NotificationUI.Instance?.Show(Pad.Active
            ? $"{LinkRules.NameOf(from)}: 이을 설비를 A로 고르세요 (B 그만)"
            : $"{LinkRules.NameOf(from)}: 이을 설비를 클릭하세요 (우클릭 · Esc 취소)");
    }

    private void End(string message)
    {
        source = null;
        endedFrame = Time.frameCount;
        line.enabled = false;
        hintRT.gameObject.SetActive(false);
        if (rangeRoot != null) { Destroy(rangeRoot.gameObject); rangeRoot = null; }
        if (!string.IsNullOrEmpty(message)) NotificationUI.Instance?.Show(message);
    }

    private void Update()
    {
        if (source is null) return;
        if (source == null) { End("연결할 설비가 사라졌어요."); return; }

        var im = InputManager.Instance;
        bool cancel = EscapeRouter.TryConsume(EscapeRouter.CancelPressed)
                   || (im != null ? im.RemoveDown : GameInput.GetMouseButtonDown(1));
        if (cancel) { End(changed > 0 ? "연결을 마쳤어요." : "연결을 취소했어요."); Sfx.Play(SfxId.UiClose); return; }
        if (PlayerTileController.IsPauseMenuOpen()) return;

        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Vector3 screen = im != null ? im.CursorScreenPos : GameInput.MousePosition;
        Vector3 world  = ScreenToWorld(screen);
        var gm         = GridManager.Instance;
        var target     = gm != null ? gm.GetTile(gm.WorldToGrid(world)) : null;
        if (target != null && target.IsGhost) target = null;

        string reason = null;
        Color  color;
        string hint;
        bool pad = Pad.Active;
        if (target == null || target == source)
        {
            color = IdleColor;
            hint  = pad ? $"{LinkRules.NameOf(source)} → 이을 설비 조준" : $"{LinkRules.NameOf(source)} → 이을 설비 클릭";
        }
        else if (source.IsLinkedTo(target))
        {
            color = UnlinkColor;
            hint  = pad ? $"A: 연결 해제 · {LinkRules.NameOf(target)}" : $"클릭하면 연결 해제: {LinkRules.NameOf(target)}";
        }
        else if (LinkRules.CanLink(source, target, out reason))
        {
            color = ValidColor;
            hint  = pad ? $"A: 연결 · {LinkRules.NameOf(target)}" : $"클릭하면 연결: {LinkRules.NameOf(target)}";
        }
        else
        {
            color = InvalidColor;
            hint  = reason;
        }

        line.SetPosition(0, WorldConnectionLine.UnderSprites(source.transform.position));
        line.SetPosition(1, WorldConnectionLine.UnderSprites(target != null && target != source ? target.transform.position : world));
        line.startColor = line.endColor = color;
        hintText.text = hint;
        MoveHint(screen);

        bool click  = im != null ? im.ConfirmDown : GameInput.GetMouseButtonDown(0);
        bool overUI = GameInput.PointerOverUI;
        if (click && !overUI && Time.frameCount != startedFrame) HandleClick(target, reason);
    }

    private void HandleClick(TileItem target, string reason)
    {
        bool keepGoing = GameInput.GetKey(KeyCode.LeftShift) || GameInput.GetKey(KeyCode.RightShift) || Pad.Active;

        if (target == null || target == source)
        {
            Sfx.Play(SfxId.UiError);
            NotificationUI.Instance?.Show(Pad.Active ? "이을 설비를 조준해 A — 그만하려면 B"
                                                     : "이을 설비를 클릭해 주세요 — 그만하려면 우클릭 · Esc");
            return;
        }

        if (source.IsLinkedTo(target))
        {
            TileItem.Unlink(source, target);
            changed++;
            Sfx.Play(SfxId.UiClose);
            NotificationUI.Instance?.Show($"연결 해제: {LinkRules.NameOf(source)} - {LinkRules.NameOf(target)}");
        }
        else if (reason == null)
        {
            TileItem.Link(source, target);
            changed++;
            Sfx.Play(SfxId.UiConfirm);
            NotificationUI.Instance?.Show($"연결됨: {LinkRules.NameOf(source)} - {LinkRules.NameOf(target)}");
        }
        else
        {
            Sfx.Play(SfxId.UiError);
            NotificationUI.Instance?.Show(reason);
            return;
        }

        if (keepGoing) endedFrame = Time.frameCount;
        else End(null);
    }

    private Vector3 ScreenToWorld(Vector3 screen)
    {
        screen.z = -cam.transform.position.z;
        var w = cam.ScreenToWorldPoint(screen);
        w.z = 0f;
        return w;
    }

    private void MoveHint(Vector3 screen)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRT, screen, null, out var local)) return;

        Vector2 half = canvasRT.rect.size * 0.5f;
        Vector2 size = hintRT.rect.size;
        var pos = local + new Vector2(18f, -22f);
        if (pos.x + size.x > half.x - 8f)  pos.x = local.x - 18f - size.x;
        if (pos.y - size.y < -half.y + 8f) pos.y = local.y + 22f + size.y;
        hintRT.anchoredPosition = pos;
    }

    private void Build()
    {
        line = WorldConnectionLine.Create(Vector3.zero, Vector3.zero, IdleColor, 0.06f, WorldConnectionLine.UnderSpritesOrder, transform);
        line.enabled = false;

        var canvasGO = new GameObject("LinkHintCanvas", typeof(RectTransform));
        canvasGO.transform.SetParent(transform, false);
        UIStyleKit.CreateOverlayCanvas(canvasGO, 650);
        canvasRT = (RectTransform)canvasGO.transform;

        hintRT = CuteUIKit.Pill(canvasGO.transform, "", CuteUIKit.Hex(0xFFF7EA, 0.95f), CuteUIKit.Cocoa, 12f, 10,
                                new RectOffset(10, 10, 4, 4), out hintText, fitSelf: true);
        hintRT.name  = "LinkHint";
        hintRT.pivot = new Vector2(0f, 1f);
        hintRT.anchorMin = hintRT.anchorMax = new Vector2(0.5f, 0.5f);
        hintRT.gameObject.SetActive(false);
    }
}
