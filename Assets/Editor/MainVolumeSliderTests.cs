using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

public class MainVolumeSliderTests
{
    private const string PrefKey = "MainVolume";

    [SetUp]
    public void SetUp()
    {
        PlayerPrefs.DeleteKey(PrefKey);
        AudioListener.volume = 1f;
    }

    [TearDown]
    public void TearDown()
    {
        PlayerPrefs.DeleteKey(PrefKey);
        AudioListener.volume = 1f;
    }

    [Test]
    public void Awake_WithoutSavedVolume_UsesDefaultVolume()
    {
        GameObject root = new GameObject("volume");
        Slider slider = root.AddComponent<Slider>();
        MainVolumeSlider controller = root.AddComponent<MainVolumeSlider>();
        controller.volumeSlider = slider;
        controller.defaultVolume = 0.75f;

        controller.InitializeForTests();

        Assert.That(slider.value, Is.EqualTo(0.75f).Within(0.001f));
        Assert.That(AudioListener.volume, Is.EqualTo(0.75f).Within(0.001f));

        Object.DestroyImmediate(root);
    }

    [Test]
    public void Awake_WithSavedVolume_RestoresSavedVolume()
    {
        PlayerPrefs.SetFloat(PrefKey, 0.25f);

        GameObject root = new GameObject("volume");
        Slider slider = root.AddComponent<Slider>();
        MainVolumeSlider controller = root.AddComponent<MainVolumeSlider>();
        controller.volumeSlider = slider;
        controller.defaultVolume = 0.75f;

        controller.InitializeForTests();

        Assert.That(slider.value, Is.EqualTo(0.25f).Within(0.001f));
        Assert.That(AudioListener.volume, Is.EqualTo(0.25f).Within(0.001f));

        Object.DestroyImmediate(root);
    }

    [Test]
    public void SetVolume_UpdatesAudioListenerAndSavesPreference()
    {
        GameObject root = new GameObject("volume");
        Slider slider = root.AddComponent<Slider>();
        MainVolumeSlider controller = root.AddComponent<MainVolumeSlider>();
        controller.volumeSlider = slider;

        controller.SetVolume(0.4f);

        Assert.That(AudioListener.volume, Is.EqualTo(0.4f).Within(0.001f));
        Assert.That(PlayerPrefs.GetFloat(PrefKey), Is.EqualTo(0.4f).Within(0.001f));

        Object.DestroyImmediate(root);
    }
}
