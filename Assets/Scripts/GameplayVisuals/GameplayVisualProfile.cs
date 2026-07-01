using UnityEngine;

namespace DuoCurtain.GameplayVisuals
{
    public enum GameplayVisualPriority
    {
        Selection = 50,
        Interaction = 60,
        EnemyVision = 70,
        HeadingPoint = 80,
        Progress = 85,
        EnemyFootprint = 90,
        Player = 100
    }

    public enum GameplayVisualDebugMode
    {
        Final = 0,
        BackgroundLuminance = 1,
        ContrastMap = 2,
        AdaptiveBlend = 3,
        Priority = 4
    }

    [CreateAssetMenu(
        fileName = "GameplayVisualProfile",
        menuName = "Duo Curtain/Gameplay Visuals/Adaptive Contrast Profile")]
    public sealed class GameplayVisualProfile : ScriptableObject
    {
        [Header("Adaptive Contrast")]
        public bool enableAdaptiveContrast = true;
        [Range(0f, 2f)] public float contrastStrength = 1f;
        [Range(0.1f, 8f)] public float contrastCurve = 2f;
        [Range(-0.5f, 0.5f)] public float brightnessBias;
        public Color primaryColor = Color.white;
        public Color secondaryColor = Color.black;
        [Range(0f, 1f)] public float adaptiveBlend = 1f;
        [Min(0f)] public float adaptiveBlendSpeed = 8f;
        public GameplayVisualPriority priority = GameplayVisualPriority.Interaction;

        [Header("Edges")]
        [Range(0f, 2f)] public float edgeContrast = 0.35f;
        public bool enableOutline;
        [Range(0f, 8f)] public float outlineWidth = 1f;
        [Range(0f, 1f)] public float outlineStrength = 0.85f;
        public Color outlineColor = Color.white;
        public bool enableHalo;
        [Range(0f, 2f)] public float haloStrength = 0.25f;

        [Header("Debug")]
        public GameplayVisualDebugMode debugMode = GameplayVisualDebugMode.Final;
    }
}
