using System;
using System.Collections.Generic;
using AE2Unity;

namespace AE2Unity.Editor
{
    internal static class Ae2ShaderCapabilityAnalyzer
    {
        private static readonly HashSet<string> SupportedBlendModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "NORMAL",
            "ADD",
            "SCREEN",
            "MULTIPLY",
            "OVERLAY",
            "ALPHA_ADD",
            "SILHOUETTE_ALPHA",
            "STENCIL_ALPHA"
        };

        private static readonly HashSet<string> SupportedEffectMatchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ADBE Fill",
            "ADBE Tint",
            "ADBE Ramp",
            "ADBE Gaussian Blur 2",
            "ADBE Glo2",
            "ADBE HUE SATURATION",
            "ADBE Turbulent Noise"
        };

        public static string[] Analyze(Ae2ShaderDocument document)
        {
            var warnings = new List<string>();

            if (document == null)
            {
                warnings.Add("Document is empty or invalid.");
                return warnings.ToArray();
            }

            if (document.comp == null)
            {
                warnings.Add("Composition metadata is missing.");
            }
            else
            {
                if (document.comp.width <= 0 || document.comp.height <= 0)
                {
                    warnings.Add("Composition dimensions are invalid.");
                }

                if (document.comp.bitDepth > 8)
                {
                    warnings.Add($"Composition uses {document.comp.bitDepth}-bit color; generated shaders should be visually checked against AE reference frames.");
                }
            }

            var layers = document.layers ?? Array.Empty<Ae2ShaderLayer>();
            for (var i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (layer == null)
                {
                    warnings.Add($"Layer {i + 1} is null.");
                    continue;
                }

                var label = string.IsNullOrEmpty(layer.name) ? $"Layer {layer.index}" : layer.name;

                if (layer.threeDLayer)
                {
                    warnings.Add($"{label}: 3D layers are not shader-converted in the MVP and should be baked.");
                }

                if (!string.IsNullOrEmpty(layer.blendMode) && !SupportedBlendModes.Contains(layer.blendMode))
                {
                    warnings.Add($"{label}: blend mode '{layer.blendMode}' is not implemented yet.");
                }

                if (HasExpression(layer.transform?.anchorPoint) ||
                    HasExpression(layer.transform?.position) ||
                    HasExpression(layer.transform?.scale) ||
                    HasExpression(layer.transform?.rotation) ||
                    HasExpression(layer.transform?.opacity) ||
                    (layer.expressions != null && layer.expressions.Length > 0))
                {
                    warnings.Add($"{label}: expressions require a later expression compiler or baked keyframes.");
                }

                var effects = layer.effects ?? Array.Empty<Ae2ShaderEffect>();
                for (var effectIndex = 0; effectIndex < effects.Length; effectIndex++)
                {
                    var effect = effects[effectIndex];
                    if (effect == null || !effect.enabled)
                    {
                        continue;
                    }

                    if (!SupportedEffectMatchNames.Contains(effect.matchName))
                    {
                        var effectName = string.IsNullOrEmpty(effect.displayName) ? effect.matchName : effect.displayName;
                        warnings.Add($"{label}: effect '{effectName}' is not implemented yet and should be baked or added as a compiler pass.");
                    }
                }

                var masks = layer.masks ?? Array.Empty<Ae2ShaderMask>();
                for (var maskIndex = 0; maskIndex < masks.Length; maskIndex++)
                {
                    var mask = masks[maskIndex];
                    if (mask == null)
                    {
                        continue;
                    }

                    if (!string.Equals(mask.mode, "ADD", StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add($"{label}: mask '{mask.name}' uses mode '{mask.mode}', which is not converted yet.");
                    }
                }
            }

            return warnings.ToArray();
        }

        private static bool HasExpression(Ae2ShaderAnimatedFloat value)
        {
            return value != null && !string.IsNullOrWhiteSpace(value.expression);
        }

        private static bool HasExpression(Ae2ShaderAnimatedVector3 value)
        {
            return value != null && !string.IsNullOrWhiteSpace(value.expression);
        }
    }
}
