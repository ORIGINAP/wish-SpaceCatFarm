using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public enum PadButton { A, B, X, Y, LB, RB, LT, RT, Start, Back, L3, R3, Up, Down, Left, Right }

// 레거시 조이스틱 대신 Gamepad.current
public static class Pad
{
    public static bool Active { get; internal set; }

    public static bool UIMode { get; internal set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Active = false;
        UIMode = false;
    }

    public static bool World => Active && !UIMode && !TextInputFocus.Active;

    public static Gamepad Device => Gamepad.current;

    public static Vector2 LeftStick  => Gamepad.current?.leftStick.ReadValue()  ?? Vector2.zero;
    public static Vector2 RightStick => Gamepad.current?.rightStick.ReadValue() ?? Vector2.zero;

    public static bool Down(PadButton b) => Control(b)?.wasPressedThisFrame  ?? false;
    public static bool Held(PadButton b) => Control(b)?.isPressed            ?? false;
    public static bool Up(PadButton b)   => Control(b)?.wasReleasedThisFrame ?? false;

    public static bool ActiveDown(PadButton b) => Active && !TextInputFocus.Active && Down(b);

    public static bool WorldDown(PadButton b) => World && Down(b);

    public static bool WorldHeld(PadButton b) => World && Held(b);

    public static ButtonControl Control(PadButton b)
    {
        var g = Gamepad.current;
        if (g == null) return null;
        switch (b)
        {
            case PadButton.A:     return g.buttonSouth;
            case PadButton.B:     return g.buttonEast;
            case PadButton.X:     return g.buttonWest;
            case PadButton.Y:     return g.buttonNorth;
            case PadButton.LB:    return g.leftShoulder;
            case PadButton.RB:    return g.rightShoulder;
            case PadButton.LT:    return g.leftTrigger;
            case PadButton.RT:    return g.rightTrigger;
            case PadButton.Start: return g.startButton;
            case PadButton.Back:  return g.selectButton;
            case PadButton.L3:    return g.leftStickButton;
            case PadButton.R3:    return g.rightStickButton;
            case PadButton.Up:    return g.dpad.up;
            case PadButton.Down:  return g.dpad.down;
            case PadButton.Left:  return g.dpad.left;
            default:              return g.dpad.right;
        }
    }

    internal static bool AnyInputThisFrame()
    {
        var g = Gamepad.current;
        if (g == null) return false;
        for (int i = 0; i <= (int)PadButton.Right; i++)
            if (Control((PadButton)i).wasPressedThisFrame) return true;
        const float StickSqr = 0.35f * 0.35f;
        return g.leftStick.ReadValue().sqrMagnitude > StickSqr || g.rightStick.ReadValue().sqrMagnitude > StickSqr;
    }
}
