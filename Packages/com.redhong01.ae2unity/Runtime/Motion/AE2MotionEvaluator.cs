using System;
using UnityEngine;

namespace AE2Unity
{
    public struct AE2MotionEvaluatedLayer
    {
        public bool valid;
        public string layerId;
        public string layerName;
        public AE2RendererHint rendererHint;
        public Vector3 anchorPoint;
        public Vector3 position;
        public Vector3 scale;
        public float rotation;
        public float opacity01;
        public Vector2 shapeCenter;
        public Vector2 shapeSize;
        public float shapeRadius;
        public float cornerRadius;
        public Vector2 pathStart;
        public Vector2 pathEnd;
        public Color fillColor;
        public Color strokeColor;
        public float strokeWidth;
        public float trimStart01;
        public float trimEnd01;
        public float trimOffset;
    }

    public static class AE2MotionEvaluator
    {
        public static bool TryEvaluateFirstRenderable(AE2MotionData motionData, float time, out AE2MotionEvaluatedLayer evaluated)
        {
            evaluated = default;
            var layers = motionData?.Document?.motion?.layers;
            if (layers == null)
            {
                return false;
            }

            for (var i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                if (layer == null || !layer.enabled || !IsRuntimeRenderable(layer.RendererHint))
                {
                    continue;
                }

                if (time < layer.inPoint || (layer.outPoint > layer.inPoint && time > layer.outPoint))
                {
                    continue;
                }

                evaluated = EvaluateLayer(layer, time);
                return evaluated.valid;
            }

            return false;
        }

        public static bool IsRuntimeRenderable(AE2RendererHint rendererHint)
        {
            return rendererHint == AE2RendererHint.ProceduralCircle ||
                   rendererHint == AE2RendererHint.ProceduralRect ||
                   rendererHint == AE2RendererHint.ProceduralStroke;
        }

        public static AE2MotionEvaluatedLayer EvaluateLayer(AE2MotionLayer layer, float time)
        {
            if (layer == null)
            {
                return default;
            }

            layer.Normalize();
            var transform = layer.transform;
            var shape = layer.shape;
            var size = Evaluate(shape.size, time);
            var radius = Evaluate(shape.radius, time);
            if (radius <= 0f)
            {
                radius = Mathf.Max(size.x, size.y) * 0.5f;
            }

            return new AE2MotionEvaluatedLayer
            {
                valid = true,
                layerId = layer.id,
                layerName = layer.name,
                rendererHint = layer.RendererHint,
                anchorPoint = Evaluate(transform.anchorPoint, time),
                position = Evaluate(transform.position, time),
                scale = Evaluate(transform.scale, time),
                rotation = Evaluate(transform.rotation, time),
                opacity01 = Mathf.Clamp01(Evaluate(transform.opacity, time) / 100f),
                shapeCenter = Evaluate(shape.center, time),
                shapeSize = size,
                shapeRadius = radius,
                cornerRadius = Evaluate(shape.cornerRadius, time),
                pathStart = Evaluate(shape.pathStart, time),
                pathEnd = Evaluate(shape.pathEnd, time),
                fillColor = Evaluate(shape.fill, time),
                strokeColor = Evaluate(shape.stroke, time),
                strokeWidth = Evaluate(shape.strokeWidth, time),
                trimStart01 = Mathf.Clamp01(Evaluate(shape.trimStart, time) / 100f),
                trimEnd01 = Mathf.Clamp01(Evaluate(shape.trimEnd, time) / 100f),
                trimOffset = Evaluate(shape.trimOffset, time)
            };
        }

        public static float Evaluate(AE2AnimatedFloat property, float time)
        {
            if (property == null)
            {
                return 0f;
            }

            property.Normalize();
            var keys = property.keys;
            if (keys.Length == 0)
            {
                return property.staticValue;
            }

            if (time <= keys[0].time)
            {
                return keys[0].value;
            }

            var last = keys[keys.Length - 1];
            if (time >= last.time)
            {
                return last.value;
            }

            for (var i = 0; i < keys.Length - 1; i++)
            {
                var a = keys[i];
                var b = keys[i + 1];
                if (time < a.time || time > b.time)
                {
                    continue;
                }

                if (IsHold(a.outInterpolation) || IsHold(b.inInterpolation))
                {
                    return a.value;
                }

                var t = Mathf.InverseLerp(a.time, b.time, time);
                return Mathf.LerpUnclamped(a.value, b.value, t);
            }

            return property.staticValue;
        }

        public static Vector2 Evaluate(AE2AnimatedVector2 property, float time)
        {
            if (property == null)
            {
                return Vector2.zero;
            }

            property.Normalize();
            var keys = property.keys;
            if (keys.Length == 0)
            {
                return property.staticValue;
            }

            if (time <= keys[0].time)
            {
                return keys[0].value;
            }

            var last = keys[keys.Length - 1];
            if (time >= last.time)
            {
                return last.value;
            }

            for (var i = 0; i < keys.Length - 1; i++)
            {
                var a = keys[i];
                var b = keys[i + 1];
                if (time < a.time || time > b.time)
                {
                    continue;
                }

                if (IsHold(a.outInterpolation) || IsHold(b.inInterpolation))
                {
                    return a.value;
                }

                var t = Mathf.InverseLerp(a.time, b.time, time);
                return Vector2.LerpUnclamped(a.value, b.value, t);
            }

            return property.staticValue;
        }

        public static Vector3 Evaluate(AE2AnimatedVector3 property, float time)
        {
            if (property == null)
            {
                return Vector3.zero;
            }

            property.Normalize();
            var keys = property.keys;
            if (keys.Length == 0)
            {
                return property.staticValue;
            }

            if (time <= keys[0].time)
            {
                return keys[0].value;
            }

            var last = keys[keys.Length - 1];
            if (time >= last.time)
            {
                return last.value;
            }

            for (var i = 0; i < keys.Length - 1; i++)
            {
                var a = keys[i];
                var b = keys[i + 1];
                if (time < a.time || time > b.time)
                {
                    continue;
                }

                if (IsHold(a.outInterpolation) || IsHold(b.inInterpolation))
                {
                    return a.value;
                }

                var t = Mathf.InverseLerp(a.time, b.time, time);
                return Vector3.LerpUnclamped(a.value, b.value, t);
            }

            return property.staticValue;
        }

        public static Color Evaluate(AE2AnimatedColor property, float time)
        {
            if (property == null)
            {
                return Color.white;
            }

            property.Normalize();
            var keys = property.keys;
            if (keys.Length == 0)
            {
                return property.staticValue;
            }

            if (time <= keys[0].time)
            {
                return keys[0].value;
            }

            var last = keys[keys.Length - 1];
            if (time >= last.time)
            {
                return last.value;
            }

            for (var i = 0; i < keys.Length - 1; i++)
            {
                var a = keys[i];
                var b = keys[i + 1];
                if (time < a.time || time > b.time)
                {
                    continue;
                }

                if (IsHold(a.outInterpolation) || IsHold(b.inInterpolation))
                {
                    return a.value;
                }

                var t = Mathf.InverseLerp(a.time, b.time, time);
                return Color.LerpUnclamped(a.value, b.value, t);
            }

            return property.staticValue;
        }

        private static bool IsHold(string interpolation)
        {
            return string.Equals(interpolation, "HOLD", StringComparison.OrdinalIgnoreCase);
        }
    }
}
