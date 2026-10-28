using System.Collections.Generic;
using UnityEngine;

// 키마우스, 패드, 터치 입력 통합
[DefaultExecutionOrder(-100)]
public class InputManager : MonoBehaviour
{
    public static InputManager Instance => Services.Get<InputManager>();

    [Header("커서 설정")]
    [Tooltip("게임패드 자유 조준 최고 속도")]
    [SerializeField] private float cursorSpeed = 800f;
    [SerializeField] private float cursorSize  = 26f;

    private const float CursorSpriteScale = 1.4f;

    private const float ToolCursorScale = 1.3f;

    private const float AutoAimReturnDistance = 2.2f;
    private const int   PlantReachCells       = 2;
    private const int   DismantleReachCells   = 3;
    private const int   LinkReachCells        = 10;

    private RectTransform cursorRT;
    private Canvas        cursorCanvas;
    private UnityEngine.UI.Image cursorImage;
    private Sprite        ringSprite;

    private Camera   cam;
    private Player   player;
    private bool     aimValid;
    private bool     aimInit;
    private bool     freeAim;
    private Vector2  freeOffset;
    private float    walkedSinceAim;
    private Vector2  lastPlayerPos;
    private Vector2  aimPoint;
    private Vector2  reticlePoint;
    private bool     reticleStrong;
    private bool     hideReticle;
    private Vector2  reticleShown;
    private bool     reticleSnap = true;
    private ResourceBlock lockedBlock;
    private TileItem lockedDismantle;
    private bool     dismantleSpent;
    private TileItem interactTarget, dismantleTarget;
    private ResidentCat residentTarget;
    private string   useVerb;
    private readonly HashSet<TileItem> scanSeen = new();
    private readonly List<ResourceBlock> nearBlocks = new();
    private readonly Collider2D[] pointHits = new Collider2D[8];

    public bool UsingController => Pad.Active;

    public bool PadWorld => aimValid && Pad.World;

    public TileItem PadInteractTarget => PadWorld ? interactTarget : null;

    public TileItem PadDismantleTarget => PadWorld ? dismantleTarget : null;

    public ResidentCat PadResidentTarget => PadWorld ? residentTarget : null;

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }
        BuildCursorGraphic();
    }

    private void OnDestroy()
    {
        Services.Unregister(this);
    }

    private void Update()
    {
        aimValid = false;
        if (Pad.World) UpdateAim();
        else { lockedBlock = null; lockedDismantle = null; }
        if (!aimValid) reticleSnap = true;

        if (Pad.WorldDown(PadButton.Down)) QuestUI.Instance?.ToggleLog();
        AddWorldHints();
    }

    internal void LateTick()
    {
        if (cam == null) cam = Camera.main;
        bool show = aimValid && Pad.World && !hideReticle && cam != null;
        if (cursorCanvas != null && cursorCanvas.gameObject.activeSelf != show) cursorCanvas.gameObject.SetActive(show);

        if (show)
        {
            Vector2 target = cam.WorldToScreenPoint(reticlePoint);
            if (reticleSnap) { reticleShown = target; reticleSnap = false; }
            else reticleShown = Vector2.Lerp(reticleShown, target, 1f - Mathf.Exp(-28f * Time.unscaledDeltaTime));
            cursorRT.anchoredPosition = reticleShown;
            var c = cursorImage.color;
            c.a = reticleStrong ? 1f : 0.45f;
            cursorImage.color = c;
        }

        if (aimValid && Pad.World && cam != null && interactTarget != null)
            GamepadHud.ShowPrompt(cam.WorldToScreenPoint(PromptAnchor(interactTarget)),
                                  InteractVerb(interactTarget, PlayerTileController.Instance?.SelectedItem));

        if (aimValid && Pad.World && cam != null && residentTarget != null)
        {
            residentTarget.ShowHover(null);
            GamepadHud.ShowPrompt(cam.WorldToScreenPoint(residentTarget.PromptAnchor),
                                  ResidentBond.PadVerb(PlayerTileController.Instance?.SelectedItem));
        }
    }

    private void BuildCursorGraphic()
    {
        var canvasGO = new GameObject("VirtualCursorCanvas");
        canvasGO.transform.SetParent(transform, false);
        cursorCanvas = canvasGO.AddComponent<Canvas>();
        cursorCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        cursorCanvas.sortingOrder = 1000;

        var go = new GameObject("VirtualCursor", typeof(RectTransform));
        go.transform.SetParent(canvasGO.transform, false);
        cursorRT = (RectTransform)go.transform;
        cursorRT.anchorMin = cursorRT.anchorMax = Vector2.zero;
        cursorRT.pivot     = new Vector2(0.5f, 0.5f);
        cursorRT.sizeDelta = new Vector2(cursorSize, cursorSize) * CursorSpriteScale;

        var img = go.AddComponent<UnityEngine.UI.Image>();
        img.sprite        = BuildCursorSprite();
        img.color         = Color.white;
        img.raycastTarget = false;
        cursorImage = img;
        ringSprite  = img.sprite;

        canvasGO.SetActive(false);
    }

    public void SetCursorOverride(Sprite sprite, Vector2 pivot)
    {
        if (cursorImage == null) return;
        bool tool = sprite != null;
        cursorImage.sprite = tool ? sprite : ringSprite;
        cursorRT.pivot     = tool ? pivot : new Vector2(0.5f, 0.5f);
        cursorRT.sizeDelta = new Vector2(cursorSize, cursorSize) * (tool ? ToolCursorScale : CursorSpriteScale);
    }

    // 고양이 귀 링 커서 직접 그림
    private static Sprite BuildCursorSprite()
    {
        const int   size     = 64;
        const float ringR    = 17f;
        const float ringW    = 3f;
        const float outline  = 2.5f;
        const float earRound = 1.5f;

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px  = new Color32[size * size];
        var center = new Vector2(size * 0.5f, size * 0.5f);

        Vector2 Polar(float radius, float deg)
            => center + new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * radius;
        var earA   = Polar(ringR, 150f);
        var earB   = Polar(ringR, 105f);
        var earTip = Polar(28.5f, 125f);

        Color ring = CuteUIKit.Strawberry, edge = Color.white, dotEdge = CuteUIKit.Cocoa;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            var p  = new Vector2(x + 0.5f, y + 0.5f);
            var mp = new Vector2(size - p.x, p.y);
            float r = Vector2.Distance(p, center);

            float band  = Mathf.Abs(r - ringR) - ringW;
            float ears  = Mathf.Min(CursorTriangle(p, earA, earB, earTip), CursorTriangle(mp, earA, earB, earTip)) - earRound;
            float shape = Mathf.Min(band, ears);

            Color col = Color.clear;
            col = CursorOver(col, edge,    CursorCoverage(shape - outline));
            col = CursorOver(col, ring,    CursorCoverage(shape));
            col = CursorOver(col, dotEdge, CursorCoverage(r - 5.5f));
            col = CursorOver(col, edge,    CursorCoverage(r - 3.5f));
            px[y * size + x] = col;
        }
        tex.SetPixels32(px);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    private static float CursorCoverage(float distance) => Mathf.Clamp01(0.5f - distance);

    private static Color CursorOver(Color dst, Color src, float coverage)
    {
        float a    = src.a * coverage;
        float outA = a + dst.a * (1f - a);
        if (outA <= 0f) return Color.clear;
        Color c = (src * a + dst * (dst.a * (1f - a))) / outA;
        c.a = outA;
        return c;
    }

    private static float CursorTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        Vector2 e0 = b - a, e1 = c - b, e2 = a - c;
        Vector2 v0 = p - a, v1 = p - b, v2 = p - c;
        Vector2 pq0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
        Vector2 pq1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
        Vector2 pq2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
        float s  = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
        float d  = Mathf.Min(pq0.sqrMagnitude, Mathf.Min(pq1.sqrMagnitude, pq2.sqrMagnitude));
        float s0 = s * (v0.x * e0.y - v0.y * e0.x);
        float s1 = s * (v1.x * e1.y - v1.y * e1.x);
        float s2 = s * (v2.x * e2.y - v2.y * e2.x);
        return -Mathf.Sqrt(d) * Mathf.Sign(Mathf.Min(s0, Mathf.Min(s1, s2)));
    }

    private void UpdateAim()
    {
        var ptc = PlayerTileController.Instance;
        var gm  = GridManager.Instance;
        if (cam == null) cam = Camera.main;
        if (ptc == null || gm == null || cam == null) return;
        if (player == null) player = ptc.GetComponentInParent<Player>();

        Vector2 me     = ptc.transform.position;
        Rect    feet   = player != null ? player.FeetRect : new Rect(me + new Vector2(-0.25f, -0.43f), new Vector2(0.5f, 0.2f));
        Vector2 facing = player != null ? player.FacingDirection : Vector2.down;
        var     item   = ptc.SelectedItem;
        if (!aimInit) { aimInit = true; reticlePoint = me + facing * gm.CellSize; lastPlayerPos = me; }
        aimValid = true;

        float walked = Vector2.Distance(me, lastPlayerPos);
        lastPlayerPos = me;
        Vector2 rs = Pad.RightStick;
        if (rs.sqrMagnitude > 0.04f)
        {
            if (!freeAim) { freeAim = true; freeOffset = reticlePoint - me; }
            float px = cursorSpeed * Mathf.Pow(Mathf.Min(1f, rs.magnitude), 2f) * Time.unscaledDeltaTime;
            freeOffset    += rs.normalized * (px * WorldPerPixel());
            walkedSinceAim = 0f;
        }
        // 좀 걸으면 자동 조준으로 복귀
        else if (freeAim && !Pad.Held(PadButton.RT) && !Pad.Held(PadButton.LT))
        {
            walkedSinceAim += walked;
            if (walkedSinceAim > AutoAimReturnDistance) freeAim = false;
        }
        if (freeAim) freeOffset = ClampToView(me + freeOffset) - me;

        useVerb       = null;
        hideReticle   = false;
        reticleStrong = freeAim;

        bool link = LinkModeController.IsActive;
        if (link)                                                        AimLink(me, feet, facing);
        else if (item != null && item.itemType == FarmingItemType.Tile)  AimPlacement(item, me, feet, facing);
        else                                                             AimUse(item, me, feet, facing);

        interactTarget  = link ? null : FindInteractTarget(me, feet, facing);
        dismantleTarget = link ? null : FindDismantleTarget(me, feet, facing);
        residentTarget  = link ? null : FindResidentTarget(me, facing);
        if (residentTarget != null) interactTarget = null;
    }

    private ResidentCat FindResidentTarget(Vector2 me, Vector2 facing)
    {
        if (ResidentCat.All.Count == 0) return null;
        if (freeAim)
        {
            var aimed = ResidentCat.At(aimPoint);
            return aimed != null && Vector2.Distance(me, aimed.transform.position) <= PlayerTileController.InteractRange ? aimed : null;
        }

        const float Reach = 2.2f;
        ResidentCat best = null;
        float bestScore = float.MaxValue;
        foreach (var c in ResidentCat.All)
        {
            if (c == null || !c.Interactable) continue;
            Vector2 to = (Vector2)c.transform.position - me;
            if (to.sqrMagnitude > Reach * Reach) continue;
            float s = Score(to, facing);
            if (s < bestScore) { bestScore = s; best = c; }
        }
        if (best != null && interactTarget != null && Score(ClosestOnFootprint(interactTarget, me) - me, facing) < bestScore) return null;
        return best;
    }

    private void AimPlacement(FarmingItemData item, Vector2 me, Rect feet, Vector2 facing)
    {
        var gm   = GridManager.Instance;
        float cs = gm.CellSize;
        int effW = item.tileWidth;
        int effH = item.tileHeight;

        Vector2Int anchor = freeAim
            ? gm.WorldToGrid(me + freeOffset - new Vector2(effW - 1, effH - 1) * (cs * 0.5f))
            : FrontAnchor(feet, facing, effW, effH);

        aimPoint     = gm.GridToWorld(anchor);
        reticlePoint = gm.GridToWorldCenter(anchor, effW, effH);
        hideReticle  = true;
        useVerb      = "설치";
    }

    private void AimUse(FarmingItemData item, Vector2 me, Rect feet, Vector2 facing)
    {
        var gm      = GridManager.Instance;
        bool seed   = item != null && item.itemType == FarmingItemType.Seed;
        bool sickle = item != null && item.itemType == FarmingItemType.Tool && item.harvestRadius > 0;
        bool fert   = item != null && item.IsFertilizer;

        if (freeAim)
        {
            lockedBlock  = null;
            aimPoint     = reticlePoint = me + freeOffset;
            useVerb      = VerbAt(aimPoint, item, me);
            return;
        }

        if (!seed && !sickle && !fert)
        {
            var block = PickBlock(me, facing);
            if (block != null)
            {
                aimPoint      = reticlePoint = block.transform.position;
                reticleStrong = true;
                useVerb       = "채굴";
                return;
            }
        }
        lockedBlock = null;

        var plant = PickPlant(feet, facing, allowEmpty: seed, fertilize: fert);
        if (plant != null)
        {
            aimPoint      = reticlePoint = gm.GridToWorld(plant.GridPosition);
            reticleStrong = true;
            useVerb       = plant.CurrentState == PlantTile.PlantState.ReadyToHarvest ? "수확" : fert ? "퇴비 주기" : "심기";
            return;
        }

        aimPoint = reticlePoint = gm.GridToWorld(FrontAnchor(feet, facing, 1, 1));
    }

    private void AimLink(Vector2 me, Rect feet, Vector2 facing)
    {
        var gm = GridManager.Instance;
        reticleStrong = true;
        if (freeAim)
        {
            aimPoint = reticlePoint = me + freeOffset;
            return;
        }

        var source = LinkModeController.Source;
        TileItem best = null;
        float bestScore = float.MaxValue;
        scanSeen.Clear();
        Vector2Int c = gm.WorldToGrid(feet.center);
        for (int dx = -LinkReachCells; dx <= LinkReachCells; dx++)
        for (int dy = -LinkReachCells; dy <= LinkReachCells; dy++)
        {
            var t = gm.GetTile(c + new Vector2Int(dx, dy));
            if (t == null || t.IsGhost || t == source || !scanSeen.Add(t)) continue;
            if (source == null || !(source.IsLinkedTo(t) || LinkRules.CanLink(source, t, out _))) continue;
            float s = Score(ClosestOnFootprint(t, me) - me, facing);
            if (s < bestScore) { bestScore = s; best = t; }
        }

        if (best != null) aimPoint = reticlePoint = best.transform.position;
        else
        {
            aimPoint      = reticlePoint = gm.GridToWorld(FrontAnchor(feet, facing, 1, 1));
            reticleStrong = false;
        }
    }

    private ResourceBlock PickBlock(Vector2 me, Vector2 facing)
    {
        float range = PlayerTileController.MiningRange;
        if (lockedBlock != null && Pad.Held(PadButton.RT) && Vector2.Distance(me, lockedBlock.transform.position) <= range)
            return lockedBlock;
        lockedBlock = null;

        int grade = PlayerTileController.Instance.CurrentToolGrade;
        ResourceBlock best = null;
        float bestScore = float.MaxValue;
        ResourceBlock.Near(new Rect(me.x - range, me.y - range, range * 2f, range * 2f), nearBlocks);
        foreach (var b in nearBlocks)
        {
            if (b == null || b.Data == null) continue;
            Vector2 to = (Vector2)b.transform.position - me;
            if (to.sqrMagnitude > range * range) continue;
            float s = Score(to, facing);
            // 못 캐는 광물은 후순위
            if (b.Data.minToolGrade > grade) s += 3f;
            if (s < bestScore) { bestScore = s; best = b; }
        }
        if (best != null && Pad.Held(PadButton.RT)) lockedBlock = best;
        return best;
    }

    private PlantTile PickPlant(Rect feet, Vector2 facing, bool allowEmpty, bool fertilize)
    {
        var gm = GridManager.Instance;
        Vector2Int c = gm.WorldToGrid(feet.center);
        PlantTile best = null;
        float bestScore = float.MaxValue;
        for (int dx = -PlantReachCells; dx <= PlantReachCells; dx++)
        for (int dy = -PlantReachCells; dy <= PlantReachCells; dy++)
        {
            var cell = c + new Vector2Int(dx, dy);
            if (gm.GetTile(cell) is not PlantTile plant || plant.IsGhost) continue;
            bool ready = plant.CurrentState == PlantTile.PlantState.ReadyToHarvest;
            bool empty = plant.CurrentState == PlantTile.PlantState.Empty;
            bool feed  = fertilize && plant.Fertility < PlantTile.MaxFertility;
            if (!ready && !(allowEmpty && empty) && !feed) continue;
            float s = Score((Vector2)gm.GridToWorld(cell) - feet.center, facing) + (ready ? 0f : 0.35f);
            if (s < bestScore) { bestScore = s; best = plant; }
        }
        return best;
    }

    private TileItem FindInteractTarget(Vector2 me, Rect feet, Vector2 facing)
    {
        var gm = GridManager.Instance;
        float range = PlayerTileController.InteractRange;
        if (freeAim)
        {
            var at = gm.GetTile(gm.WorldToGrid(aimPoint));
            if (IsInteractable(at) && at.DistanceFrom(me) <= range) return at;
        }

        int r = Mathf.CeilToInt(range / gm.CellSize) + 1;
        Vector2Int c = gm.WorldToGrid(feet.center);
        TileItem best = null;
        float bestScore = float.MaxValue;
        scanSeen.Clear();
        for (int dx = -r; dx <= r; dx++)
        for (int dy = -r; dy <= r; dy++)
        {
            var t = gm.GetTile(c + new Vector2Int(dx, dy));
            if (t == null || !scanSeen.Add(t) || !IsInteractable(t) || t.DistanceFrom(me) > range) continue;
            float s = Score(ClosestOnFootprint(t, me) - me, facing) + (t is PlantTile ? 0.3f : 0f);
            if (s < bestScore) { bestScore = s; best = t; }
        }
        return best;
    }

    private TileItem FindDismantleTarget(Vector2 me, Rect feet, Vector2 facing)
    {
        bool held = Pad.Held(PadButton.LT);
        if (!held) dismantleSpent = false;
        if (lockedDismantle != null && held && lockedDismantle.CanDismantle) return lockedDismantle;
        // 해체 끝나면 버튼 뗄 때까지 대기
        if (held && !ReferenceEquals(lockedDismantle, null) && lockedDismantle == null) dismantleSpent = true;
        lockedDismantle = null;
        if (dismantleSpent) return null;

        var gm = GridManager.Instance;
        TileItem best = gm.GetTile(gm.WorldToGrid(aimPoint));
        if (best != null && (best.IsGhost || !best.CanDismantle)) best = null;

        if (best == null && !freeAim)
        {
            Vector2Int c = gm.WorldToGrid(feet.center);
            float bestScore = float.MaxValue;
            scanSeen.Clear();
            for (int dx = -DismantleReachCells; dx <= DismantleReachCells; dx++)
            for (int dy = -DismantleReachCells; dy <= DismantleReachCells; dy++)
            {
                var t = gm.GetTile(c + new Vector2Int(dx, dy));
                if (t == null || t.IsGhost || !t.CanDismantle || !scanSeen.Add(t)) continue;
                float s = Score(ClosestOnFootprint(t, me) - me, facing);
                if (s < bestScore) { bestScore = s; best = t; }
            }
        }
        if (best != null && Pad.Held(PadButton.LT)) lockedDismantle = best;
        return best;
    }

    private static bool IsInteractable(TileItem t)
        => t != null && !t.IsGhost &&
           (t is CenterCore || t is SolarPanel || LinkRules.IsLinkable(t) || (t is Generator g && g.UsesFuel) ||
            (t is PlantTile p && p.CurrentState == PlantTile.PlantState.ReadyToHarvest));

    private static string InteractVerb(TileItem t, FarmingItemData item) => t switch
    {
        PlantTile                                                                => "수확",
        ShuttlePad pad when pad.Shuttle != null && ShuttleService.Loadable(pad.Shuttle) > 0 => "짐 싣기",
        ShuttlePad                                                               => "셔틀",
        SolarPanel                                                               => "정보",
        Generator g when g.UsesFuel && item != null && item.canUseFuel           => "연료 넣기",
        AutoSeeder when item != null && item.itemType == FarmingItemType.Seed    => "씨앗 넣기",
        _                                                                        => "열기",
    };

    private string VerbAt(Vector2 p, FarmingItemData item, Vector2 me)
    {
        var gm = GridManager.Instance;
        bool seed = item != null && item.itemType == FarmingItemType.Seed;
        int n = Physics2D.OverlapPoint(p, ContactFilter2D.noFilter, pointHits);
        for (int i = 0; i < n; i++)
            if (pointHits[i] != null && pointHits[i].GetComponent<ResourceBlock>() != null)
                return !seed && Vector2.Distance(me, pointHits[i].transform.position) <= PlayerTileController.MiningRange ? "채굴" : null;
        if (gm.GetTile(gm.WorldToGrid(p)) is PlantTile plant)
        {
            if (plant.CurrentState == PlantTile.PlantState.ReadyToHarvest) return "수확";
            if (seed && plant.CurrentState == PlantTile.PlantState.Empty) return "심기";
            if (item != null && item.IsFertilizer && plant.Fertility < PlantTile.MaxFertility) return "퇴비 주기";
        }
        return null;
    }

    private static Vector2Int FrontAnchor(Rect feet, Vector2 facing, int w, int h)
    {
        var gm = GridManager.Instance;
        Vector2Int c = gm.WorldToGrid(feet.center);
        if (facing.x >  0.5f) return new Vector2Int(gm.WorldToGrid(new Vector2(feet.xMax, feet.center.y)).x + 1, c.y - (h - 1) / 2);
        if (facing.x < -0.5f) return new Vector2Int(gm.WorldToGrid(new Vector2(feet.xMin, feet.center.y)).x - w, c.y - (h - 1) / 2);
        if (facing.y >  0.5f) return new Vector2Int(c.x - (w - 1) / 2, gm.WorldToGrid(new Vector2(feet.center.x, feet.yMax)).y + 1);
        return                       new Vector2Int(c.x - (w - 1) / 2, gm.WorldToGrid(new Vector2(feet.center.x, feet.yMin)).y - h);
    }

    private static Vector2 ClosestOnFootprint(TileItem t, Vector2 p)
    {
        var gm   = GridManager.Instance;
        float cs = gm.CellSize;
        Vector2 min = gm.GridOrigin + (Vector2)t.FootprintMin * cs;
        Vector2 max = gm.GridOrigin + ((Vector2)t.FootprintMax + Vector2.one) * cs;
        return new Vector2(Mathf.Clamp(p.x, min.x, max.x), Mathf.Clamp(p.y, min.y, max.y));
    }

    // 바라보는 방향일수록 점수 낮음
    private static float Score(Vector2 to, Vector2 facing)
    {
        float d = to.magnitude;
        if (d < 0.001f) return 0f;
        float cos = Vector2.Dot(to / d, facing);
        return d * (1f + 0.9f * (1f - cos));
    }

    private static Vector3 PromptAnchor(TileItem t)
    {
        if (t is ShuttlePad pad) return pad.HintAnchor;
        var sr = t.GetComponentInChildren<SpriteRenderer>();
        float top = sr != null ? sr.bounds.max.y : t.transform.position.y + 0.4f;
        return new Vector3(t.transform.position.x, top + 0.1f, 0f);
    }

    private float WorldPerPixel()
    {
        float depth = -cam.transform.position.z;
        Vector3 a = cam.ScreenToWorldPoint(new Vector3(0f, 0f, depth));
        Vector3 b = cam.ScreenToWorldPoint(new Vector3(0f, 100f, depth));
        return (b - a).magnitude / 100f;
    }

    private Vector2 ClampToView(Vector2 w)
    {
        Vector3 s = cam.WorldToScreenPoint(new Vector3(w.x, w.y, 0f));
        const float Margin = 24f;
        s.x = Mathf.Clamp(s.x, Margin, Screen.width - Margin);
        s.y = Mathf.Clamp(s.y, Margin, Screen.height - Margin);
        s.z = -cam.transform.position.z;
        return cam.ScreenToWorldPoint(s);
    }

    private void AddWorldHints()
    {
        if (!PadWorld) return;
        var h   = GamepadHud.Hints;
        var ptc = PlayerTileController.Instance;

        if (LinkModeController.IsActive)
        {
            var gm     = GridManager.Instance;
            var target = gm.GetTile(gm.WorldToGrid(aimPoint));
            var source = LinkModeController.Source;
            h.Add(new PadHint(PadGlyph.A, source != null && source.IsLinkedTo(target) ? "끊기" : "잇기"));
            h.Add(new PadHint(PadGlyph.RStick, "조준"));
            h.Add(new PadHint(PadGlyph.B, "그만"));
            return;
        }

        if (useVerb != null)         h.Add(new PadHint(PadGlyph.RT, useVerb));
        if (dismantleTarget != null) h.Add(new PadHint(PadGlyph.LT, "해체"));
        if (ptc.SelectedItem != null) h.Add(new PadHint(PadGlyph.B, "빈손"));
        h.Add(new PadHint(PadGlyph.LB, PadGlyph.RB, "핫바"));
        h.Add(new PadHint(PadGlyph.Y, "가방"));
        h.Add(new PadHint(PadGlyph.Back, "미니맵"));
    }

    public Vector3 CursorScreenPos
    {
        get
        {
            if (GamepadPointer.Active) return GamepadPointer.Position;
            if (PadWorld && cam != null) return cam.WorldToScreenPoint(new Vector3(aimPoint.x, aimPoint.y, 0f));
            return GameInput.MousePosition;
        }
    }

    public Vector2 MoveVector
    {
        get
        {
            if (TextInputFocus.Active) return Vector2.zero;
            var kb = new Vector2(
                (GameInput.GetKey(KeyCode.D) || GameInput.GetKey(KeyCode.RightArrow) ? 1f : 0f) - (GameInput.GetKey(KeyCode.A) || GameInput.GetKey(KeyCode.LeftArrow) ? 1f : 0f),
                (GameInput.GetKey(KeyCode.W) || GameInput.GetKey(KeyCode.UpArrow)    ? 1f : 0f) - (GameInput.GetKey(KeyCode.S) || GameInput.GetKey(KeyCode.DownArrow) ? 1f : 0f));
            if (kb == Vector2.zero && TouchInput.StickActive) return TouchInput.Stick;
            if (kb != Vector2.zero || !Pad.World) return kb;

            Vector2 s = Pad.LeftStick;
            float m = s.magnitude;
            return m < 0.01f ? Vector2.zero : s * (Mathf.Min(1f, m * 1.15f) / m);
        }
    }

    public bool ActionDown => GameInput.GetMouseButtonDown(0) || Pad.WorldDown(PadButton.RT);
    public bool ActionHeld => GameInput.GetMouseButton(0)     || Pad.WorldHeld(PadButton.RT);

    public bool RemoveDown => GameInput.GetMouseButtonDown(1) || Pad.WorldDown(PadButton.LT);
    public bool RemoveHeld => GameInput.GetMouseButton(1)     || Pad.WorldHeld(PadButton.LT);

    public bool InteractDown    => !TextInputFocus.Active && GameInput.GetKeyDown(KeyCode.F);
    public bool PadInteractDown => PadWorld && Pad.Down(PadButton.A);

    public bool ConfirmDown => GameInput.GetMouseButtonDown(0) || Pad.WorldDown(PadButton.A);

    public bool PrevSlot     => !TextInputFocus.Active && (GameInput.GetKeyDown(KeyCode.Q) || Pad.WorldDown(PadButton.LB));
    public bool NextSlot     => !TextInputFocus.Active && (GameInput.GetKeyDown(KeyCode.E) || Pad.WorldDown(PadButton.RB));
    public bool DeselectDown => Pad.WorldDown(PadButton.B);

    public float ScrollDelta => GameInput.MouseScrollDelta.y;

    public bool CraftingButton  => Pad.WorldDown(PadButton.Up);
    public bool ShopButton      => Pad.WorldDown(PadButton.Right);
    public bool TechButton      => Pad.WorldDown(PadButton.Left);
    public bool InventoryButton => Pad.WorldDown(PadButton.Y);
    public bool SaveMenuButton  => Pad.ActiveDown(PadButton.Start);
    public bool MinimapButton   => Pad.ActiveDown(PadButton.Back);
    public bool GridButton      => Pad.WorldDown(PadButton.L3);
}
