#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace DuoCurtain.Editor
{
    [InitializeOnLoad]
    internal static class DOTweenStartupPopupSuppressor
    {
        private const string EditorToolsEnabledKey = "DuoCurtain.DOTween.EditorToolsEnabled";
        private const string OpenPanelRequestedKey = "DuoCurtain.DOTween.OpenPanelRequested";
        private const string DOTweenMenuPath = "Tools/Demigiant/DOTween Utility Panel";
        private const string EnableMenuPath = "Tools/Duo Curtain/DOTween/Enable Editor Tools And Open Panel";
        private const string SilenceMenuPath = "Tools/Duo Curtain/DOTween/Silence Editor Tools";
        private const string DOTweenEditorDllPath = "Assets/Plugins/Demigiant/DOTween/Editor/DOTweenEditor.dll";
        private const string DOTweenUpgradeManagerDllPath = "Assets/Plugins/Demigiant/DOTween/Editor/DOTweenUpgradeManager.dll";
        private const double SuppressionWindowSeconds = 30.0d;

        private static double suppressUntilTime;
        private static bool manualOpenRequested;
        private static int openPanelAttempts;

        static DOTweenStartupPopupSuppressor()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            suppressUntilTime = EditorApplication.timeSinceStartup + SuppressionWindowSeconds;
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
            EditorApplication.delayCall += EnforceSilentMode;
            EditorApplication.update += SuppressDOTweenStartupWindows;

            if (EditorPrefs.GetBool(EditorToolsEnabledKey, false) &&
                SessionState.GetBool(OpenPanelRequestedKey, false))
            {
                manualOpenRequested = true;
                EditorApplication.delayCall += TryOpenDOTweenUtilityPanel;
            }
        }

        [MenuItem(EnableMenuPath)]
        private static void EnableEditorToolsAndOpenPanel()
        {
            manualOpenRequested = true;
            openPanelAttempts = 0;
            SessionState.SetBool(OpenPanelRequestedKey, true);
            EditorPrefs.SetBool(EditorToolsEnabledKey, true);
            SetDOTweenEditorToolsEnabled(true);
            EditorApplication.delayCall += TryOpenDOTweenUtilityPanel;
        }

        [MenuItem(EnableMenuPath, true)]
        private static bool ValidateEnableEditorToolsAndOpenPanel()
        {
            Menu.SetChecked(EnableMenuPath, EditorPrefs.GetBool(EditorToolsEnabledKey, false));
            return true;
        }

        [MenuItem(SilenceMenuPath)]
        private static void SilenceEditorTools()
        {
            manualOpenRequested = false;
            openPanelAttempts = 0;
            SessionState.SetBool(OpenPanelRequestedKey, false);
            suppressUntilTime = EditorApplication.timeSinceStartup + SuppressionWindowSeconds;
            EditorPrefs.SetBool(EditorToolsEnabledKey, false);
            SetDOTweenEditorToolsEnabled(false);
            MarkDOTweenSetupDialogAsHandled();
            CloseDOTweenEditorWindows();
        }

        [MenuItem(SilenceMenuPath, true)]
        private static bool ValidateSilenceEditorTools()
        {
            Menu.SetChecked(SilenceMenuPath, !EditorPrefs.GetBool(EditorToolsEnabledKey, false));
            return true;
        }

        private static void EnforceSilentMode()
        {
            if (!EditorPrefs.GetBool(EditorToolsEnabledKey, false))
            {
                SetDOTweenEditorToolsEnabled(false);
            }

            MarkDOTweenSetupDialogAsHandled();
            CloseDOTweenEditorWindows();
        }

        private static void SuppressDOTweenStartupWindows()
        {
            if (EditorApplication.timeSinceStartup > suppressUntilTime)
            {
                EditorApplication.update -= SuppressDOTweenStartupWindows;
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoaded;
                return;
            }

            if (manualOpenRequested)
            {
                return;
            }

            MarkDOTweenSetupDialogAsHandled();
            CloseDOTweenEditorWindows();
        }

        private static void OnAssemblyLoaded(object sender, AssemblyLoadEventArgs args)
        {
            var assemblyName = args.LoadedAssembly.GetName().Name;
            if (assemblyName != "DOTweenEditor")
            {
                return;
            }

            MarkDOTweenSetupDialogAsHandled();
        }

        private static void SetDOTweenEditorToolsEnabled(bool enabled)
        {
            var changed = false;
            changed |= SetPluginEditorCompatibility(DOTweenEditorDllPath, enabled);
            changed |= SetPluginEditorCompatibility(DOTweenUpgradeManagerDllPath, enabled);

            if (changed)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static bool SetPluginEditorCompatibility(string assetPath, bool enabled)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as PluginImporter;
            if (importer == null)
            {
                return false;
            }

            if (importer.GetCompatibleWithEditor() == enabled)
            {
                return false;
            }

            importer.SetCompatibleWithEditor(enabled);
            importer.SaveAndReimport();
            return true;
        }

        private static void TryOpenDOTweenUtilityPanel()
        {
            openPanelAttempts++;
            MarkDOTweenSetupDialogAsHandled();

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                QueueOpenPanelRetry();
                return;
            }

            if (EditorApplication.ExecuteMenuItem(DOTweenMenuPath))
            {
                manualOpenRequested = false;
                SessionState.SetBool(OpenPanelRequestedKey, false);
                StopStartupSuppression();
                return;
            }

            QueueOpenPanelRetry();
        }

        private static void QueueOpenPanelRetry()
        {
            if (openPanelAttempts >= 20)
            {
                manualOpenRequested = false;
                SessionState.SetBool(OpenPanelRequestedKey, false);
                Debug.LogWarning("[DuoCurtain] DOTween editor tools were enabled, but the DOTween Utility Panel menu was not available yet.");
                return;
            }

            EditorApplication.delayCall += TryOpenDOTweenUtilityPanel;
        }

        private static void StopStartupSuppression()
        {
            suppressUntilTime = 0.0d;
            EditorApplication.update -= SuppressDOTweenStartupWindows;
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoaded;
        }

        private static void MarkDOTweenSetupDialogAsHandled()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types;
                }
                catch
                {
                    continue;
                }

                if (types == null)
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (type == null || !IsDOTweenEditorType(type))
                    {
                        continue;
                    }

                    SetBoolField(type, "_setupDialogRequested", true);
                    SetBoolField(type, "_setupRequired", false);
                }
            }
        }

        private static void SetBoolField(Type type, string fieldName, bool value)
        {
            var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null || field.FieldType != typeof(bool))
            {
                return;
            }

            try
            {
                if (field.IsStatic)
                {
                    field.SetValue(null, value);
                }
                else
                {
                    foreach (var instance in Resources.FindObjectsOfTypeAll(type))
                    {
                        field.SetValue(instance, value);
                    }
                }
            }
            catch
            {
                // DOTween editor code is shipped as DLLs; keep suppression best-effort.
            }
        }

        private static void CloseDOTweenEditorWindows()
        {
            var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            foreach (var window in windows)
            {
                if (window == null || !IsDOTweenWindow(window))
                {
                    continue;
                }

                window.Close();
            }
        }

        private static bool IsDOTweenWindow(EditorWindow window)
        {
            var type = window.GetType();
            var typeName = type.FullName ?? type.Name;
            var title = window.titleContent != null ? window.titleContent.text : string.Empty;

            return typeName.IndexOf("DOTween", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   title.IndexOf("DOTween", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsDOTweenEditorType(Type type)
        {
            var fullName = type.FullName ?? type.Name;
            return fullName.IndexOf("DOTween", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   fullName.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
#endif
