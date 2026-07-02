#if UNITY_EDITOR
using System.IO;
using Curtain.Settings;
using UnityEditor;
using UnityEngine;

namespace Curtain.Editor
{
    [InitializeOnLoad]
    public static class CurtainSettingsBundleInstaller
    {
        private const string LegacyBundleAssetPath = "Assets/Resources/CurtainSettingsBundle.asset";
        private const string BundleAssetPath = "Assets/Curtain/Settings/CurtainSettingsBundle.asset";
        private const string SettingsFolder = "Assets/Curtain/Settings";

        static CurtainSettingsBundleInstaller()
        {
            EditorApplication.delayCall += EnsureBundle;
        }

        [MenuItem("Tools/Curtain/Ensure Settings Bundle")]
        public static void EnsureBundleMenu()
        {
            EnsureBundle();
        }

        public static void EnsureBundle()
        {
            EnsureFolder(SettingsFolder);
            DeleteLegacyResourcesBundle();

            CurtainSettingsBundle bundle = AssetDatabase.LoadAssetAtPath<CurtainSettingsBundle>(BundleAssetPath);
            if (bundle == null)
            {
                bundle = ScriptableObject.CreateInstance<CurtainSettingsBundle>();
                AssetDatabase.CreateAsset(bundle, BundleAssetPath);
            }

            bool changed = false;
            changed |= Assign(ref bundle.enemy, SettingsFolder + "/EnemySettings.asset");
            changed |= Assign(ref bundle.vision, SettingsFolder + "/VisionSettings.asset");
            changed |= Assign(ref bundle.door, SettingsFolder + "/DoorSettings.asset");
            changed |= Assign(ref bundle.camera, SettingsFolder + "/CameraSettings.asset");
            changed |= Assign(ref bundle.sanity, SettingsFolder + "/SanitySettings.asset");
            changed |= Assign(ref bundle.economy, SettingsFolder + "/EconomySettings.asset");
            changed |= Assign(ref bundle.footprint, SettingsFolder + "/FootprintSettings.asset");
            changed |= Assign(ref bundle.accessibility, SettingsFolder + "/AccessibilitySettings.asset");
            changed |= Assign(ref bundle.localization, SettingsFolder + "/LocalizationSettings.asset");
            changed |= Assign(ref bundle.debug, SettingsFolder + "/DebugSettings.asset");

            if (changed)
            {
                EditorUtility.SetDirty(bundle);
                AssetDatabase.SaveAssets();
            }

            CurtainSettingsLocator.InvalidateCache();
        }

        private static void DeleteLegacyResourcesBundle()
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(LegacyBundleAssetPath) == null &&
                !File.Exists(LegacyBundleAssetPath))
                return;

            AssetDatabase.DeleteAsset(LegacyBundleAssetPath);
        }

        private static bool Assign<T>(ref T field, string assetPath) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null || field == asset)
                return false;

            field = asset;
            return true;
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
                return;

            string parent = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            string name = Path.GetFileName(assetPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                return;

            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
