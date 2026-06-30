#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    public static class RuntimeTileMeshBuildDebug
    {
        public static bool Enabled = false;

        public static void LogBuild(
            string source,
            RuntimeTileMeshBuildResult result,
            Object context = null)
        {
            if (!Enabled || result == null)
                return;

            StringBuilder builder = new StringBuilder();
            builder.Append("[RuntimeTileMeshBuildDebug] ").Append(source);
            builder.Append(" | Components=").Append(result.components.Count);

            for (int i = 0; i < result.components.Count; i++)
            {
                RuntimeTileMeshComponentResult component = result.components[i];
                builder.Append("\n  Component ").Append(i);
                builder.Append(" | Tiles=").Append(component.tiles.Count);
                builder.Append(" | Success=").Append(component.success);

                if (component.meshData != null)
                {
                    builder.Append(" | Vertices=").Append(component.meshData.vertices.Count);
                    builder.Append(" | Triangles=").Append(component.meshData.triangles.Count / 3);
                    builder.Append(" | Loops=").Append(component.meshData.loops.Count);
                    builder.Append(" | HasHoles=").Append(component.meshData.hasHoles);
                }

                for (int warningIndex = 0; warningIndex < component.warnings.Count; warningIndex++)
                    builder.Append("\n    Warning: ").Append(component.warnings[warningIndex]);
            }

            for (int i = 0; i < result.warnings.Count; i++)
                builder.Append("\n  Global Warning: ").Append(result.warnings[i]);

            Debug.Log(builder.ToString(), context);
        }

        public static void LogComponentPipeline(
            string source,
            IList<Vector2Int> tiles,
            List<DirectedTileEdge> boundaryEdges,
            List<List<Vector2Int>> loops,
            RuntimeTileMeshComponentResult result,
            Object context = null)
        {
            if (!Enabled)
                return;

            StringBuilder builder = new StringBuilder();
            builder.Append("[RuntimeTileMeshBuildDebug] ").Append(source);
            builder.Append(" | Tiles=").Append(tiles != null ? tiles.Count : 0);
            builder.Append(" | BoundaryEdges=").Append(boundaryEdges != null ? boundaryEdges.Count : 0);
            builder.Append(" | Loops=").Append(loops != null ? loops.Count : 0);

            if (loops != null)
            {
                for (int i = 0; i < loops.Count; i++)
                    builder.Append("\n  Loop ").Append(i).Append(" points=").Append(loops[i].Count);
            }

            if (result != null)
            {
                builder.Append("\n  Success=").Append(result.success);
                for (int i = 0; i < result.warnings.Count; i++)
                    builder.Append("\n  Warning: ").Append(result.warnings[i]);
            }

            Debug.Log(builder.ToString(), context);
        }
    }
}
#endif
