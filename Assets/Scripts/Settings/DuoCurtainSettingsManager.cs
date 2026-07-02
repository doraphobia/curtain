using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public sealed class DuoCurtainSettingsManager : MonoBehaviour
{
    private const string PrefsPrefix = "DuoCurtain.Settings.";
    private const string PrefMasterVolume = PrefsPrefix + "MasterVolume";
    private const string PrefResolutionWidth = PrefsPrefix + "ResolutionWidth";
    private const string PrefResolutionHeight = PrefsPrefix + "ResolutionHeight";
    private const string PrefFullscreenMode = PrefsPrefix + "FullscreenMode";

    public const string MasterVolumeExposedParam = "MasterVolume";

    public static DuoCurtainSettingsManager Instance { get; private set; }

    [Header("Audio")]
    [Tooltip("Optional. If assigned and exposes 'MasterVolume', this controls volume. Otherwise falls back to AudioListener.volume.")]
    [SerializeField] private AudioMixer audioMixer;

    public DuoCurtainSettingsData Current { get; private set; } = new DuoCurtainSettingsData();

    public event Action SettingsChanged;

    private static readonly (int w, int h)[] CommonResolutions =
    {
        (1280, 720),
        (1600, 900),
        (1920, 1080),
        (2560, 1440),
        (2880, 1800),
        (3840, 2160)
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (Instance != null)
            return;

        GameObject existing = GameObject.Find("DuoCurtain Settings");
        if (existing != null && existing.TryGetComponent(out DuoCurtainSettingsManager found))
        {
            Instance = found;
            Instance.InitializeIfNeeded();
            return;
        }

        GameObject go = new GameObject("DuoCurtain Settings");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<DuoCurtainSettingsManager>();
        Instance.InitializeIfNeeded();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        InitializeIfNeeded();
    }

    private bool initialized;

    private void InitializeIfNeeded()
    {
        if (initialized)
            return;

        initialized = true;
        if (audioMixer == null)
            audioMixer = Resources.Load<AudioMixer>("DuoCurtainMaster");
        LoadFromPrefs();
        ApplyAll(save: false);
    }

    public void SetAudioMixer(AudioMixer mixer)
    {
        audioMixer = mixer;
        ApplyAudio(save: false);
    }

    public void SetMasterVolume01(float volume)
    {
        Current.masterVolume = Mathf.Clamp01(volume);
        ApplyAudio(save: true);
        SettingsChanged?.Invoke();
    }

    public void SetResolution(int width, int height)
    {
        Current.resolutionWidth = Mathf.Max(0, width);
        Current.resolutionHeight = Mathf.Max(0, height);
        ApplyDisplay(save: true);
        SettingsChanged?.Invoke();
    }

    public void SetFullscreenMode(FullScreenMode mode)
    {
        Current.fullScreenMode = mode;
        ApplyDisplay(save: true);
        SettingsChanged?.Invoke();
    }

    public void ResetToDefaults()
    {
        DuoCurtainSettingsData defaults = BuildDefaults();
        Current.masterVolume = defaults.masterVolume;
        Current.resolutionWidth = defaults.resolutionWidth;
        Current.resolutionHeight = defaults.resolutionHeight;
        Current.fullScreenMode = defaults.fullScreenMode;
        ApplyAll(save: true);
        SettingsChanged?.Invoke();
    }

    public IReadOnlyList<Vector2Int> GetSupportedCommonResolutions()
    {
        HashSet<(int, int)> supported = new HashSet<(int, int)>();
        Resolution[] all = Screen.resolutions;
        if (all != null)
        {
            for (int i = 0; i < all.Length; i++)
                supported.Add((all[i].width, all[i].height));
        }

        List<Vector2Int> results = new List<Vector2Int>();
        for (int i = 0; i < CommonResolutions.Length; i++)
        {
            (int w, int h) = CommonResolutions[i];
            if (supported.Count == 0 || supported.Contains((w, h)))
                results.Add(new Vector2Int(w, h));
        }

        if (results.Count == 0)
            results.Add(new Vector2Int(Screen.width, Screen.height));

        return results;
    }

    public void ApplyAll(bool save)
    {
        ApplyAudio(save);
        ApplyDisplay(save);
    }

    private void ApplyAudio(bool save)
    {
        float v01 = Mathf.Clamp01(Current.masterVolume);

        bool mixerOk = audioMixer != null && audioMixer.SetFloat(MasterVolumeExposedParam, ToDb(v01));
        if (!mixerOk)
            AudioListener.volume = v01;

        if (save)
        {
            PlayerPrefs.SetFloat(PrefMasterVolume, v01);
            PlayerPrefs.Save();
        }
    }

    private void ApplyDisplay(bool save)
    {
        int w = Current.resolutionWidth;
        int h = Current.resolutionHeight;
        if (w <= 0 || h <= 0)
        {
            w = Screen.width;
            h = Screen.height;
        }

        FullScreenMode mode = Current.fullScreenMode;
        if (mode == FullScreenMode.ExclusiveFullScreen && Application.platform == RuntimePlatform.OSXPlayer)
            mode = FullScreenMode.FullScreenWindow;

        Screen.SetResolution(w, h, mode);

        if (save)
        {
            PlayerPrefs.SetInt(PrefResolutionWidth, w);
            PlayerPrefs.SetInt(PrefResolutionHeight, h);
            PlayerPrefs.SetInt(PrefFullscreenMode, (int)mode);
            PlayerPrefs.Save();
        }
    }

    private void LoadFromPrefs()
    {
        Current.masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(PrefMasterVolume, Current.masterVolume));
        Current.resolutionWidth = Mathf.Max(0, PlayerPrefs.GetInt(PrefResolutionWidth, 0));
        Current.resolutionHeight = Mathf.Max(0, PlayerPrefs.GetInt(PrefResolutionHeight, 0));

        int modeRaw = PlayerPrefs.GetInt(PrefFullscreenMode, (int)Current.fullScreenMode);
        if (Enum.IsDefined(typeof(FullScreenMode), modeRaw))
            Current.fullScreenMode = (FullScreenMode)modeRaw;
    }

    private static float ToDb(float v01)
    {
        if (v01 <= 0.0001f)
            return -80f;
        return Mathf.Log10(v01) * 20f;
    }

    private static DuoCurtainSettingsData BuildDefaults()
    {
        return new DuoCurtainSettingsData
        {
            masterVolume = 0.8f,
            resolutionWidth = Screen.currentResolution.width,
            resolutionHeight = Screen.currentResolution.height,
            fullScreenMode = FullScreenMode.FullScreenWindow
        };
    }
}

