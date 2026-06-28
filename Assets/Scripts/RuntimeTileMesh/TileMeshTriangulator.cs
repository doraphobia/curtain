using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    public static class TileMeshTriangulator
    {
        public static bool TriangulateSimplePolygon(
            IList<Vector2> polygon,
            List<int> triangles,
            out string warning)
        {
            warning = null;
            triangles.Clear();

            if (polygon == null || polygon.Count < 3)
            {
                warning = "Polygon has fewer than 3 points.";
                return false;
            }

            List<int> indices = new List<int>(polygon.Count);
            for (int i = 0; i < polygon.Count; i++)
                indices.Add(i);

            if (PolygonUtility.IsClockwise(polygon))
                indices.Reverse();

            int guard = polygon.Count * polygon.Count;
            while (indices.Count > 3 && guard-- > 0)
            {
                bool clippedEar = false;
                for (int i = 0; i < indices.Count; i++)
                {
                    int previousIndex = indices[(i - 1 + indices.Count) % indices.Count];
                    int currentIndex = indices[i];
                    int nextIndex = indices[(i + 1) % indices.Count];

                    Vector2 previous = polygon[previousIndex];
                    Vector2 current = polygon[currentIndex];
                    Vector2 next = polygon[nextIndex];

                    if (!IsConvex(previous, current, next))
                        continue;

                    if (ContainsAnyPointInEar(polygon, indices, previousIndex, currentIndex, nextIndex))
                        continue;

                    triangles.Add(previousIndex);
                    triangles.Add(currentIndex);
                    triangles.Add(nextIndex);
                    indices.RemoveAt(i);
                    clippedEar = true;
                    break;
                }

                if (!clippedEar)
                    break;
            }

            if (indices.Count == 3)
            {
                triangles.Add(indices[0]);
                triangles.Add(indices[1]);
                triangles.Add(indices[2]);
                return true;
            }

            warning = "Ear clipping failed. The loop may be self-touching or not a simple polygon.";
            triangles.Clear();
            return false;
        }

        private static bool IsConvex(Vector2 previous, Vector2 current, Vector2 next)
        {
            return PolygonUtility.Cross(current - previous, next - current) > 0.000001f;
        }

        private static bool ContainsAnyPointInEar(
            IList<Vector2> polygon,
            List<int> indices,
            int previousIndex,
            int currentIndex,
            int nextIndex)
        {
            Vector2 previous = polygon[previousIndex];
            Vector2 current = polygon[currentIndex];
            Vector2 next = polygon[nextIndex];

            for (int i = 0; i < indices.Count; i++)
            {
                int index = indices[i];
                if (index == previousIndex || index == currentIndex || index == nextIndex)
                    continue;

                if (PolygonUtility.PointInTriangle(polygon[index], previous, current, next, 0.000001f))
                    return true;
            }

            return false;
        }
    }
}
