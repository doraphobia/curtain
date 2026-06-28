using System;
using System.Collections.Generic;
using System.IO;
using AE2Unity;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace AE2Unity.Editor
{
    [ScriptedImporter(1, "ae2motion")]
    public sealed class AE2MotionImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var json = File.ReadAllText(ctx.assetPath);
            var warnings = new List<string>();
            var document = ParseDocument(json, warnings);
            AppendExportWarnings(document, warnings);
            AppendCapabilityWarnings(document, warnings);

            var motionData = ScriptableObject.CreateInstance<AE2MotionData>();
            motionData.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            motionData.Initialize(document, json, warnings.ToArray());
            ctx.AddObjectToAsset("AE2Motion Data", motionData);
            ctx.SetMainObject(motionData);

            var shaderName = AE2MotionAssetGenerator.GetShaderNameForMotionData(motionData);
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                warnings.Add($"Preview shader '{shaderName}' was not found.");
                motionData.Initialize(document, json, warnings.ToArray());
                return;
            }

            var material = new Material(shader)
            {
                name = $"{motionData.name} Preview"
            };

            if (AE2MotionEvaluator.TryEvaluateFirstRenderable(motionData, 0f, out var evaluated))
            {
                AE2ShaderPropertyBinder.Bind(material, motionData, evaluated, 0f);
            }

            ctx.AddObjectToAsset("Preview Material", material);
        }

        private static AE2MotionDocument ParseDocument(string json, List<string> warnings)
        {
            try
            {
                var document = JsonUtility.FromJson<AE2MotionDocument>(json);
                if (document == null)
                {
                    warnings.Add("JSON parse returned an empty AE2MotionDocument.");
                    return new AE2MotionDocument();
                }

                return document;
            }
            catch (Exception exception)
            {
                warnings.Add($"JSON parse failed: {exception.Message}");
                return new AE2MotionDocument();
            }
        }

        private static void AppendExportWarnings(AE2MotionDocument document, List<string> warnings)
        {
            var exportedWarnings = document?.warnings ?? Array.Empty<AE2MotionWarning>();
            for (var i = 0; i < exportedWarnings.Length; i++)
            {
                AppendWarning(exportedWarnings[i], warnings);
            }

            var layers = document?.motion?.layers ?? Array.Empty<AE2MotionLayer>();
            for (var i = 0; i < layers.Length; i++)
            {
                var layerWarnings = layers[i]?.warnings ?? Array.Empty<AE2MotionWarning>();
                for (var j = 0; j < layerWarnings.Length; j++)
                {
                    AppendWarning(layerWarnings[j], warnings);
                }
            }
        }

        private static void AppendCapabilityWarnings(AE2MotionDocument document, List<string> warnings)
        {
            var layers = document?.motion?.layers ?? Array.Empty<AE2MotionLayer>();
            for (var i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (layer == null)
                {
                    warnings.Add($"Layer {i} could not be parsed.");
                    continue;
                }

                layer.Normalize();
                if (layer.RendererHint == AE2RendererHint.UnsupportedShape)
                {
                    warnings.Add($"Layer '{layer.name}' is not supported by the current procedural motion runtime.");
                }
                else if (!AE2MotionEvaluator.IsRuntimeRenderable(layer.RendererHint))
                {
                    warnings.Add($"Layer '{layer.name}' exports as {layer.rendererHint}, but no runtime shader is currently mapped for that renderer hint.");
                }

                var expressions = layer.expressions ?? Array.Empty<string>();
                if (expressions.Length > 0)
                {
                    warnings.Add($"Layer '{layer.name}' contains AE expressions. They are preserved as metadata but are not evaluated at runtime.");
                }
            }
        }

        private static void AppendWarning(AE2MotionWarning warning, List<string> warnings)
        {
            if (warning == null || string.IsNullOrWhiteSpace(warning.message))
            {
                return;
            }

            var prefix = string.IsNullOrWhiteSpace(warning.code) ? string.Empty : $"{warning.code}: ";
            var layer = string.IsNullOrWhiteSpace(warning.layerId) ? string.Empty : $" [{warning.layerId}]";
            warnings.Add($"{prefix}{warning.message}{layer}");
        }
    }
}
