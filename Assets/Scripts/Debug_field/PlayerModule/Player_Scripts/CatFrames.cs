using System.Collections.Generic;
using UnityEngine;

public class CatFrames : ScriptableObject
{
    private const string ResourcePath = "CatFrames";

    [Tooltip("고양이 프레임")]
    public Sprite[] frames;

    [Tooltip("한 프레임을 보여 주는 시간(초)")]
    public float frameSeconds = 1f / 6f;

    private static CatFrames loaded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => loaded = null;

    public static CatFrames Load() => loaded != null ? loaded : (loaded = Resources.Load<CatFrames>(ResourcePath));

    public static IReadOnlyList<Sprite> Sprites
    {
        get
        {
            var asset = Load();
            return asset != null && asset.frames != null ? asset.frames : System.Array.Empty<Sprite>();
        }
    }

    public static Sprite First => Sprites.Count > 0 ? Sprites[0] : null;

    public static float FrameSeconds
    {
        get
        {
            var asset = Load();
            return asset != null ? Mathf.Max(0.02f, asset.frameSeconds) : 1f / 6f;
        }
    }
}
