using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AE2Unity
{
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class Ae2VectorCompositionGraphic : MaskableGraphic
    {
        private const int RoundedCornerSegments = 8;

        [SerializeField] private Ae2ShaderClip clip;
        [SerializeField] private bool playOnAwake = true;
        [SerializeField] private bool loop = true;
        [SerializeField] private float playbackSpeed = 1f;
        [SerializeField] private float timeSeconds;

        private readonly List<Vector2> points = new List<Vector2>(64);

        public Ae2ShaderClip Clip
        {
            get => clip;
            set
            {
                if (clip == value)
                {
                    return;
                }

                clip = value;
                SetAllDirty();
            }
        }

        public float TimeSeconds
        {
            get => timeSeconds;
            set
            {
                if (Mathf.Approximately(timeSeconds, value))
                {
                    return;
                }

                timeSeconds = Mathf.Max(0f, value);
                SetVerticesDirty();
            }
        }

        public override Texture mainTexture => Texture2D.whiteTexture;

        protected override void OnEnable()
        {
            base.OnEnable();
            SetAllDirty();
        }

        private void Update()
        {
            if (clip?.Document?.vectorAnimation == null)
            {
                return;
            }

            if (Application.isPlaying && playOnAwake)
            {
                var duration = GetDuration();
                timeSeconds += Time.deltaTime * playbackSpeed;
                if (loop && duration > 0f)
                {
                    timeSeconds = Mathf.Repeat(timeSeconds, duration);
                }
                else
                {
                    timeSeconds = Mathf.Clamp(timeSeconds, 0f, Mathf.Max(0f, duration));
                }

                SetVerticesDirty();
            }
            else if (!Application.isPlaying)
            {
                SetVerticesDirty();
            }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            var document = clip?.Document;
            var vectorAnimation = document?.vectorAnimation;
            if (document?.comp == null || vectorAnimation == null || !vectorAnimation.enabled || vectorAnimation.primitives == null)
            {
                return;
            }

            var frameRate = Mathf.Max(1f, vectorAnimation.frameRate > 0f ? vectorAnimation.frameRate : document.comp.frameRate);
            var duration = GetDuration();
            var playbackTime = loop && duration > 0f ? Mathf.Repeat(timeSeconds, duration) : Mathf.Clamp(timeSeconds, 0f, duration);
            var frameIndex = Mathf.Clamp(Mathf.FloorToInt(playbackTime * frameRate + 0.0001f), 0, Mathf.Max(0, vectorAnimation.frameCount - 1));

            for (var i = 0; i < vectorAnimation.primitives.Length; i++)
            {
                var primitive = vectorAnimation.primitives[i];
                var frame = FindFrame(primitive, frameIndex);
                if (frame == null || !frame.visible)
                {
                    continue;
                }

                if (string.Equals(primitive.type, "rect", System.StringComparison.OrdinalIgnoreCase))
                {
                    PopulateRect(vh, document.comp, frame);
                }
                else if (string.Equals(primitive.type, "path", System.StringComparison.OrdinalIgnoreCase))
                {
                    PopulatePath(vh, document.comp, frame);
                }
            }
        }

        private float GetDuration()
        {
            var document = clip?.Document;
            var vectorAnimation = document?.vectorAnimation;
            if (vectorAnimation != null && vectorAnimation.duration > 0f)
            {
                return vectorAnimation.duration;
            }

            return Mathf.Max(0f, document?.comp?.duration ?? 0f);
        }

        private static Ae2VectorFrame FindFrame(Ae2VectorPrimitive primitive, int frameIndex)
        {
            if (primitive?.frames == null || primitive.frames.Length == 0)
            {
                return null;
            }

            Ae2VectorFrame best = null;
            for (var i = 0; i < primitive.frames.Length; i++)
            {
                var candidate = primitive.frames[i];
                if (candidate.frameIndex == frameIndex)
                {
                    return candidate;
                }

                if (candidate.frameIndex <= frameIndex)
                {
                    best = candidate;
                }
            }

            return best ?? primitive.frames[0];
        }

        private void PopulateRect(VertexHelper vh, Ae2ShaderComp comp, Ae2VectorFrame frame)
        {
            points.Clear();

            var radius = Mathf.Clamp(frame.roundness, 0f, Mathf.Min(Mathf.Abs(frame.width), Mathf.Abs(frame.height)) * 0.5f);
            if (radius <= 0.001f)
            {
                AddMappedPoint(comp, frame, frame.x, frame.y);
                AddMappedPoint(comp, frame, frame.x + frame.width, frame.y);
                AddMappedPoint(comp, frame, frame.x + frame.width, frame.y + frame.height);
                AddMappedPoint(comp, frame, frame.x, frame.y + frame.height);
            }
            else
            {
                AddCorner(comp, frame, frame.x + frame.width - radius, frame.y + radius, radius, -90f, 0f);
                AddCorner(comp, frame, frame.x + frame.width - radius, frame.y + frame.height - radius, radius, 0f, 90f);
                AddCorner(comp, frame, frame.x + radius, frame.y + frame.height - radius, radius, 90f, 180f);
                AddCorner(comp, frame, frame.x + radius, frame.y + radius, radius, 180f, 270f);
            }

            PopulateCurrentPolygon(vh, frame, true);
        }

        private void PopulatePath(VertexHelper vh, Ae2ShaderComp comp, Ae2VectorFrame frame)
        {
            points.Clear();
            if (frame.path == null || frame.path.Length < 2)
            {
                return;
            }

            for (var i = 0; i < frame.path.Length; i++)
            {
                AddMappedPoint(comp, frame, frame.path[i].x, frame.path[i].y);
            }

            PopulateCurrentPolygon(vh, frame, frame.closed);
        }

        private void PopulateCurrentPolygon(VertexHelper vh, Ae2VectorFrame frame, bool closed)
        {
            if (points.Count < 2)
            {
                return;
            }

            if (frame.fillEnabled && closed && points.Count >= 3)
            {
                AddFill(vh, points, MultiplyGraphicColor(new Color(frame.fillR, frame.fillG, frame.fillB, frame.fillA)));
            }

            if (frame.strokeEnabled && frame.strokeWidth > 0.001f)
            {
                var strokeScale = EstimateStrokeScale(frame);
                AddStroke(vh, points, frame.strokeWidth * strokeScale, closed,
                    frame.dash * strokeScale,
                    frame.gap * strokeScale,
                    frame.dashOffset * strokeScale,
                    MultiplyGraphicColor(new Color(frame.strokeR, frame.strokeG, frame.strokeB, frame.strokeA)));
            }
        }

        private void AddFill(VertexHelper vh, List<Vector2> polygon, Color32 fillColor)
        {
            var center = Vector2.zero;
            for (var i = 0; i < polygon.Count; i++)
            {
                center += polygon[i];
            }

            center /= polygon.Count;
            var centerIndex = AddVertex(vh, center, fillColor);
            var clockwise = PolygonArea(polygon) < 0f;

            for (var i = 0; i < polygon.Count; i++)
            {
                var next = (i + 1) % polygon.Count;
                var currentIndex = AddVertex(vh, polygon[i], fillColor);
                var nextIndex = AddVertex(vh, polygon[next], fillColor);
                if (clockwise)
                {
                    vh.AddTriangle(centerIndex, nextIndex, currentIndex);
                }
                else
                {
                    vh.AddTriangle(centerIndex, currentIndex, nextIndex);
                }
            }
        }

        private void AddStroke(VertexHelper vh, List<Vector2> polyline, float thickness, bool closed, float dash, float gap, float dashOffset, Color32 strokeColor)
        {
            if (dash > 0.001f && gap > 0.001f)
            {
                AddDashedStroke(vh, polyline, thickness, closed, dash, gap, dashOffset, strokeColor);
                return;
            }

            var segmentCount = closed ? polyline.Count : polyline.Count - 1;
            for (var i = 0; i < segmentCount; i++)
            {
                AddStrokeSegment(vh, polyline[i], polyline[(i + 1) % polyline.Count], thickness, strokeColor);
            }
        }

        private void AddDashedStroke(VertexHelper vh, List<Vector2> polyline, float thickness, bool closed, float dash, float gap, float dashOffset, Color32 strokeColor)
        {
            var patternLength = Mathf.Max(0.001f, dash + gap);
            var distance = 0f;
            var segmentCount = closed ? polyline.Count : polyline.Count - 1;
            for (var i = 0; i < segmentCount; i++)
            {
                var a = polyline[i];
                var b = polyline[(i + 1) % polyline.Count];
                var segment = b - a;
                var length = segment.magnitude;
                if (length <= 0.0001f)
                {
                    continue;
                }

                var direction = segment / length;
                var cursor = 0f;
                while (cursor < length)
                {
                    var patternPosition = PositiveModulo(distance + cursor + dashOffset, patternLength);
                    var inDash = patternPosition < dash;
                    var step = inDash ? dash - patternPosition : patternLength - patternPosition;
                    var nextCursor = Mathf.Min(length, cursor + Mathf.Max(0.001f, step));
                    if (inDash && nextCursor > cursor)
                    {
                        AddStrokeSegment(vh, a + direction * cursor, a + direction * nextCursor, thickness, strokeColor);
                    }

                    cursor = nextCursor;
                }

                distance += length;
            }
        }

        private void AddStrokeSegment(VertexHelper vh, Vector2 a, Vector2 b, float thickness, Color32 strokeColor)
        {
            var half = Mathf.Max(0.001f, thickness * 0.5f);
            var direction = b - a;
            if (direction.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            direction.Normalize();
            var normal = new Vector2(-direction.y, direction.x) * half;
            var i0 = AddVertex(vh, a - normal, strokeColor);
            var i1 = AddVertex(vh, a + normal, strokeColor);
            var i2 = AddVertex(vh, b + normal, strokeColor);
            var i3 = AddVertex(vh, b - normal, strokeColor);
            vh.AddTriangle(i0, i1, i2);
            vh.AddTriangle(i0, i2, i3);
        }

        private static float PositiveModulo(float value, float divisor)
        {
            return ((value % divisor) + divisor) % divisor;
        }

        private void AddCorner(Ae2ShaderComp comp, Ae2VectorFrame frame, float centerX, float centerY, float radius, float startDegrees, float endDegrees)
        {
            for (var i = 0; i <= RoundedCornerSegments; i++)
            {
                var t = i / (float)RoundedCornerSegments;
                var angle = Mathf.Lerp(startDegrees, endDegrees, t) * Mathf.Deg2Rad;
                AddMappedPoint(comp, frame, centerX + Mathf.Cos(angle) * radius, centerY + Mathf.Sin(angle) * radius);
            }
        }

        private void AddMappedPoint(Ae2ShaderComp comp, Ae2VectorFrame frame, float x, float y)
        {
            var aeX = frame.m00 * x + frame.m01 * y + frame.m02;
            var aeY = frame.m10 * x + frame.m11 * y + frame.m12;
            points.Add(MapCompPoint(comp, aeX, aeY));
        }

        private Vector2 MapCompPoint(Ae2ShaderComp comp, float aeX, float aeY)
        {
            var rect = rectTransform.rect;
            var width = Mathf.Max(1f, comp.width);
            var height = Mathf.Max(1f, comp.height);
            return new Vector2(
                rect.xMin + aeX / width * rect.width,
                rect.yMax - aeY / height * rect.height);
        }

        private float EstimateStrokeScale(Ae2VectorFrame frame)
        {
            var rect = rectTransform.rect;
            var comp = clip?.Document?.comp;
            var compWidth = Mathf.Max(1f, comp?.width ?? 1f);
            var compHeight = Mathf.Max(1f, comp?.height ?? 1f);
            var transformScaleX = Mathf.Sqrt(frame.m00 * frame.m00 + frame.m10 * frame.m10);
            var transformScaleY = Mathf.Sqrt(frame.m01 * frame.m01 + frame.m11 * frame.m11);
            var transformScale = Mathf.Max(0.0001f, (transformScaleX + transformScaleY) * 0.5f);
            var rectScale = (Mathf.Abs(rect.width) / compWidth + Mathf.Abs(rect.height) / compHeight) * 0.5f;
            return transformScale * Mathf.Max(0.0001f, rectScale);
        }

        private Color32 MultiplyGraphicColor(Color source)
        {
            var c = color;
            return new Color(
                source.r * c.r,
                source.g * c.g,
                source.b * c.b,
                source.a * c.a);
        }

        private static int AddVertex(VertexHelper vh, Vector2 position, Color32 color)
        {
            var index = vh.currentVertCount;
            vh.AddVert(position, color, Vector2.zero);
            return index;
        }

        private static float PolygonArea(List<Vector2> polygon)
        {
            var area = 0f;
            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
            }

            return area * 0.5f;
        }
    }
}
