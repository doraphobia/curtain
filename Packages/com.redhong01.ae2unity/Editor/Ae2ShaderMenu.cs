using AE2Unity;
using UnityEditor;

namespace AE2Unity.Editor
{
    internal static class Ae2ShaderMenu
    {
        [MenuItem(Ae2ShaderConstants.GeneratedShaderMenu, true)]
        private static bool ValidateGenerateShader()
        {
            return TryGetSelectedClip(out _, out _);
        }

        [MenuItem(Ae2ShaderConstants.GeneratedShaderMenu)]
        private static void GenerateShader()
        {
            if (!TryGetSelectedClip(out var clip, out var sourcePath))
            {
                EditorUtility.DisplayDialog("AE2Unity", "Select an .ae2shader asset first.", "OK");
                return;
            }

            GenerateShaderAssets(clip, sourcePath);
        }

        [MenuItem("Tools/AE2Unity/Process AEBridge Inbox Now")]
        private static void ProcessBridgeInboxNow()
        {
            AeBridgeReceiver.ProcessInbox();
        }

        [MenuItem("Tools/AE2Unity/Open AEBridge Folder")]
        private static void OpenBridgeFolder()
        {
            AeBridgePaths.EnsureFolders();
            EditorUtility.RevealInFinder(AeBridgePaths.BridgeRoot);
        }

        public static void GenerateShaderAssets(Ae2ShaderClip clip, string sourcePath)
        {
            var result = Ae2ShaderAssetGenerator.Generate(clip, sourcePath, Ae2ShaderEditorSettings.instance.OverwriteGeneratedAssets);
            if (!result.Success)
            {
                EditorUtility.DisplayDialog("AE2Unity", result.Message, "OK");
                return;
            }

            var selectionPath = string.IsNullOrEmpty(result.PrefabAssetPath) ? result.MaterialAssetPath : result.PrefabAssetPath;
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(selectionPath);
            var generatedText = string.IsNullOrEmpty(result.PrefabAssetPath)
                ? $"Generated:\n{result.ShaderAssetPath}\n{result.MaterialAssetPath}"
                : $"Generated:\n{result.PrefabAssetPath}\n{result.ShaderAssetPath}\n{result.MaterialAssetPath}";
            EditorUtility.DisplayDialog("AE2Unity", generatedText, "OK");
        }

        private static bool TryGetSelectedClip(out Ae2ShaderClip clip, out string assetPath)
        {
            clip = null;
            assetPath = string.Empty;

            var activeObject = Selection.activeObject;
            if (activeObject == null)
            {
                return false;
            }

            assetPath = AssetDatabase.GetAssetPath(activeObject);
            clip = activeObject as Ae2ShaderClip;
            if (clip == null && !string.IsNullOrEmpty(assetPath))
            {
                clip = AssetDatabase.LoadAssetAtPath<Ae2ShaderClip>(assetPath);
            }

            return clip != null && !string.IsNullOrEmpty(assetPath);
        }
    }
}
