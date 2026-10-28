// 메뉴에서 인게임으로 넘기는 값
public static class GameLaunchContext
{
    public static int SelectedSlot = 0;

    public static bool IsNewGame = true;

    public static WorldSize PendingWorldSize = WorldSize.Middle;

    public static WorldTheme PendingWorldTheme = WorldTheme.Wasteland;

    public static int PendingWorldSeed = 0;

    public static CatLook PendingCharacter;

    public static int PendingGridWidth, PendingGridHeight;

    public static bool LaunchedFromMenu = false;

    // 도메인 리로드 없이 재시작 대비
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        SelectedSlot      = 0;
        IsNewGame         = true;
        PendingWorldSize  = WorldSize.Middle;
        PendingWorldTheme = WorldTheme.Wasteland;
        PendingWorldSeed  = 0;
        PendingCharacter  = default;
        PendingGridWidth  = PendingGridHeight = 0;
        LaunchedFromMenu  = false;
    }
}
