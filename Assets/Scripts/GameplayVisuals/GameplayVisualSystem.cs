using UnityEngine;

namespace DuoCurtain.GameplayVisuals
{
    public static class GameplayVisualSystem
    {
        public const string ShaderName = "Duo Curtain/Gameplay Visual Adaptive Contrast";
        private static readonly int GlobalDebugModeId = Shader.PropertyToID("_GameplayVisualGlobalDebugMode");

        public static GameplayVisualDebugMode GlobalDebugMode { get; private set; }

        public static void SetGlobalDebugMode(GameplayVisualDebugMode mode)
        {
            GlobalDebugMode = mode;
            Shader.SetGlobalFloat(GlobalDebugModeId, (float)mode);
        }

        public static Shader FindAdaptiveShader()
        {
            return Shader.Find(ShaderName);
        }
    }
}
