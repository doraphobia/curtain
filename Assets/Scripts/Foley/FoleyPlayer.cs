using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class FoleyPlayer : MonoBehaviour
{
    private class PooledSource
    {
        public AudioSource source;
        public float busyUntil;
    }

    private class ProfileState
    {
        public float lastPlayTime = -999f;
        public string surfaceId = string.Empty;
        public float nuisanceVolume = 1f;
    }

    [Header("Surface")]
    public FoleySurfaceResolver2D surfaceResolver;

    [Header("Source Pool")]
    [Min(1)]
    public int initialPoolSize = 3;
    [Min(1)]
    public int maxPoolSize = 12;
    public bool createSourcesOnAwake = true;
    public bool useUnscaledTime = false;

    [Header("3D Source Defaults")]
    [Min(0.01f)]
    public float minDistance = 1f;
    [Min(0.01f)]
    public float maxDistance = 500f;
    public AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;
    public bool spatialize = false;

    private readonly List<PooledSource> sourcePool = new List<PooledSource>();
    private readonly Dictionary<int, ProfileState> profileStates = new Dictionary<int, ProfileState>();
    private readonly Dictionary<string, int> lastClipIndices = new Dictionary<string, int>();

    void Awake()
    {
        if (surfaceResolver == null)
            surfaceResolver = GetComponent<FoleySurfaceResolver2D>();

        if (createSourcesOnAwake)
            WarmPool(initialPoolSize);
    }

    public bool Play(FoleyProfile profile)
    {
        return Play(profile, transform.position, 1f, null);
    }

    public bool Play(FoleyProfile profile, Vector3 worldPosition, float volumeMultiplier)
    {
        return Play(profile, worldPosition, volumeMultiplier, null);
    }

    public bool Play(FoleyProfile profile, Vector3 worldPosition, float volumeMultiplier, string surfaceId)
    {
        return Play(profile, worldPosition, volumeMultiplier, surfaceId, 1f, 1f);
    }

    public bool Play(
        FoleyProfile profile,
        Vector3 worldPosition,
        float volumeMultiplier,
        string surfaceId,
        float pitchMultiplier,
        float delayMultiplier)
    {
        if (profile == null)
            return false;

        string resolvedSurfaceId = surfaceId;
        if (string.IsNullOrWhiteSpace(resolvedSurfaceId) && surfaceResolver != null)
            resolvedSurfaceId = surfaceResolver.ResolveSurfaceId(worldPosition);

        FoleyProfile.SurfaceBank bank = profile.GetBank(resolvedSurfaceId);
        if (bank == null || bank.layers == null || bank.layers.Count == 0)
            return false;

        float now = GetTime();
        ProfileState state = GetState(profile);
        string bankSurfaceId = string.IsNullOrWhiteSpace(bank.surfaceId) ? profile.defaultSurfaceId : bank.surfaceId.Trim();

        if (!string.Equals(state.surfaceId, bankSurfaceId, System.StringComparison.OrdinalIgnoreCase))
        {
            state.surfaceId = bankSurfaceId;
            if (profile.resetNuisanceOnSurfaceChange)
                state.nuisanceVolume = 1f;
        }

        if (now < state.lastPlayTime + profile.minSecondsBetweenPlays)
            return false;

        float nuisanceMultiplier = profile.useNuisanceVolume ? state.nuisanceVolume : 1f;
        bool playedAnyLayer = false;

        for (int i = 0; i < bank.layers.Count; i++)
        {
            FoleyProfile.FoleyLayer layer = bank.layers[i];
            if (layer == null)
                continue;

            AudioClip clip = PickClip(profile, bank, layer, i);
            if (clip == null)
                continue;

            PooledSource pooledSource = GetAvailableSource(now);
            if (pooledSource == null)
                continue;

            float layerPitch = RandomRange(layer.pitchRange, 1f);
            float masterPitch = RandomRange(profile.masterPitchRange, 1f);
            float pitch = Mathf.Max(0.01f, layerPitch * masterPitch * Mathf.Max(0.01f, pitchMultiplier));
            float volume = Mathf.Clamp01(profile.masterVolume * layer.volume * volumeMultiplier * nuisanceMultiplier);
            float delay = Mathf.Max(0f, RandomRange(layer.delayRange, 0f) * Mathf.Max(0f, delayMultiplier));

            AudioSource source = pooledSource.source;
            source.transform.position = worldPosition;
            source.playOnAwake = false;
            source.loop = false;
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.spatialBlend = layer.overrideSpatialBlend ? layer.spatialBlend : profile.spatialBlend;
            ApplySourceSpatialDefaults(source);
            source.outputAudioMixerGroup = layer.outputMixerGroup != null ? layer.outputMixerGroup : profile.outputMixerGroup;

            if (delay > 0f)
                source.PlayDelayed(delay);
            else
                source.Play();

            pooledSource.busyUntil = now + delay + (clip.length / pitch) + 0.05f;
            playedAnyLayer = true;
        }

        if (!playedAnyLayer)
            return false;

        state.lastPlayTime = now;
        if (profile.useNuisanceVolume)
        {
            state.nuisanceVolume = Mathf.Max(
                profile.nuisanceMinimumVolume,
                state.nuisanceVolume - profile.nuisanceVolumeDropPerPlay
            );
        }

        return true;
    }

    public void ResetNuisance(FoleyProfile profile)
    {
        if (profile == null)
            return;

        GetState(profile).nuisanceVolume = 1f;
    }

    public void ConfigureSpatialDefaults(
        float sourceMinDistance,
        float sourceMaxDistance,
        AudioRolloffMode sourceRolloffMode,
        bool sourceSpatialize)
    {
        minDistance = Mathf.Max(0.01f, sourceMinDistance);
        maxDistance = Mathf.Max(minDistance + 0.01f, sourceMaxDistance);
        rolloffMode = sourceRolloffMode;
        spatialize = sourceSpatialize;

        for (int i = 0; i < sourcePool.Count; i++)
        {
            PooledSource pooledSource = sourcePool[i];
            if (pooledSource?.source != null)
                ApplySourceSpatialDefaults(pooledSource.source);
        }
    }

    private void WarmPool(int count)
    {
        int targetCount = Mathf.Clamp(count, 1, Mathf.Max(1, maxPoolSize));
        while (sourcePool.Count < targetCount)
            AddSource();
    }

    private PooledSource GetAvailableSource(float now)
    {
        for (int i = 0; i < sourcePool.Count; i++)
        {
            PooledSource pooledSource = sourcePool[i];
            if (pooledSource == null || pooledSource.source == null)
                continue;

            if (!pooledSource.source.isPlaying && now >= pooledSource.busyUntil)
                return pooledSource;
        }

        if (sourcePool.Count >= Mathf.Max(1, maxPoolSize))
            return null;

        return AddSource();
    }

    private PooledSource AddSource()
    {
        AudioSource source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        ApplySourceSpatialDefaults(source);

        PooledSource pooledSource = new PooledSource
        {
            source = source,
            busyUntil = -999f
        };
        sourcePool.Add(pooledSource);
        return pooledSource;
    }

    private void ApplySourceSpatialDefaults(AudioSource source)
    {
        if (source == null)
            return;

        source.minDistance = Mathf.Max(0.01f, minDistance);
        source.maxDistance = Mathf.Max(source.minDistance + 0.01f, maxDistance);
        source.rolloffMode = rolloffMode;
        source.spatialize = spatialize;
    }

    private ProfileState GetState(FoleyProfile profile)
    {
        int key = profile.GetInstanceID();
        if (!profileStates.TryGetValue(key, out ProfileState state))
        {
            state = new ProfileState();
            profileStates.Add(key, state);
        }

        return state;
    }

    private AudioClip PickClip(FoleyProfile profile, FoleyProfile.SurfaceBank bank, FoleyProfile.FoleyLayer layer, int layerIndex)
    {
        if (layer.clips == null || layer.clips.Length == 0)
            return null;

        string key = profile.GetInstanceID() + "|" + bank.surfaceId + "|" + layerIndex;
        bool hasLastIndex = lastClipIndices.TryGetValue(key, out int lastIndex);

        for (int attempts = 0; attempts < layer.clips.Length; attempts++)
        {
            int index = Random.Range(0, layer.clips.Length);
            AudioClip clip = layer.clips[index];
            if (clip == null)
                continue;

            if (hasLastIndex && layer.preventImmediateRepeat && layer.clips.Length > 1 && index == lastIndex)
                continue;

            lastClipIndices[key] = index;
            return clip;
        }

        for (int i = 0; i < layer.clips.Length; i++)
        {
            if (layer.clips[i] == null)
                continue;

            lastClipIndices[key] = i;
            return layer.clips[i];
        }

        return null;
    }

    private float GetTime()
    {
        return useUnscaledTime ? Time.unscaledTime : Time.time;
    }

    private static float RandomRange(Vector2 range, float fallback)
    {
        float min = Mathf.Min(range.x, range.y);
        float max = Mathf.Max(range.x, range.y);
        if (Mathf.Approximately(min, 0f) && Mathf.Approximately(max, 0f))
            return fallback;

        if (Mathf.Approximately(min, max))
            return min;

        return Random.Range(min, max);
    }
}
