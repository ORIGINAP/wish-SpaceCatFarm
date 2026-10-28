using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// 구 Input API 대체 래퍼
public static class GameInput
{
    public static bool GetKey(KeyCode key)     => (Button(key)?.isPressed            ?? false) || tapsNow.Contains(key);
    public static bool GetKeyDown(KeyCode key) => (Button(key)?.wasPressedThisFrame  ?? false) || tapsNow.Contains(key);
    public static bool GetKeyUp(KeyCode key)   => (Button(key)?.wasReleasedThisFrame ?? false) || (tapsPrev.Contains(key) && !tapsNow.Contains(key));

    public static bool GetMouseButton(int button)     => (MouseButton(button)?.isPressed            ?? false) || TouchInput.Held(button);
    public static bool GetMouseButtonDown(int button) => (MouseButton(button)?.wasPressedThisFrame  ?? false) || TouchInput.Down(button);
    public static bool GetMouseButtonUp(int button)   => (MouseButton(button)?.wasReleasedThisFrame ?? false) || TouchInput.Up(button);

    public static Vector3 MousePosition
    {
        get
        {
            if (TouchInput.UsingTouch) return TouchInput.Position;
            var p = RealPointer;
            return p != null ? (Vector3)p.position.ReadValue() : Vector3.zero;
        }
    }

    public static bool PointerOverUI => TouchInput.UsingTouch
        ? TouchInput.OverUI
        : EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    public static Vector2 MouseScrollDelta => RealMouse?.scroll.ReadValue() ?? Vector2.zero;

    public static bool AnyKeyDown =>
        (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) || tapsNow.Count > 0 ||
        GetMouseButtonDown(0) || GetMouseButtonDown(1) || GetMouseButtonDown(2);

    private static HashSet<KeyCode> tapsNext = new(), tapsNow = new(), tapsPrev = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        tapsNext.Clear();
        tapsNow.Clear();
        tapsPrev.Clear();
    }

    // 가상 키 입력, 다음 프레임에 반영
    public static void TapKey(KeyCode key) => tapsNext.Add(key);

    public static void BeginFrame()
    {
        (tapsPrev, tapsNow, tapsNext) = (tapsNow, tapsNext, tapsPrev);
        tapsNext.Clear();
    }

    public static float GetAxisRaw(string axis) => axis switch
    {
        "Horizontal"        => Axis(KeyCode.LeftArrow, KeyCode.A, KeyCode.RightArrow, KeyCode.D),
        "Vertical"          => Axis(KeyCode.DownArrow, KeyCode.S, KeyCode.UpArrow, KeyCode.W),
        "Mouse ScrollWheel" => MouseScrollDelta.y * 0.1f,
        _                   => 0f,
    };

    private static float Axis(KeyCode neg, KeyCode altNeg, KeyCode pos, KeyCode altPos)
        => (GetKey(pos) || GetKey(altPos) ? 1f : 0f) - (GetKey(neg) || GetKey(altNeg) ? 1f : 0f);

    // 가상 장치 말고 실제 마우스 우선
    private static Mouse RealMouse
    {
        get
        {
            var m = Mouse.current;
            if (m == null || m.native) return m;
            foreach (var d in InputSystem.devices)
                if (d is Mouse other && other.native) return other;
            return null;
        }
    }

    public static Mouse RealMouseDevice => RealMouse;

    private static Pointer RealPointer => (Pointer)RealMouse ?? Touchscreen.current;

    private static ButtonControl MouseButton(int button)
    {
        var m = RealMouse;
        if (m == null) return button == 0 && !MobileMode.Active ? Touchscreen.current?.press : null;
        return button switch
        {
            0 => m.leftButton,
            1 => m.rightButton,
            2 => m.middleButton,
            3 => m.backButton,
            4 => m.forwardButton,
            _ => null,
        };
    }

    private static ButtonControl Button(KeyCode key)
    {
        if (key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6) return MouseButton(key - KeyCode.Mouse0);
        var kb = Keyboard.current;
        if (kb == null) return null;
        var k = ToKey(key);
        return k == Key.None ? null : kb[k];
    }

    private static Key ToKey(KeyCode c)
    {
        if (c >= KeyCode.A       && c <= KeyCode.Z)       return Key.A       + (c - KeyCode.A);
        if (c >= KeyCode.Alpha1  && c <= KeyCode.Alpha9)  return Key.Digit1  + (c - KeyCode.Alpha1);
        if (c >= KeyCode.Keypad0 && c <= KeyCode.Keypad9) return Key.Numpad0 + (c - KeyCode.Keypad0);
        if (c >= KeyCode.F1      && c <= KeyCode.F12)     return Key.F1      + (c - KeyCode.F1);
        if (c >= KeyCode.F13     && c <= KeyCode.F15)     return Key.F13     + (c - KeyCode.F13);
        return c switch
        {
            KeyCode.Alpha0         => Key.Digit0,
            KeyCode.Space          => Key.Space,
            KeyCode.Return         => Key.Enter,
            KeyCode.Tab            => Key.Tab,
            KeyCode.Escape         => Key.Escape,
            KeyCode.Backspace      => Key.Backspace,
            KeyCode.Delete         => Key.Delete,
            KeyCode.Insert         => Key.Insert,
            KeyCode.Home           => Key.Home,
            KeyCode.End            => Key.End,
            KeyCode.PageUp         => Key.PageUp,
            KeyCode.PageDown       => Key.PageDown,
            KeyCode.UpArrow        => Key.UpArrow,
            KeyCode.DownArrow      => Key.DownArrow,
            KeyCode.LeftArrow      => Key.LeftArrow,
            KeyCode.RightArrow     => Key.RightArrow,
            KeyCode.LeftShift      => Key.LeftShift,
            KeyCode.RightShift     => Key.RightShift,
            KeyCode.LeftControl    => Key.LeftCtrl,
            KeyCode.RightControl   => Key.RightCtrl,
            KeyCode.LeftAlt        => Key.LeftAlt,
            KeyCode.RightAlt       => Key.RightAlt,
            KeyCode.LeftCommand    => Key.LeftMeta,
            KeyCode.LeftWindows    => Key.LeftMeta,
            KeyCode.RightCommand   => Key.RightMeta,
            KeyCode.RightWindows   => Key.RightMeta,
            KeyCode.Menu           => Key.ContextMenu,
            KeyCode.CapsLock       => Key.CapsLock,
            KeyCode.Numlock        => Key.NumLock,
            KeyCode.ScrollLock     => Key.ScrollLock,
            KeyCode.Pause          => Key.Pause,
            KeyCode.Print          => Key.PrintScreen,
            KeyCode.BackQuote      => Key.Backquote,
            KeyCode.Minus          => Key.Minus,
            KeyCode.Equals         => Key.Equals,
            KeyCode.Plus           => Key.Equals,
            KeyCode.LeftBracket    => Key.LeftBracket,
            KeyCode.RightBracket   => Key.RightBracket,
            KeyCode.Backslash      => Key.Backslash,
            KeyCode.Semicolon      => Key.Semicolon,
            KeyCode.Quote          => Key.Quote,
            KeyCode.Comma          => Key.Comma,
            KeyCode.Period         => Key.Period,
            KeyCode.Slash          => Key.Slash,
            KeyCode.KeypadEnter    => Key.NumpadEnter,
            KeyCode.KeypadPeriod   => Key.NumpadPeriod,
            KeyCode.KeypadDivide   => Key.NumpadDivide,
            KeyCode.KeypadMultiply => Key.NumpadMultiply,
            KeyCode.KeypadMinus    => Key.NumpadMinus,
            KeyCode.KeypadPlus     => Key.NumpadPlus,
            KeyCode.KeypadEquals   => Key.NumpadEquals,
            _                      => Key.None,
        };
    }
}
