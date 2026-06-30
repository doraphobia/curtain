using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.Vision
{
    public enum VisionDetectionSource
    {
        None,
        Direct,
        OpenWindowPortal,
        OpenDoorPortal
    }

    public sealed class PortalVisionPolygon
    {
        private readonly List<Vector2> polygon = new List<Vector2>(64);

        public IVisionPortal portal;
        public Vector2 portalHitPoint;
        public Vector2 portalExitOrigin;
        public int targetRoomId;
        public VisionDetectionSource detectionSource;
        public IReadOnlyList<Vector2> Polygon => polygon;
        public bool IsValid => polygon.Count >= 2;

        internal void SetPoints(IReadOnlyList<Vector2> points)
        {
            polygon.Clear();
            if (points == null)
                return;

            for (int i = 0; i < points.Count; i++)
                polygon.Add(points[i]);
        }
    }

    /// <summary>
    /// Renderer-independent output of one vision sampling pass.
    /// It intentionally contains no Mesh, Material, Shader, or Renderer references.
    /// </summary>
    public sealed class VisionSnapshot
    {
        private readonly List<VisionRaySample> raySamples = new List<VisionRaySample>(128);
        private readonly List<Vector2> visibilityPolygon = new List<Vector2>(128);
        private readonly List<PortalVisionPolygon> portalPolygons = new List<PortalVisionPolygon>(8);

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
        public IReadOnlyList<PortalVisionPolygon> PortalPolygons => portalPolygons;
        public int SampleCount => raySamples.Count;
        public bool IsValid => (visibilityPolygon.Count >= 2 || portalPolygons.Count > 0) && viewDistance > 0f;

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
            portalPolygons.Clear();
        }

        internal bool AddSample(VisionRaySample sample, int maximumSamples)
        {
            if (raySamples.Count >= Mathf.Max(2, maximumSamples))
                return false;

            raySamples.Add(sample);
            visibilityPolygon.Add(sample.point);
            return true;
        }

        internal PortalVisionPolygon AddPortalPolygon(
            IVisionPortal portal,
            Vector2 portalHitPoint,
            Vector2 portalExitOrigin,
            IReadOnlyList<Vector2> points,
            int targetRoomId,
            VisionDetectionSource detectionSource)
        {
            PortalVisionPolygon polygon = new PortalVisionPolygon
            {
                portal = portal,
                portalHitPoint = portalHitPoint,
                portalExitOrigin = portalExitOrigin,
                targetRoomId = targetRoomId,
                detectionSource = detectionSource
            };
            polygon.SetPoints(points);
            if (polygon.IsValid)
                portalPolygons.Add(polygon);
            return polygon;
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

            for (int i = 0; i < portalPolygons.Count; i++)
            {
                PortalVisionPolygon portalPolygon = portalPolygons[i];
                sampleBounds.Encapsulate(new Vector3(
                    portalPolygon.portalExitOrigin.x,
                    portalPolygon.portalExitOrigin.y,
                    0f));
                for (int j = 0; j < portalPolygon.Polygon.Count; j++)
                {
                    Vector2 point = portalPolygon.Polygon[j];
                    sampleBounds.Encapsulate(new Vector3(point.x, point.y, 0f));
                }
            }

            bounds = sampleBounds;
        }

        public bool ContainsWorldPoint(Vector2 worldPoint)
        {
            if (!IsValid || !bounds.Contains(new Vector3(worldPoint.x, worldPoint.y, bounds.center.z)))
                return false;

            if (ContainsPolygonPoint(worldPoint, origin, visibilityPolygon, viewAngleDegrees))
                return true;

            for (int i = 0; i < portalPolygons.Count; i++)
            {
                PortalVisionPolygon portalPolygon = portalPolygons[i];
                if (ContainsPolygonPoint(worldPoint, portalPolygon.portalExitOrigin, portalPolygon.Polygon, 179f))
                    return true;
            }

            return false;
        }

        public bool TryGetDetectionSource(Vector2 worldPoint, out VisionDetectionSource source)
        {
            return TryGetDetectionInfo(worldPoint, out source, out _);
        }

        public bool TryGetDetectionInfo(
            Vector2 worldPoint,
            out VisionDetectionSource source,
            out PortalVisionPolygon portalPolygon)
        {
            source = VisionDetectionSource.None;
            portalPolygon = null;
            if (!IsValid || !bounds.Contains(new Vector3(worldPoint.x, worldPoint.y, bounds.center.z)))
                return false;

            if (ContainsPolygonPoint(worldPoint, origin, visibilityPolygon, viewAngleDegrees))
            {
                source = VisionDetectionSource.Direct;
                return true;
            }

            for (int i = 0; i < portalPolygons.Count; i++)
            {
                PortalVisionPolygon candidate = portalPolygons[i];
                if (ContainsPolygonPoint(worldPoint, candidate.portalExitOrigin, candidate.Polygon, 179f))
                {
                    source = candidate.detectionSource;
                    portalPolygon = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsPolygonPoint(
            Vector2 worldPoint,
            Vector2 polygonOrigin,
            IReadOnlyList<Vector2> polygon,
            float angleDegrees)
        {
            if (polygon == null || polygon.Count < 2)
                return false;

            bool includeOrigin = angleDegrees < 359.999f;
            int polygonVertexCount = polygon.Count + (includeOrigin ? 1 : 0);
            bool inside = false;
            int previous = polygonVertexCount - 1;
            for (int current = 0; current < polygonVertexCount; current++)
            {
                Vector2 a = GetPolygonVertex(current, includeOrigin, polygonOrigin, polygon);
                Vector2 b = GetPolygonVertex(previous, includeOrigin, polygonOrigin, polygon);
                bool crosses = (a.y > worldPoint.y) != (b.y > worldPoint.y);
                if (crosses &&
                    worldPoint.x <
                    (b.x - a.x) * (worldPoint.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;

                previous = current;
            }

            return inside;
        }

        private static Vector2 GetPolygonVertex(
            int index,
            bool includeOrigin,
            Vector2 polygonOrigin,
            IReadOnlyList<Vector2> polygon)
        {
            if (!includeOrigin)
                return polygon[index];

            return index == 0 ? polygonOrigin : polygon[index - 1];
        }
    }
}
