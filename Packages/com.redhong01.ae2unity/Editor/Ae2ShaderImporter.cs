using System;
using System.Collections.Generic;
using System.IO;
using AE2Unity;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace AE2Unity.Editor
{
    [ScriptedImporter(2, "ae2shader")]
    public sealed class Ae2ShaderImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var json = File.ReadAllText(ctx.assetPath);
            var warnings = new List<string>();
            var document = ParseDocument(json, warnings);
            AppendExportWarnings(document, warnings);
            warnings.AddRange(Ae2ShaderCapabilityAnalyzer.Analyze(document));

            var clip = ScriptableObject.CreateInstance<Ae2ShaderClip>();
            clip.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            clip.Initialize(document, json, warnings.ToArray());
            ctx.AddObjectToAsset("AE2Shader Clip", clip);
            ctx.SetMainObject(clip);

            var shader = Shader.Find(Ae2ShaderConstants.PreviewShaderName);
            if (shader == null)
            {
                warnings.Add($"Preview shader '{Ae2ShaderConstants.PreviewShaderName}' was not found.");
                clip.Initialize(document, json, warnings.ToArray());
                return;
            }

            var material = new Material(shader)
            {
                name = $"{clip.name} Preview"
            };

            if (document?.comp != null)
            {
                material.SetVector("_CompSize", new Vector4(
                    document.comp.width,
                    document.comp.height,
                    1f / Mathf.Max(1, document.comp.width),
                    1f / Mathf.Max(1, document.comp.height)));
            }

            ctx.AddObjectToAsset("Preview Material", material);
        }

        private static Ae2ShaderDocument ParseDocument(string json, List<string> warnings)
        {
            try
            {
                var document = JsonUtility.FromJson<Ae2ShaderDocument>(json);
                Normalize(document);
                return document;
            }
            catch (Exception exception)
            {
                warnings.Add($"JSON parse failed: {exception.Message}");
                return new Ae2ShaderDocument();
            }
        }

        private static void AppendExportWarnings(Ae2ShaderDocument document, List<string> warnings)
        {
            var exportedWarnings = document?.warnings ?? Array.Empty<Ae2ShaderWarning>();
            for (var i = 0; i < exportedWarnings.Length; i++)
            {
                var warning = exportedWarnings[i];
                if (warning == null || string.IsNullOrWhiteSpace(warning.message))
                {
                    continue;
                }

                warnings.Add(string.IsNullOrWhiteSpace(warning.code)
                    ? warning.message
                    : $"{warning.code}: {warning.message}");
            }
        }

        private static void Normalize(Ae2ShaderDocument document)
        {
            if (document == null)
            {
                return;
            }

            document.comp ??= new Ae2ShaderComp();
            document.layers ??= Array.Empty<Ae2ShaderLayer>();
            document.assets ??= Array.Empty<Ae2ShaderAsset>();
            document.vectorAnimation ??= new Ae2VectorAnimation();
            document.vectorAnimation.primitives ??= Array.Empty<Ae2VectorPrimitive>();
            document.bakedFrames ??= new Ae2ShaderBakedFrames();
            document.warnings ??= Array.Empty<Ae2ShaderWarning>();

            foreach (var layer in document.layers)
            {
                if (layer == null)
                {
                    continue;
                }

                layer.transform ??= new Ae2ShaderTransform();
                layer.effects ??= Array.Empty<Ae2ShaderEffect>();
                layer.masks ??= Array.Empty<Ae2ShaderMask>();
                layer.expressions ??= Array.Empty<string>();
                layer.transform.anchorPoint ??= Ae2ShaderAnimatedVector3.Zero();
                layer.transform.position ??= Ae2ShaderAnimatedVector3.Zero();
                layer.transform.scale ??= Ae2ShaderAnimatedVector3.OneHundred();
                layer.transform.rotation ??= Ae2ShaderAnimatedFloat.Zero();
                layer.transform.opacity ??= Ae2ShaderAnimatedFloat.OneHundred();
                layer.transform.anchorPoint.keys ??= Array.Empty<Ae2ShaderVector3Keyframe>();
                layer.transform.position.keys ??= Array.Empty<Ae2ShaderVector3Keyframe>();
                layer.transform.scale.keys ??= Array.Empty<Ae2ShaderVector3Keyframe>();
                layer.transform.rotation.keys ??= Array.Empty<Ae2ShaderFloatKeyframe>();
                layer.transform.opacity.keys ??= Array.Empty<Ae2ShaderFloatKeyframe>();
            }
        }
    }
}
