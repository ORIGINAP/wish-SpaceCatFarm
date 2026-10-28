using System;
using System.Collections.Generic;
using UnityEngine;

public static class GameSettings
{
    private const string KeyVolume = "MainVolume";
    private const string KeyMusic  = "Settings_MusicVolume";
    private const string KeySfx    = "Settings_SfxVolume";
    private const string KeyResW   = "Settings_ResolutionWidth";
    private const string KeyResH   = "Settings_ResolutionHeight";
    private const string KeyScreen = "Settings_FullScreenMode";
    private const string KeyAiNpc  = "Settings_AiNpcEnabled";
    private const string KeyAiKey  = "Settings_AiApiKey";
    private const string KeyRumble = "Settings_VibrationStrength";

    public static event Action VolumeChanged;

    public static event Action<bool> AiNpcEnabledChanged;

    public static readonly FullScreenMode[] ScreenModes =
    {
        FullScreenMode.Windowed,
        FullScreenMode.FullScreenWindow,
        FullScreenMode.ExclusiveFullScreen,
    };

    private static Vector2Int mobileNative;

    private static readonly float[] MobileResolutionSteps = { 0.7f, 0.85f, 1f };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        VolumeChanged = null;
        AiNpcEnabledChanged = null;
        mobileNative = default;
        sfxVolume = -1f;
        vibration = -1f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyOnStartup()
    {
        AudioListener.volume = MasterVolume;
        if (MobileMode.Active) mobileNative = Landscape(Screen.width, Screen.height);
#if !UNITY_EDITOR
        // 모바일은 화면에 안 맞으면 저장값 폐기
        if (PlayerPrefs.HasKey(KeyResW) && PlayerPrefs.HasKey(KeyResH))
        {
            var saved = new Vector2Int(PlayerPrefs.GetInt(KeyResW), PlayerPrefs.GetInt(KeyResH));
            if (!MobileMode.Active)
                Screen.SetResolution(saved.x, saved.y, SavedScreenMode);
            else if (FitsMobileScreen(saved))
                Screen.SetResolution(saved.x, saved.y, FullScreenMode.FullScreenWindow);
            else
            {
                PlayerPrefs.DeleteKey(KeyResW);
                PlayerPrefs.DeleteKey(KeyResH);
                PlayerPrefs.Save();
            }
        }
#endif
    }

    private static Vector2Int Landscape(int w, int h) => new(Mathf.Max(w, h), Mathf.Min(w, h));

    private static bool FitsMobileScreen(Vector2Int r)
        => r.x > 0 && r.y > 0 && r.x <= mobileNative.x && r.y <= mobileNative.y &&
           Mathf.Abs((float)r.x / r.y - (float)mobileNative.x / mobileNative.y) < 0.01f;

    public static float MasterVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(KeyVolume, 1f));
        set
        {
            float v = Mathf.Clamp01(value);
            AudioListener.volume = v;
            PlayerPrefs.SetFloat(KeyVolume, v);
        }
    }

    public static float MusicVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(KeyMusic, 1f));
        set
        {
            PlayerPrefs.SetFloat(KeyMusic, Mathf.Clamp01(value));
            VolumeChanged?.Invoke();
        }
    }

    private static float sfxVolume = -1f;

    public static float SfxVolume
    {
        get
        {
            if (sfxVolume < 0f) sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KeySfx, 1f));
            return sfxVolume;
        }
        set
        {
            sfxVolume = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeySfx, sfxVolume);
            VolumeChanged?.Invoke();
        }
    }

    private static float vibration = -1f;

    public static float VibrationStrength
    {
        get
        {
            if (vibration < 0f) vibration = Mathf.Clamp01(PlayerPrefs.GetFloat(KeyRumble, 1f));
            return vibration;
        }
        set
        {
            vibration = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat(KeyRumble, vibration);
        }
    }

    public static void Flush() => PlayerPrefs.Save();

    public static bool AiNpcEnabled
    {
        get => PlayerPrefs.GetInt(KeyAiNpc, 1) == 1;
        set
        {
            if (AiNpcEnabled == value) return;
            PlayerPrefs.SetInt(KeyAiNpc, value ? 1 : 0);
            PlayerPrefs.Save();
            AiNpcEnabledChanged?.Invoke(value);
        }
    }

    public static string AiApiKey
    {
        get => PlayerPrefs.GetString(KeyAiKey, "");
        set
        {
            string key = value?.Trim() ?? "";
            if (key.Length == 0) PlayerPrefs.DeleteKey(KeyAiKey);
            else                 PlayerPrefs.SetString(KeyAiKey, key);
            PlayerPrefs.Save();
        }
    }

    public static FullScreenMode SavedScreenMode
        => (FullScreenMode)PlayerPrefs.GetInt(KeyScreen, (int)Screen.fullScreenMode);

    public static Vector2Int SavedResolution
        => PlayerPrefs.HasKey(KeyResW) && PlayerPrefs.HasKey(KeyResH)
            ? new Vector2Int(PlayerPrefs.GetInt(KeyResW), PlayerPrefs.GetInt(KeyResH))
            : new Vector2Int(Screen.width, Screen.height);

    public static List<Vector2Int> GetResolutionOptions()
    {
        if (MobileMode.Active)
        {
            var native = mobileNative.x > 0 ? mobileNative : Landscape(Screen.width, Screen.height);
            var steps  = new List<Vector2Int>();
            foreach (float k in MobileResolutionSteps)
                // 짝수 해상도로 반올림
                steps.Add(new Vector2Int(Mathf.RoundToInt(native.x * k / 2f) * 2, Mathf.RoundToInt(native.y * k / 2f) * 2));
            return steps;
        }

        var set = new HashSet<Vector2Int>();
        foreach (var r in Screen.resolutions)
            if (r.width >= 800 && r.height >= 600) set.Add(new Vector2Int(r.width, r.height));

        if (set.Count < 3)
        {
            set.Add(new Vector2Int(1280, 720));
            set.Add(new Vector2Int(1600, 900));
            set.Add(new Vector2Int(1920, 1080));
        }
        set.Add(SavedResolution);

        var list = new List<Vector2Int>(set);
        list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return list;
    }

    public static void ApplyResolution(Vector2Int resolution, FullScreenMode mode)
    {
        if (MobileMode.Active) mode = FullScreenMode.FullScreenWindow;
        Screen.SetResolution(resolution.x, resolution.y, mode);
        PlayerPrefs.SetInt(KeyResW, resolution.x);
        PlayerPrefs.SetInt(KeyResH, resolution.y);
        PlayerPrefs.SetInt(KeyScreen, (int)mode);
        PlayerPrefs.Save();
    }

    public static string ResolutionName(Vector2Int r)
    {
        if (!MobileMode.Active || mobileNative.x <= 0) return $"{r.x} × {r.y}";
        return $"{r.x} × {r.y} ({Mathf.RoundToInt(100f * r.x / mobileNative.x)}%)";
    }

    public static string ScreenModeName(FullScreenMode mode) => mode switch
    {
        FullScreenMode.Windowed            => "창 모드",
        FullScreenMode.FullScreenWindow    => "전체 화면 (테두리 없음)",
        FullScreenMode.ExclusiveFullScreen => "전체 화면",
        FullScreenMode.MaximizedWindow     => "최대화 창",
        _                                  => mode.ToString(),
    };

    public static void ReapplyAll()
    {
        AudioListener.volume = MasterVolume;
        sfxVolume = -1f;
        vibration = -1f;
        VolumeChanged?.Invoke();
        AiNpcEnabledChanged?.Invoke(AiNpcEnabled);
    }
}
