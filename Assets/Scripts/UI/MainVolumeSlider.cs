using UnityEngine;
using UnityEngine.UI;

public class MainVolumeSlider : MonoBehaviour
{
    private const string VolumePreferenceKey = "MainVolume";

    public Slider volumeSlider;
    [Range(0f, 1f)]
    public float defaultVolume = 1f;

    private void Awake()
    {
        Initialize();
    }

    private void OnDestroy()
    {
        if (volumeSlider != null)
        {
            volumeSlider.onValueChanged.RemoveListener(SetVolume);
        }
    }

    public void SetVolume(float value)
    {
        float clampedValue = Mathf.Clamp01(value);
        AudioListener.volume = clampedValue;
        PlayerPrefs.SetFloat(VolumePreferenceKey, clampedValue);
        PlayerPrefs.Save();
    }

    public void InitializeForTests()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (volumeSlider == null)
        {
            volumeSlider = GetComponent<Slider>();
        }

        float initialVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolumePreferenceKey, defaultVolume));
        AudioListener.volume = initialVolume;

        if (volumeSlider == null)
        {
            Debug.LogWarning("MainVolumeSlider requires a Slider reference.", this);
            return;
        }

        volumeSlider.minValue = 0f;
        volumeSlider.maxValue = 1f;
        volumeSlider.wholeNumbers = false;
        volumeSlider.SetValueWithoutNotify(initialVolume);
        volumeSlider.onValueChanged.RemoveListener(SetVolume);
        volumeSlider.onValueChanged.AddListener(SetVolume);
    }
}
