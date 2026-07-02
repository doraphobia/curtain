#if UNITY_EDITOR
using System.IO;
using Curtain.Settings;
using DuoCurtain.RuntimeTileMesh;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Curtain.Editor
{
    [InitializeOnLoad]
    public static class CurtainSettingsBundleInstaller
    {
        private const string LegacyBundleAssetPath = "Assets/Resources/CurtainSettingsBundle.asset";
        private const string BundleAssetPath = "Assets/Curtain/Settings/CurtainSettingsBundle.asset";
        private const string SettingsFolder = "Assets/Curtain/Settings";
        private const string RedScenePath = "Assets/Scenes/RedScene.unity";

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
            MigrateLegacyBundleAsset();

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

        private static void MigrateLegacyBundleAsset()
        {
            if (!AssetDatabase.LoadAssetAtPath<CurtainSettingsBundle>(LegacyBundleAssetPath))
                return;

            if (AssetDatabase.LoadAssetAtPath<CurtainSettingsBundle>(BundleAssetPath))
            {
                AssetDatabase.DeleteAsset(LegacyBundleAssetPath);
                return;
            }

            string error = AssetDatabase.MoveAsset(LegacyBundleAssetPath, BundleAssetPath);
            if (!string.IsNullOrEmpty(error))
                Debug.LogWarning("[CurtainSettingsBundleInstaller] Failed to migrate legacy bundle asset: " + error);
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
