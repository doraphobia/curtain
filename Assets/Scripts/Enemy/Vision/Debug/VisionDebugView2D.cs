using UnityEngine;

namespace DuoCurtain.Vision
{
    [DisallowMultipleComponent]
    public sealed class VisionDebugView2D : MonoBehaviour
    {
        public VisionSensor2D sensor;
        public bool showRays;
        public bool showPolygon = true;
        public bool showVertices;
        public bool showBounds;
        public bool showVisibilityWorldSegments;
        public bool showHitPoints;
        public Color rayColor = new Color(1f, 0.65f, 0.1f, 0.35f);
        public Color polygonColor = new Color(1f, 0.2f, 0.1f, 0.9f);
        public Color vertexColor = Color.cyan;
        public Color boundsColor = Color.magenta;
        public Color wallSegmentColor = Color.white;
        public Color closedDoorSegmentColor = new Color(0.1f, 0.35f, 1f, 0.95f);
        public Color openDoorSegmentColor = new Color(0.35f, 0.85f, 1f, 0.55f);
        public Color closedWindowSegmentColor = new Color(1f, 0.3f, 0.2f, 0.95f);
        public Color openWindowSegmentColor = new Color(0.2f, 1f, 0.45f, 0.65f);
        public Color unknownSegmentColor = Color.magenta;
        public Color hitPointColor = new Color(1f, 0.85f, 0.05f, 0.95f);
        [Min(0.001f)]
        public float vertexRadius = 0.035f;
        [Min(0.001f)]
        public float hitPointRadius = 0.025f;

        void OnDrawGizmos()
        {
            if (sensor == null)
                sensor = GetComponent<VisionSensor2D>();
            if (sensor == null)
                return;

            VisionSnapshot snapshot = sensor.LatestSnapshot;
            if (snapshot == null || !snapshot.IsValid)
                return;

            if (showRays)
            {
                Gizmos.color = rayColor;
                for (int i = 0; i < snapshot.RaySamples.Count; i++)
                    Gizmos.DrawLine(snapshot.origin, snapshot.RaySamples[i].point);
            }

            if (showPolygon)
            {
                Gizmos.color = polygonColor;
                Vector2 previous = snapshot.origin;
                for (int i = 0; i < snapshot.VisibilityPolygon.Count; i++)
                {
                    Vector2 point = snapshot.VisibilityPolygon[i];
                    Gizmos.DrawLine(previous, point);
                    previous = point;
                }

                Gizmos.DrawLine(previous, snapshot.origin);
            }

            if (showVertices)
            {
                Gizmos.color = vertexColor;
                for (int i = 0; i < snapshot.VisibilityPolygon.Count; i++)
                    Gizmos.DrawSphere(snapshot.VisibilityPolygon[i], vertexRadius);
            }

            if (showBounds)
            {
                Gizmos.color = boundsColor;
                Gizmos.DrawWireCube(snapshot.bounds.center, snapshot.bounds.size);
            }

            if (showHitPoints)
            {
                Gizmos.color = hitPointColor;
                for (int i = 0; i < snapshot.RaySamples.Count; i++)
                {
                    if (snapshot.RaySamples[i].hit)
                        Gizmos.DrawSphere(snapshot.RaySamples[i].point, hitPointRadius);
                }
            }

            if (showVisibilityWorldSegments)
                DrawVisibilityWorldSegments();
        }

        private void DrawVisibilityWorldSegments()
        {
            VisibilityWorld world = VisibilityWorld.Instance;
            if (world == null)
                return;

            world.RebuildIfDirty();
            for (int i = 0; i < world.Segments.Count; i++)
            {
                VisibilitySegment segment = world.Segments[i];
                Gizmos.color = GetSegmentColor(segment.type);
                Gizmos.DrawLine(segment.a, segment.b);
            }
        }

        private Color GetSegmentColor(VisibilitySegmentType type)
        {
            switch (type)
            {
                case VisibilitySegmentType.Wall:
                    return wallSegmentColor;
                case VisibilitySegmentType.ClosedDoor:
                    return closedDoorSegmentColor;
                case VisibilitySegmentType.OpenDoor:
                    return openDoorSegmentColor;
                case VisibilitySegmentType.ClosedWindow:
                    return closedWindowSegmentColor;
                case VisibilitySegmentType.OpenWindow:
                    return openWindowSegmentColor;
                default:
                    return unknownSegmentColor;
            }
        }
    }
}
