using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using ETouch      = UnityEngine.InputSystem.EnhancedTouch.Touch;
using ETouchPhase = UnityEngine.InputSystem.TouchPhase;

public static class TouchInput
{
    // 핀치랑 구분하려고 살짝 대기
    public const float PressDelay = 0.08f;

    private const float StickZoneWidth  = 0.28f;
    private const float StickZoneHeight = 0.55f;
    private const float StickRadius     = 0.11f;
    private const float StickDeadZone   = 0.12f;
    private const float StickFullAt     = 0.8f;

    private enum Role { Pointer, Stick, Pinch, Ignored }

    private sealed class Finger
    {
        public int     id;
        public Role    role;
        public Vector2 pos;
        public float   downTime;
        public bool    startUI;
        public bool    overUI;
        public bool    pressed;
        public bool    alive;
    }

    private static readonly List<Finger> fingers = new();
    private static Finger pointer, stick, pinchA, pinchB;
    private static float  pinchDistance, nextIdleCheck;

    private static bool held, prevHeld, releaseNext;
    private static int  button, prevButton;

    private static readonly List<RaycastResult> uiHits = new();
    private static PointerEventData eventData;
    private static EventSystem      eventDataOwner;

    public static Vector2 Stick { get; private set; }

    public static bool StickActive => stick != null;

    public static Vector2 StickOrigin { get; private set; }
    public static Vector2 StickKnob   { get; private set; }

    public static float StickRadiusPx => Screen.height * StickRadius;

    public static bool StickEnabled { get; set; }

    public static float PinchRatio { get; private set; } = 1f;

    public static bool DismantleMode { get; set; }

    public static bool UsingTouch { get; private set; }

    public static Vector2 Position { get; private set; } = new(-100f, -100f);

    public static bool OverUI { get; private set; }

    public static bool Held(int b) => held && button == b;
    public static bool Down(int b) => held && button == b && !(prevHeld && prevButton == b);
    public static bool Up(int b)   => prevHeld && prevButton == b && !(held && button == b);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Reset();
        pinchDistance = nextIdleCheck = 0f;
        button = prevButton = 0;
        eventData = null;
        eventDataOwner = null;
        StickOrigin = StickKnob = Vector2.zero;
        StickEnabled = false;
    }

    public static void Reset()
    {
        fingers.Clear();
        pointer = stick = pinchA = pinchB = null;
        held = prevHeld = releaseNext = false;
        Stick = Vector2.zero;
        PinchRatio = 1f;
        DismantleMode = false;
        UsingTouch = false;
        Position = new Vector2(-100f, -100f);
        OverUI = false;
    }

    public static void Tick()
    {
        PinchRatio = 1f;
        prevHeld   = held;
        prevButton = button;
        if (releaseNext) { held = false; releaseNext = false; }

        // 움직임만으론 마우스 전환 안 함
        var mouse = GameInput.RealMouseDevice;
        if (mouse == null) UsingTouch = true;
        else if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.scroll.ReadValue().sqrMagnitude > 0.01f)
            UsingTouch = false;

        foreach (var f in fingers) f.alive = false;
        var touches = ETouch.activeTouches;
        for (int i = 0; i < touches.Count; i++)
        {
            var t = touches[i];
            var f = Find(t.touchId) ?? Begin(t.touchId, t.screenPosition);
            f.pos   = t.screenPosition;
            f.alive = t.phase != ETouchPhase.Ended && t.phase != ETouchPhase.Canceled;
        }
        for (int i = fingers.Count - 1; i >= 0; i--)
            if (!fingers[i].alive) End(fingers[i]);

        UpdateStick();
        UpdatePinch();
        UpdatePointer();
    }

    private static Finger Find(int id)
    {
        foreach (var f in fingers) if (f.id == id) return f;
        return null;
    }

    private static Finger Begin(int id, Vector2 pos)
    {
        bool ui = HitsUI(pos);
        var f = new Finger { id = id, pos = pos, downTime = Time.unscaledTime, startUI = ui, overUI = ui };
        fingers.Add(f);

        if (!ui && stick == null && StickEnabled && pos.x < Screen.width * StickZoneWidth && pos.y < Screen.height * StickZoneHeight)
        {
            f.role      = Role.Stick;
            stick       = f;
            StickOrigin = StickKnob = pos;
            return f;
        }

        if (pointer == null && pinchA == null)
        {
            f.role     = Role.Pointer;
            pointer    = f;
            UsingTouch = true;
            return f;
        }

        if (pointer != null && !pointer.startUI && !ui)
        {
            if (pointer.pressed) held = false;
            pointer.role = f.role = Role.Pinch;
            pinchA = pointer;
            pinchB = f;
            pointer = null;
            pinchDistance = Vector2.Distance(pinchA.pos, pinchB.pos);
            return f;
        }

        f.role = Role.Ignored;
        return f;
    }

    private static void End(Finger f)
    {
        fingers.Remove(f);
        if (f == stick)
        {
            stick = null;
            Stick = Vector2.zero;
        }
        else if (f == pointer)
        {
            pointer  = null;
            Position = f.pos;
            OverUI   = HitsUI(f.pos);
            if (f.pressed) held = false;
            else
            {
                held        = true;
                button      = ButtonFor(f);
                releaseNext = true;
            }
        }
        else if (f == pinchA || f == pinchB)
        {
            var other = f == pinchA ? pinchB : pinchA;
            if (other != null) other.role = Role.Ignored;
            pinchA = pinchB = null;
        }
    }

    private static void UpdateStick()
    {
        if (stick == null) { Stick = Vector2.zero; return; }

        // 멀리 끌면 받침이 따라옴
        float   r = StickRadiusPx;
        Vector2 d = stick.pos - StickOrigin;
        if (d.magnitude > r)
        {
            StickOrigin = stick.pos - d.normalized * r;
            d = stick.pos - StickOrigin;
        }
        StickKnob = stick.pos;

        Vector2 v = d / r;
        float   m = v.magnitude;
        Stick = m < StickDeadZone ? Vector2.zero : v * (Mathf.Min(1f, (m - StickDeadZone) / (StickFullAt - StickDeadZone)) / m);
    }

    private static void UpdatePinch()
    {
        if (pinchA == null || pinchB == null) return;
        float d = Vector2.Distance(pinchA.pos, pinchB.pos);
        if (pinchDistance > 10f && d > 10f) PinchRatio = pinchDistance / d;
        pinchDistance = d;
    }

    private static void UpdatePointer()
    {
        if (pointer == null)
        {
            if (UsingTouch && Time.unscaledTime >= nextIdleCheck)
            {
                nextIdleCheck = Time.unscaledTime + 0.1f;
                OverUI = HitsUI(Position);
            }
            return;
        }
        Position = pointer.pos;
        pointer.overUI = HitsUI(pointer.pos);
        OverUI = pointer.overUI;

        if (!pointer.pressed && (pointer.startUI || Time.unscaledTime - pointer.downTime >= PressDelay))
        {
            pointer.pressed = true;
            held   = true;
            button = ButtonFor(pointer);
        }
    }

    private static int ButtonFor(Finger f) => DismantleMode && !f.startUI ? 1 : 0;

    private static bool HitsUI(Vector2 pos)
    {
        var es = EventSystem.current;
        if (es == null) return false;
        if (eventData == null || eventDataOwner != es)
        {
            eventData      = new PointerEventData(es);
            eventDataOwner = es;
        }
        eventData.position = pos;
        uiHits.Clear();
        es.RaycastAll(eventData, uiHits);
        return uiHits.Count > 0;
    }
}
