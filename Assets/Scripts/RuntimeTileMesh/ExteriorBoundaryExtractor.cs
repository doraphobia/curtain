using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    /// <summary>
    /// Extracts geometric exterior edges from occupied grid cells. Shared with gizmo debug and
    /// the Game View boundary reveal renderer, but does not participate in interior wall logic.
    /// </summary>
    public static class ExteriorBoundaryExtractor
    {
        private static readonly Vector2Int[] NeighborOffsets =
        {
            Vector2Int.right,
            Vector2Int.left,
            Vector2Int.up,
            Vector2Int.down
        };

        public static void ExtractFromBlockCells(
            ICollection<Vector2Int> blockCells,
            Vector2 gridOrigin,
            float gridSize,
            List<ExteriorBoundarySegment> results)
        {
            if (blockCells == null || results == null)
                return;

            HashSet<Vector2Int> cellLookup = blockCells as HashSet<Vector2Int> ?? new HashSet<Vector2Int>(blockCells);
            if (cellLookup.Count == 0)
                return;

            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            foreach (Vector2Int cell in cellLookup)
            {
                for (int i = 0; i < NeighborOffsets.Length; i++)
                    TryAddExteriorEdge(cellLookup, cell, NeighborOffsets[i], gridOrigin, safeGridSize, results);
            }
        }

        public static void ExtractFromActiveBlocks(
            IEnumerable<RuntimeTileMeshDraggableBlock> blocks,
            Vector2 gridOrigin,
            float gridSize,
            List<ExteriorBoundarySegment> results)
        {
            if (blocks == null || results == null)
                return;

            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            foreach (RuntimeTileMeshDraggableBlock block in blocks)
            {
                if (block == null || !block.isActiveAndEnabled)
                    continue;

                HashSet<Vector2Int> blockCells = block.GetWorldCells(safeGridSize, gridOrigin);
                ExtractFromBlockCells(blockCells, gridOrigin, safeGridSize, results);
            }
        }

        private static void TryAddExteriorEdge(
            HashSet<Vector2Int> blockCells,
            Vector2Int cell,
            Vector2Int neighborOffset,
            Vector2 gridOrigin,
            float safeGridSize,
            List<ExteriorBoundarySegment> results)
        {
            if (blockCells.Contains(cell + neighborOffset))
                return;

            Vector2 start;
            Vector2 end;
            if (neighborOffset.x != 0)
            {
                int edgeX = neighborOffset.x > 0 ? cell.x + 1 : cell.x;
                float x = gridOrigin.x + edgeX * safeGridSize;
                float y = gridOrigin.y + cell.y * safeGridSize;
                start = new Vector2(x, y);
                end = new Vector2(x, y + safeGridSize);
            }
            else
            {
                int edgeY = neighborOffset.y > 0 ? cell.y + 1 : cell.y;
                float x = gridOrigin.x + cell.x * safeGridSize;
                float y = gridOrigin.y + edgeY * safeGridSize;
                start = new Vector2(x, y);
                end = new Vector2(x + safeGridSize, y);
            }

            results.Add(new ExteriorBoundarySegment(start, end, cell, neighborOffset));
        }
    }
}
