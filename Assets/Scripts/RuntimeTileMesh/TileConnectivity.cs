using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    public static class TileConnectivity
    {
        private static readonly Vector2Int[] FourNeighbors =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1)
        };

        public static List<List<Vector2Int>> SplitIntoConnectedComponents(IEnumerable<Vector2Int> tiles)
        {
            HashSet<Vector2Int> remaining = TileOccupancy.ToSet(tiles);
            List<List<Vector2Int>> components = new List<List<Vector2Int>>();
            Queue<Vector2Int> queue = new Queue<Vector2Int>();

            while (remaining.Count > 0)
            {
                Vector2Int start = FindLexicographicMinimum(remaining);
                List<Vector2Int> component = new List<Vector2Int>();
                remaining.Remove(start);
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    Vector2Int tile = queue.Dequeue();
                    component.Add(tile);

                    for (int i = 0; i < FourNeighbors.Length; i++)
                    {
                        Vector2Int neighbor = tile + FourNeighbors[i];
                        if (!remaining.Remove(neighbor))
                            continue;

                        queue.Enqueue(neighbor);
                    }
                }

                component.Sort(ComparePoints);
                components.Add(component);
            }

            components.Sort(CompareComponent);
            return components;
        }

        private static Vector2Int FindLexicographicMinimum(HashSet<Vector2Int> remaining)
        {
            Vector2Int best = default(Vector2Int);
            bool hasBest = false;

            foreach (Vector2Int tile in remaining)
            {
                if (!hasBest || ComparePoints(tile, best) < 0)
                {
                    best = tile;
                    hasBest = true;
                }
            }

            return best;
        }

        private static int CompareComponent(List<Vector2Int> a, List<Vector2Int> b)
        {
            if (a == null || a.Count == 0)
                return b == null || b.Count == 0 ? 0 : 1;

            if (b == null || b.Count == 0)
                return -1;

            return ComparePoints(a[0], b[0]);
        }

        private static int ComparePoints(Vector2Int a, Vector2Int b)
        {
            int xCompare = a.x.CompareTo(b.x);
            return xCompare != 0 ? xCompare : a.y.CompareTo(b.y);
        }
    }
}
