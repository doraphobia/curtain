using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    public static class PolygonUtility
    {
        public static float SignedArea(IList<Vector2> loop)
        {
            if (loop == null || loop.Count < 3)
                return 0f;

            float area = 0f;
            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 a = loop[i];
                Vector2 b = loop[(i + 1) % loop.Count];
                area += a.x * b.y - b.x * a.y;
            }

            return area * 0.5f;
        }

        public static bool IsClockwise(IList<Vector2> loop)
        {
            return SignedArea(loop) < 0f;
        }

        public static List<Vector2> RemoveCollinearPoints(IList<Vector2> loop, float epsilon)
        {
            List<Vector2> cleaned = RemoveConsecutiveDuplicates(loop, epsilon);
            if (cleaned.Count <= 3)
                return cleaned;

            bool removed = true;
            int guard = cleaned.Count * 2;
            while (removed && guard-- > 0 && cleaned.Count > 3)
            {
                removed = false;
                for (int i = cleaned.Count - 1; i >= 0; i--)
                {
                    Vector2 previous = cleaned[(i - 1 + cleaned.Count) % cleaned.Count];
                    Vector2 current = cleaned[i];
                    Vector2 next = cleaned[(i + 1) % cleaned.Count];

                    Vector2 a = current - previous;
                    Vector2 b = next - current;
                    float cross = Mathf.Abs(Cross(a, b));
                    if (cross > epsilon)
                        continue;

                    cleaned.RemoveAt(i);
                    removed = true;
                }
            }

            return cleaned;
        }

        public static void EnsureOrientation(List<Vector2> loop, bool clockwise)
        {
            if (loop == null || loop.Count < 3)
                return;

            if (IsClockwise(loop) != clockwise)
                loop.Reverse();
        }

        public static Bounds CalculateBounds(IList<Vector2> points)
        {
            if (points == null || points.Count == 0)
                return new Bounds(Vector3.zero, Vector3.zero);

            Vector3 first = new Vector3(points[0].x, points[0].y, 0f);
            Bounds bounds = new Bounds(first, Vector3.zero);
            for (int i = 1; i < points.Count; i++)
                bounds.Encapsulate(new Vector3(points[i].x, points[i].y, 0f));

            return bounds;
        }

        public static bool ContainsPoint(IList<Vector2> polygon, Vector2 point)
        {
            bool inside = false;
            if (polygon == null || polygon.Count < 3)
                return false;

            int j = polygon.Count - 1;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];

                bool intersects = (a.y > point.y) != (b.y > point.y) &&
                                  point.x < (b.x - a.x) * (point.y - a.y) /
                                  Mathf.Max(0.000001f, b.y - a.y) + a.x;
                if (intersects)
                    inside = !inside;

                j = i;
            }

            return inside;
        }

        public static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c, float epsilon)
        {
            float area = Cross(b - a, c - a);
            float s = Cross(point - a, c - a) / area;
            float t = Cross(b - a, point - a) / area;
            float u = 1f - s - t;

            return s >= -epsilon && t >= -epsilon && u >= -epsilon;
        }

        public static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static List<Vector2> RemoveConsecutiveDuplicates(IList<Vector2> loop, float epsilon)
        {
            List<Vector2> cleaned = new List<Vector2>();
            if (loop == null)
                return cleaned;

            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 point = loop[i];
                if (cleaned.Count > 0 && Vector2.Distance(cleaned[cleaned.Count - 1], point) <= epsilon)
                    continue;

                cleaned.Add(point);
            }

            if (cleaned.Count > 1 && Vector2.Distance(cleaned[0], cleaned[cleaned.Count - 1]) <= epsilon)
                cleaned.RemoveAt(cleaned.Count - 1);

            return cleaned;
        }
    }
}
