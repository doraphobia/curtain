using AE2Unity;
using UnityEditor;
using UnityEngine;

namespace AE2Unity.Editor
{
    [CustomEditor(typeof(AE2MotionData))]
    public sealed class AE2MotionDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var motionData = (AE2MotionData)target;
            var document = motionData.Document;
            var comp = document?.comp;

            EditorGUILayout.LabelField("AE2Unity Motion Data", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Schema", document?.schemaVersion ?? "unknown");
                EditorGUILayout.TextField("Comp", comp?.name ?? "unknown");
                EditorGUILayout.IntField("Width", comp?.width ?? 0);
                EditorGUILayout.IntField("Height", comp?.height ?? 0);
                EditorGUILayout.FloatField("Frame Rate", comp?.frameRate ?? 0f);
                EditorGUILayout.FloatField("Duration", comp?.duration ?? 0f);
                EditorGUILayout.IntField("Layers", motionData.LayerCount);
                EditorGUILayout.IntField("Supported Layers", AE2MotionAssetGenerator.CountSupportedLayers(motionData));
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Generate Runtime Assets"))
            {
                var path = AssetDatabase.GetAssetPath(motionData);
                AE2MotionMenu.GenerateMotionAssets(motionData, path);
            }

            if (GUILayout.Button("Create Preview GameObject"))
            {
                AE2MotionAssetGenerator.CreatePreviewGameObject(motionData);
            }

            var warnings = motionData.ImportWarnings;
            if (warnings == null || warnings.Length == 0)
            {
                EditorGUILayout.HelpBox("No import warnings. Validate against AE reference frames before shipping.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Import Warnings", EditorStyles.boldLabel);
            for (var i = 0; i < warnings.Length; i++)
            {
                EditorGUILayout.HelpBox(warnings[i], MessageType.Warning);
            }
        }
    }
}
