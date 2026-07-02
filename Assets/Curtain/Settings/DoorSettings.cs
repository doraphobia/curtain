using UnityEngine;

namespace Curtain.Settings
{
    [CreateAssetMenu(menuName = "Curtain/Settings/Door Settings", fileName = "DoorSettings")]
    public sealed class DoorSettings : ScriptableObject
    {
        [Header("Health")]
        [Min(0.01f)] public float maxHealth = 100f;
        public bool invulnerable;
        [Min(0f)] public float destroyDelay;

        [Header("Interaction")]
        [Min(0f)] public float toggleCooldown = 0.22f;
        [Range(1f, 179f)] public float openAngleDegrees = 90f;
        [Range(0f, 1f)] public float doorwayPassableOpenAmount = 0.82f;

        [Header("Animation")]
        public bool animateDoor = true;
        [Min(0f)] public float openDuration = 0.25f;
        [Min(0f)] public float closeDuration = 0.2f;
        public AnimationCurve swingCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        public bool useEndWobble = true;
        [Min(0f)] public float endWobbleDuration = 0.18f;
        [Min(0f)] public float endWobbleAmplitudeDegrees = 6f;
        [Min(0.5f)] public float endWobbleOscillations = 2.5f;

        [Header("Visual (Debug Style)")]
        public bool includeWallVisual = true;
        public bool useDefaultWallDebugVisual = true;
        public Color wallColor = new Color(0.38f, 0.38f, 0.38f, 0.95f);
        [Min(0.005f)] public float wallLineWidth = 0.035f;
        [Min(0.01f)] public float wallDashLength = 0.28f;
        [Min(0.01f)] public float wallGapLength = 0.16f;

        [Header("Debug")]
        public bool registerForVisibility = true;
    }
}

