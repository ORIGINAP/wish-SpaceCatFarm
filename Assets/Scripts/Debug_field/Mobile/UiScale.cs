using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// PC 1600x900, 모바일 1333x667 기준
public static class UiScale
{
    public static readonly Vector2 PcReference     = new(1600f, 900f);
    public static readonly Vector2 MobileReference = new(1333f, 667f);

    public const float MinUserScale     = 0.7f;
    public const float MaxUserScale     = 1.2f;
    public const float DefaultUserScale = 0.85f;
    private const string UserScaleKey   = "UiScalePc";

    private static float userScale = -1f;

    public static float UserScale
    {
        get
        {
            if (userScale < 0f) userScale = Mathf.Clamp(PlayerPrefs.GetFloat(UserScaleKey, DefaultUserScale), MinUserScale, MaxUserScale);
            return userScale;
        }
    }

    public static void SetUserScale(float value)
    {
        float v = Mathf.Clamp(Mathf.Round(value * 20f) / 20f, MinUserScale, MaxUserScale);
        if (Mathf.Approximately(v, UserScale)) return;
        userScale = v;
        PlayerPrefs.SetFloat(UserScaleKey, v);
        PlayerPrefs.Save();
        ApplyAll();
    }

    private static float SceneUserScale
        => MobileMode.Active || SceneManager.GetActiveScene().name == "MainMenu" ? 1f : UserScale;

    public static Vector2 Reference => MobileMode.Active ? MobileReference : PcReference / SceneUserScale;

    public static float MobileFactor => Mathf.Min(Screen.width / MobileReference.x, Screen.height / MobileReference.y);

    public static float PcFactor => Mathf.Min(Screen.width / PcReference.x, Screen.height / PcReference.y);

    public static float Factor => MobileMode.Active ? MobileFactor : PcFactor * SceneUserScale;

    private static readonly List<CanvasScaler> tracked = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        userScale = -1f;
        tracked.Clear();
    }

    public static void Track(CanvasScaler scaler)
    {
        if (scaler != null && !tracked.Contains(scaler)) tracked.Add(scaler);
    }

    public static void ApplyAll()
    {
        float factor = Factor;
        foreach (var s in Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (s.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize && !Mathf.Approximately(s.scaleFactor, factor))
                s.scaleFactor = factor;

        Vector2 reference = Reference;
        for (int i = tracked.Count - 1; i >= 0; i--)
        {
            var s = tracked[i];
            if (s == null) { tracked.RemoveAt(i); continue; }
            if (s.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize && s.referenceResolution != reference)
                s.referenceResolution = reference;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (MobileMode.Active) return;
        var go = new GameObject("UiScaleWatcher");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<Watcher>();
    }

    private class Watcher : MonoBehaviour
    {
        private Vector2Int size;
        private float nextSweep;

        private void Awake() => SceneManager.sceneLoaded += OnLoaded;
        private void OnDestroy() => SceneManager.sceneLoaded -= OnLoaded;
        private void OnLoaded(Scene s, LoadSceneMode m) => ApplyAll();

        private void LateUpdate()
        {
            var now = new Vector2Int(Screen.width, Screen.height);
            if (now != size || Time.unscaledTime >= nextSweep)
            {
                size = now;
                nextSweep = Time.unscaledTime + 2f;
                ApplyAll();
            }
        }
    }
}
