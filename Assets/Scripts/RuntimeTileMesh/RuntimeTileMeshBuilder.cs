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
            RuntimeTileMeshData meshData = new RuntimeTileMeshData();

            if (!TileGridMeshGenerator.TryBuild(componentTiles, settings, meshData, out string gridWarning))
            {
                if (!string.IsNullOrEmpty(gridWarning))
                    result.warnings.Add(gridWarning);
                result.meshData = meshData;
                return result;
            }

            if (!TileGridMeshGenerator.CoversAllTiles(componentTiles, settings, meshData))
            {
                result.warnings.Add("Grid mesh did not cover every occupied tile center.");
                result.meshData = meshData;
                return result;
            }

            result.meshData = meshData;
            result.success = true;

#if UNITY_EDITOR
            List<DirectedTileEdge> boundaryEdges = TileBoundaryExtractor.ExtractBoundaryEdges(componentTiles);
            List<List<Vector2Int>> rawLoops = PolygonLoopBuilder.BuildLoops(boundaryEdges);
            RuntimeTileMeshBuildDebug.LogComponentPipeline(
                "BuildComponent",
                componentTiles,
                boundaryEdges,
                rawLoops,
                result);
#endif

            return result;
        }
    }
}
