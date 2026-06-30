#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace DuoCurtain.Editor
{
    [InitializeOnLoad]
    public static class DuoCurtainAutoCompile
    {
        private const string EnabledPreferenceKey = "DuoCurtain.AutoCompile.Enabled";
        private const string MenuPath = "Tools/Duo Curtain/Compile/Auto Compile Enabled";
        private const string CompileNowMenuPath = "Tools/Duo Curtain/Compile/Compile Now";
        private const double CompileDelaySeconds = 0.35d;

        private static bool pendingCompile;
        private static double compileAtTime;
        private static string pendingReason;

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledPreferenceKey, true);
            set
            {
                EditorPrefs.SetBool(EnabledPreferenceKey, value);
                Menu.SetChecked(MenuPath, value);
            }
        }

        static DuoCurtainAutoCompile()
        {
            Menu.SetChecked(MenuPath, Enabled);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        [MenuItem(MenuPath)]
        private static void ToggleAutoCompile()
        {
            Enabled = !Enabled;
            Debug.Log("[DuoCurtain Auto Compile] " + (Enabled ? "Enabled." : "Disabled."));
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateToggleAutoCompile()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        [MenuItem(CompileNowMenuPath)]
        private static void CompileNow()
        {
            QueueCompile("manual menu request", 0d);
        }

        public static void NotifyAssetsChanged(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (!Enabled)
                return;

            if (!ContainsScriptPath(importedAssets) &&
                !ContainsScriptPath(deletedAssets) &&
                !ContainsScriptPath(movedAssets) &&
                !ContainsScriptPath(movedFromAssetPaths))
            {
                return;
            }

            QueueCompile("script asset change", CompileDelaySeconds);
        }

        public static void QueueCompile(string reason, double delaySeconds)
        {
            pendingCompile = true;
            pendingReason = string.IsNullOrWhiteSpace(reason) ? "script change" : reason;
            compileAtTime = EditorApplication.timeSinceStartup + Math.Max(0d, delaySeconds);
        }

        private static void Tick()
        {
            if (!pendingCompile)
                return;

            if (EditorApplication.timeSinceStartup < compileAtTime)
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            pendingCompile = false;
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            CompilationPipeline.RequestScriptCompilation();
            Debug.Log("[DuoCurtain Auto Compile] Requested script compilation after " + pendingReason + ".");
        }

        private static bool ContainsScriptPath(string[] paths)
        {
            if (paths == null || paths.Length == 0)
                return false;

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (!string.IsNullOrEmpty(path) &&
                    path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public sealed class DuoCurtainAutoCompilePostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            DuoCurtainAutoCompile.NotifyAssetsChanged(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
        }
    }
}
#endif
