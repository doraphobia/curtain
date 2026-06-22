using AE2Unity;
using UnityEditor;
using UnityEngine;

namespace AE2Unity.Editor
{
    [CustomEditor(typeof(Ae2ShaderClip))]
    public sealed class Ae2ShaderClipEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var clip = (Ae2ShaderClip)target;
            var document = clip.Document;
            var comp = document?.comp;

            EditorGUILayout.LabelField("AE2Unity Clip", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Schema", document?.schemaVersion ?? "unknown");
                EditorGUILayout.TextField("Comp", comp?.name ?? "unknown");
                EditorGUILayout.IntField("Width", comp?.width ?? 0);
                EditorGUILayout.IntField("Height", comp?.height ?? 0);
                EditorGUILayout.FloatField("Frame Rate", comp?.frameRate ?? 0f);
                EditorGUILayout.FloatField("Duration", comp?.duration ?? 0f);
                EditorGUILayout.IntField("Layers", document?.layers?.Length ?? 0);
                EditorGUILayout.IntField("Assets", document?.assets?.Length ?? 0);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Generate Shader And Material"))
            {
                var path = AssetDatabase.GetAssetPath(clip);
                Ae2ShaderMenu.GenerateShaderAssets(clip, path);
            }

            var warnings = clip.ImportWarnings;
            if (warnings == null || warnings.Length == 0)
            {
                EditorGUILayout.HelpBox("No import warnings. This does not guarantee pixel-perfect output; validate against AE reference frames.", MessageType.Info);
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
