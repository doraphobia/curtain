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

            foreach (Vector2Int tile in tiles)
            {
                Vector2Int bottomLeft = new Vector2Int(tile.x, tile.y);
                Vector2Int bottomRight = new Vector2Int(tile.x + 1, tile.y);
                Vector2Int topRight = new Vector2Int(tile.x + 1, tile.y + 1);
                Vector2Int topLeft = new Vector2Int(tile.x, tile.y + 1);

                AddOrCancelEdge(exposedEdges, bottomLeft, bottomRight);
                AddOrCancelEdge(exposedEdges, bottomRight, topRight);
                AddOrCancelEdge(exposedEdges, topRight, topLeft);
                AddOrCancelEdge(exposedEdges, topLeft, bottomLeft);
            }

            return new List<DirectedTileEdge>(exposedEdges.Values);
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
    }
}
