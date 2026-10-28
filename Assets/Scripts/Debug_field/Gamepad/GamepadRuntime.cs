using UnityEngine;
using UnityEngine.InputSystem;

// EventSystem보다 먼저
[DefaultExecutionOrder(-1900)]
public class GamepadRuntime : MonoBehaviour
{
    private static GamepadRuntime instance;

    private Vector3 lastMouse;
    private bool    mouseInit;
    private bool    cursorHidden;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null) return;
        var go = new GameObject("GamepadRuntime");
        go.AddComponent<GamepadRuntime>();
        go.AddComponent<GamepadLateTick>();
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        Shutdown();
    }

    private void OnApplicationQuit() => Shutdown();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    // 포커스 잃으면 진동 미리 멈춤
    private void OnApplicationFocus(bool focused)
    {
        if (!focused) GamepadRumble.StopAll();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) GamepadRumble.StopAll();
    }

#if UNITY_EDITOR
    private void OnEnable()  => UnityEditor.EditorApplication.pauseStateChanged += OnEditorPause;
    private void OnDisable() => UnityEditor.EditorApplication.pauseStateChanged -= OnEditorPause;

    private static void OnEditorPause(UnityEditor.PauseState state)
    {
        if (state == UnityEditor.PauseState.Paused) GamepadRumble.StopAll();
    }
#endif

    private static void Shutdown()
    {
        GamepadRumble.StopAll();
        InputSystem.ResetHaptics();
        GamepadPointer.Shutdown();
        Pad.Active = false;
        Cursor.visible = true;
    }

    private void Update()
    {
        GamepadHud.BeginFrame();
        UpdateDevice();
        Pad.UIMode = PlayerTileController.Instance == null || GamepadPanels.Open.Count > 0;

        bool hide = Pad.Active;
        if (hide != cursorHidden)
        {
            cursorHidden   = hide;
            Cursor.visible = !hide;
        }

        GamepadPointer.Tick();
    }

    private void UpdateDevice()
    {
        Vector3 m = GameInput.MousePosition;
        if (!mouseInit) { lastMouse = m; mouseInit = true; }
        bool mouseUsed = (m - lastMouse).sqrMagnitude > 9f
                      || GameInput.GetMouseButtonDown(0) || GameInput.GetMouseButtonDown(1) || GameInput.GetMouseButtonDown(2)
                      || GameInput.MouseScrollDelta.sqrMagnitude > 0.01f;
        lastMouse = m;

        if (Gamepad.current == null || mouseUsed) Pad.Active = false;
        else if (Pad.AnyInputThisFrame())         Pad.Active = true;
    }
}
