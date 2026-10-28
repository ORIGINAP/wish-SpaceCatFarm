using UnityEngine;

[CreateAssetMenu(fileName = "NewCameraBounds", menuName = "Map/Camera Bounds")]
public class CameraBoundsData : ScriptableObject
{
    public Vector2 minBounds;
    public Vector2 maxBounds;
}