using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.Vision
{
    /// <summary>
    /// Renderer-independent output of one vision sampling pass.
    /// It intentionally contains no Mesh, Material, Shader, or Renderer references.
    /// </summary>
    public sealed class VisionSnapshot
    {
        private readonly List<VisionRaySample> raySamples = new List<VisionRaySample>(128);
        private readonly List<Vector2> visibilityPolygon = new List<Vector2>(128);

        public Vector2 origin { get; private set; }
        public Vector2 forward { get; private set; }
        public float viewAngleDegrees { get; private set; }
        public float viewDistance { get; private set; }
        public float timestamp { get; private set; }
        public int frameIndex { get; private set; }
        public uint version { get; private set; }
        public Bounds bounds { get; private set; }

        public IReadOnlyList<VisionRaySample> RaySamples => raySamples;
        public IReadOnlyList<Vector2> VisibilityPolygon => visibilityPolygon;
        public int SampleCount => raySamples.Count;
        public bool IsValid => visibilityPolygon.Count >= 2 && viewDistance > 0f;

        internal void Begin(
            Vector2 worldOrigin,
            Vector2 worldForward,
            float angleDegrees,
            float distance,
            float sampleTimestamp,
            int sampleFrame)
        {
            origin = worldOrigin;
            forward = worldForward.sqrMagnitude > 0.000001f ? worldForward.normalized : Vector2.up;
            viewAngleDegrees = Mathf.Clamp(angleDegrees, 0.01f, 360f);
            viewDistance = Mathf.Max(0.0001f, distance);
            timestamp = sampleTimestamp;
            frameIndex = sampleFrame;
            raySamples.Clear();
            visibilityPolygon.Clear();
        }

        internal bool AddSample(VisionRaySample sample, int maximumSamples)
        {
            if (raySamples.Count >= Mathf.Max(2, maximumSamples))
                return false;

            raySamples.Add(sample);
            visibilityPolygon.Add(sample.point);
            return true;
        }

        internal void Complete()
        {
            version++;
            Bounds sampleBounds = new Bounds(
                new Vector3(origin.x, origin.y, 0f),
                new Vector3(0.001f, 0.001f, 0.001f));
            for (int i = 0; i < visibilityPolygon.Count; i++)
            {
                Vector2 point = visibilityPolygon[i];
                sampleBounds.Encapsulate(new Vector3(point.x, point.y, 0f));
            }

            bounds = sampleBounds;
        }

        public bool ContainsWorldPoint(Vector2 worldPoint)
        {
            if (!IsValid || !bounds.Contains(new Vector3(worldPoint.x, worldPoint.y, bounds.center.z)))
                return false;

            bool includeOrigin = viewAngleDegrees < 359.999f;
            int polygonVertexCount = visibilityPolygon.Count + (includeOrigin ? 1 : 0);
            bool inside = false;
            int previous = polygonVertexCount - 1;
            for (int current = 0; current < polygonVertexCount; current++)
            {
                Vector2 a = GetPolygonVertex(current, includeOrigin);
                Vector2 b = GetPolygonVertex(previous, includeOrigin);
                bool crosses = (a.y > worldPoint.y) != (b.y > worldPoint.y);
                if (crosses &&
                    worldPoint.x <
                    (b.x - a.x) * (worldPoint.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;

                previous = current;
            }

            return inside;
        }

        private Vector2 GetPolygonVertex(int index, bool includeOrigin)
        {
            if (!includeOrigin)
                return visibilityPolygon[index];

            return index == 0 ? origin : visibilityPolygon[index - 1];
        }
    }
}
