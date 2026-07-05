using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    /// <summary>
    /// One exposed edge of a merged floor / block shape. This is visual-only data for the
    /// exterior boundary indicator and is intentionally separate from interior wall segments.
    /// </summary>
    public struct ExteriorBoundarySegment
    {
        public Vector2 start;
        public Vector2 end;
        public Vector2Int gridCell;
        public Vector2Int neighborOffset;

        public ExteriorBoundarySegment(
            Vector2 start,
            Vector2 end,
            Vector2Int gridCell,
            Vector2Int neighborOffset)
        {
            this.start = start;
            this.end = end;
            this.gridCell = gridCell;
            this.neighborOffset = neighborOffset;
        }

        public Vector2 Midpoint => (start + end) * 0.5f;
    }
}
