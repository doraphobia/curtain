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
        private const string DOTweenMenuPath = "Tools/Demigiant/DOTween Utility Panel";
        private const string ManualMenuPath = "Tools/Duo Curtain/DOTween/Open Utility Panel";
        private const double SuppressionWindowSeconds = 8.0d;

        private static readonly double SuppressUntilTime;
        private static bool manualOpenRequested;

        static DOTweenStartupPopupSuppressor()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            SuppressUntilTime = EditorApplication.timeSinceStartup + SuppressionWindowSeconds;
            EditorApplication.update += SuppressDOTweenStartupWindows;
        }

        [MenuItem(ManualMenuPath)]
        private static void OpenDOTweenUtilityPanel()
        {
            manualOpenRequested = true;
            var opened = EditorApplication.ExecuteMenuItem(DOTweenMenuPath);
            EditorApplication.delayCall += () => manualOpenRequested = false;

            if (!opened)
            {
                Debug.LogWarning("[DuoCurtain] DOTween Utility Panel menu was not found.");
            }
        }

        private static void SuppressDOTweenStartupWindows()
        {
            if (EditorApplication.timeSinceStartup > SuppressUntilTime)
            {
                EditorApplication.update -= SuppressDOTweenStartupWindows;
                return;
            }

            if (manualOpenRequested)
            {
                return;
            }

            MarkDOTweenSetupDialogAsHandled();
            CloseDOTweenEditorWindows();
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

                    var field = type.GetField("_setupDialogRequested", BindingFlags.Static | BindingFlags.NonPublic);
                    if (field != null && field.FieldType == typeof(bool))
                    {
                        try
                        {
                            field.SetValue(null, true);
                        }
                        catch
                        {
                            // DOTween ships editor code as DLLs in this project; keep startup suppression best-effort.
                        }
                    }
                }
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
