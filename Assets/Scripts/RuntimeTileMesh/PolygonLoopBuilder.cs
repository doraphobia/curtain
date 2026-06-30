using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    public static class PolygonLoopBuilder
    {
        public static List<List<Vector2Int>> BuildLoops(IList<DirectedTileEdge> boundaryEdges)
        {
            List<List<Vector2Int>> loops = new List<List<Vector2Int>>();
            if (boundaryEdges == null || boundaryEdges.Count == 0)
                return loops;

            Dictionary<Vector2Int, List<Vector2Int>> neighbors = BuildUndirectedNeighbors(boundaryEdges);
            List<TileBoundaryEdge> sortedEdges = BuildSortedUndirectedEdges(boundaryEdges);
            HashSet<TileBoundaryEdge> remaining = new HashSet<TileBoundaryEdge>(sortedEdges);

            for (int edgeIndex = 0; edgeIndex < sortedEdges.Count; edgeIndex++)
            {
                TileBoundaryEdge startEdge = sortedEdges[edgeIndex];
                if (!remaining.Remove(startEdge))
                    continue;

                Vector2Int start = startEdge.a;
                Vector2Int previous = start;
                Vector2Int current = startEdge.b;
                List<Vector2Int> loop = new List<Vector2Int> { start, current };

                int guard = boundaryEdges.Count + 8;
                while (guard-- > 0 && current != start)
                {
                    if (!TryTakeNextVertex(previous, current, neighbors, remaining, out Vector2Int next))
                        break;

                    remaining.Remove(new TileBoundaryEdge(current, next));
                    previous = current;
                    current = next;
                    loop.Add(current);
                }

                if (loop.Count > 1 && loop[loop.Count - 1] == loop[0])
                    loop.RemoveAt(loop.Count - 1);

                if (loop.Count >= 3 && current == start)
                    loops.Add(loop);
            }

            return loops;
        }

        private static Dictionary<Vector2Int, List<Vector2Int>> BuildUndirectedNeighbors(
            IList<DirectedTileEdge> boundaryEdges)
        {
            Dictionary<Vector2Int, List<Vector2Int>> neighbors = new Dictionary<Vector2Int, List<Vector2Int>>();
            for (int i = 0; i < boundaryEdges.Count; i++)
            {
                DirectedTileEdge edge = boundaryEdges[i];
                AddNeighbor(neighbors, edge.from, edge.to);
                AddNeighbor(neighbors, edge.to, edge.from);
            }

            foreach (KeyValuePair<Vector2Int, List<Vector2Int>> pair in neighbors)
                pair.Value.Sort(ComparePoints);

            return neighbors;
        }

        private static List<TileBoundaryEdge> BuildSortedUndirectedEdges(IList<DirectedTileEdge> boundaryEdges)
        {
            HashSet<TileBoundaryEdge> unique = new HashSet<TileBoundaryEdge>();
            for (int i = 0; i < boundaryEdges.Count; i++)
            {
                DirectedTileEdge edge = boundaryEdges[i];
                unique.Add(new TileBoundaryEdge(edge.from, edge.to));
            }

            List<TileBoundaryEdge> sorted = new List<TileBoundaryEdge>(unique);
            sorted.Sort((a, b) =>
            {
                int aCompare = ComparePoints(a.a, b.a);
                return aCompare != 0 ? aCompare : ComparePoints(a.b, b.b);
            });
            return sorted;
        }

        private static bool TryTakeNextVertex(
            Vector2Int previous,
            Vector2Int current,
            Dictionary<Vector2Int, List<Vector2Int>> neighbors,
            HashSet<TileBoundaryEdge> remaining,
            out Vector2Int next)
        {
            next = default(Vector2Int);
            if (!neighbors.TryGetValue(current, out List<Vector2Int> candidates) || candidates.Count == 0)
                return false;

            if (candidates.Count == 1)
            {
                next = candidates[0];
                return next != previous;
            }

            if (candidates.Count == 2)
            {
                next = candidates[0] == previous ? candidates[1] : candidates[0];
                return remaining.Contains(new TileBoundaryEdge(current, next));
            }

            Vector2 incoming = new Vector2(current.x - previous.x, current.y - previous.y);
            if (incoming.sqrMagnitude <= 0.000001f)
                incoming = Vector2.right;

            bool found = false;
            float bestTurn = float.NegativeInfinity;
            for (int i = 0; i < candidates.Count; i++)
            {
                Vector2Int candidate = candidates[i];
                if (candidate == previous)
                    continue;

                if (!remaining.Contains(new TileBoundaryEdge(current, candidate)))
                    continue;

                Vector2 outgoing = new Vector2(candidate.x - current.x, candidate.y - current.y);
                float turn = PolygonUtility.Cross(incoming, outgoing);
                if (found && turn <= bestTurn + 0.000001f)
                    continue;

                bestTurn = turn;
                next = candidate;
                found = true;
            }

            return found;
        }

        private static void AddNeighbor(
            Dictionary<Vector2Int, List<Vector2Int>> neighbors,
            Vector2Int from,
            Vector2Int to)
        {
            if (!neighbors.TryGetValue(from, out List<Vector2Int> list))
            {
                list = new List<Vector2Int>();
                neighbors.Add(from, list);
            }

            if (!list.Contains(to))
                list.Add(to);
        }

        private static int ComparePoints(Vector2Int a, Vector2Int b)
        {
            int xCompare = a.x.CompareTo(b.x);
            return xCompare != 0 ? xCompare : a.y.CompareTo(b.y);
        }
    }
}
