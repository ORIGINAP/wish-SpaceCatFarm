using UnityEngine;

public class SmoothCameraFollow : MonoBehaviour
{
    public Transform player;

    public float smoothSpeed = 0.125f;


    private Vector2 minBounds;
    private Vector2 maxBounds;
    private bool hasBounds = false;
    public float fixedZ = -10.0f;
    
    void Start()
    {
        Camera.main.orthographic = false;
    }

    private void UpdateCameraBounds(CameraBoundsData data)
    {
        minBounds = data.minBounds;
        maxBounds = data.maxBounds;
        hasBounds = true;
    }
void LateUpdate()
{
    if (player == null)
    {
        Debug.LogWarning("플레이어 Transform이 할당되지 않았습니다!");
        return;
    }

    Vector3 desiredPosition = new Vector3(player.position.x, player.position.y, transform.position.z);

    Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
    
    Vector3 finalPosition = new Vector3(smoothedPosition.x, smoothedPosition.y, fixedZ);

    if (hasBounds)
    {
        // 원근 카메라 시야 기준 경계 클램프
        float distance = Mathf.Abs(finalPosition.z - player.position.z);
        float halfHeight = Mathf.Tan(Camera.main.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance;
        float halfWidth = halfHeight * Camera.main.aspect;

        finalPosition.x = Mathf.Clamp(finalPosition.x, minBounds.x + halfWidth, maxBounds.x - halfWidth);
        finalPosition.y = Mathf.Clamp(finalPosition.y, minBounds.y + halfHeight, maxBounds.y - halfHeight);
    }

    transform.position = finalPosition;
}
}