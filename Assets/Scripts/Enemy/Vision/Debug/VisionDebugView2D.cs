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
        public Color rayColor = new Color(1f, 0.65f, 0.1f, 0.35f);
        public Color polygonColor = new Color(1f, 0.2f, 0.1f, 0.9f);
        public Color vertexColor = Color.cyan;
        public Color boundsColor = Color.magenta;
        [Min(0.001f)]
        public float vertexRadius = 0.035f;

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
        }
    }
}
