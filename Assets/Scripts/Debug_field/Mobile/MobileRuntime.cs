using UnityEngine;
using UnityEngine.SceneManagement;

// GamepadRuntime보다 먼저
[DefaultExecutionOrder(-2000)]
public class MobileRuntime : MonoBehaviour
{
    private Vector2Int screenSize;

    private void Awake()
    {
        TouchInput.Reset();
        SceneManager.sceneLoaded += OnSceneLoaded;
        MobileHud.Create(transform);
    }

    private void OnDestroy() => SceneManager.sceneLoaded -= OnSceneLoaded;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        TouchInput.DismantleMode = false;
        UiScale.ApplyAll();
    }

    private void Update()
    {
        GameInput.BeginFrame();
        TouchInput.Tick();

        var size = new Vector2Int(Screen.width, Screen.height);
        if (size != screenSize)
        {
            screenSize = size;
            UiScale.ApplyAll();
        }
    }
}
