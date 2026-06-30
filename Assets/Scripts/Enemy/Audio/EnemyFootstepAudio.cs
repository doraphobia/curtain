using System.Collections.Generic;
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
    [SerializeField] private string foleySurfaceId = "EnemyFootstep";

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

    private readonly List<AudioSource> sourcePool = new List<AudioSource>();

    void Awake()
    {
        if (foleyPlayer == null)
            foleyPlayer = FindFirstObjectByType<FoleyPlayer>();

        WarmPool();
    }

    public void PlayFootstep(Vector3 position, FootprintSide side)
    {
        if (useFoleyPlayerWhenAvailable && foleyPlayer != null && foleyProfile != null)
        {
            if (foleyPlayer.Play(foleyProfile, position, volume, foleySurfaceId))
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
        source.volume = volume;
        source.pitch = 1f + Random.Range(-pitchRandomRange, pitchRandomRange);
        source.spatialBlend = spatialBlend;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.rolloffMode = rolloffMode;
        source.spatialize = spatialize;
        source.Play();
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
            sourcePool.Add(CreatePooledSource());
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
        source.spatialBlend = spatialBlend;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.rolloffMode = rolloffMode;
        source.spatialize = spatialize;
        sourcePool.Add(source);
        return source;
    }
}
