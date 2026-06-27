using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(menuName = "Duo Curtain/Foley Profile", fileName = "FoleyProfile")]
public class FoleyProfile : ScriptableObject
{
    [Serializable]
    public class FoleyLayer
    {
        public string name = "Layer";
        public AudioClip[] clips;
        [Range(0f, 1f)]
        public float volume = 1f;
        public Vector2 pitchRange = new Vector2(0.95f, 1.05f);
        public Vector2 delayRange = Vector2.zero;
        public bool preventImmediateRepeat = true;
        public AudioMixerGroup outputMixerGroup;
        public bool overrideSpatialBlend = false;
        [Range(0f, 1f)]
        public float spatialBlend = 0f;
    }

    [Serializable]
    public class SurfaceBank
    {
        public string surfaceId = "Default";
        public List<FoleyLayer> layers = new List<FoleyLayer> { new FoleyLayer() };
    }

    [Header("Routing")]
    public string defaultSurfaceId = "Default";
    public AudioMixerGroup outputMixerGroup;
    [Range(0f, 1f)]
    public float spatialBlend = 0f;

    [Header("Mix")]
    [Range(0f, 1f)]
    public float masterVolume = 1f;
    public Vector2 masterPitchRange = new Vector2(1f, 1f);
    [Min(0f)]
    public float minSecondsBetweenPlays = 0.05f;

    [Header("Nuisance Control")]
    public bool useNuisanceVolume = false;
    [Range(0f, 1f)]
    public float nuisanceMinimumVolume = 0.55f;
    [Range(0f, 1f)]
    public float nuisanceVolumeDropPerPlay = 0.04f;
    public bool resetNuisanceOnSurfaceChange = true;

    [Header("Surfaces")]
    public List<SurfaceBank> surfaceBanks = new List<SurfaceBank> { new SurfaceBank() };

    public SurfaceBank GetBank(string surfaceId)
    {
        if (surfaceBanks == null || surfaceBanks.Count == 0)
            return null;

        string requestedSurfaceId = string.IsNullOrWhiteSpace(surfaceId) ? defaultSurfaceId : surfaceId.Trim();
        SurfaceBank defaultBank = null;

        for (int i = 0; i < surfaceBanks.Count; i++)
        {
            SurfaceBank bank = surfaceBanks[i];
            if (bank == null)
                continue;

            if (MatchesSurface(bank.surfaceId, requestedSurfaceId))
                return bank;

            if (defaultBank == null && MatchesSurface(bank.surfaceId, defaultSurfaceId))
                defaultBank = bank;
        }

        if (defaultBank != null)
            return defaultBank;

        return surfaceBanks.Count > 0 ? surfaceBanks[0] : null;
    }

    public bool HasAnyClips()
    {
        if (surfaceBanks == null)
            return false;

        for (int i = 0; i < surfaceBanks.Count; i++)
        {
            SurfaceBank bank = surfaceBanks[i];
            if (bank == null || bank.layers == null)
                continue;

            for (int layerIndex = 0; layerIndex < bank.layers.Count; layerIndex++)
            {
                FoleyLayer layer = bank.layers[layerIndex];
                if (layer == null || layer.clips == null)
                    continue;

                for (int clipIndex = 0; clipIndex < layer.clips.Length; clipIndex++)
                {
                    if (layer.clips[clipIndex] != null)
                        return true;
                }
            }
        }

        return false;
    }

    private static bool MatchesSurface(string bankSurfaceId, string requestedSurfaceId)
    {
        if (string.IsNullOrWhiteSpace(bankSurfaceId) || string.IsNullOrWhiteSpace(requestedSurfaceId))
            return false;

        return string.Equals(bankSurfaceId.Trim(), requestedSurfaceId.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
