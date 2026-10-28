using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(RectTransform))]
public class ShuttleUI : MonoBehaviour
{
    public static ShuttleUI Instance => Services.Get<ShuttleUI>();
    public static bool IsOpen => Instance != null && Instance.isOpen;

    public RectTransform HudPill => hudPill;

    private const KeyCode ToggleKey = KeyCode.J;
    private const float   Width     = 660f;

    private bool          isOpen;
    private RectTransform panelRT;
    private CanvasGroup   panelCG;
    private Coroutine     animCo;
    private Transform     list;
    private LayoutElement listBox;
    private ScrollRect    listScroll;
    private RectTransform focusCard, completeCard;
    private bool          scrollToFocus;
    private TextMeshProUGUI popText, nextText, fleetText;
    private Image           nextFill;
    private TextMeshProUGUI codexLabel;
    private Button          firstButton, firstLoadable, firstComplete;
    private Vector2Int?     focusPad;
    private bool            dirty;
    private float           nextTick;
    private int             shownHaveSignature;
    // 0.25초마다 글자만 갱신
    private readonly List<System.Action> live = new();
    private readonly Dictionary<int, RectTransform> shipIcons = new();

    private RectTransform hudPill;
    private static TextMeshProUGUI hudLabel;
    private static Image           hudBadge;
    private static TextMeshProUGUI hudBadgeText;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        hudLabel = null;
        hudBadge = null;
        hudBadgeText = null;
    }

    public static void Ensure()
    {
        if (Instance != null) return;
        Transform parent = GeneratorFuelUI.Instance != null ? GeneratorFuelUI.Instance.transform.parent : null;
        var go = new GameObject("ShuttleUI", typeof(RectTransform));
        if (parent != null) go.transform.SetParent(parent, false);
        else UIStyleKit.CreateOverlayCanvas(go, 300);
        go.AddComponent<ShuttleUI>();
        if (MobileMode.Active) Instance.StartCoroutine(Instance.BuildHudWhenReady());
    }

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }
        BuildPanel();
        panelRT.localScale = Vector3.zero;
        UIStyleKit.SetScrolling(panelRT, false);
    }

    private void Start()
    {
        if (ShuttleService.Instance != null) ShuttleService.Instance.OnChanged += MarkDirty;
        if (Inventory.Instance != null) Inventory.Instance.OnInventoryChanged += OnInventoryChanged;
    }

    private void OnDestroy()
    {
        if (ShuttleService.Instance != null) ShuttleService.Instance.OnChanged -= MarkDirty;
        if (Inventory.Instance != null) Inventory.Instance.OnInventoryChanged -= OnInventoryChanged;
        Services.Unregister(this);
    }

    private void MarkDirty() => dirty = true;

    // 짐 아이템 수 바뀔 때만 다시 그림
    private void OnInventoryChanged()
    {
        int sig = HaveSignature();
        if (sig == shownHaveSignature) return;
        shownHaveSignature = sig;
        dirty = true;
    }

    private static int HaveSignature()
    {
        var svc = ShuttleService.Instance;
        var inv = Inventory.Instance;
        if (svc == null || inv == null) return 0;
        unchecked
        {
            int h = 17;
            foreach (var s in svc.Shuttles)
                foreach (var l in s.cargo)
                {
                    var item = NetWorld.Item(l.item);
                    h = h * 31 + Mathf.Min(item != null ? inv.GetCount(item) : 0, Mathf.Max(1, l.Missing));
                }
            return h;
        }
    }

    public void Toggle()
    {
        if (isOpen) Close();
        else Open();
    }

    public void Open(ShuttlePad pad = null)
    {
        focusPad = pad != null ? pad.GridPosition : (Vector2Int?)null;
        scrollToFocus = true;
        if (isOpen) { Rebuild(); return; }
        isOpen = true;
        if (InventoryUI.Instance != null && InventoryUI.Instance.IsOpen) InventoryUI.Instance.Toggle();
        if (CraftingTableUI.Instance != null && CraftingTableUI.Instance.IsOpen) CraftingTableUI.Instance.Toggle();
        if (ShopUI.Instance != null && ShopUI.Instance.IsOpen) ShopUI.Instance.Toggle();
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
            UIStyleKit.SetScrolling(panelRT, true);
            CuteUIKit.WiggleEars(this, panelRT);
            yield return CuteUIKit.PopIn(panelRT, panelCG, 0.3f);
        }
        else
        {
            yield return CuteUIKit.PopOut(panelRT, panelCG, 0.16f);
            panelCG.alpha = 0f;
            UIStyleKit.SetScrolling(panelRT, false);
        }
    }

    private void Update()
    {
        if (!TextInputFocus.Active && GameInput.GetKeyDown(ToggleKey) && !PlayerTileController.IsPauseMenuOpen())
            Toggle();

        if (Time.unscaledTime >= nextTick)
        {
            nextTick = Time.unscaledTime + 0.25f;
            RefreshHud();
            if (isOpen) foreach (var a in live) a();
        }

        if (!isOpen) return;
        if (EscapeRouter.TryConsume(EscapeRouter.CancelPressed)) { Close(); return; }
        if (firstButton != null) UIStyleKit.KeepNavigable(firstButton.gameObject, transform);
    }

    private void LateUpdate()
    {
        if (!dirty) return;
        dirty = false;
        RefreshHud();
        if (isOpen) Rebuild();
    }

    private void Rebuild()
    {
        var svc = ShuttleService.Instance;
        for (int i = list.childCount - 1; i >= 0; i--) DestroyImmediate(list.GetChild(i).gameObject);
        firstButton = firstLoadable = firstComplete = null;
        focusCard = completeCard = null;
        live.Clear();
        shipIcons.Clear();
        if (svc == null) return;
        shownHaveSignature = HaveSignature();
        if (codexLabel != null) codexLabel.text = $"도감 {Codex.Total}";

        RefreshSummary();
        live.Add(RefreshSummary);

        var gm = GridManager.Instance;
        var pads = new List<TileItem>();
        gm?.CollectTiles(TileType.ShuttlePad, pads);
        pads.RemoveAll(p => p == null || p.IsGhost);

        foreach (var s in svc.Shuttles) BuildCard(s);
        if (svc.Shuttles.Count == 0) BuildNoShuttle(pads.Count);
        else if (pads.Count > svc.Shuttles.Count && svc.Shuttles.Count >= ShuttleService.Capacity)
            Note($"착륙장 {pads.Count}곳 · 셔틀 {svc.Shuttles.Count}척 — 통신탑을 세우면 셔틀이 한 척 더 와요 (최대 {ShuttleService.MaxShuttles}척)");
        else if (svc.Shuttles.Count < ShuttleService.Capacity)
            Note($"통신탑 덕분에 셔틀이 한 척 더 올 수 있어요 — 셔틀 착륙장을 하나 더 설치해요");
        FitList();
    }

    private void FitList()
    {
        var content = (RectTransform)list;
        var view    = listScroll.viewport;
        float keepY = content.anchoredPosition.y;

        listBox.preferredHeight = 0f;
        view.offsetMax = Vector2.zero;
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRT);
        float others = LayoutUtility.GetPreferredHeight(panelRT);
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        float want = LayoutUtility.GetPreferredHeight(content);

        float room = ScreenHeight() - others - 2f * (ScreenMargin + Mathf.Abs(panelRT.anchoredPosition.y));
        room = Mathf.Max(MinListHeight, room);
        if (want > room)
        {
            view.offsetMax = new Vector2(-14f, 0f);
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            want = LayoutUtility.GetPreferredHeight(content);
        }
        listBox.preferredHeight = Mathf.Min(want, room);
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRT);

        float max = Mathf.Max(0f, want - listBox.preferredHeight);
        float y = keepY;
        if (scrollToFocus)
        {
            var target = focusCard != null ? focusCard : completeCard;
            y = target != null ? -target.localPosition.y - target.rect.height * (1f - target.pivot.y) - 4f : 0f;
            scrollToFocus = false;
        }
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, Mathf.Clamp(y, 0f, max));
        listScroll.velocity = Vector2.zero;
    }

    private float ScreenHeight()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas != null) canvas = canvas.rootCanvas;
        return canvas != null ? ((RectTransform)canvas.transform).rect.height : 900f;
    }

    private void RefreshSummary()
    {
        int pop = ShuttleService.Population;
        popText.text = $"주민 <size=130%>{pop}</size>마리";
        var core = CenterCore.Current;
        var next = core != null ? core.NextStage : null;
        CoreRequirement req = null;
        if (next != null)
            foreach (var r in next.requirements)
                if (r.type == CoreRequirementType.Residents) { req = r; break; }
        if (req != null)
        {
            int target = CoreProgress.Target(req);
            nextText.text = pop >= target
                ? $"<color=#3F8A66>다음 문명(Lv.{core.Level + 1} {next.title}) 주민 조건 완료!</color>"
                : $"다음 문명(Lv.{core.Level + 1} {next.title})까지 주민 {pop}/{target}마리";
            nextFill.fillAmount = Mathf.Clamp01(pop / (float)Mathf.Max(1, target));
        }
        else
        {
            nextText.text = core != null && core.IsMaxLevel ? "모든 문명 단계 완료 — 큰 이주선이 남은 고양이들을 데려왔어요!" : "셔틀로 고향별 고양이들을 데려와요";
            nextFill.fillAmount = 1f;
        }
        int n = ShuttleService.Instance != null ? ShuttleService.Instance.Shuttles.Count : 0;
        fleetText.text = $"셔틀 {n}척";
    }

    private void BuildCard(ShuttleSave s)
    {
        bool focus = focusPad.HasValue && focusPad.Value.x == s.padX && focusPad.Value.y == s.padY;
        var card = Card(list, s.away ? CuteUIKit.Lighter(CuteUIKit.SkyTag, 0.35f) : CuteUIKit.LatteLight, $"Shuttle_{s.id}", focus);
        if (focus) focusCard = (RectTransform)card;

        var shipBox = new GameObject("Ship", typeof(RectTransform));
        shipBox.transform.SetParent(card, false);
        var sle = shipBox.AddComponent<LayoutElement>();
        sle.preferredWidth = sle.minWidth = 52f;
        sle.preferredHeight = sle.minHeight = 74f;
        var shipImg = new GameObject("Image", typeof(RectTransform)).AddComponent<Image>();
        shipImg.transform.SetParent(shipBox.transform, false);
        shipImg.sprite = ShipSprite;
        shipImg.enabled = shipImg.sprite != null;
        shipImg.preserveAspect = true;
        shipImg.raycastTarget = false;
        shipImg.color = s.away ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
        var srt = shipImg.rectTransform;
        srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
        srt.sizeDelta = new Vector2(44f, 68f);
        shipIcons[s.id] = srt;

        var mid = Column(card, flexible: true);
        var head = Row(mid);
        int cats = s.cats != null ? s.cats.Count : 0;
        string who = cats > 0 ? $"<b>{s.family} 가족</b> {cats}마리" : "빈 셔틀";

        if (s.away)
        {
            bool approaching = s.cargo.Count == 0;
            string what = approaching ? "새 착륙장으로 셔틀이 날아오는 중"
                        : s.padX == ShuttleService.NoPad ? $"{who}를 태우고 궤도에서 기다려요 — 착륙장을 설치해 주세요"
                        : $"{who}를 태우러 고향별에 다녀오는 중";
            var t = CuteUIKit.Text(head, what, 13f, CuteUIKit.Cocoa, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
            t.richText = true;
            if (cats > 0) Portraits(mid, s.cats);
            var bar = Bar(mid, out var fill);
            var left = CuteUIKit.Text(mid, "", 12f, CuteUIKit.SkyText, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            int id = s.id;
            System.Action tick = () =>
            {
                var cur = ShuttleService.Instance != null ? ShuttleService.Instance.Find(id) : null;
                if (cur == null || !cur.away) return;
                float total = Mathf.Max(0.1f, cur.tripTime);
                fill.fillAmount = Mathf.Clamp01(1f - cur.timer / total);
                int sec = Mathf.CeilToInt(cur.timer);
                left.text = cur.padX == ShuttleService.NoPad ? "착륙장을 기다리는 중" : $"{sec / 60}:{sec % 60:00} 뒤 도착";
            };
            tick();
            live.Add(tick);
            return;
        }

        CuteUIKit.Text(head, $"{who}를 태워 와요", 13f, CuteUIKit.Cocoa, FontStyles.Normal, TextAlignmentOptions.MidlineLeft).richText = true;
        if (cats > 0) Portraits(mid, s.cats);
        if (!string.IsNullOrEmpty(s.message))
        {
            var msg = CuteUIKit.Text(mid, s.message, 11f, CuteUIKit.CocoaSoft, FontStyles.Italic, TextAlignmentOptions.MidlineLeft);
            msg.textWrappingMode = TextWrappingModes.Normal;
        }

        var inv = Inventory.Instance;
        foreach (var line in s.cargo)
        {
            var item = NetWorld.Item(line.item);
            if (item == null) continue;
            int have = inv != null ? inv.GetCount(item) : 0;
            var cell = Row(mid);
            Icon(cell, item.icon, 26f);
            bool done = line.Missing <= 0;
            string count = done ? $"<color=#3F8A66>{line.amount}/{line.amount}</color>"
                                : $"<color=#{(line.loaded > 0 ? "A8741A" : "7A5A4F")}>{line.loaded}/{line.amount}</color>";
            string bag = done ? "" : have >= line.Missing ? $" <size=85%><color=#3F8A66>가방 {Mathf.Min(have, 999)}</color></size>"
                                                         : $" <size=85%><color=#C9485B>가방 {Mathf.Min(have, 999)}</color></size>";
            var lt = CuteUIKit.Text(cell, $"{item.itemName} {count}{bag}", 12f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            lt.richText = true;
        }

        var right = Column(card, flexible: false, width: 138f);
        var rewardRow = Row(right);
        var coin = Icon(rewardRow, CuteUIKit.Coin(), 18f);
        coin.color = Color.white;
        CuteUIKit.Text(rewardRow, CurrencyManager.Format(s.reward), 16f, CuteUIKit.AmberText, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        CuteUIKit.Text(rewardRow, "짐값", 10f, CuteUIKit.CocoaSoft, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        var bonusItem = NetWorld.Item(s.bonusItem);
        if (bonusItem != null && s.bonusAmount > 0)
            CuteUIKit.Text(right, $"덤: {bonusItem.itemName} ×{s.bonusAmount}", 10f, CuteUIKit.MintText, FontStyles.Bold);

        int loadable = ShuttleService.Loadable(s);
        bool complete = ShuttleService.CanComplete(s);
        int sid = s.id;
        Button load = null;
        load = CuteUIKit.Button(right, complete ? "싣고 출발!" : "싣기", loadable > 0 ? CuteUIKit.ButtonMint : CuteUIKit.ButtonLatte,
                                () => Load(sid, load), 124f, 30f, 13f);
        if (loadable > 0 && firstLoadable == null)
        {
            firstLoadable = load;
            load.gameObject.AddComponent<GamepadDefault>();
        }
        if (complete && firstComplete == null) { firstComplete = load; completeCard = (RectTransform)card; }
        CuteUIKit.Button(right, "다른 짐 받기", CuteUIKit.ButtonLatte, () => Reroll(sid), 124f, 24f, 11f);
        var padTile = ShuttleService.PadOf(s);
        if (padTile != null && (CenterCore.CurrentLevel >= 3 || padTile.Links.Count > 0))
        {
            int links = padTile.Links.Count;
            if (links == 0)
                CuteUIKit.Button(right, "창고 연결", CuteUIKit.ButtonSky, () => { Close(); LinkModeController.Begin(padTile); }, 124f, 22f, 10f);
            else
            {
                var linkRow = new GameObject("LinkRow", typeof(RectTransform));
                linkRow.transform.SetParent(right, false);
                var h = linkRow.AddComponent<HorizontalLayoutGroup>();
                h.spacing = 4f;
                h.childControlWidth = h.childControlHeight = true;
                h.childForceExpandWidth = h.childForceExpandHeight = false;
                CuteUIKit.Button(linkRow.transform, $"창고 {links}곳 연결됨", CuteUIKit.ButtonSky,
                                 () => { Close(); LinkModeController.Begin(padTile); }, 84f, 22f, 10f);
                CuteUIKit.Button(linkRow.transform, "끊기", CuteUIKit.ButtonCoral, () =>
                {
                    padTile.UnlinkAll();
                    Sfx.Play(SfxId.UiClose);
                    NotificationUI.Instance?.Show($"{LinkRules.NameOf(padTile)}: 창고 연결 {links}개를 끊었어요");
                    dirty = true;
                }, 36f, 22f, 10f);
            }
        }
        if (firstButton == null) firstButton = load;
    }

    private void BuildNoShuttle(int pads)
    {
        var card = Card(list, CuteUIKit.Lighter(CuteUIKit.PinkSoft, 0.3f), "NoShuttle", false);
        var mid = Column(card, flexible: true);
        if (pads > 0)
        {
            CuteUIKit.Text(mid, "셔틀이 착륙장으로 오고 있어요…", 13f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            return;
        }
        CuteUIKit.Text(mid, "셔틀 착륙장이 없어요", 14f, CuteUIKit.CoralText, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        var t = CuteUIKit.Text(mid, "착륙장을 설치하면 고향별 셔틀이 내려와요. [제작] → 문명 탭 → 셔틀 착륙장", 12f, CuteUIKit.CocoaSoft, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        t.textWrappingMode = TextWrappingModes.Normal;
        var btn = CuteUIKit.Button(card, "제작창에서 보기", CuteUIKit.ButtonMint, OpenPadRecipe, 130f, 30f, 12f);
        btn.gameObject.AddComponent<GamepadDefault>();
        firstButton = btn;
    }

    private static void OpenPadRecipe()
    {
        var cm = CraftingManager.Instance;
        var ui = CraftingTableUI.Instance;
        if (cm == null || ui == null) return;
        foreach (var r in cm.Recipes)
            if (r != null && r.outputItem == ShuttleService.PadItem)
            {
                if (!ui.IsOpen) ui.Open();
                ui.Focus(r);
                return;
            }
        if (!ui.IsOpen) ui.Open();
    }

    private void Note(string text)
    {
        var t = CuteUIKit.Text(list, text, 11f, CuteUIKit.CocoaSoft, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        t.textWrappingMode = TextWrappingModes.Normal;
    }

    public RectTransform LoadButtonRect
    {
        get
        {
            var b = firstComplete != null ? firstComplete : firstLoadable;
            return isOpen && b != null && b.gameObject.activeInHierarchy ? (RectTransform)b.transform : null;
        }
    }

    private void Load(int id, Button button)
    {
        var svc = ShuttleService.Instance;
        var s   = svc != null ? svc.Find(id) : null;
        if (s == null || s.away) return;
        long before = CurrencyManager.Instance != null ? CurrencyManager.Instance.Currency : 0;
        Vector3 from = button != null ? button.transform.position : panelRT.position;
        var got = new List<(FarmingItemData item, int amount)>();
        int n = svc.LoadFromBag(s, got);
        if (n <= 0)
        {
            Sfx.Play(SfxId.UiError);
            NotificationUI.Instance?.Show($"가방에 실을 짐이 없어요 — {MissingText(s)}");
            return;
        }
        Sfx.Play(SfxId.StoreItem);
        GamepadRumble.Play(Haptic.Store);
        shipIcons.TryGetValue(id, out var ship);
        foreach (var (item, _) in got) ItemCellUI.FlyIcon(this, item.icon, from, ship);
        ItemCellUI.FloatText(this, from + Vector3.up * 26f * ((RectTransform)transform).lossyScale.y, $"+{n} 실음", CuteUIKit.MintText, 16f, null);

        long gain = CurrencyManager.Instance != null ? CurrencyManager.Instance.Currency - before : 0;
        if (gain > 0)
        {
            CuteUIKit.SparkleBurst(this, panelRT, CuteUIKit.Butter, 14, 120f);
            var coins = CurrencyHUD.Instance;
            if (coins != null) ItemCellUI.CoinShower(this, from, (RectTransform)coins.transform, gain, null);
        }
        dirty = true;
    }

    private void Reroll(int id)
    {
        var svc = ShuttleService.Instance;
        var s   = svc != null ? svc.Find(id) : null;
        if (s == null || s.away) return;
        int loaded = ShuttleService.LoadedTotal(s);
        svc.Reroll(s);
        Sfx.Play(SfxId.UiConfirm, 0.7f);
        NotificationUI.Instance?.Show(loaded > 0 ? $"다른 가족의 짐을 받았어요 — 실어 둔 짐 {loaded}개는 가방으로 돌려받았어요" : "다른 가족의 짐을 받았어요");
        dirty = true;
    }

    public static string MissingText(ShuttleSave s)
    {
        var inv = Inventory.Instance;
        var parts = new List<string>();
        foreach (var l in s.cargo)
        {
            if (l.Missing <= 0) continue;
            var item = NetWorld.Item(l.item);
            if (item == null) continue;
            int lack = l.Missing - (inv != null ? inv.GetCount(item) : 0);
            if (lack > 0) parts.Add($"{item.itemName} {lack}개 더");
        }
        return parts.Count > 0 ? string.Join(" · ", parts) : "실을 수 있는 짐이 없어요";
    }

    private IEnumerator BuildHudWhenReady()
    {
        CurrencyHUD coins = null;
        for (int i = 0; i < 120 && coins == null; i++)
        {
            coins = CurrencyHUD.Instance;
            if (coins == null) yield return null;
        }
        if (coins == null) yield break;
        yield return null;

        var coinRT = (RectTransform)coins.transform;
        var go = new GameObject("ShuttleButton", typeof(RectTransform));
        go.transform.SetParent(coinRT.parent, false);
        var rt = hudPill = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = coinRT.anchoredPosition + new Vector2(0f, -(coinRT.rect.height + 8f));

        CuteUIKit.DecorateWindow(rt, catEars: false, radius: 16, border: 3);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(10, 12, 5, 5);
        hlg.spacing                = 6f;
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        var fit = go.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        var hit = go.AddComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0f);
        var btn = go.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(Toggle);
        go.AddComponent<CutePop>();

        hudLabel = CuteUIKit.Text(go.transform, "이주 셔틀", 13f, CuteUIKit.Cocoa, FontStyles.Bold);
        hudLabel.raycastTarget = false;

        var badge = CuteUIKit.Pill(go.transform, "0", CuteUIKit.Strawberry, Color.white, 11f, 9, new RectOffset(6, 6, 1, 1), out hudBadgeText);
        hudBadge = badge.GetComponent<Image>();
        badge.gameObject.SetActive(false);
        RefreshHud();
    }

    private static void RefreshHud()
    {
        if (hudLabel == null || hudBadge == null) return;
        var svc = ShuttleService.Instance;
        int ready = svc != null ? svc.CompletableCount() : 0;
        hudLabel.text = $"이주 셔틀 · 주민 {ShuttleService.Population}";
        hudBadge.gameObject.SetActive(ready > 0);
        if (ready > 0) hudBadgeText.text = $"출발 가능 {ready}";
    }

    public static Sprite ShipSprite
    {
        get
        {
            var prefab = ShuttleService.PadItem != null ? ShuttleService.PadItem.tilePrefab : null;
            var pad = prefab != null ? prefab.GetComponent<ShuttlePad>() : null;
            return pad != null ? pad.ShipSprite : null;
        }
    }

    private void BuildPanel()
    {
        panelRT = (RectTransform)transform;
        panelRT.anchorMin = panelRT.anchorMax = panelRT.pivot = new Vector2(0.5f, 0.5f);
        panelRT.anchoredPosition = new Vector2(0f, 20f);
        panelRT.sizeDelta        = new Vector2(Width, 0f);

        if (GetComponent<Image>() == null) gameObject.AddComponent<Image>();
        CuteUIKit.DecorateWindow(panelRT);

        panelCG = gameObject.AddComponent<CanvasGroup>();
        panelCG.alpha          = 0f;
        panelCG.blocksRaycasts = false;
        panelCG.interactable   = false;

        var vlg = gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding                = new RectOffset(18, 18, 16, 18);
        vlg.spacing                = 8f;
        vlg.childForceExpandWidth  = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(transform, false);
        var hlg = header.AddComponent<HorizontalLayoutGroup>();
        hlg.childAlignment         = TextAnchor.MiddleCenter;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        header.AddComponent<LayoutElement>().minHeight = 30f;
        CuteUIKit.TitleRibbon(header.transform, "이주 셔틀", "J");
        var close = CuteUIKit.CloseButton(header.transform, Close, 26f);
        close.GetComponent<LayoutElement>().ignoreLayout = true;
        var crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 0.5f);
        crt.sizeDelta        = new Vector2(26f, 26f);
        crt.anchoredPosition = new Vector2(-13f, 1f);

        var codexButton = CuteUIKit.Button(header.transform, "도감", CuteUIKit.ButtonButter, CodexUI.Toggle, 84f, 24f, 11f);
        var cle = codexButton.GetComponent<LayoutElement>();
        if (cle == null) cle = codexButton.gameObject.AddComponent<LayoutElement>();
        cle.ignoreLayout = true;
        var cdt = (RectTransform)codexButton.transform;
        cdt.anchorMin = cdt.anchorMax = new Vector2(0f, 0.5f);
        cdt.sizeDelta        = new Vector2(84f, 24f);
        cdt.anchoredPosition = new Vector2(42f, 1f);
        codexLabel = codexButton.GetComponentInChildren<TextMeshProUGUI>();

        var sum = CuteUIKit.Card(transform, CuteUIKit.ButterSoft, 14, "Summary");
        sum.raycastTarget = false;
        var shl = sum.gameObject.AddComponent<HorizontalLayoutGroup>();
        shl.padding                = new RectOffset(10, 12, 7, 7);
        shl.spacing                = 10f;
        shl.childAlignment         = TextAnchor.MiddleLeft;
        shl.childControlWidth      = true;
        shl.childControlHeight     = true;
        shl.childForceExpandWidth  = false;
        shl.childForceExpandHeight = false;
        Icon(sum.transform, HudDock.CatIcon, 34f);
        popText = CuteUIKit.Text(sum.transform, "", 16f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        popText.richText = true;
        popText.gameObject.AddComponent<LayoutElement>().minWidth = 120f;
        var col = Column(sum.transform, flexible: true);
        nextText = CuteUIKit.Text(col, "", 12f, CuteUIKit.Cocoa, FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
        nextText.richText = true;
        Bar(col, out nextFill, CuteUIKit.Strawberry);
        CuteUIKit.Pill(sum.transform, "셔틀 1척", CuteUIKit.SkyTag, CuteUIKit.SkyText, 11f, 9, new RectOffset(8, 8, 2, 3), out fleetText);

        var hint = CuteUIKit.Text(transform, "짐을 다 실으면 셔틀이 고향별에 다녀오며 고양이 가족을 태워 와요. 착륙장의 셔틀을 클릭해도 짐이 실려요.",
                                  11f, CuteUIKit.CocoaSoft, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        hint.textWrappingMode = TextWrappingModes.Normal;
        CuteUIKit.DottedDivider(transform);

        var boxGO = new GameObject("Shuttles", typeof(RectTransform));
        boxGO.transform.SetParent(transform, false);
        listBox = boxGO.AddComponent<LayoutElement>();
        var content = HelpUI.BuildScrollView((RectTransform)boxGO.transform, Vector2.zero, Vector2.zero);
        var lvlg = content.GetComponent<VerticalLayoutGroup>();
        lvlg.padding = new RectOffset(4, 4, 4, 4);
        lvlg.spacing = 6f;
        listScroll = boxGO.GetComponentInChildren<ScrollRect>();
        list = content;
    }

    private const float ScreenMargin  = 16f;
    private const float MinListHeight = 160f;

    private static Transform Card(Transform parent, Color fill, string name, bool focus)
    {
        var card = CuteUIKit.Card(parent, fill, 12, name);
        card.raycastTarget = false;
        if (focus)
        {
            var ring = CuteUIKit.Layer(card.transform, "Focus", CuteUIKit.Rounded(12), CuteUIKit.Strawberry, -3f);
            ring.raycastTarget = false;
            ring.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            ring.transform.SetAsFirstSibling();
            var inner = CuteUIKit.Layer(card.transform, "FocusFill", CuteUIKit.Rounded(12), fill, 0f);
            inner.raycastTarget = false;
            inner.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            inner.transform.SetSiblingIndex(1);
            card.color = Color.clear;
        }
        var hlg = card.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.padding                = new RectOffset(8, 10, 7, 7);
        hlg.spacing                = 10f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        return card.transform;
    }

    private static Transform Column(Transform parent, bool flexible, float width = -1f)
    {
        var go = new GameObject("Col", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.spacing                = 4f;
        vlg.childAlignment         = flexible ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
        vlg.childControlWidth      = true;
        vlg.childControlHeight     = true;
        vlg.childForceExpandWidth  = flexible;
        vlg.childForceExpandHeight = false;
        var le = go.AddComponent<LayoutElement>();
        if (flexible) { le.flexibleWidth = 1f; le.preferredWidth = 0f; }
        else le.preferredWidth = le.minWidth = width;
        return go.transform;
    }

    private static Transform Row(Transform parent)
    {
        var go = new GameObject("Row", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing                = 5f;
        hlg.childAlignment         = TextAnchor.MiddleLeft;
        hlg.childControlWidth      = true;
        hlg.childControlHeight     = true;
        hlg.childForceExpandWidth  = false;
        hlg.childForceExpandHeight = false;
        return go.transform;
    }

    private static Image Icon(Transform parent, Sprite sprite, float size)
    {
        var go = new GameObject("Icon", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.sprite         = sprite;
        img.preserveAspect = true;
        img.raycastTarget  = false;
        img.enabled        = sprite != null;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = le.minWidth = le.minHeight = size;
        return img;
    }

    private static RectTransform Bar(Transform parent, out Image fill, Color? color = null)
    {
        var bg = CuteUIKit.Card(parent, CuteUIKit.Lighter(CuteUIKit.Latte, 0.25f), 5, "Bar");
        bg.raycastTarget = false;
        var le = bg.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = 10f;
        le.flexibleWidth = 1f;
        fill = CuteUIKit.Layer(bg.transform, "Fill", CuteUIKit.Rounded(5), color ?? CuteUIKit.SkyText, 1.5f);
        fill.raycastTarget = false;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = 0;
        fill.fillAmount = 0f;
        return bg.rectTransform;
    }

    private static void Portraits(Transform parent, List<CatLook> cats)
    {
        var row = Row(parent);
        row.GetComponent<HorizontalLayoutGroup>().spacing = 1f;
        int shown = Mathf.Min(6, cats.Count);
        for (int i = 0; i < shown; i++)
        {
            var p = CatPortrait.Create(row, new Vector2(34f, 34f), 2, false, new Vector2(0f, -2f));
            p.Set(cats[i]);
        }
        if (cats.Count > shown)
            CuteUIKit.Pill(row, $"+{cats.Count - shown}", CuteUIKit.LatteLight, CuteUIKit.CocoaSoft, 11f, 8, new RectOffset(6, 6, 1, 2), out _);
        if (cats.Count > 4) return;
        var names = new List<string>();
        for (int i = 0; i < shown; i++) names.Add(cats[i].name);
        var gap = new GameObject("Gap", typeof(RectTransform));
        gap.transform.SetParent(row, false);
        gap.AddComponent<LayoutElement>().preferredWidth = 4f;
        var t = CuteUIKit.Text(row, string.Join(" · ", names), 10f, CuteUIKit.CocoaSoft, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        t.gameObject.AddComponent<LayoutElement>().minWidth = 10f;
    }
}
