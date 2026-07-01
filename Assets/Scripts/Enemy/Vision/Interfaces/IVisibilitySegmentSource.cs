using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.Vision
{
    public interface IVisibilitySegmentSource
    {
        void CollectVisibilitySegments(List<VisibilitySegment> results);
    }

    public enum OpeningGeometryType
    {
        Segment,
        Rectangle,
        Polygon,
        Spline
    }

    public enum OpeningProjectionRule
    {
        ContinueIncomingRay
    }

    public struct OpeningGeometry
    {
        public OpeningGeometryType type;
        public Vector2 segmentA;
        public Vector2 segmentB;
        public Vector2 normal;

        public static OpeningGeometry Segment(Vector2 a, Vector2 b, Vector2 openingNormal)
        {
            return new OpeningGeometry
            {
                type = OpeningGeometryType.Segment,
                segmentA = a,
                segmentB = b,
                normal = openingNormal.sqrMagnitude > 0.000001f ? openingNormal.normalized : Vector2.right
            };
        }
    }

    public struct VisibilityOpening
    {
        public OpeningGeometry geometry;
        public bool allowsVision;
        public OpeningProjectionRule projectionRule;
        public GameObject sourceObject;
        public Component sourceComponent;
        public int sourceId;

        public VisibilityOpening(
            OpeningGeometry openingGeometry,
            bool visionAllowed,
            OpeningProjectionRule rule,
            GameObject ownerObject,
            Component ownerComponent)
        {
            geometry = openingGeometry;
            allowsVision = visionAllowed;
            projectionRule = rule;
            sourceObject = ownerObject;
            sourceComponent = ownerComponent;
            sourceId = ownerObject != null ? ownerObject.GetInstanceID() : 0;
        }
    }

    public interface IVisibilityOpeningSource
    {
        void CollectVisibilityOpenings(List<VisibilityOpening> results);
    }
}
