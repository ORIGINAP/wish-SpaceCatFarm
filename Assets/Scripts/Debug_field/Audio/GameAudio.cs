using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DefaultExecutionOrder(-2000)]
public class GameAudio : MonoBehaviour
{
    public static GameAudio Instance => Services.Get<GameAudio>();

    private const string ClipFolder         = "Audio/SFX";
    private const int    VoiceCount         = 24;
    private const float  ButtonScanInterval = 0.5f;

    private readonly struct Spec
    {
        public readonly float volume, pitchJitter, cooldown;
        public readonly int   maxVoices;

        public Spec(float volume, float pitchJitter, float cooldown, int maxVoices)
        {
            this.volume      = volume;
            this.pitchJitter = pitchJitter;
            this.cooldown    = cooldown;
            this.maxVoices   = maxVoices;
        }
    }

    private static Spec SpecOf(SfxId id) => id switch
    {
        SfxId.UiClick       => new Spec(0.5f,  0.04f, 0.03f, 2),
        SfxId.UiHover       => new Spec(0.2f,  0.05f, 0.05f, 1),
        SfxId.UiOpen        => new Spec(0.45f, 0.02f, 0.06f, 1),
        SfxId.UiClose       => new Spec(0.4f,  0.02f, 0.06f, 1),
        SfxId.UiError       => new Spec(0.5f,  0f,    0.15f, 1),
        SfxId.UiConfirm     => new Spec(0.5f,  0.02f, 0.08f, 1),
        SfxId.MineHit       => new Spec(0.45f, 0.08f, 0.05f, 3),
        SfxId.Pickup        => new Spec(0.4f,  0.06f, 0.04f, 3),
        SfxId.Plant         => new Spec(0.5f,  0.08f, 0.05f, 2),
        SfxId.Harvest       => new Spec(0.5f,  0.06f, 0.05f, 3),
        SfxId.LaserMining   => new Spec(0.32f, 0.06f, 0.08f, 3),
        SfxId.LaserSeeding  => new Spec(0.3f,  0.06f, 0.08f, 3),
        SfxId.LaserHarvest  => new Spec(0.3f,  0.06f, 0.08f, 3),
        SfxId.QuestComplete => new Spec(0.65f, 0f,    0.5f,  1),
        SfxId.StormWarning  => new Spec(0.6f,  0f,    1f,    1),
        SfxId.StormHit      => new Spec(0.55f, 0.05f, 0.1f,  2),
        SfxId.GameStart     => new Spec(0.6f,  0f,    0.5f,  1),
        SfxId.ShuttleLaunch => new Spec(0.55f, 0.02f, 0.3f,  2),
        SfxId.ShuttleLand   => new Spec(0.55f, 0.02f, 0.3f,  2),
        _                   => new Spec(0.55f, 0.03f, 0.05f, 2),
    };

    private readonly Dictionary<SfxId, AudioClip[]> clips = new();
    private AudioSource[] voices;
    private float[]       voiceStartTime;
    private SfxId[]       voiceId;
    private float[]       lastPlayTime;
    private AudioMixerGroup sfxGroup;

    private readonly List<(AudioSource source, float baseVolume)> music = new();

    private Selectable[] selectables = new Selectable[256];
    private readonly HashSet<Button> sfxButtons = new();
    private int   seenSelectables = -1;
    private float nextButtonScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance == null) new GameObject("GameAudio").AddComponent<GameAudio>();
    }

    private void Awake()
    {
        if (!Services.Register(this)) { Destroy(gameObject); return; }
        DontDestroyOnLoad(gameObject);

        LoadClips();
        CreateVoices();
        SceneManager.sceneLoaded   += OnSceneLoaded;
        GameSettings.VolumeChanged += ApplyMusicVolume;
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        Services.Unregister(this);
        SceneManager.sceneLoaded   -= OnSceneLoaded;
        GameSettings.VolumeChanged -= ApplyMusicVolume;
    }

    private void Update()
    {
        // 씬 검색 대신 Selectable 목록 사용
        int count = Selectable.allSelectableCount;
        if (count == seenSelectables && !GameInput.AnyKeyDown && Time.unscaledTime < nextButtonScan) return;
        seenSelectables = count;
        nextButtonScan  = Time.unscaledTime + ButtonScanInterval;
        AddClickSounds(count);
    }

    private void AddClickSounds(int count)
    {
        if (selectables.Length < count) selectables = new Selectable[Mathf.NextPowerOfTwo(count)];
        int n = Selectable.AllSelectablesNoAlloc(selectables);
        for (int i = 0; i < n; i++)
        {
            if (selectables[i] is Button button && sfxButtons.Add(button) && !button.TryGetComponent<UiButtonSfx>(out _))
                button.gameObject.AddComponent<UiButtonSfx>();
            selectables[i] = null;
        }

        if (sfxButtons.Count > 2 * n + 256) sfxButtons.RemoveWhere(IsDestroyed);
    }

    private static readonly Predicate<Button> IsDestroyed = button => button == null;

    public void PlayUi(SfxId id, float volume = 1f) => PlayInternal(id, volume, 0f);

    public void PlayWorld(SfxId id, Vector3 worldPos, float volume = 1f)
    {
        if (!WorldGain(worldPos, out float gain, out float pan)) return;
        PlayInternal(id, volume * gain, pan);
    }

    private void PlayInternal(SfxId id, float volume, float pan)
    {
        if (!clips.TryGetValue(id, out var variants)) return;

        var   spec = SpecOf(id);
        float now  = Time.unscaledTime;
        if (now - lastPlayTime[(int)id] < spec.cooldown || CountPlaying(id) >= spec.maxVoices) return;

        float v = spec.volume * volume * GameSettings.SfxVolume;
        if (v <= 0.001f) return;
        lastPlayTime[(int)id] = now;

        int i   = PickVoice();
        var src = voices[i];
        src.Stop();
        src.clip      = variants[variants.Length == 1 ? 0 : UnityEngine.Random.Range(0, variants.Length)];
        src.volume    = Mathf.Clamp01(v);
        src.pitch     = 1f + UnityEngine.Random.Range(-spec.pitchJitter, spec.pitchJitter);
        src.panStereo = pan;
        src.Play();
        voiceStartTime[i] = now;
        voiceId[i]        = id;
    }

    private static bool WorldGain(Vector3 worldPos, out float gain, out float pan)
    {
        gain = 1f;
        pan  = 0f;
        var cam = Camera.main;
        if (cam == null) return true;

        Vector3 vp = cam.WorldToViewportPoint(worldPos);
        if (vp.z <= 0f) return false;

        float dx = (vp.x - 0.5f) * 2f, dy = (vp.y - 0.5f) * 2f;
        float d  = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
        gain = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.8f, 1.6f, d));
        pan  = Mathf.Clamp(dx * 0.35f, -0.6f, 0.6f);
        return gain > 0.02f;
    }

    private int CountPlaying(SfxId id)
    {
        int n = 0;
        for (int i = 0; i < voices.Length; i++)
            if (voiceId[i] == id && voices[i].isPlaying) n++;
        return n;
    }

    // 빈 채널, 없으면 가장 오래된 채널
    private int PickVoice()
    {
        int oldest = 0;
        for (int i = 0; i < voices.Length; i++)
        {
            if (!voices[i].isPlaying) return i;
            if (voiceStartTime[i] < voiceStartTime[oldest]) oldest = i;
        }
        return oldest;
    }

    private void LoadClips()
    {
        var byName = new Dictionary<string, List<AudioClip>>(StringComparer.Ordinal);
        foreach (var clip in Resources.LoadAll<AudioClip>(ClipFolder))
        {
            string key = clip.name;
            int us = key.LastIndexOf('_');
            if (us > 0 && int.TryParse(key.Substring(us + 1), out _)) key = key.Substring(0, us);
            if (!byName.TryGetValue(key, out var list)) byName[key] = list = new List<AudioClip>();
            list.Add(clip);
            clip.LoadAudioData();
        }

        foreach (SfxId id in Enum.GetValues(typeof(SfxId)))
            if (byName.TryGetValue(id.ToString(), out var list)) clips[id] = list.ToArray();

        if (clips.Count == 0) Debug.LogWarning($"[GameAudio] Resources/{ClipFolder}에서 효과음을 찾지 못했습니다.");
    }

    private void CreateVoices()
    {
        voices         = new AudioSource[VoiceCount];
        voiceStartTime = new float[VoiceCount];
        voiceId        = new SfxId[VoiceCount];
        for (int i = 0; i < VoiceCount; i++)
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake  = false;
            src.spatialBlend = 0f;
            src.dopplerLevel = 0f;
            voices[i] = src;
        }

        lastPlayTime = new float[Enum.GetValues(typeof(SfxId)).Length];
        for (int i = 0; i < lastPlayTime.Length; i++) lastPlayTime[i] = float.NegativeInfinity;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        music.RemoveAll(m => m.source == null);
        foreach (var src in FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (src.transform.IsChildOf(transform) || !IsMusic(src) || music.Exists(m => m.source == src)) continue;
            music.Add((src, src.volume));
            if (sfxGroup == null && src.outputAudioMixerGroup != null) UseMixer(src.outputAudioMixerGroup.audioMixer);
        }
        ApplyMusicVolume();
    }

    private static bool IsMusic(AudioSource src)
        => src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name == "BG" : src.loop;

    private void ApplyMusicVolume()
    {
        float k = GameSettings.MusicVolume;
        foreach (var (source, baseVolume) in music)
            if (source != null) source.volume = baseVolume * k;
    }

    private void UseMixer(AudioMixer mixer)
    {
        foreach (var group in mixer.FindMatchingGroups("Master"))
        {
            if (group.name != "SFX") continue;
            sfxGroup = group;
            foreach (var v in voices) v.outputAudioMixerGroup = group;
            return;
        }
    }
}
