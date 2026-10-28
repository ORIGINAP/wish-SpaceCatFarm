using Unity.Cinemachine;
using UnityEngine;

public class ConfineSwitch : MonoBehaviour
{
    [Header("New Boundary Settings")]
    [Tooltip("카메라 제한 영역으로 새로 설정할 Collider 오브젝트")]
    [SerializeField] private BoxCollider2D newConfineBoundary;

    [Tooltip("전환할 가상 카메라 (PlayerCam)")]
    [SerializeField] private CinemachineCamera virtualCamera;

    [Header("Warp & Player")]
    [Tooltip("플레이어가 이동할 목표 스폰 포인트")]
    [SerializeField] private Transform targetSpawnPoint;
    private Transform playerTransform;

    private void OnTriggerEnter2D(Collider2D other)
    { 
        if (other.CompareTag("Player"))
        {
            playerTransform = other.transform;
            SwitchBoundary();
        }
    }

    public void getSwitchBoundary(BoxCollider2D boundary)
    {
        newConfineBoundary = boundary;
    }

    private void SwitchBoundary()
    {
        if (virtualCamera == null)
        {
            Debug.LogError("ConfineSwitch: Virtual Camera가 할당되지 않았습니다.");
            return;
        }

        CinemachineConfiner2D confiner = virtualCamera.GetComponent<CinemachineConfiner2D>();

        if (confiner == null)
        {
            Debug.LogError("ConfineSwitch: 가상 카메라에 Cinemachine Confiner가 없습니다.");
            return;
        }

        if (newConfineBoundary != null)
        {
            confiner.BoundingShape2D = newConfineBoundary; 
            
            confiner.InvalidateBoundingShapeCache();

            // 워프 후 카메라 순간이동
            virtualCamera.OnTargetObjectWarped(playerTransform, targetSpawnPoint.position-playerTransform.position);
            virtualCamera.PreviousStateIsValid = false;
            Debug.Log($"Confines switched to: {newConfineBoundary.name}");
        }
        else
        {
             Debug.LogWarning("ConfineSwitch: 새로운 경계 콜라이더가 할당되지 않았습니다.");
        }
    }
}