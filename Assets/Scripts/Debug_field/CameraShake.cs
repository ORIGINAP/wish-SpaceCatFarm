using UnityEngine;
using Unity.Cinemachine;
using System.Collections;

public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance => Services.Get<CameraShake>();

    [Tooltip("연속적인 흔들림 느낌을 위해 짧은 임펄스를 반복 발사하는 간격(초)")]
    [SerializeField] private float pulseInterval = 0.08f;

    private CinemachineImpulseSource impulseSource;
    private Coroutine shakeCoroutine;

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }

        SetupImpulseSource();
        EnsureListenersOnActiveCameras();
    }

    private void OnDestroy() => Services.Unregister(this);

    public static CameraShake GetOrCreate()
    {
        if (Instance != null) return Instance;
        return new GameObject("CameraShake").AddComponent<CameraShake>();
    }

    public void Shake(float duration, float magnitude) => Shake(duration, magnitude, magnitude);

    public void Shake(float duration, float startMagnitude, float endMagnitude)
    {
        if (shakeCoroutine != null) StopCoroutine(shakeCoroutine);
        shakeCoroutine = StartCoroutine(ShakeRoutine(duration, startMagnitude, endMagnitude));
        GamepadRumble.Shake(duration, startMagnitude, endMagnitude);
    }

    private void SetupImpulseSource()
    {
        impulseSource = gameObject.AddComponent<CinemachineImpulseSource>();

        var def = impulseSource.ImpulseDefinition;
        def.ImpulseType     = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
        def.ImpulseShape     = CinemachineImpulseDefinition.ImpulseShapes.Bump;
        def.ImpulseDuration  = Mathf.Max(pulseInterval * 1.5f, 0.1f);
    }

    private static void EnsureListenersOnActiveCameras()
    {
        var vcams = FindObjectsByType<CinemachineCamera>(FindObjectsSortMode.None);

        foreach (var vcam in vcams)
        {
            var listener = vcam.GetComponent<CinemachineImpulseListener>();
            if (listener == null)
                listener = vcam.gameObject.AddComponent<CinemachineImpulseListener>();

            if (listener.ChannelMask == 0) listener.ChannelMask = 1;
            if (listener.Gain == 0f)       listener.Gain = 1f;
        }
    }

    private IEnumerator ShakeRoutine(float duration, float startMagnitude, float endMagnitude)
    {
        float elapsed       = 0f;
        float nextPulseTime = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            // 짧은 임펄스 반복으로 연속 흔들림
            if (elapsed >= nextPulseTime)
            {
                float t = duration > 0f ? Mathf.Clamp01(elapsed / duration) : 1f;
                float magnitude = Mathf.Lerp(startMagnitude, endMagnitude, t);
                impulseSource.GenerateImpulseWithForce(magnitude);
                nextPulseTime = elapsed + pulseInterval;
            }

            yield return null;
        }

        shakeCoroutine = null;
    }
}
