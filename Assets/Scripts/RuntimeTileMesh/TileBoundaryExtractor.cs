using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    public static class TileBoundaryExtractor
    {
        public static List<DirectedTileEdge> ExtractBoundaryEdges(IEnumerable<Vector2Int> tiles)
        {
            Dictionary<TileBoundaryEdge, DirectedTileEdge> exposedEdges =
                new Dictionary<TileBoundaryEdge, DirectedTileEdge>();

            if (tiles == null)
                return new List<DirectedTileEdge>();

            List<Vector2Int> sortedTiles = new List<Vector2Int>();
            foreach (Vector2Int tile in tiles)
                sortedTiles.Add(tile);

            sortedTiles.Sort(ComparePoints);

            for (int i = 0; i < sortedTiles.Count; i++)
            {
                Vector2Int tile = sortedTiles[i];
                Vector2Int bottomLeft = new Vector2Int(tile.x, tile.y);
                Vector2Int bottomRight = new Vector2Int(tile.x + 1, tile.y);
                Vector2Int topRight = new Vector2Int(tile.x + 1, tile.y + 1);
                Vector2Int topLeft = new Vector2Int(tile.x, tile.y + 1);

                AddOrCancelEdge(exposedEdges, bottomLeft, bottomRight);
                AddOrCancelEdge(exposedEdges, bottomRight, topRight);
                AddOrCancelEdge(exposedEdges, topRight, topLeft);
                AddOrCancelEdge(exposedEdges, topLeft, bottomLeft);
            }

            List<DirectedTileEdge> edges = new List<DirectedTileEdge>(exposedEdges.Values);
            edges.Sort(CompareDirectedEdges);
            return edges;
        }

        private static void AddOrCancelEdge(
            Dictionary<TileBoundaryEdge, DirectedTileEdge> edges,
            Vector2Int from,
            Vector2Int to)
        {
            TileBoundaryEdge key = new TileBoundaryEdge(from, to);
            if (edges.ContainsKey(key))
            {
                edges.Remove(key);
                return;
            }

            edges.Add(key, new DirectedTileEdge(from, to));
        }

        private static int CompareDirectedEdges(DirectedTileEdge a, DirectedTileEdge b)
        {
            int fromCompare = ComparePoints(a.from, b.from);
            if (fromCompare != 0)
                return fromCompare;

            return ComparePoints(a.to, b.to);
        }

        private static int ComparePoints(Vector2Int a, Vector2Int b)
        {
            int xCompare = a.x.CompareTo(b.x);
            return xCompare != 0 ? xCompare : a.y.CompareTo(b.y);
        }
    }
}
