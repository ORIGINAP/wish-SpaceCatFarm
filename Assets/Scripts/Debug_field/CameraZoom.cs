using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Cinemachine;

[RequireComponent(typeof(CinemachinePositionComposer))]
public class CameraZoom : MonoBehaviour
{
    [Tooltip("기본 거리 대비 최대 확대 배율")]
    [SerializeField] private float maxZoomIn  = 5f;
    [Tooltip("기본 거리 대비 최대 축소 배율")]
    [SerializeField] private float maxZoomOut = 2.5f;
    [Tooltip("휠 한 칸당 거리 배율")]
    [SerializeField] private float stepPerNotch  = 1.15f;
    [Tooltip("목표 거리로 따라가는 속도 (클수록 빠름)")]
    [SerializeField] private float smoothSpeed   = 12f;

    private CinemachinePositionComposer composer;
    private float baseDistance;
    private float targetDistance;
    private Vector3 baseDamping;

    private static readonly float[] PadZoomLevels = { 1f, 1.8f, 0.6f };
    private int padZoomIndex;

    private const float MobileStartZoom = 0.75f;

    // 씬 카메라에 자동 부착
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        AttachToSceneCameras();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachToSceneCameras();

    private static void AttachToSceneCameras()
    {
        foreach (var c in FindObjectsByType<CinemachinePositionComposer>(FindObjectsSortMode.None))
            if (c.GetComponent<CameraZoom>() == null)
                c.gameObject.AddComponent<CameraZoom>();
    }

    private void Awake()
    {
        composer       = GetComponent<CinemachinePositionComposer>();
        baseDistance   = composer.CameraDistance;
        targetDistance = baseDistance;
        baseDamping    = composer.Damping;

        if (MobileMode.Active) composer.CameraDistance = targetDistance = baseDistance * MobileStartZoom;
    }

    private void Update()
    {
        if (IsCtrlHeld() && !GameInput.PointerOverUI)
        {
            float wheel = GameInput.MouseScrollDelta.y;
            if (wheel != 0f)
            {
                targetDistance *= Mathf.Pow(stepPerNotch, -wheel);
                targetDistance  = Mathf.Clamp(targetDistance, baseDistance / maxZoomIn, baseDistance * maxZoomOut);
            }
        }

        if (TouchInput.PinchRatio != 1f)
            targetDistance = Mathf.Clamp(targetDistance * TouchInput.PinchRatio, baseDistance / maxZoomIn, baseDistance * maxZoomOut);

        if (Pad.WorldDown(PadButton.R3))
        {
            padZoomIndex   = (padZoomIndex + 1) % PadZoomLevels.Length;
            targetDistance = baseDistance * PadZoomLevels[padZoomIndex];
        }

        // 프레임레이트 무관 보간
        composer.CameraDistance = Mathf.Lerp(composer.CameraDistance, targetDistance,
                                             1f - Mathf.Exp(-smoothSpeed * Time.unscaledDeltaTime));

        float ratio = composer.CameraDistance / baseDistance;
        composer.Damping = new Vector3(baseDamping.x * ratio, baseDamping.y * ratio, baseDamping.z);
    }

    public static bool IsCtrlHeld() => GameInput.GetKey(KeyCode.LeftControl) || GameInput.GetKey(KeyCode.RightControl);
}
