#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace DuoCurtain.Editor
{
    [InitializeOnLoad]
    internal static class CrossPlatformEditorGuard
    {
        private const string GitWarningPreferenceKey = "DuoCurtain.CrossPlatform.GitWarningShown";

        static CrossPlatformEditorGuard()
        {
            EditorApplication.delayCall += ValidateEnvironmentOnce;
        }

        private static void ValidateEnvironmentOnce()
        {
            if (EditorPrefs.GetBool(GitWarningPreferenceKey, false))
                return;

            if (!string.IsNullOrEmpty(CrossPlatformEditorUtility.ResolveGitExecutable()))
                return;

            EditorPrefs.SetBool(GitWarningPreferenceKey, true);
            Debug.LogWarning(
                "[CrossPlatformEditorGuard] git was not found on this machine. " +
                "Build archive manifests may miss commit metadata until git is installed and available on PATH.");
        }
    }
}
#endif
