using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    public static class TileMeshUVGenerator
    {
        public static void GenerateUVs(
            IList<Vector3> vertices,
            Bounds bounds,
            RuntimeTileMeshSettings settings,
            List<Vector2> uvs)
        {
            uvs.Clear();
            Vector2 tiling = settings.uvTilingScale;
            if (Mathf.Abs(tiling.x) <= 0.0001f)
                tiling.x = 1f;
            if (Mathf.Abs(tiling.y) <= 0.0001f)
                tiling.y = 1f;

            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 vertex = vertices[i];
                Vector2 uv;
                if (settings.uvMode == RuntimeTileMeshUVMode.Bounds)
                {
                    float width = Mathf.Max(0.0001f, bounds.size.x);
                    float height = Mathf.Max(0.0001f, bounds.size.y);
                    uv = new Vector2(
                        (vertex.x - bounds.min.x) / width,
                        (vertex.y - bounds.min.y) / height
                    );
                    uv = Vector2.Scale(uv, tiling) + settings.uvOffset;
                }
                else
                {
                    uv = Vector2.Scale(new Vector2(vertex.x, vertex.y), tiling) + settings.uvOffset;
                }

                uvs.Add(uv);
            }
        }
    }
}
