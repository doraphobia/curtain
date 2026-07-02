#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace DuoCurtain.Editor
{
    [InitializeOnLoad]
    public static class DuoCurtainAppIconSync
    {
        private const string BrandingFolder = "Assets/Branding/AppIcons";
        private const string IconSetAssetPath = BrandingFolder + "/DuoCurtainAppIconSet.asset";

        static DuoCurtainAppIconSync()
        {
            EditorApplication.delayCall += EnsureBrandingAssetExists;
        }

        [MenuItem("Tools/Duo Curtain/Branding/Select App Icon Set")]
        private static void SelectIconSet()
        {
            EnsureBrandingAssetExists();
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(IconSetAssetPath);
            if (asset != null)
                Selection.activeObject = asset;
        }

        [MenuItem("Tools/Duo Curtain/Branding/Apply App Icons (Mac+Windows+WebGL)")]
        private static void ApplyIconsMenu()
        {
            EnsureBrandingAssetExists();
            DuoCurtainAppIconSet set = AssetDatabase.LoadAssetAtPath<DuoCurtainAppIconSet>(IconSetAssetPath);
            if (set == null)
            {
                Debug.LogWarning("[DuoCurtainAppIconSync] Icon set asset missing.");
                return;
            }

            ApplyIcons(set);
        }

        private static void EnsureBrandingAssetExists()
        {
            EnsureFolder("Assets/Branding");
            EnsureFolder(BrandingFolder);

            DuoCurtainAppIconSet set = AssetDatabase.LoadAssetAtPath<DuoCurtainAppIconSet>(IconSetAssetPath);
            if (set != null)
                return;

            set = ScriptableObject.CreateInstance<DuoCurtainAppIconSet>();
            AssetDatabase.CreateAsset(set, IconSetAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(IconSetAssetPath, ImportAssetOptions.ForceUpdate);

            Debug.LogFormat(
                LogType.Log,
                LogOption.NoStacktrace,
                null,
                "[DuoCurtainAppIconSync] Created icon set at {0}. Assign icons there, then run Apply App Icons.",
                IconSetAssetPath);
        }

        private static void ApplyIcons(DuoCurtainAppIconSet set)
        {
            bool any = false;

            if (set.standaloneIcon != null)
            {
                any |= ApplyForTarget(NamedBuildTarget.Standalone, set.standaloneIcon);
            }
            else
            {
                Debug.LogWarning("[DuoCurtainAppIconSync] Standalone icon is not assigned (Mac+Windows).");
            }

            if (set.webglIcon != null)
            {
                any |= ApplyForTarget(NamedBuildTarget.WebGL, set.webglIcon);
            }
            else
            {
                Debug.LogWarning("[DuoCurtainAppIconSync] WebGL icon is not assigned.");
            }

            if (any)
            {
                AssetDatabase.SaveAssets();
                Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "[DuoCurtainAppIconSync] Applied app icons to PlayerSettings.");
            }
        }

        private static bool ApplyForTarget(NamedBuildTarget target, Texture2D icon)
        {
            try
            {
                Texture2D[] current = PlayerSettings.GetIcons(target, IconKind.Application);
                if (current == null || current.Length == 0)
                {
                    // Best-effort: if Unity returns no slots, we can't set icons for this group.
                    Debug.LogWarning($"[DuoCurtainAppIconSync] No icon slots for {target}. Unity version/settings may not support it.");
                    return false;
                }

                Texture2D[] filled = new Texture2D[current.Length];
                for (int i = 0; i < filled.Length; i++)
                    filled[i] = icon;

                PlayerSettings.SetIcons(target, filled, IconKind.Application);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DuoCurtainAppIconSync] Failed applying icons for {target}: {ex.Message}");
                return false;
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path)?.Replace("\\", "/");
            string name = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                return;

            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif

