using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;

public static class MobileMode
{
    private static bool? active;

    public static bool Active => active ??= UnityEngine.Device.Application.isMobilePlatform || EditorPreview;

#if UNITY_EDITOR
    public const string PreviewPrefKey = "WISH_MobilePreview";
    private static bool EditorPreview => UnityEditor.EditorPrefs.GetBool(PreviewPrefKey, false);
#else
    private static bool EditorPreview => false;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() => active = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (!Active) return;

        Application.targetFrameRate = 60;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged += ResetFrameRateOnExit;
#endif
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Screen.autorotateToPortrait           = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft      = true;
        Screen.autorotateToLandscapeRight     = true;
        Screen.orientation = ScreenOrientation.AutoRotation;
        EnhancedTouchSupport.Enable();

        var go = new GameObject("MobileRuntime");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<MobileRuntime>();
    }

#if UNITY_EDITOR
    // 에디트 모드 돌아온 뒤에 되돌려야 함
    private static void ResetFrameRateOnExit(UnityEditor.PlayModeStateChange change)
    {
        if (change != UnityEditor.PlayModeStateChange.EnteredEditMode) return;
        Application.targetFrameRate = -1;
        UnityEditor.EditorApplication.playModeStateChanged -= ResetFrameRateOnExit;
    }
#endif
}
