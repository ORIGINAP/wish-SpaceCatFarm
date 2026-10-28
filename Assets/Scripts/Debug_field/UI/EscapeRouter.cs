using UnityEngine;

// Esc 한 번에 동작 하나만
public static class EscapeRouter
{
    private static int consumedFrame = -1;

    // 도메인 리로드 없이 재시작 대비
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => consumedFrame = -1;

    public static bool ConsumedThisFrame => consumedFrame == Time.frameCount;

    public static void Consume() => consumedFrame = Time.frameCount;

    public static bool EscPressed => !TextInputFocus.Active && GameInput.GetKeyDown(KeyCode.Escape);

    public static bool CancelPressed => EscPressed || Pad.ActiveDown(PadButton.B);

    public static bool TryConsume(bool pressed)
    {
        if (!pressed || ConsumedThisFrame) return false;
        Consume();
        return true;
    }
}
