using UnityEngine;
using Unity.Cinemachine;
using DG.Tweening;

[RequireComponent(typeof(CinemachineCamera))]
public class CinemachineZoomController : MonoBehaviour
{
    [Header("Zoom Settings")]
    [Tooltip("The Field of View to use when in UI mode")]
    public float zoomedInFOV = 30f;
    [Tooltip("The duration of the zoom animation in seconds")]
    public float zoomDuration = 0.5f;

    private CinemachineCamera _vcam;
    private float _initialFOV;
    private bool _isInitialized = false;

    void Awake()
    {
        _vcam = GetComponent<CinemachineCamera>();
    }

    void OnEnable()
    {
        GameStateManager.OnUIModeChanged += HandleUIModeChanged;
    }

    void OnDisable()
    {
        DOTween.Kill(this.GetInstanceID());
        GameStateManager.OnUIModeChanged -= HandleUIModeChanged;
    }

    private void HandleUIModeChanged(bool isUIMode)
    {
        if (_vcam == null) return;
        
        // 첫 전환 때 원래 FOV 기억
        if (!_isInitialized)
        {
            _initialFOV = _vcam.Lens.FieldOfView;
            _isInitialized = true;
        }

        DOTween.Kill(this.GetInstanceID());

        float targetFOV = isUIMode ? zoomedInFOV : _initialFOV;

        DOTween.To(() => _vcam.Lens.FieldOfView, fov => _vcam.Lens.FieldOfView = fov, targetFOV, zoomDuration)
            .SetEase(Ease.OutCubic)
            .SetId(this.GetInstanceID());
    }
}
