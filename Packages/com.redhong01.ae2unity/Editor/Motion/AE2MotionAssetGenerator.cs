using System;
using System.IO;
using AE2Unity;
using UnityEditor;
using UnityEngine;

namespace AE2Unity.Editor
{
    internal readonly struct AE2MotionGenerationResult
    {
        public readonly bool Success;
        public readonly string ShaderAssetPath;
        public readonly string MaterialAssetPath;
        public readonly string PrefabAssetPath;
        public readonly string Message;

        public AE2MotionGenerationResult(bool success, string shaderAssetPath, string materialAssetPath, string prefabAssetPath, string message)
        {
            Success = success;
            ShaderAssetPath = shaderAssetPath;
            MaterialAssetPath = materialAssetPath;
            PrefabAssetPath = prefabAssetPath;
            Message = message;
        }
    }

    internal static class AE2MotionAssetGenerator
    {
        public const string ProceduralCircleShaderName = "AE2Unity/Procedural/Circle Unlit";
        public const string ProceduralRectShaderName = "AE2Unity/Procedural/Rect Unlit";
        public const string ProceduralStrokeShaderName = "AE2Unity/Procedural/Stroke Unlit";
        public const string ProceduralCircleShaderAssetPath = "Packages/com.redhong01.ae2unity/Runtime/Shaders/Procedural/AE2UnityProceduralCircle.shader";
        public const string ProceduralRectShaderAssetPath = "Packages/com.redhong01.ae2unity/Runtime/Shaders/Procedural/AE2UnityProceduralRect.shader";
        public const string ProceduralStrokeShaderAssetPath = "Packages/com.redhong01.ae2unity/Runtime/Shaders/Procedural/AE2UnityProceduralStroke.shader";

        public static AE2MotionGenerationResult Generate(AE2MotionData motionData, string sourcePath, bool overwriteExisting, bool generatePrefab = true)
        {
            if (motionData == null)
            {
                return new AE2MotionGenerationResult(false, string.Empty, string.Empty, string.Empty, "MotionData is null.");
            }

            var shaderName = GetShaderNameForMotionData(motionData);
            var shaderPath = GetShaderAssetPathForMotionData(motionData);
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                return new AE2MotionGenerationResult(false, shaderPath, string.Empty, string.Empty, $"Shader '{shaderName}' was not found.");
            }

            var directory = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrEmpty(directory))
            {
                directory = "Assets";
            }

            var baseName = Path.GetFileNameWithoutExtension(sourcePath);
            var materialAssetPath = NormalizeAssetPath(Path.Combine(directory, $"{baseName}.motion.mat"));
            if (!overwriteExisting)
            {
                materialAssetPath = AssetDatabase.GenerateUniqueAssetPath(materialAssetPath);
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(materialAssetPath);
            if (material == null)
            {
                material = new Material(shader)
                {
                    name = $"{baseName}.motion"
                };
                AssetDatabase.CreateAsset(material, materialAssetPath);
            }
            else
            {
                material.shader = shader;
            }

            ApplyMaterialDefaults(material, motionData);
            EditorUtility.SetDirty(material);

            var prefabAssetPath = generatePrefab
                ? CreateOrUpdatePrefab(motionData, material, directory, baseName, overwriteExisting)
                : string.Empty;

            AssetDatabase.SaveAssets();
            var message = string.IsNullOrEmpty(prefabAssetPath)
                ? $"Generated motion material: {materialAssetPath}"
                : $"Generated motion material and prefab: {materialAssetPath}, {prefabAssetPath}";

            return new AE2MotionGenerationResult(true, shaderPath, materialAssetPath, prefabAssetPath, message);
        }

        public static GameObject CreatePreviewGameObject(AE2MotionData motionData, Material material = null)
        {
            if (motionData == null)
            {
                return null;
            }

            material ??= CreatePreviewMaterial(motionData);
            if (material == null)
            {
                return null;
            }

            var root = CreateQuadRoot(motionData, motionData.CompName);
            var renderer = root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            var player = root.AddComponent<AE2MotionPlayer>();
            player.TargetRenderer = renderer;
            player.TargetMaterial = material;
            player.MotionData = motionData;
            player.Apply();

            Undo.RegisterCreatedObjectUndo(root, "Create AE2Motion Preview");
            Selection.activeGameObject = root;
            return root;
        }

        public static int CountSupportedLayers(AE2MotionData motionData)
        {
            var layers = motionData?.Document?.motion?.layers ?? Array.Empty<AE2MotionLayer>();
            var count = 0;
            for (var i = 0; i < layers.Length; i++)
            {
                if (layers[i] != null && AE2MotionEvaluator.IsRuntimeRenderable(layers[i].RendererHint))
                {
                    count++;
                }
            }

            return count;
        }

        public static string GetShaderNameForMotionData(AE2MotionData motionData)
        {
            return GetShaderNameForHint(GetFirstRenderableHint(motionData));
        }

        public static string GetShaderAssetPathForMotionData(AE2MotionData motionData)
        {
            return GetShaderAssetPathForHint(GetFirstRenderableHint(motionData));
        }

        private static AE2RendererHint GetFirstRenderableHint(AE2MotionData motionData)
        {
            var layers = motionData?.Document?.motion?.layers ?? Array.Empty<AE2MotionLayer>();
            for (var i = 0; i < layers.Length; i++)
            {
                if (layers[i] != null && AE2MotionEvaluator.IsRuntimeRenderable(layers[i].RendererHint))
                {
                    return layers[i].RendererHint;
                }
            }

            return AE2RendererHint.ProceduralCircle;
        }

        private static string GetShaderNameForHint(AE2RendererHint rendererHint)
        {
            return rendererHint switch
            {
                AE2RendererHint.ProceduralRect => ProceduralRectShaderName,
                AE2RendererHint.ProceduralStroke => ProceduralStrokeShaderName,
                _ => ProceduralCircleShaderName
            };
        }

        private static string GetShaderAssetPathForHint(AE2RendererHint rendererHint)
        {
            return rendererHint switch
            {
                AE2RendererHint.ProceduralRect => ProceduralRectShaderAssetPath,
                AE2RendererHint.ProceduralStroke => ProceduralStrokeShaderAssetPath,
                _ => ProceduralCircleShaderAssetPath
            };
        }

        private static void ApplyMaterialDefaults(Material material, AE2MotionData motionData)
        {
            material.SetFloat(Shader.PropertyToID("_AE_Feather"), 1.5f);
            if (AE2MotionEvaluator.TryEvaluateFirstRenderable(motionData, 0f, out var evaluated))
            {
                AE2ShaderPropertyBinder.Bind(material, motionData, evaluated, 0f);
            }
        }

        private static Material CreatePreviewMaterial(AE2MotionData motionData)
        {
            var shader = Shader.Find(GetShaderNameForMotionData(motionData));
            if (shader == null)
            {
                return null;
            }

            var material = new Material(shader)
            {
                name = $"{motionData.CompName} Motion Preview"
            };
            ApplyMaterialDefaults(material, motionData);
            return material;
        }

        private static string CreateOrUpdatePrefab(AE2MotionData motionData, Material material, string directory, string baseName, bool overwriteExisting)
        {
            var prefabPath = NormalizeAssetPath(Path.Combine(directory, $"{baseName}.motion.prefab"));
            if (!overwriteExisting)
            {
                prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);
            }

            var root = CreateQuadRoot(motionData, baseName);
            var renderer = root.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;

            var player = root.AddComponent<AE2MotionPlayer>();
            player.TargetRenderer = renderer;
            player.TargetMaterial = material;
            player.MotionData = motionData;
            player.Apply();

            var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return saved == null ? string.Empty : prefabPath;
        }

        private static GameObject CreateQuadRoot(AE2MotionData motionData, string baseName)
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Quad);
            root.name = string.IsNullOrWhiteSpace(baseName) ? "AE2Motion Preview" : baseName;

            var collider = root.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            var width = Mathf.Max(1, motionData.Width);
            var height = Mathf.Max(1, motionData.Height);
            root.transform.localScale = new Vector3(width / 100f, height / 100f, 1f);
            return root;
        }

        private static string NormalizeAssetPath(string path)
        {
            return path.Replace('\\', '/');
        }
    }
}
