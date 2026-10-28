using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

// 가상 마우스로 UI 조작
public static class GamepadPointer
{
    private static Mouse   device;
    private static Vector2 pos;
    private static bool    parked;
    private static bool    leftDown;
    private static bool    carrying;
    private static int     nudgeFrames;
    private static int     carryCheckFrame;
    private static GameObject current, pressTarget;
    private static float   nextRepeatClick;
    private static int     repeatClicks;
    private static Vector2Int heldDir;
    private static float   nextMove;
    private static int     moveRepeats;
    private static bool    scrolling;
    private static bool    wasActive;
    private static bool    padWasActive;
    private static Transform pendingFocus;
    private static float   pendingUntil;
    private static GameObject pendingPick;
    private static Rect    pendingRect;
    private static int     pendingStable;
    private static bool    navBlocked;

    private static float   scrollDetent;
    private static bool    navEdgeBumped, scrollEdgeBumped;
    private const  float   ScrollDetentPx = 100f;

    private static List<GamepadPanels.Panel> panels => GamepadPanels.Open;
    private static readonly HashSet<Transform>                 lastOpen   = new();
    private static readonly HashSet<Transform>                 nowOpen    = new();
    private static readonly Dictionary<Transform, GameObject>  remembered = new();

    private static readonly List<GameObject>   cands   = new();
    private static readonly HashSet<GameObject> candSet = new();
    private static readonly List<Selectable>    selBuf  = new();
    private static readonly List<MonoBehaviour> mbBuf   = new();
    private static readonly List<ScrollRect>    srBuf   = new();
    private static readonly List<RaycastResult> hits    = new();
    private static readonly Vector3[]           corners = new Vector3[4];
    private static PointerEventData ped;
    private static EventSystem pedOwner;

    public static bool Active { get; private set; }

    public static Vector2 Position => pos;

    public static Vector2 TooltipAnchor
    {
        get
        {
            if (current == null) return pos;
            var r = ScreenRect(current);
            return new Vector2(r.xMax - 6f, r.yMax + 8f);
        }
    }

    internal static void Tick()
    {
        var es = EventSystem.current;

        if (!Pad.Active)
        {
            // 클릭이 엉뚱한 곳에 가지 않게
            if (padWasActive)
            {
                padWasActive = false;
                ReleaseCarry();
                pos = GameInput.MousePosition;
                SendState(pos);
            }
            parked = false;
            Active = wasActive = false;
            lastOpen.Clear();
            return;
        }
        padWasActive = true;

        if (es != null) ClearStraySelection(es);

        if (!Pad.UIMode || es == null)
        {
            if (leftDown || carrying) ReleaseCarry();
            if (!parked) Park();
            Active = wasActive = false;
            lastOpen.Clear();
            return;
        }

        EnsureDevice();
        Active = true;
        bool justEntered = !wasActive;
        wasActive = true;
        parked = false;

        UpdateFocus(justEntered);
        HandleButtons();
        HandleNavigation();
        HandleScroll();

        if (!scrolling && current != null) pos = ScreenRect(current).center;
        Vector2 send = pos;
        if (nudgeFrames > 0) { send += new Vector2(12f, 0f); nudgeFrames--; }
        SendState(send);

        if (carrying && Time.frameCount >= carryCheckFrame && (InventoryUI.Instance == null || !InventoryUI.Instance.IsDragging))
        {
            carrying = false;
            leftDown = false;
            SendState(pos);
        }

        if (current != null) GamepadHud.ShowSelection(ScreenRect(current), carrying);
        AddHints();
    }

    internal static void Shutdown()
    {
        if (device != null && device.added) InputSystem.RemoveDevice(device);
        device = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Shutdown();
        pos = default;
        parked = leftDown = carrying = false;
        nudgeFrames = carryCheckFrame = 0;
        current = pressTarget = null;
        nextRepeatClick = 0f;
        repeatClicks = 0;
        heldDir = default;
        nextMove = 0f;
        moveRepeats = 0;
        scrolling = wasActive = padWasActive = false;
        pendingFocus = null;
        pendingUntil = 0f;
        pendingPick = null;
        pendingRect = default;
        pendingStable = 0;
        navBlocked = false;
        scrollDetent = 0f;
        navEdgeBumped = scrollEdgeBumped = false;
        lastOpen.Clear();
        nowOpen.Clear();
        remembered.Clear();
        cands.Clear();
        candSet.Clear();
        ped = null;
        pedOwner = null;
        Active = false;
    }

    private static void EnsureDevice()
    {
        if (device != null && device.added) return;
        device = InputSystem.AddDevice<Mouse>("GamepadUIPointer");
        // Mouse.current 뺏기지 않게
        foreach (var d in InputSystem.devices)
            if (d is Mouse m && m != device) { m.MakeCurrent(); break; }
    }

    private static void SendState(Vector2 p)
    {
        if (device == null || !device.added) return;
        var state = new MouseState { position = p }.WithButton(MouseButton.Left, leftDown);
        InputState.Change(device, state);
    }

    private static void Park()
    {
        EnsureDevice();
        pos = new Vector2(-300f, -300f);
        SendState(pos);
        parked = true;
        current = null;
    }

    private static void UpdateFocus(bool justEntered)
    {
        float now = Time.unscaledTime;

        nowOpen.Clear();
        Transform newest = null;
        foreach (var p in panels)
        {
            nowOpen.Add(p.root);
            if (!lastOpen.Contains(p.root)) newest = p.root;
        }
        lastOpen.Clear();
        lastOpen.UnionWith(nowOpen);
        if (newest != null) BeginPendingFocus(newest);

        if (pendingFocus != null)
        {
            // 창 애니메이션 끝날 때까지 기다림
            var target = (RemembersFocus(pendingFocus) ? RememberedIn(pendingFocus) : null) ?? DefaultIn(pendingFocus);
            if (target != null)
            {
                Rect r = ScreenRect(target);
                bool same = target == pendingPick && Mathf.Abs(r.x - pendingRect.x) < 0.5f && Mathf.Abs(r.y - pendingRect.y) < 0.5f
                                                  && Mathf.Abs(r.width - pendingRect.width) < 0.5f && Mathf.Abs(r.height - pendingRect.height) < 0.5f;
                pendingStable = same ? pendingStable + 1 : 0;
                pendingPick   = target;
                pendingRect   = r;
                current       = target;
                if (pendingStable >= 2 || now > pendingUntil) pendingFocus = null;
            }
            else if (now > pendingUntil || !nowOpen.Contains(pendingFocus)) pendingFocus = null;
        }

        if (current != null && !Usable(current)) current = null;
        if (current == null && !scrolling && pendingFocus == null) current = NearestTo(pos);

        if (current != null)
        {
            var owner = PanelOf(current);
            if (owner != null) remembered[owner] = current;
            if (justEntered) pos = ScreenRect(current).center;
        }
    }

    // 창 연 방향 입력은 뗄 때까지 무시
    private static void BeginPendingFocus(Transform root)
    {
        pendingFocus  = root;
        pendingUntil  = Time.unscaledTime + 1f;
        pendingPick   = null;
        pendingStable = 0;
        navBlocked    = true;
    }

    private static bool RemembersFocus(Transform root)
    {
        foreach (var p in panels)
            if (p.root == root) return p.ring >= 0 && p.ring <= 3;
        return false;
    }

    private static GameObject RememberedIn(Transform root)
        => remembered.TryGetValue(root, out var go) && go != null && go.transform.IsChildOf(root) && Usable(go) ? go : null;

    private static GameObject DefaultIn(Transform root)
    {
        Collect(root);
        GameObject best = null;
        foreach (var go in cands)
        {
            if (!Reachable(go, out bool viaScroll) || viaScroll) continue;
            if (go.GetComponent<GamepadDefault>() != null) return go;
            if (go.TryGetComponent(out InventorySlotUI slot) && slot.SlotIndex == 0) return go;
        }

        root.GetComponentsInChildren(false, srBuf);
        foreach (var sr in srBuf)
        {
            if (!sr.horizontal || !sr.vertical) continue;
            Vector2 center = ScreenRect(sr.viewport != null ? sr.viewport.gameObject : sr.gameObject).center;
            float bestD = float.MaxValue;
            foreach (var go in cands)
            {
                if (!go.transform.IsChildOf(sr.transform) || !Reachable(go, out bool viaScroll) || viaScroll) continue;
                float d = (ScreenRect(go).center - center).sqrMagnitude;
                if (d < bestD) { bestD = d; best = go; }
            }
            if (best != null) return best;
        }

        float topY = float.MinValue;
        foreach (var go in cands)
        {
            if (IsCloseButton(go) || !Reachable(go, out bool viaScroll) || viaScroll) continue;
            topY = Mathf.Max(topY, ScreenRect(go).center.y);
        }
        float bestX = float.MaxValue;
        foreach (var go in cands)
        {
            if (IsCloseButton(go) || !Reachable(go, out bool viaScroll) || viaScroll) continue;
            var c = ScreenRect(go).center;
            if (c.y < topY - 14f || c.x >= bestX) continue;
            bestX = c.x;
            best  = go;
        }
        if (best != null) return best;

        foreach (var go in cands)
            if (Reachable(go, out bool viaScroll) && !viaScroll) return go;
        return null;
    }

    private static GameObject NearestTo(Vector2 p)
    {
        CollectAll();
        GameObject best = null;
        float bestD = float.MaxValue;
        foreach (var go in cands)
        {
            if (!Reachable(go, out bool viaScroll) || viaScroll) continue;
            float d = (ScreenRect(go).center - p).sqrMagnitude;
            if (d < bestD) { bestD = d; best = go; }
        }
        return best;
    }

    private static bool Usable(GameObject go)
        => go != null && go.activeInHierarchy && PanelOf(go) != null && Reachable(go, out bool viaScroll) && !viaScroll;

    private static Transform PanelOf(GameObject go)
    {
        foreach (var p in panels)
            if (go.transform.IsChildOf(p.root)) return p.root;
        return null;
    }

    private static int FocusRing()
    {
        if (current != null)
            foreach (var p in panels)
                if (p.ring >= 0 && current.transform.IsChildOf(p.root)) return p.ring;
        for (int i = panels.Count - 1; i >= 0; i--)
            if (panels[i].ring >= 0) return panels[i].ring;
        return -1;
    }

    private static bool IsCloseButton(GameObject go) => go.name == "Btn_Close";

    private static void HandleButtons()
    {
        float now = Time.unscaledTime;

        if (Pad.Down(PadButton.A))
        {
            if (carrying)
            {
                carrying = false;
                leftDown = false;
                GamepadRumble.Play(Haptic.UiDrop);
            }
            else if (current != null && current.GetComponent<Slider>() == null)
            {
                pendingFocus    = null;
                leftDown        = true;
                pressTarget     = current;
                repeatClicks    = 0;
                nextRepeatClick = now + 0.4f;
                navEdgeBumped   = false;
                GamepadRumble.Play(Haptic.UiClick);
            }
        }
        else if (leftDown && !carrying)
        {
            if (Pad.Held(PadButton.A))
            {
                if (pressTarget != null && pressTarget == current && now >= nextRepeatClick
                    && pressTarget.GetComponent<GamepadHoldRepeat>() != null
                    && pressTarget.TryGetComponent(out Button b) && b.IsInteractable())
                {
                    var qty    = pressTarget.GetComponentInParent<QuantityField>();
                    int before = qty != null ? qty.Value : 0;
                    b.onClick.Invoke();
                    repeatClicks++;
                    nextRepeatClick = now + (repeatClicks > 8 ? 0.035f : 0.08f);
                    if (qty != null && qty.Value == before) BumpEdge();
                    else GamepadRumble.Play(Haptic.UiMove);
                }
            }
            else if (current == pressTarget && IsDragSource(current) && InventoryUI.Instance != null && !InventoryUI.Instance.IsDragging)
            {
                carrying        = true;
                nudgeFrames     = 2;
                carryCheckFrame = Time.frameCount + 4;
                GamepadRumble.Play(Haptic.UiGrab);
            }
            else leftDown = false;
        }

        if (carrying && EscapeRouter.TryConsume(Pad.Down(PadButton.B)))
        {
            ReleaseCarry();
            Sfx.Play(SfxId.UiClose);
        }

        if (Pad.Down(PadButton.X) && !carrying && current != null && current.TryGetComponent(out InventorySlotUI slot)
            && InventoryUI.Instance != null && !IsEmpty(slot))
        {
            ItemTooltipUI.Instance?.Hide();
            InventoryUI.Instance.TrashFrom(slot.SlotIndex);
        }

        int ring = FocusRing();

        if (Pad.Down(PadButton.Y) && ring >= 0)
        {
            ReleaseCarry();
            if (GamepadPanels.IsRingOpen(0)) GamepadPanels.SetRingOpen(0, false);
            else { GamepadPanels.SetRingOpen(ring, false); GamepadPanels.SetRingOpen(0, true); }
        }

        if (ring >= 0 && (Pad.Down(PadButton.LB) || Pad.Down(PadButton.RB)))
        {
            ReleaseCarry();
            GamepadPanels.SwitchRing(ring, Pad.Down(PadButton.LB) ? -1 : +1);
            GamepadRumble.Play(Haptic.UiTab);
        }

        if (ring >= 0 && (Pad.Down(PadButton.LT) || Pad.Down(PadButton.RT)))
        {
            ReleaseCarry();
            if (GamepadPanels.CycleTab(ring, Pad.Down(PadButton.LT) ? -1 : +1))
            {
                GamepadRumble.Play(Haptic.UiTab);
                foreach (var p in panels)
                    if (p.ring == ring) { remembered.Remove(p.root); BeginPendingFocus(p.root); }
                current = null;
            }
        }

        if (Pad.Down(PadButton.Start) && PlayerTileController.Instance != null && !PlayerTileController.IsPauseMenuOpen())
        {
            ReleaseCarry();
            GamepadPanels.CloseAllForMenu();
        }
    }

    private static bool IsEmpty(InventorySlotUI slot)
    {
        var s = Inventory.Instance?.GetSlot(slot.SlotIndex);
        return s == null || s.IsEmpty;
    }

    private static bool IsDragSource(GameObject go)
    {
        if (go == null || Inventory.Instance == null) return false;
        if (go.TryGetComponent(out InventorySlotUI slot)) return !IsEmpty(slot);
        if (go.GetComponent<TrashSlotUI>() != null)       return Inventory.Instance.TrashItem != null;
        return false;
    }

    private static void ReleaseCarry()
    {
        if (carrying) InventoryUI.Instance?.CancelDragIfActive();
        carrying    = false;
        leftDown    = false;
        nudgeFrames = 0;
    }

    private static void HandleNavigation()
    {
        // A 누른 채로는 이동 안 함
        if (leftDown && !carrying && !IsDragSource(pressTarget)) { heldDir = Vector2Int.zero; return; }

        var d = ReadDirection();
        float now = Time.unscaledTime;
        if (navBlocked)
        {
            if (d != Vector2Int.zero) return;
            navBlocked = false;
        }
        if (d == Vector2Int.zero) { heldDir = d; return; }
        if (d != heldDir)
        {
            heldDir       = d;
            moveRepeats   = 0;
            nextMove      = now + 0.35f;
            navEdgeBumped = false;
            Move(d);
        }
        else if (now >= nextMove)
        {
            moveRepeats++;
            nextMove = now + (moveRepeats > 5 ? 0.055f : 0.09f);
            Move(d);
        }
    }

    private static Vector2Int ReadDirection()
    {
        var g = Pad.Device;
        if (g == null) return Vector2Int.zero;
        if (g.dpad.up.isPressed)    return Vector2Int.up;
        if (g.dpad.down.isPressed)  return Vector2Int.down;
        if (g.dpad.left.isPressed)  return Vector2Int.left;
        if (g.dpad.right.isPressed) return Vector2Int.right;
        var s = Pad.LeftStick;
        if (s.sqrMagnitude < 0.25f) return Vector2Int.zero;
        return Mathf.Abs(s.x) > Mathf.Abs(s.y)
            ? new Vector2Int(s.x > 0f ? 1 : -1, 0)
            : new Vector2Int(0, s.y > 0f ? 1 : -1);
    }

    private static void Move(Vector2Int d)
    {
        pendingFocus = null;
        if (current != null && d.x != 0 && current.TryGetComponent(out Slider slider) && slider.IsInteractable())
        {
            float step   = slider.wholeNumbers ? 1f : (slider.maxValue - slider.minValue) * 0.05f;
            float before = slider.value;
            slider.value += d.x * step;
            if (Mathf.Approximately(slider.value, before)) { BumpEdge(); return; }
            navEdgeBumped = false;
            GamepadRumble.Play(Haptic.UiMove);
            return;
        }

        var next = FindNext(current, new Vector2(d.x, d.y), out bool viaScroll);
        if (next == null) { BumpEdge(); return; }
        if (viaScroll) EnsureVisible(next);
        current       = next;
        navEdgeBumped = false;
        GamepadRumble.Play(Haptic.UiMove);
    }

    private static void BumpEdge()
    {
        if (navEdgeBumped) return;
        navEdgeBumped = true;
        GamepadRumble.Play(Haptic.UiEdge);
    }

    private static GameObject FindNext(GameObject from, Vector2 dir, out bool viaScroll)
    {
        viaScroll = false;
        CollectAll();
        Rect fr = from != null ? ScreenRect(from) : new Rect(pos, Vector2.zero);
        Vector2 fc = fr.center;
        bool horizontal = dir.x != 0f;

        GameObject best = null;
        float bestCost = float.MaxValue;
        foreach (var go in cands)
        {
            if (go == from || !Reachable(go, out bool scroll)) continue;
            Rect r = ScreenRect(go);
            Vector2 c = r.center;

            float along = Vector2.Dot(c - fc, dir);
            if (along < 4f) continue;

            float gapAlong = Mathf.Max(0f, along - (horizontal ? (fr.width + r.width) : (fr.height + r.height)) * 0.5f);
            float gapSide  = horizontal ? SpanGap(fr.yMin, fr.yMax, r.yMin, r.yMax) : SpanGap(fr.xMin, fr.xMax, r.xMin, r.xMax);
            float offSide  = horizontal ? Mathf.Abs(c.y - fc.y) : Mathf.Abs(c.x - fc.x);
            if (gapSide > along * 2.5f + 40f) continue;

            float cost = gapAlong + gapSide * 3f + offSide * 0.25f;
            if (cost < bestCost) { bestCost = cost; best = go; viaScroll = scroll; }
        }
        return best;
    }

    private static float SpanGap(float aMin, float aMax, float bMin, float bMax)
        => Mathf.Max(0f, Mathf.Max(aMin - bMax, bMin - aMax));

    private static void HandleScroll()
    {
        Vector2 s = Pad.RightStick;
        if (s.sqrMagnitude > 0.04f && !leftDown)
        {
            var sr = ScrollTarget();
            if (sr == null) return;
            scrolling = true;
            float px    = 1100f * Mathf.Pow(Mathf.Min(1f, s.magnitude), 1.5f) * Time.unscaledDeltaTime;
            float moved = ScrollBy(sr, s.normalized * px);

            if (moved > 0.5f)
            {
                scrollEdgeBumped = false;
                scrollDetent += moved;
                if (scrollDetent >= ScrollDetentPx) { scrollDetent = 0f; GamepadRumble.Play(Haptic.UiMove, 0.8f); }
            }
            else if (!scrollEdgeBumped)
            {
                scrollEdgeBumped = true;
                GamepadRumble.Play(Haptic.UiEdge);
            }
        }
        else if (scrolling)
        {
            scrolling        = false;
            scrollEdgeBumped = false;
            current = NearestTo(pos) ?? current;
        }
    }

    private static ScrollRect ScrollTarget()
    {
        if (current != null)
        {
            var own = current.GetComponentInParent<ScrollRect>();
            if (own != null && CanScroll(own)) return own;
        }
        var root = current != null ? PanelOf(current) : (panels.Count > 0 ? panels[panels.Count - 1].root : null);
        if (root == null) return null;

        ScrollRect best = null;
        float bestArea = 0f;
        root.GetComponentsInChildren(false, srBuf);
        foreach (var sr in srBuf)
        {
            if (!sr.isActiveAndEnabled || !CanScroll(sr)) continue;
            var r = ScreenRect(sr.viewport != null ? sr.viewport.gameObject : sr.gameObject);
            float area = r.width * r.height;
            if (area > bestArea) { bestArea = area; best = sr; }
        }
        return best;
    }

    private static bool CanScroll(ScrollRect sr)
    {
        if (sr.content == null) return false;
        var v = ScreenRect(sr.viewport != null ? sr.viewport.gameObject : sr.gameObject);
        var c = ScreenRect(sr.content.gameObject);
        return (sr.vertical && c.height > v.height + 1f) || (sr.horizontal && c.width > v.width + 1f);
    }

    private static float ScrollBy(ScrollRect sr, Vector2 px)
    {
        var v = ScreenRect(sr.viewport != null ? sr.viewport.gameObject : sr.gameObject);
        var c = ScreenRect(sr.content.gameObject);
        float moved = 0f;
        if (sr.vertical && c.height > v.height + 1f)
        {
            float range = c.height - v.height, before = sr.verticalNormalizedPosition;
            sr.verticalNormalizedPosition = Mathf.Clamp01(before + px.y / range);
            moved += Mathf.Abs(sr.verticalNormalizedPosition - before) * range;
        }
        if (sr.horizontal && c.width > v.width + 1f)
        {
            float range = c.width - v.width, before = sr.horizontalNormalizedPosition;
            sr.horizontalNormalizedPosition = Mathf.Clamp01(before + px.x / range);
            moved += Mathf.Abs(sr.horizontalNormalizedPosition - before) * range;
        }
        sr.velocity = Vector2.zero;
        return moved;
    }

    private static void EnsureVisible(GameObject go)
    {
        var sr = go.GetComponentInParent<ScrollRect>();
        if (sr == null || sr.content == null) return;
        var v = ScreenRect(sr.viewport != null ? sr.viewport.gameObject : sr.gameObject);
        var r = ScreenRect(go);
        const float Margin = 8f;
        float dy = r.yMax > v.yMax ? r.yMax - v.yMax + Margin : r.yMin < v.yMin ? r.yMin - v.yMin - Margin : 0f;
        float dx = r.xMax > v.xMax ? r.xMax - v.xMax + Margin : r.xMin < v.xMin ? r.xMin - v.xMin - Margin : 0f;
        ScrollBy(sr, new Vector2(dx, dy));
    }

    private static void CollectAll()
    {
        cands.Clear();
        candSet.Clear();
        foreach (var p in panels) AddFrom(p.root);
    }

    private static void Collect(Transform root)
    {
        cands.Clear();
        candSet.Clear();
        AddFrom(root);
    }

    private static void AddFrom(Transform root)
    {
        if (root == null || !root.gameObject.activeInHierarchy) return;

        root.GetComponentsInChildren(false, selBuf);
        foreach (var s in selBuf)
        {
            if (!s.IsInteractable() || s is TMP_InputField || s is InputField || s is Scrollbar) continue;
            Add(s.gameObject, root);
        }

        root.GetComponentsInChildren(false, mbBuf);
        foreach (var mb in mbBuf)
        {
            if (mb == null || mb.transform == root || !mb.isActiveAndEnabled) continue;
            if (mb is InventorySlotUI || mb is TrashSlotUI || mb is EquipmentSlotUI || mb is EventTrigger)
                Add(mb.gameObject, root);
        }
    }

    private static void Add(GameObject go, Transform root)
    {
        if (go.transform == root || !candSet.Add(go)) return;
        cands.Add(go);
    }

    private static bool Reachable(GameObject go, out bool viaScroll)
    {
        viaScroll = false;
        if (go == null || !go.activeInHierarchy) return false;
        Rect r = ScreenRect(go);
        if (r.width < 2f || r.height < 2f) return false;
        if (r.width * r.height > Screen.width * Screen.height * 0.45f) return false;

        Vector2 c = r.center;
        bool onScreen = c.x >= 0f && c.y >= 0f && c.x <= Screen.width && c.y <= Screen.height;
        if (onScreen)
        {
            var hit = RaycastTop(c);
            if (hit != null && hit.transform.IsChildOf(go.transform)) return true;
        }

        var sr = go.GetComponentInParent<ScrollRect>();
        if (sr == null || sr.content == null || !go.transform.IsChildOf(sr.content)) return false;
        var vpGo = sr.viewport != null ? sr.viewport.gameObject : sr.gameObject;
        Rect v = ScreenRect(vpGo);
        if (v.Overlaps(r) && v.Contains(c)) return false;
        var vpHit = RaycastTop(v.center);
        if (vpHit == null || !vpHit.transform.IsChildOf(sr.transform)) return false;
        viaScroll = true;
        return true;
    }

    private static GameObject RaycastTop(Vector2 screen)
    {
        var es = EventSystem.current;
        if (es == null) return null;
        if (ped == null || pedOwner != es) { ped = new PointerEventData(es); pedOwner = es; }
        ped.position = screen;
        hits.Clear();
        es.RaycastAll(ped, hits);
        return hits.Count > 0 ? hits[0].gameObject : null;
    }

    private static Rect ScreenRect(GameObject go)
    {
        var rt = go.transform as RectTransform;
        if (rt == null) return new Rect(pos, Vector2.zero);
        rt.GetWorldCorners(corners);
        var canvas = go.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
        Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
        for (int i = 0; i < 4; i++)
        {
            Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static void ClearStraySelection(EventSystem es)
    {
        var sel = es.currentSelectedGameObject;
        if (sel == null || sel.GetComponent<TMP_InputField>() != null || sel.GetComponent<InputField>() != null) return;
        es.SetSelectedGameObject(null);
    }

    private static void AddHints()
    {
        var h = GamepadHud.Hints;
        if (carrying)
        {
            h.Add(new PadHint(PadGlyph.A, "놓기"));
            h.Add(new PadHint(PadGlyph.B, "취소"));
            return;
        }

        if (current != null && current.GetComponent<Slider>() != null) h.Add(new PadHint(PadGlyph.Left, PadGlyph.Right, "조절"));
        else if (IsDragSource(current))                               h.Add(new PadHint(PadGlyph.A, "집기"));
        else if (current != null)                                     h.Add(new PadHint(PadGlyph.A, "선택"));

        if (current != null && current.TryGetComponent(out InventorySlotUI slot) && !IsEmpty(slot))
            h.Add(new PadHint(PadGlyph.X, "버리기"));

        int ring = FocusRing();
        if (ring >= 0)
        {
            h.Add(new PadHint(PadGlyph.LB, PadGlyph.RB, "창 전환"));
            if (ring == 1)              h.Add(new PadHint(PadGlyph.LT, PadGlyph.RT, "탭"));
            if (ring == 2)              h.Add(new PadHint(PadGlyph.LT, PadGlyph.RT, "가게 / 가방"));
            if (ring == 3)              h.Add(new PadHint(PadGlyph.LT, PadGlyph.RT, "확대·축소"));
        }
        if (ScrollTarget() != null) h.Add(new PadHint(PadGlyph.RStick, ring == 3 ? "둘러보기" : "스크롤"));
        h.Add(new PadHint(PadGlyph.B, PlayerTileController.Instance == null ? "뒤로" : "닫기"));
    }
}
