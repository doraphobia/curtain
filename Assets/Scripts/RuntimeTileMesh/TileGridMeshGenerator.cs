using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    /// <summary>
    /// Builds render mesh directly from occupied grid cells. Each logical tile becomes
    /// one axis-aligned quad, so merged 1x1 (or any fixed tileSize) occupancy never loses structure.
    /// </summary>
    public static class TileGridMeshGenerator
    {
        public static bool TryBuild(
            IList<Vector2Int> tiles,
            RuntimeTileMeshSettings settings,
            RuntimeTileMeshData meshData,
            out string warning)
        {
            warning = null;
            if (meshData == null)
            {
                warning = "Mesh data container was null.";
                return false;
            }

            meshData.vertices.Clear();
            meshData.triangles.Clear();
            meshData.uvs.Clear();
            meshData.loops.Clear();
            meshData.colliderPaths.Clear();
            meshData.hasHoles = false;

            if (tiles == null || tiles.Count == 0)
            {
                warning = "No tiles were supplied.";
                return false;
            }

            HashSet<Vector2Int> tileSet = TileOccupancy.ToSet(tiles);
            List<Vector2Int> sortedTiles = new List<Vector2Int>(tileSet);
            sortedTiles.Sort(ComparePoints);

            Vector2 tileSize = settings.SafeTileSize;
            float originX = settings.origin.x;
            float originY = settings.origin.y;

            for (int i = 0; i < sortedTiles.Count; i++)
            {
                Vector2Int tile = sortedTiles[i];
                float x0 = originX + tile.x * tileSize.x;
                float y0 = originY + tile.y * tileSize.y;
                float x1 = x0 + tileSize.x;
                float y1 = y0 + tileSize.y;

                int baseIndex = meshData.vertices.Count;
                meshData.vertices.Add(new Vector3(x0, y0, 0f));
                meshData.vertices.Add(new Vector3(x1, y0, 0f));
                meshData.vertices.Add(new Vector3(x1, y1, 0f));
                meshData.vertices.Add(new Vector3(x0, y1, 0f));

                meshData.triangles.Add(baseIndex);
                meshData.triangles.Add(baseIndex + 2);
                meshData.triangles.Add(baseIndex + 1);
                meshData.triangles.Add(baseIndex);
                meshData.triangles.Add(baseIndex + 3);
                meshData.triangles.Add(baseIndex + 2);
            }

            if (meshData.vertices.Count == 0)
            {
                warning = "Grid mesh generation produced no vertices.";
                return false;
            }

            meshData.bounds = CalculateTileBounds(sortedTiles, settings);
            TileMeshUVGenerator.GenerateUVs(meshData.vertices, meshData.bounds, settings, meshData.uvs);
            PopulateColliderPaths(tileSet, settings, meshData);
            return true;
        }

        public static List<Vector2Int> FindUncoveredTiles(
            IList<Vector2Int> tiles,
            RuntimeTileMeshSettings settings,
            RuntimeTileMeshData meshData)
        {
            List<Vector2Int> uncovered = new List<Vector2Int>();
            if (tiles == null || meshData == null)
                return uncovered;

            HashSet<Vector2Int> tileSet = TileOccupancy.ToSet(tiles);
            Vector2 tileSize = settings.SafeTileSize;
            foreach (Vector2Int tile in tileSet)
            {
                Vector2 center = new Vector2(
                    settings.origin.x + (tile.x + 0.5f) * tileSize.x,
                    settings.origin.y + (tile.y + 0.5f) * tileSize.y);
                if (!PointInsideMesh(center, meshData))
                    uncovered.Add(tile);
            }

            uncovered.Sort(ComparePoints);
            return uncovered;
        }

        public static bool CoversAllTiles(
            IList<Vector2Int> tiles,
            RuntimeTileMeshSettings settings,
            RuntimeTileMeshData meshData)
        {
            if (tiles == null || meshData == null || meshData.triangles.Count < 3)
                return false;

            HashSet<Vector2Int> tileSet = TileOccupancy.ToSet(tiles);
            Vector2 tileSize = settings.SafeTileSize;
            foreach (Vector2Int tile in tileSet)
            {
                Vector2 center = new Vector2(
                    settings.origin.x + (tile.x + 0.5f) * tileSize.x,
                    settings.origin.y + (tile.y + 0.5f) * tileSize.y);
                if (!PointInsideMesh(center, meshData))
                    return false;
            }

            return true;
        }

        private static void PopulateColliderPaths(
            HashSet<Vector2Int> tileSet,
            RuntimeTileMeshSettings settings,
            RuntimeTileMeshData meshData)
        {
            List<DirectedTileEdge> boundaryEdges = TileBoundaryExtractor.ExtractBoundaryEdges(tileSet);
            List<List<Vector2Int>> rawLoops = PolygonLoopBuilder.BuildLoops(boundaryEdges);
            if (rawLoops.Count == 0)
                return;

            Vector2 tileSize = settings.SafeTileSize;
            for (int i = 0; i < rawLoops.Count; i++)
            {
                List<Vector2Int> rawLoop = rawLoops[i];
                List<Vector2> loop = new List<Vector2>(rawLoop.Count);
                for (int pointIndex = 0; pointIndex < rawLoop.Count; pointIndex++)
                {
                    Vector2Int point = rawLoop[pointIndex];
                    loop.Add(new Vector2(
                        settings.origin.x + point.x * tileSize.x,
                        settings.origin.y + point.y * tileSize.y));
                }

                if (loop.Count >= 3)
                {
                    meshData.loops.Add(loop);
                    meshData.colliderPaths.Add(loop);
                }
            }

            meshData.hasHoles = meshData.loops.Count > 1;
        }

        private static Bounds CalculateTileBounds(IList<Vector2Int> sortedTiles, RuntimeTileMeshSettings settings)
        {
            Vector2Int min = sortedTiles[0];
            Vector2Int max = sortedTiles[sortedTiles.Count - 1];
            for (int i = 1; i < sortedTiles.Count; i++)
            {
                Vector2Int tile = sortedTiles[i];
                min = Vector2Int.Min(min, tile);
                max = Vector2Int.Max(max, tile);
            }

            Vector2 tileSize = settings.SafeTileSize;
            Vector3 minimum = new Vector3(
                settings.origin.x + min.x * tileSize.x,
                settings.origin.y + min.y * tileSize.y,
                0f);
            Vector3 maximum = new Vector3(
                settings.origin.x + (max.x + 1) * tileSize.x,
                settings.origin.y + (max.y + 1) * tileSize.y,
                0f);
            Vector3 center = (minimum + maximum) * 0.5f;
            Vector3 size = maximum - minimum;
            return new Bounds(center, size);
        }

        private static bool PointInsideMesh(Vector2 point, RuntimeTileMeshData meshData)
        {
            for (int i = 0; i < meshData.triangles.Count; i += 3)
            {
                Vector3 a = meshData.vertices[meshData.triangles[i]];
                Vector3 b = meshData.vertices[meshData.triangles[i + 1]];
                Vector3 c = meshData.vertices[meshData.triangles[i + 2]];
                if (PolygonUtility.PointInTriangle(
                        point,
                        new Vector2(a.x, a.y),
                        new Vector2(b.x, b.y),
                        new Vector2(c.x, c.y),
                        0.000001f))
                {
                    return true;
                }
            }

            return false;
        }

        private static int ComparePoints(Vector2Int a, Vector2Int b)
        {
            int yCompare = a.y.CompareTo(b.y);
            return yCompare != 0 ? yCompare : a.x.CompareTo(b.x);
        }
    }
}
