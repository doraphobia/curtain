using System;
using UnityEngine;

namespace AE2Unity
{
    public enum AE2RendererHint
    {
        UnsupportedShape,
        ProceduralCircle,
        ProceduralRect,
        ProceduralStroke
    }

    public sealed class AE2MotionData : ScriptableObject
    {
        [SerializeField] private AE2MotionDocument document = new AE2MotionDocument();
        [SerializeField, TextArea(4, 16)] private string sourceJson = string.Empty;
        [SerializeField] private string[] importWarnings = Array.Empty<string>();

        public AE2MotionDocument Document => document;
        public string SourceJson => sourceJson;
        public string[] ImportWarnings => importWarnings;

        public string CompName => document?.comp?.name ?? "Untitled";
        public int Width => document?.comp?.width ?? 0;
        public int Height => document?.comp?.height ?? 0;
        public float Duration => document?.comp?.duration ?? 0f;
        public float FrameRate => document?.comp?.frameRate ?? 0f;
        public int LayerCount => document?.motion?.layers?.Length ?? 0;

        public void Initialize(AE2MotionDocument importedDocument, string json, string[] warnings)
        {
            document = importedDocument ?? new AE2MotionDocument();
            sourceJson = json ?? string.Empty;
            importWarnings = warnings ?? Array.Empty<string>();
            Normalize();
        }

        private void OnValidate()
        {
            Normalize();
        }

        public void Normalize()
        {
            document ??= new AE2MotionDocument();
            document.comp ??= new AE2MotionComp();
            document.motion ??= new AE2MotionRoot();
            document.motion.layers ??= Array.Empty<AE2MotionLayer>();
            document.warnings ??= Array.Empty<AE2MotionWarning>();

            foreach (var layer in document.motion.layers)
            {
                layer?.Normalize();
            }
        }
    }

    [Serializable]
    public sealed class AE2MotionDocument
    {
        public string schemaVersion = "0.2.1";
        public string exporter = "AE2Unity Motion Exporter";
        public string exportedAt = string.Empty;
        public AE2MotionComp comp = new AE2MotionComp();
        public AE2MotionRoot motion = new AE2MotionRoot();
        public AE2MotionWarning[] warnings = Array.Empty<AE2MotionWarning>();
    }

    [Serializable]
    public sealed class AE2MotionComp
    {
        public string name = "Untitled";
        public int width = 1920;
        public int height = 1080;
        public float frameRate = 24f;
        public float duration = 1f;
        public float workAreaStart;
        public float workAreaDuration = 1f;
    }

    [Serializable]
    public sealed class AE2MotionRoot
    {
        public string units = "pixels";
        public string timeMode = "seconds";
        public string coordinateSystem = "AE_TOP_LEFT";
        public AE2MotionLayer[] layers = Array.Empty<AE2MotionLayer>();
    }

    [Serializable]
    public sealed class AE2MotionLayer
    {
        public string id = string.Empty;
        public string name = "Layer";
        public int index;
        public string type = "unknown";
        public bool enabled = true;
        public float inPoint;
        public float outPoint = 1f;
        public string parentId = string.Empty;
        public string blendMode = "NORMAL";
        public string rendererHint = "UnsupportedShape";
        public AE2MotionShape shape = new AE2MotionShape();
        public AE2MotionTransform transform = new AE2MotionTransform();
        public string[] expressions = Array.Empty<string>();
        public AE2MotionWarning[] warnings = Array.Empty<AE2MotionWarning>();

        public AE2RendererHint RendererHint
        {
            get
            {
                if (Enum.TryParse(rendererHint, true, out AE2RendererHint parsed))
                {
                    return parsed;
                }

                return AE2RendererHint.UnsupportedShape;
            }
        }

        public void Normalize()
        {
            shape ??= new AE2MotionShape();
            transform ??= new AE2MotionTransform();
            expressions ??= Array.Empty<string>();
            warnings ??= Array.Empty<AE2MotionWarning>();
            shape.Normalize();
            transform.Normalize();
        }
    }

    [Serializable]
    public sealed class AE2MotionTransform
    {
        public AE2AnimatedVector3 anchorPoint = AE2AnimatedVector3.Zero();
        public AE2AnimatedVector3 position = AE2AnimatedVector3.Zero();
        public AE2AnimatedVector3 scale = AE2AnimatedVector3.OneHundred();
        public AE2AnimatedFloat rotation = AE2AnimatedFloat.Zero();
        public AE2AnimatedFloat opacity = AE2AnimatedFloat.OneHundred();

        public void Normalize()
        {
            anchorPoint ??= AE2AnimatedVector3.Zero();
            position ??= AE2AnimatedVector3.Zero();
            scale ??= AE2AnimatedVector3.OneHundred();
            rotation ??= AE2AnimatedFloat.Zero();
            opacity ??= AE2AnimatedFloat.OneHundred();
            anchorPoint.Normalize();
            position.Normalize();
            scale.Normalize();
            rotation.Normalize();
            opacity.Normalize();
        }
    }

    [Serializable]
    public sealed class AE2MotionShape
    {
        public string kind = "unsupported";
        public AE2AnimatedVector2 center = AE2AnimatedVector2.Zero();
        public AE2AnimatedVector2 size = AE2AnimatedVector2.Zero();
        public AE2AnimatedFloat radius = AE2AnimatedFloat.Zero();
        public AE2AnimatedFloat cornerRadius = AE2AnimatedFloat.Zero();
        public AE2AnimatedVector2 pathStart = AE2AnimatedVector2.Zero();
        public AE2AnimatedVector2 pathEnd = AE2AnimatedVector2.Zero();
        public AE2AnimatedColor fill = AE2AnimatedColor.White();
        public AE2AnimatedColor stroke = AE2AnimatedColor.Clear();
        public AE2AnimatedFloat strokeWidth = AE2AnimatedFloat.Zero();
        public AE2AnimatedFloat trimStart = AE2AnimatedFloat.Zero();
        public AE2AnimatedFloat trimEnd = AE2AnimatedFloat.OneHundred();
        public AE2AnimatedFloat trimOffset = AE2AnimatedFloat.Zero();

        public void Normalize()
        {
            center ??= AE2AnimatedVector2.Zero();
            size ??= AE2AnimatedVector2.Zero();
            radius ??= AE2AnimatedFloat.Zero();
            cornerRadius ??= AE2AnimatedFloat.Zero();
            pathStart ??= AE2AnimatedVector2.Zero();
            pathEnd ??= AE2AnimatedVector2.Zero();
            fill ??= AE2AnimatedColor.White();
            stroke ??= AE2AnimatedColor.Clear();
            strokeWidth ??= AE2AnimatedFloat.Zero();
            trimStart ??= AE2AnimatedFloat.Zero();
            trimEnd ??= AE2AnimatedFloat.OneHundred();
            trimOffset ??= AE2AnimatedFloat.Zero();
            center.Normalize();
            size.Normalize();
            radius.Normalize();
            cornerRadius.Normalize();
            pathStart.Normalize();
            pathEnd.Normalize();
            fill.Normalize();
            stroke.Normalize();
            strokeWidth.Normalize();
            trimStart.Normalize();
            trimEnd.Normalize();
            trimOffset.Normalize();
        }
    }

    [Serializable]
    public sealed class AE2AnimatedFloat
    {
        public float staticValue;
        public string expression = string.Empty;
        public AE2FloatKeyframe[] keys = Array.Empty<AE2FloatKeyframe>();

        public static AE2AnimatedFloat Zero() => new AE2AnimatedFloat { staticValue = 0f };
        public static AE2AnimatedFloat OneHundred() => new AE2AnimatedFloat { staticValue = 100f };
        public void Normalize() => keys ??= Array.Empty<AE2FloatKeyframe>();
    }

    [Serializable]
    public sealed class AE2AnimatedVector2
    {
        public Vector2 staticValue;
        public string expression = string.Empty;
        public AE2Vector2Keyframe[] keys = Array.Empty<AE2Vector2Keyframe>();

        public static AE2AnimatedVector2 Zero() => new AE2AnimatedVector2 { staticValue = Vector2.zero };
        public void Normalize() => keys ??= Array.Empty<AE2Vector2Keyframe>();
    }

    [Serializable]
    public sealed class AE2AnimatedVector3
    {
        public Vector3 staticValue;
        public string expression = string.Empty;
        public AE2Vector3Keyframe[] keys = Array.Empty<AE2Vector3Keyframe>();

        public static AE2AnimatedVector3 Zero() => new AE2AnimatedVector3 { staticValue = Vector3.zero };
        public static AE2AnimatedVector3 OneHundred() => new AE2AnimatedVector3 { staticValue = new Vector3(100f, 100f, 100f) };
        public void Normalize() => keys ??= Array.Empty<AE2Vector3Keyframe>();
    }

    [Serializable]
    public sealed class AE2AnimatedColor
    {
        public Color staticValue = Color.white;
        public string expression = string.Empty;
        public AE2ColorKeyframe[] keys = Array.Empty<AE2ColorKeyframe>();

        public static AE2AnimatedColor White() => new AE2AnimatedColor { staticValue = Color.white };
        public static AE2AnimatedColor Clear() => new AE2AnimatedColor { staticValue = Color.clear };
        public void Normalize() => keys ??= Array.Empty<AE2ColorKeyframe>();
    }

    [Serializable]
    public sealed class AE2FloatKeyframe
    {
        public float time;
        public float value;
        public string inInterpolation = "LINEAR";
        public string outInterpolation = "LINEAR";
    }

    [Serializable]
    public sealed class AE2Vector2Keyframe
    {
        public float time;
        public Vector2 value;
        public string inInterpolation = "LINEAR";
        public string outInterpolation = "LINEAR";
    }

    [Serializable]
    public sealed class AE2Vector3Keyframe
    {
        public float time;
        public Vector3 value;
        public string inInterpolation = "LINEAR";
        public string outInterpolation = "LINEAR";
    }

    [Serializable]
    public sealed class AE2ColorKeyframe
    {
        public float time;
        public Color value = Color.white;
        public string inInterpolation = "LINEAR";
        public string outInterpolation = "LINEAR";
    }

    [Serializable]
    public sealed class AE2MotionWarning
    {
        public string code = string.Empty;
        public string message = string.Empty;
        public string layerId = string.Empty;
    }
}
