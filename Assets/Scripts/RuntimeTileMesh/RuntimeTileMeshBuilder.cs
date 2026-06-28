using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    public static class RuntimeTileMeshBuilder
    {
        public static RuntimeTileMeshBuildResult Build(
            IEnumerable<Vector2Int> tiles,
            RuntimeTileMeshSettings settings)
        {
            RuntimeTileMeshBuildResult result = new RuntimeTileMeshBuildResult();
            List<List<Vector2Int>> components = TileConnectivity.SplitIntoConnectedComponents(tiles);

            for (int i = 0; i < components.Count; i++)
            {
                RuntimeTileMeshComponentResult componentResult = BuildComponent(components[i], settings);
                result.components.Add(componentResult);

                for (int warningIndex = 0; warningIndex < componentResult.warnings.Count; warningIndex++)
                    result.warnings.Add("Component " + i + ": " + componentResult.warnings[warningIndex]);
            }

            return result;
        }

        public static RuntimeTileMeshComponentResult BuildComponent(
            IList<Vector2Int> componentTiles,
            RuntimeTileMeshSettings settings)
        {
            RuntimeTileMeshComponentResult result = new RuntimeTileMeshComponentResult();
            if (componentTiles == null || componentTiles.Count == 0)
            {
                result.warnings.Add("No tiles were supplied.");
                return result;
            }

            result.tiles.AddRange(componentTiles);
            List<DirectedTileEdge> boundaryEdges = TileBoundaryExtractor.ExtractBoundaryEdges(componentTiles);
            List<List<Vector2Int>> rawLoops = PolygonLoopBuilder.BuildLoops(boundaryEdges);
            List<List<Vector2>> loops = ConvertLoops(rawLoops, settings);

            for (int i = loops.Count - 1; i >= 0; i--)
            {
                if (settings.removeCollinearPoints)
                    loops[i] = PolygonUtility.RemoveCollinearPoints(loops[i], Mathf.Max(0.000001f, settings.collinearEpsilon));

                if (loops[i].Count < 3)
                    loops.RemoveAt(i);
            }

            if (loops.Count == 0)
            {
                result.warnings.Add("No closed boundary loop could be reconstructed.");
                return result;
            }

            int outerIndex = FindLargestAbsAreaLoop(loops);
            List<Vector2> outer = loops[outerIndex];
            PolygonUtility.EnsureOrientation(outer, false);

            List<List<Vector2>> holes = new List<List<Vector2>>();
            for (int i = 0; i < loops.Count; i++)
            {
                if (i == outerIndex)
                    continue;

                if (PolygonUtility.ContainsPoint(outer, loops[i][0]))
                {
                    PolygonUtility.EnsureOrientation(loops[i], true);
                    holes.Add(loops[i]);
                }
                else
                {
                    result.warnings.Add("Extra boundary loop was not inside the outer loop and was ignored.");
                }
            }

            RuntimeTileMeshData meshData = new RuntimeTileMeshData();
            meshData.hasHoles = holes.Count > 0;
            meshData.loops.Add(outer);
            meshData.colliderPaths.Add(outer);
            for (int i = 0; i < holes.Count; i++)
            {
                meshData.loops.Add(holes[i]);
                meshData.colliderPaths.Add(holes[i]);
            }

            if (holes.Count > 0)
            {
                result.meshData = meshData;
                result.warnings.Add("Hole loops were detected. Fallback ear clipping does not triangulate holes yet; install LibTessDotNet or keep this component unrendered until the tessellator is upgraded.");
                return result;
            }

            for (int i = 0; i < outer.Count; i++)
                meshData.vertices.Add(new Vector3(outer[i].x, outer[i].y, 0f));

            if (!TileMeshTriangulator.TriangulateSimplePolygon(outer, meshData.triangles, out string triangulationWarning))
            {
                result.meshData = meshData;
                result.warnings.Add(triangulationWarning);
                return result;
            }

            ReverseTriangleWinding(meshData.triangles);
            meshData.bounds = PolygonUtility.CalculateBounds(outer);
            TileMeshUVGenerator.GenerateUVs(meshData.vertices, meshData.bounds, settings, meshData.uvs);
            result.meshData = meshData;
            result.success = true;
            return result;
        }

        private static List<List<Vector2>> ConvertLoops(
            List<List<Vector2Int>> rawLoops,
            RuntimeTileMeshSettings settings)
        {
            List<List<Vector2>> loops = new List<List<Vector2>>();
            Vector2 tileSize = settings.SafeTileSize;

            for (int loopIndex = 0; loopIndex < rawLoops.Count; loopIndex++)
            {
                List<Vector2Int> rawLoop = rawLoops[loopIndex];
                List<Vector2> loop = new List<Vector2>(rawLoop.Count);
                for (int i = 0; i < rawLoop.Count; i++)
                {
                    Vector2Int point = rawLoop[i];
                    loop.Add(new Vector2(
                        settings.origin.x + point.x * tileSize.x,
                        settings.origin.y + point.y * tileSize.y
                    ));
                }

                loops.Add(loop);
            }

            return loops;
        }

        private static int FindLargestAbsAreaLoop(List<List<Vector2>> loops)
        {
            int bestIndex = 0;
            float bestArea = 0f;
            for (int i = 0; i < loops.Count; i++)
            {
                float area = Mathf.Abs(PolygonUtility.SignedArea(loops[i]));
                if (area <= bestArea)
                    continue;

                bestArea = area;
                bestIndex = i;
            }

            return bestIndex;
        }

        private static void ReverseTriangleWinding(List<int> triangles)
        {
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                int first = triangles[i];
                triangles[i] = triangles[i + 2];
                triangles[i + 2] = first;
            }
        }
    }
}
