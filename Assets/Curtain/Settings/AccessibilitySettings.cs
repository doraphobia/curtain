#if UNITY_EDITOR
using UnityEngine;

namespace Curtain.Settings
{
    [CreateAssetMenu(menuName = "Curtain/Settings/Accessibility Settings", fileName = "AccessibilitySettings")]
    public sealed class AccessibilitySettings : ScriptableObject
    {
        [Header("Contrast")]
        [Range(0f, 2f)] public float gameplayContrast = 1f;
        [Range(0f, 2f)] public float outlineStrength = 1f;
        [Range(0f, 2f)] public float adaptiveBrightness = 1f;

        [Header("Future")]
        public bool reservedForColorBlindSupport;
    }
}

#endif

