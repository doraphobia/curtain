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

            Dictionary<Vector2Int, List<DirectedTileEdge>> outgoing =
                new Dictionary<Vector2Int, List<DirectedTileEdge>>();
            HashSet<TileBoundaryEdge> remaining = new HashSet<TileBoundaryEdge>();

            for (int i = 0; i < boundaryEdges.Count; i++)
            {
                DirectedTileEdge edge = boundaryEdges[i];
                if (!outgoing.TryGetValue(edge.from, out List<DirectedTileEdge> list))
                {
                    list = new List<DirectedTileEdge>();
                    outgoing.Add(edge.from, list);
                }

                list.Add(edge);
                remaining.Add(new TileBoundaryEdge(edge.from, edge.to));
            }

            int guardLimit = boundaryEdges.Count + 8;
            while (remaining.Count > 0)
            {
                DirectedTileEdge startEdge = GetFirstRemainingEdge(remaining, boundaryEdges);
                Vector2Int start = startEdge.from;
                Vector2Int current = startEdge.to;
                List<Vector2Int> loop = new List<Vector2Int> { start, current };
                remaining.Remove(new TileBoundaryEdge(startEdge.from, startEdge.to));

                int guard = guardLimit;
                while (guard-- > 0 && current != start)
                {
                    if (!TryTakeNextEdge(current, outgoing, remaining, out DirectedTileEdge nextEdge))
                        break;

                    current = nextEdge.to;
                    loop.Add(current);
                }

                if (loop.Count > 1 && loop[loop.Count - 1] == loop[0])
                    loop.RemoveAt(loop.Count - 1);

                if (loop.Count >= 3)
                    loops.Add(loop);
            }

            return loops;
        }

        private static DirectedTileEdge GetFirstRemainingEdge(
            HashSet<TileBoundaryEdge> remaining,
            IList<DirectedTileEdge> edges)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                DirectedTileEdge edge = edges[i];
                if (remaining.Contains(new TileBoundaryEdge(edge.from, edge.to)))
                    return edge;
            }

            return default(DirectedTileEdge);
        }

        private static bool TryTakeNextEdge(
            Vector2Int from,
            Dictionary<Vector2Int, List<DirectedTileEdge>> outgoing,
            HashSet<TileBoundaryEdge> remaining,
            out DirectedTileEdge edge)
        {
            if (!outgoing.TryGetValue(from, out List<DirectedTileEdge> candidates))
            {
                edge = default(DirectedTileEdge);
                return false;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                DirectedTileEdge candidate = candidates[i];
                TileBoundaryEdge key = new TileBoundaryEdge(candidate.from, candidate.to);
                if (!remaining.Contains(key))
                    continue;

                remaining.Remove(key);
                edge = candidate;
                return true;
            }

            edge = default(DirectedTileEdge);
            return false;
        }
    }
}
