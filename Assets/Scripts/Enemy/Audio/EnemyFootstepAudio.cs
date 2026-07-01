using System.Collections.Generic;
using DuoCurtain.RuntimeTileMesh;
using UnityEngine;

/// <summary>
/// 3D spatial footstep audio for invisible enemy footprints.
/// Uses pooled AudioSources with full spatial blend. Optionally delegates to <see cref="FoleyPlayer"/>.
/// </summary>
[DisallowMultipleComponent]
public class EnemyFootstepAudio : MonoBehaviour
{
    [Header("Footstep Clips")]
    [SerializeField] private AudioClip[] leftFootstepClips;
    [SerializeField] private AudioClip[] rightFootstepClips;

    [Header("Foley Integration")]
    [SerializeField] private bool useFoleyPlayerWhenAvailable = true;
    [SerializeField] private FoleyPlayer foleyPlayer;
    [SerializeField] private FoleyProfile foleyProfile;
    [SerializeField] private bool usePlayerFootstepProfileWhenMissing = true;
    [SerializeField] private bool createRuntime3DProfileClone = true;

    [Header("Surface Resolution")]
    [SerializeField] private string indoorSurfaceId = "Concrete";
    [SerializeField] private string outdoorSurfaceId = "Outdoor";
    [SerializeField] private string outdoorFallbackSurfaceId = "Grass";
    [SerializeField] private bool useSideSpecificSurfaceIds = true;
    [SerializeField] private string leftSurfaceSuffix = ".Left";
    [SerializeField] private string rightSurfaceSuffix = ".Right";

    [Header("3D Audio")]
    [SerializeField] private AudioSource audioSourcePrefab;
    [Min(0f)] [SerializeField] private float volume = 1f;
    [Min(0f)] [SerializeField] private float pitchRandomRange = 0.05f;
    [Min(0.01f)] [SerializeField] private float minDistance = 1f;
    [Min(0.01f)] [SerializeField] private float maxDistance = 12f;
    [SerializeField] private AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;
    [SerializeField] private bool spatialize = true;
    [Range(0f, 1f)] [SerializeField] private float spatialBlend = 1f;
    [Min(1)] [SerializeField] private int poolSize = 6;

    [Header("State Mix")]
    [Min(0f)] [SerializeField] private float normalMovingVolumeMultiplier = 1f;
    [Min(0f)] [SerializeField] private float targetingDoorVolumeMultiplier = 1.18f;
    [Min(0f)] [SerializeField] private float chasingVolumeMultiplier = 1.28f;
    [Min(0f)] [SerializeField] private float watchingVolumeMultiplier = 0.75f;
    [Min(0.01f)] [SerializeField] private float normalPitchMultiplier = 0.96f;
    [Min(0.01f)] [SerializeField] private float urgencyPitchMultiplier = 1.04f;

    private readonly List<AudioSource> sourcePool = new List<AudioSource>();
    private FoleyProfile runtimeFoleyProfile;
    private FoleyProfile runtimeFoleyProfileSource;
    private FusionNightFootprintEnemy fusionEnemy;

    void Awake()
    {
        if (foleyPlayer == null)
            foleyPlayer = GetComponent<FoleyPlayer>();
        if (foleyPlayer == null)
            foleyPlayer = gameObject.AddComponent<FoleyPlayer>();

        fusionEnemy = GetComponent<FusionNightFootprintEnemy>();
        ConfigureLocalFoleyPlayer();

        WarmPool();
    }

    public void PlayFootstep(Vector3 position, FootprintSide side)
    {
        PlayFootstep(position, side, EnemyTraceState.NormalMoving, ResolveIsIndoor(position));
    }

    public void PlayFootstep(Vector3 position, FootprintSide side, EnemyTraceState traceState, bool isIndoor)
    {
        float stateVolume = ResolveStateVolumeMultiplier(traceState);
        float statePitch = ResolveStatePitchMultiplier(traceState);

        FoleyProfile playbackProfile = GetPlaybackProfile();
        if (useFoleyPlayerWhenAvailable && foleyPlayer != null && playbackProfile != null)
        {
            string surfaceId = ResolveSurfaceId(playbackProfile, side, isIndoor);
            if (foleyPlayer.Play(playbackProfile, position, volume * stateVolume, surfaceId, statePitch, 1f))
                return;
        }

        AudioClip clip = PickClip(side);
        if (clip == null)
            return;

        AudioSource source = GetAvailableSource();
        if (source == null)
            return;

        source.transform.position = position;
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume * stateVolume);
        source.pitch = Mathf.Max(0.01f, (1f + Random.Range(-pitchRandomRange, pitchRandomRange)) * statePitch);
        source.spatialBlend = spatialBlend;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.rolloffMode = rolloffMode;
        source.spatialize = spatialize;
        source.Play();
    }

    public void ConfigureFoley(
        FoleyProfile profile,
        string indoorSurface,
        string outdoorSurface,
        string outdoorFallbackSurface)
    {
        if (profile != null)
            foleyProfile = profile;

        if (!string.IsNullOrWhiteSpace(indoorSurface))
            indoorSurfaceId = indoorSurface.Trim();
        if (!string.IsNullOrWhiteSpace(outdoorSurface))
            outdoorSurfaceId = outdoorSurface.Trim();
        if (!string.IsNullOrWhiteSpace(outdoorFallbackSurface))
            outdoorFallbackSurfaceId = outdoorFallbackSurface.Trim();

        runtimeFoleyProfile = null;
        runtimeFoleyProfileSource = null;
    }

    public void Configure3DAudio(
        float newVolume,
        float newMinDistance,
        float newMaxDistance,
        AudioRolloffMode newRolloffMode,
        bool newSpatialize,
        float newSpatialBlend)
    {
        volume = Mathf.Max(0f, newVolume);
        minDistance = Mathf.Max(0.01f, newMinDistance);
        maxDistance = Mathf.Max(minDistance + 0.01f, newMaxDistance);
        rolloffMode = newRolloffMode;
        spatialize = newSpatialize;
        spatialBlend = Mathf.Clamp01(newSpatialBlend);
        ConfigureLocalFoleyPlayer();

        for (int i = 0; i < sourcePool.Count; i++)
            ApplySourceDefaults(sourcePool[i]);
    }

    public void ConfigureStateMix(
        float normalVolume,
        float targetingDoorVolume,
        float chasingVolume,
        float watchingVolume,
        float normalPitch,
        float urgencyPitch)
    {
        normalMovingVolumeMultiplier = Mathf.Max(0f, normalVolume);
        targetingDoorVolumeMultiplier = Mathf.Max(0f, targetingDoorVolume);
        chasingVolumeMultiplier = Mathf.Max(0f, chasingVolume);
        watchingVolumeMultiplier = Mathf.Max(0f, watchingVolume);
        normalPitchMultiplier = Mathf.Max(0.01f, normalPitch);
        urgencyPitchMultiplier = Mathf.Max(0.01f, urgencyPitch);
    }

    private AudioClip PickClip(FootprintSide side)
    {
        AudioClip[] clips = side == FootprintSide.Left ? leftFootstepClips : rightFootstepClips;
        if (clips == null || clips.Length == 0)
            clips = side == FootprintSide.Left ? rightFootstepClips : leftFootstepClips;

        if (clips == null || clips.Length == 0)
            return null;

        for (int attempt = 0; attempt < clips.Length; attempt++)
        {
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            if (clip != null)
                return clip;
        }

        return null;
    }

    private void WarmPool()
    {
        while (sourcePool.Count < poolSize)
            CreatePooledSource();
    }

    private AudioSource GetAvailableSource()
    {
        for (int i = 0; i < sourcePool.Count; i++)
        {
            AudioSource source = sourcePool[i];
            if (source != null && !source.isPlaying)
                return source;
        }

        if (sourcePool.Count < poolSize * 2)
            return CreatePooledSource();

        return sourcePool[0];
    }

    private AudioSource CreatePooledSource()
    {
        AudioSource source = audioSourcePrefab != null
            ? Instantiate(audioSourcePrefab, transform)
            : gameObject.AddComponent<AudioSource>();

        source.playOnAwake = false;
        source.loop = false;
        ApplySourceDefaults(source);
        sourcePool.Add(source);
        return source;
    }

    private FoleyProfile GetPlaybackProfile()
    {
        if (foleyProfile == null && usePlayerFootstepProfileWhenMissing)
        {
            PlayerControl player = FindFirstObjectByType<PlayerControl>();
            if (player != null && player.footstepFoleyProfile != null)
                foleyProfile = player.footstepFoleyProfile;
        }

        if (foleyProfile == null || !createRuntime3DProfileClone)
            return foleyProfile;

        if (runtimeFoleyProfile == null || runtimeFoleyProfileSource != foleyProfile)
        {
            runtimeFoleyProfile = Instantiate(foleyProfile);
            runtimeFoleyProfile.name = foleyProfile.name + " Enemy 3D Runtime";
            runtimeFoleyProfile.spatialBlend = spatialBlend;
            runtimeFoleyProfileSource = foleyProfile;
        }

        runtimeFoleyProfile.spatialBlend = spatialBlend;
        return runtimeFoleyProfile;
    }

    private string ResolveSurfaceId(FoleyProfile profile, FootprintSide side, bool isIndoor)
    {
        string baseSurface = isIndoor ? indoorSurfaceId : outdoorSurfaceId;
        string fallbackSurface = isIndoor ? "Concrete" : outdoorFallbackSurfaceId;

        if (profile == null)
            return string.IsNullOrWhiteSpace(baseSurface) ? fallbackSurface : baseSurface;

        if (useSideSpecificSurfaceIds)
        {
            string suffix = side == FootprintSide.Left ? leftSurfaceSuffix : rightSurfaceSuffix;
            string sideSurface = CombineSurfaceSuffix(baseSurface, suffix);
            if (profile.HasSurface(sideSurface))
                return sideSurface;

            string sideFallback = CombineSurfaceSuffix(fallbackSurface, suffix);
            if (profile.HasSurface(sideFallback))
                return sideFallback;
        }

        if (profile.HasSurface(baseSurface))
            return baseSurface;

        if (profile.HasSurface(fallbackSurface))
            return fallbackSurface;

        return string.IsNullOrWhiteSpace(baseSurface) ? fallbackSurface : baseSurface;
    }

    private bool ResolveIsIndoor(Vector3 position)
    {
        if (RoomManager.IsInsideAnyRoom(position))
            return true;

        if (fusionEnemy == null)
            fusionEnemy = GetComponent<FusionNightFootprintEnemy>();

        return fusionEnemy != null && fusionEnemy.IsInsideAnyFusionRoom(position);
    }

    private float ResolveStateVolumeMultiplier(EnemyTraceState traceState)
    {
        switch (traceState)
        {
            case EnemyTraceState.TargetingDoor:
                return targetingDoorVolumeMultiplier;
            case EnemyTraceState.ChasingPlayer:
            case EnemyTraceState.Attacking:
                return chasingVolumeMultiplier;
            case EnemyTraceState.Watching:
                return watchingVolumeMultiplier;
            default:
                return normalMovingVolumeMultiplier;
        }
    }

    private float ResolveStatePitchMultiplier(EnemyTraceState traceState)
    {
        switch (traceState)
        {
            case EnemyTraceState.TargetingDoor:
            case EnemyTraceState.ChasingPlayer:
            case EnemyTraceState.Attacking:
                return urgencyPitchMultiplier;
            default:
                return normalPitchMultiplier;
        }
    }

    private void ConfigureLocalFoleyPlayer()
    {
        if (foleyPlayer == null)
            return;

        foleyPlayer.ConfigureSpatialDefaults(minDistance, maxDistance, rolloffMode, spatialize);
    }

    private void ApplySourceDefaults(AudioSource source)
    {
        if (source == null)
            return;

        source.spatialBlend = spatialBlend;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.rolloffMode = rolloffMode;
        source.spatialize = spatialize;
    }

    private static string CombineSurfaceSuffix(string surfaceId, string suffix)
    {
        if (string.IsNullOrWhiteSpace(surfaceId))
            return string.Empty;
        if (string.IsNullOrWhiteSpace(suffix))
            return surfaceId.Trim();

        return surfaceId.Trim() + suffix.Trim();
    }
}
