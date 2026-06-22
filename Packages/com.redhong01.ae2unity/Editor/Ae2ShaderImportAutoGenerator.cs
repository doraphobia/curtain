using System.Collections.Generic;
using AE2Unity;
using UnityEditor;
using UnityEngine;

namespace AE2Unity.Editor
{
    internal sealed class Ae2ShaderImportAutoGenerator : AssetPostprocessor
    {
        private static readonly HashSet<string> PendingPaths = new HashSet<string>();
        private static bool scheduled;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (AeBridgeReceiver.IsProcessingBridgeJob)
            {
                return;
            }

            if (!Ae2ShaderEditorSettings.instance.AutoGenerateOnImport)
            {
                return;
            }

            for (var i = 0; i < importedAssets.Length; i++)
            {
                var path = importedAssets[i];
                if (path.EndsWith(".ae2shader", System.StringComparison.OrdinalIgnoreCase))
                {
                    PendingPaths.Add(path);
                }
            }

            if (PendingPaths.Count == 0 || scheduled)
            {
                return;
            }

            scheduled = true;
            EditorApplication.delayCall += ProcessPending;
        }

        private static void ProcessPending()
        {
            scheduled = false;
            if (PendingPaths.Count == 0)
            {
                return;
            }

            var paths = new List<string>(PendingPaths);
            PendingPaths.Clear();

            for (var i = 0; i < paths.Count; i++)
            {
                GenerateForPath(paths[i]);
            }
        }

        private static void GenerateForPath(string path)
        {
            var clip = AssetDatabase.LoadAssetAtPath<Ae2ShaderClip>(path);
            if (clip == null)
            {
                Debug.LogWarning($"AE2Unity auto-generate skipped because no clip was loaded at {path}.");
                return;
            }

            var result = Ae2ShaderAssetGenerator.Generate(
                clip,
                path,
                Ae2ShaderEditorSettings.instance.OverwriteGeneratedAssets);

            if (!result.Success)
            {
                Debug.LogWarning($"AE2Unity auto-generate failed for {path}: {result.Message}");
                return;
            }

            if (string.IsNullOrEmpty(result.PrefabAssetPath))
            {
                Debug.Log($"AE2Unity auto-generated {result.ShaderAssetPath} and {result.MaterialAssetPath}.");
            }
            else
            {
                Debug.Log($"AE2Unity auto-generated {result.ShaderAssetPath}, {result.MaterialAssetPath}, and {result.PrefabAssetPath}.");
            }
        }
    }
}
