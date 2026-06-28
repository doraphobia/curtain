using AE2Unity;
using UnityEditor;

namespace AE2Unity.Editor
{
    internal static class AE2MotionMenu
    {
        private const string GenerateMotionMenu = "Assets/AE2Unity/Generate Motion Runtime Assets";
        private const string CreatePreviewMenu = "Assets/AE2Unity/Create Motion Preview GameObject";

        [MenuItem(GenerateMotionMenu, true)]
        private static bool ValidateGenerateMotion()
        {
            return TryGetSelectedMotionData(out _, out _);
        }

        [MenuItem(GenerateMotionMenu)]
        private static void GenerateMotion()
        {
            if (!TryGetSelectedMotionData(out var motionData, out var sourcePath))
            {
                EditorUtility.DisplayDialog("AE2Unity", "Select an .ae2motion asset first.", "OK");
                return;
            }

            GenerateMotionAssets(motionData, sourcePath);
        }

        [MenuItem(CreatePreviewMenu, true)]
        private static bool ValidateCreatePreview()
        {
            return TryGetSelectedMotionData(out _, out _);
        }

        [MenuItem(CreatePreviewMenu)]
        private static void CreatePreview()
        {
            if (!TryGetSelectedMotionData(out var motionData, out _))
            {
                EditorUtility.DisplayDialog("AE2Unity", "Select an .ae2motion asset first.", "OK");
                return;
            }

            AE2MotionAssetGenerator.CreatePreviewGameObject(motionData);
        }

        public static void GenerateMotionAssets(AE2MotionData motionData, string sourcePath)
        {
            var result = AE2MotionAssetGenerator.Generate(
                motionData,
                sourcePath,
                Ae2ShaderEditorSettings.instance.OverwriteGeneratedAssets);

            if (!result.Success)
            {
                EditorUtility.DisplayDialog("AE2Unity", result.Message, "OK");
                return;
            }

            var selectionPath = string.IsNullOrEmpty(result.PrefabAssetPath) ? result.MaterialAssetPath : result.PrefabAssetPath;
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(selectionPath);
            var generatedText = string.IsNullOrEmpty(result.PrefabAssetPath)
                ? $"Generated:\n{result.MaterialAssetPath}"
                : $"Generated:\n{result.PrefabAssetPath}\n{result.MaterialAssetPath}";
            EditorUtility.DisplayDialog("AE2Unity", generatedText, "OK");
        }

        private static bool TryGetSelectedMotionData(out AE2MotionData motionData, out string assetPath)
        {
            motionData = null;
            assetPath = string.Empty;

            var activeObject = Selection.activeObject;
            if (activeObject == null)
            {
                return false;
            }

            assetPath = AssetDatabase.GetAssetPath(activeObject);
            motionData = activeObject as AE2MotionData;
            if (motionData == null && !string.IsNullOrEmpty(assetPath))
            {
                motionData = AssetDatabase.LoadAssetAtPath<AE2MotionData>(assetPath);
            }

            return motionData != null && !string.IsNullOrEmpty(assetPath);
        }
    }
}
