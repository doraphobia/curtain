using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    public enum RuntimeTileMeshUVMode
    {
        Bounds,
        ObjectSpace
    }

    public struct RuntimeTileMeshSettings
    {
        public Vector2 origin;
        public Vector2 tileSize;
        public RuntimeTileMeshUVMode uvMode;
        public Vector2 uvTilingScale;
        public Vector2 uvOffset;
        public bool markDynamic;
        public bool removeCollinearPoints;
        public float collinearEpsilon;

        public static RuntimeTileMeshSettings Default
        {
            get
            {
                return new RuntimeTileMeshSettings
                {
                    origin = Vector2.zero,
                    tileSize = Vector2.one,
                    uvMode = RuntimeTileMeshUVMode.Bounds,
                    uvTilingScale = Vector2.one,
                    uvOffset = Vector2.zero,
                    markDynamic = false,
                    removeCollinearPoints = true,
                    collinearEpsilon = 0.0001f
                };
            }
        }

        public Vector2 SafeTileSize
        {
            get
            {
                return new Vector2(
                    Mathf.Abs(tileSize.x) <= 0.0001f ? 1f : tileSize.x,
                    Mathf.Abs(tileSize.y) <= 0.0001f ? 1f : tileSize.y
                );
            }
        }
    }

    public struct DirectedTileEdge
    {
        public readonly Vector2Int from;
        public readonly Vector2Int to;

        public DirectedTileEdge(Vector2Int from, Vector2Int to)
        {
            this.from = from;
            this.to = to;
        }
    }

    public struct TileBoundaryEdge : System.IEquatable<TileBoundaryEdge>
    {
        public readonly Vector2Int a;
        public readonly Vector2Int b;

        public TileBoundaryEdge(Vector2Int first, Vector2Int second)
        {
            if (Compare(first, second) <= 0)
            {
                a = first;
                b = second;
            }
            else
            {
                a = second;
                b = first;
            }
        }

        public bool Equals(TileBoundaryEdge other)
        {
            return a == other.a && b == other.b;
        }

        public override bool Equals(object obj)
        {
            return obj is TileBoundaryEdge other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (a.GetHashCode() * 397) ^ b.GetHashCode();
            }
        }

        private static int Compare(Vector2Int first, Vector2Int second)
        {
            int xCompare = first.x.CompareTo(second.x);
            return xCompare != 0 ? xCompare : first.y.CompareTo(second.y);
        }
    }

    public sealed class RuntimeTileMeshData
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<int> triangles = new List<int>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<List<Vector2>> loops = new List<List<Vector2>>();
        public readonly List<List<Vector2>> colliderPaths = new List<List<Vector2>>();
        public Bounds bounds;
        public bool hasHoles;

        public Mesh ToMesh(string meshName, bool markDynamic)
        {
            Mesh mesh = new Mesh();
            mesh.name = meshName;

            if (markDynamic)
                mesh.MarkDynamic();

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    public sealed class RuntimeTileMeshComponentResult
    {
        public readonly List<Vector2Int> tiles = new List<Vector2Int>();
        public readonly List<string> warnings = new List<string>();
        public RuntimeTileMeshData meshData;
        public bool success;
    }

    public sealed class RuntimeTileMeshBuildResult
    {
        public readonly List<RuntimeTileMeshComponentResult> components =
            new List<RuntimeTileMeshComponentResult>();
        public readonly List<string> warnings = new List<string>();

        public bool HasWarnings
        {
            get { return warnings.Count > 0; }
        }
    }
}
