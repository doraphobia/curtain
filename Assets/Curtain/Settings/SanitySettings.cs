#if UNITY_EDITOR
using UnityEngine;

namespace Curtain.Settings
{
    [CreateAssetMenu(menuName = "Curtain/Settings/Sanity Settings", fileName = "SanitySettings")]
    public sealed class SanitySettings : ScriptableObject
    {
        [Header("Recovery / Decay")]
        [Min(1f)] public float maxSanity = 100f;
        [Min(0f)] public float startSanity = 100f;
        [Min(0f)] public float nightOutdoorDrainPerSecond = 4f;
        [Min(0f)] public float nightIndoorRecoveryPerSecond = 1.25f;
        [Min(0f)] public float dayIndoorRecoveryPerSecond = 2f;
        [Min(0f)] public float dayOutdoorRecoveryPerSecond = 3.5f;

        [Header("Damage Sources")]
        [Min(0f)] public float enemyTouchDamage = 10f;
        [Min(0f)] public float windowDetectionDamage = 8f;

        [Header("Death")]
        public bool freezeOnDeath = true;
        public Color deathTint = new Color(0.8f, 0f, 0f, 0.42f);
        public float deathFadeDuration = 0.75f;
        [Range(1, 8)] public int deathBlurDownsample = 3;
        [Range(0, 12)] public int deathBlurRadius = 6;
        [Range(1, 4)] public int deathBlurIterations = 2;
    }
}

#endif

