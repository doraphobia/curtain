#if UNITY_EDITOR
using UnityEngine;

namespace Curtain.Settings
{
    public static class CurtainSettingsLocator
    {
        private const string BundleAssetPath = "Assets/Curtain/Settings/CurtainSettingsBundle.asset";

        private static CurtainSettingsBundle bundle;

        public static CurtainSettingsBundle Bundle => bundle != null ? bundle : (bundle = LoadBundle());

        public static EnemySettings Enemy => Bundle != null ? Bundle.enemy : null;
        public static VisionSettings Vision => Bundle != null ? Bundle.vision : null;
        public static DoorSettings Door => Bundle != null ? Bundle.door : null;
        public static CameraSettings Camera => Bundle != null ? Bundle.camera : null;
        public static SanitySettings Sanity => Bundle != null ? Bundle.sanity : null;
        public static EconomySettings Economy => Bundle != null ? Bundle.economy : null;
        public static FootprintSettings Footprint => Bundle != null ? Bundle.footprint : null;
        public static AccessibilitySettings Accessibility => Bundle != null ? Bundle.accessibility : null;
        public static LocalizationSettings Localization => Bundle != null ? Bundle.localization : null;
        public static DebugSettings Debug => Bundle != null ? Bundle.debug : null;

        public static EnemySettings Resolve(EnemySettings assigned) => assigned != null ? assigned : Enemy;
        public static VisionSettings Resolve(VisionSettings assigned) => assigned != null ? assigned : Vision;
        public static DoorSettings Resolve(DoorSettings assigned) => assigned != null ? assigned : Door;
        public static CameraSettings Resolve(CameraSettings assigned) => assigned != null ? assigned : Camera;
        public static SanitySettings Resolve(SanitySettings assigned) => assigned != null ? assigned : Sanity;
        public static DebugSettings Resolve(DebugSettings assigned) => assigned != null ? assigned : Debug;

        public static void RegisterBundle(CurtainSettingsBundle registered)
        {
            if (registered == null)
                return;

            bundle = registered;
        }

        public static void UnregisterBundle(CurtainSettingsBundle registered)
        {
            if (bundle == registered)
                bundle = null;
        }

        public static void InvalidateCache()
        {
            bundle = null;
        }

        private static CurtainSettingsBundle LoadBundle()
        {
#if UNITY_EDITOR
            CurtainSettingsRuntimeProvider provider = Object.FindFirstObjectByType<CurtainSettingsRuntimeProvider>();
            if (provider != null && provider.Bundle != null)
                return provider.Bundle;

            return UnityEditor.AssetDatabase.LoadAssetAtPath<CurtainSettingsBundle>(BundleAssetPath);
#else
            return null;
#endif
        }
    }
}

#endif
