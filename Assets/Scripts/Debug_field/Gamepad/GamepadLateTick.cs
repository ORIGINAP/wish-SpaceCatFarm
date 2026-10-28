using UnityEngine;

// 카메라 LateUpdate 뒤에 그려야 안 밀림
[DefaultExecutionOrder(10000)]
public class GamepadLateTick : MonoBehaviour
{
    private void LateUpdate()
    {
        GamepadRumble.Tick();
        if (InputManager.Instance != null) InputManager.Instance.LateTick();
        GamepadHud.EndFrame();
    }
}
