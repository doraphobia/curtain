using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DuoCurtain.RuntimeTileMesh
{
    [DisallowMultipleComponent]
    public class RuntimeTileMeshView : MonoBehaviour
    {
        private const string GeneratedRootName = "__Runtime Tile Mesh Components";

        [Header("Input")]
        public List<Vector2Int> tiles = new List<Vector2Int> { Vector2Int.zero };
        public bool rebuildOnStart = true;

        [Header("Output")]
        public Material material;
        public bool buildPolygonCollider2D = false;
        public bool polygonColliderIsTrigger = true;
        public int sortingOrder = 0;

        [Header("Geometry")]
        public Vector2 origin = Vector2.zero;
        public Vector2 tileSize = Vector2.one;
        public bool markDynamic = false;
        public bool removeCollinearPoints = true;
        [Min(0.000001f)]
        public float collinearEpsilon = 0.0001f;

        [Header("UV")]
        public RuntimeTileMeshUVMode uvMode = RuntimeTileMeshUVMode.Bounds;
        public Vector2 uvTilingScale = Vector2.one;
        public Vector2 uvOffset = Vector2.zero;

        [Header("Debug")]
        public bool drawDebugTiles = true;
        public bool drawDebugBoundaryEdges = true;
        public bool drawDebugLoopPoints = true;
        public Color debugTileColor = new Color(0.2f, 0.8f, 1f, 0.25f);
        public Color debugBoundaryColor = new Color(1f, 0.82f, 0.1f, 1f);
        public Color debugLoopPointColor = new Color(0.3f, 1f, 0.35f, 1f);

        private RuntimeTileMeshBuildResult lastBuildResult;
        private Material runtimeFallbackMaterial;

        public event Action<RuntimeTileMeshView> Rebuilt;

        void Start()
        {
            if (rebuildOnStart)
                Rebuild();
        }

        void OnDestroy()
        {
            DestroyGeneratedRoot();
            if (runtimeFallbackMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(runtimeFallbackMaterial);
                else
                    DestroyImmediate(runtimeFallbackMaterial);
            }
        }

        [ContextMenu("Rebuild Runtime Tile Mesh")]
        public void Rebuild()
        {
            RebuildFromTiles(tiles);
        }

        public RuntimeTileMeshBuildResult RebuildFromTiles(IEnumerable<Vector2Int> inputTiles)
        {
            DestroyGeneratedRoot();
            RuntimeTileMeshSettings settings = CreateSettings();
            lastBuildResult = RuntimeTileMeshBuilder.Build(inputTiles, settings);

            Transform root = CreateGeneratedRoot();
            for (int i = 0; i < lastBuildResult.components.Count; i++)
                CreateComponentObject(root, lastBuildResult.components[i], i, settings);

            if (lastBuildResult.HasWarnings)
            {
                for (int i = 0; i < lastBuildResult.warnings.Count; i++)
                    Debug.LogWarning("[RuntimeTileMeshView] " + lastBuildResult.warnings[i], this);
            }

            Rebuilt?.Invoke(this);
            return lastBuildResult;
        }

        public void CollectGeneratedRenderers(List<Renderer> results)
        {
            if (results == null)
                return;

            results.Clear();
            Transform root = transform.Find(GeneratedRootName);
            if (root == null)
                return;

            root.GetComponentsInChildren(true, results);
        }

        public RuntimeTileMeshSettings CreateSettings()
        {
            RuntimeTileMeshSettings settings = RuntimeTileMeshSettings.Default;
            settings.origin = origin;
            settings.tileSize = tileSize;
            settings.uvMode = uvMode;
            settings.uvTilingScale = uvTilingScale;
            settings.uvOffset = uvOffset;
            settings.markDynamic = markDynamic;
            settings.removeCollinearPoints = removeCollinearPoints;
            settings.collinearEpsilon = collinearEpsilon;
            return settings;
        }

        private Transform CreateGeneratedRoot()
        {
            GameObject rootObject = new GameObject(GeneratedRootName);
            Transform root = rootObject.transform;
            root.SetParent(transform, false);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            return root;
        }

        private void CreateComponentObject(
            Transform root,
            RuntimeTileMeshComponentResult component,
            int index,
            RuntimeTileMeshSettings settings)
        {
            if (component == null || !component.success || component.meshData == null)
                return;

            GameObject componentObject = new GameObject("Runtime Tile Mesh Component " + index);
            Transform componentTransform = componentObject.transform;
            componentTransform.SetParent(root, false);
            componentTransform.localPosition = Vector3.zero;
            componentTransform.localRotation = Quaternion.identity;
            componentTransform.localScale = Vector3.one;

            MeshFilter filter = componentObject.AddComponent<MeshFilter>();
            filter.sharedMesh = component.meshData.ToMesh(componentObject.name + " Mesh", settings.markDynamic);

            MeshRenderer renderer = componentObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material != null ? material : GetFallbackMaterial();
            renderer.sortingOrder = sortingOrder;

            if (buildPolygonCollider2D)
                AddPolygonCollider(componentObject, component.meshData);
        }

        private void AddPolygonCollider(GameObject target, RuntimeTileMeshData meshData)
        {
            if (meshData.colliderPaths.Count == 0)
                return;

            PolygonCollider2D collider = target.AddComponent<PolygonCollider2D>();
            collider.isTrigger = polygonColliderIsTrigger;
            collider.pathCount = meshData.colliderPaths.Count;
            for (int i = 0; i < meshData.colliderPaths.Count; i++)
                collider.SetPath(i, meshData.colliderPaths[i].ToArray());
        }

        private Material GetFallbackMaterial()
        {
            MeshRenderer renderer = GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterial != null)
                return renderer.sharedMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Unlit/Texture");

            if (shader == null)
                return null;

            if (runtimeFallbackMaterial == null || runtimeFallbackMaterial.shader != shader)
            {
                if (runtimeFallbackMaterial != null)
                {
                    if (Application.isPlaying)
                        Destroy(runtimeFallbackMaterial);
                    else
                        DestroyImmediate(runtimeFallbackMaterial);
                }

                runtimeFallbackMaterial = new Material(shader);
                runtimeFallbackMaterial.name = "RuntimeTileMesh Default White";
            }

            ApplyFallbackMaterialDefaults(runtimeFallbackMaterial);

            return runtimeFallbackMaterial;
        }

        private static void ApplyFallbackMaterialDefaults(Material fallback)
        {
            if (fallback == null)
                return;

            if (fallback.HasProperty("_BaseMap"))
                fallback.SetTexture("_BaseMap", null);
            if (fallback.HasProperty("_MainTex"))
                fallback.SetTexture("_MainTex", null);
            if (fallback.HasProperty("_BaseColor"))
                fallback.SetColor("_BaseColor", Color.white);
            if (fallback.HasProperty("_Color"))
                fallback.SetColor("_Color", Color.white);
            if (fallback.HasProperty("_Surface"))
                fallback.SetFloat("_Surface", 0f);
            if (fallback.HasProperty("_Cull"))
                fallback.SetFloat("_Cull", 0f);
            if (fallback.HasProperty("_ZWrite"))
                fallback.SetFloat("_ZWrite", 0f);
        }

        private void DestroyGeneratedRoot()
        {
            Transform root = transform.Find(GeneratedRootName);
            if (root == null)
                return;

            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                Mesh mesh = filters[i] != null ? filters[i].sharedMesh : null;
                if (mesh == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(mesh);
                else
                    DestroyImmediate(mesh);
            }

            if (Application.isPlaying)
                Destroy(root.gameObject);
            else
                DestroyImmediate(root.gameObject);
        }

        void OnDrawGizmos()
        {
            RuntimeTileMeshSettings settings = CreateSettings();
            if (drawDebugTiles)
                DrawTileOccupancy(settings);

            if (drawDebugBoundaryEdges || drawDebugLoopPoints)
                DrawBoundaryDebug(settings);
        }

        private void DrawTileOccupancy(RuntimeTileMeshSettings settings)
        {
            Vector2 safeTileSize = settings.SafeTileSize;
            Gizmos.color = debugTileColor;
            for (int i = 0; i < tiles.Count; i++)
            {
                Vector2Int tile = tiles[i];
                Vector3 center = transform.TransformPoint(new Vector3(
                    settings.origin.x + (tile.x + 0.5f) * safeTileSize.x,
                    settings.origin.y + (tile.y + 0.5f) * safeTileSize.y,
                    0f
                ));
                Vector3 size = new Vector3(Mathf.Abs(safeTileSize.x), Mathf.Abs(safeTileSize.y), 0.01f);
                Gizmos.DrawWireCube(center, size);
            }
        }

        private void DrawBoundaryDebug(RuntimeTileMeshSettings settings)
        {
            List<DirectedTileEdge> edges = TileBoundaryExtractor.ExtractBoundaryEdges(tiles);
            Vector2 safeTileSize = settings.SafeTileSize;

            if (drawDebugBoundaryEdges)
            {
                Gizmos.color = debugBoundaryColor;
                for (int i = 0; i < edges.Count; i++)
                {
                    Vector3 from = CornerToWorld(edges[i].from, settings, safeTileSize);
                    Vector3 to = CornerToWorld(edges[i].to, settings, safeTileSize);
                    Gizmos.DrawLine(from, to);
                }
            }

#if UNITY_EDITOR
            if (drawDebugLoopPoints)
            {
                List<List<Vector2Int>> loops = PolygonLoopBuilder.BuildLoops(edges);
                GUIStyle style = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = debugLoopPointColor },
                    alignment = TextAnchor.MiddleCenter
                };

                for (int loopIndex = 0; loopIndex < loops.Count; loopIndex++)
                {
                    List<Vector2Int> loop = loops[loopIndex];
                    for (int pointIndex = 0; pointIndex < loop.Count; pointIndex++)
                    {
                        Vector3 world = CornerToWorld(loop[pointIndex], settings, safeTileSize);
                        Handles.Label(world, loopIndex + ":" + pointIndex, style);
                    }
                }
            }
#endif
        }

        private Vector3 CornerToWorld(Vector2Int corner, RuntimeTileMeshSettings settings, Vector2 safeTileSize)
        {
            return transform.TransformPoint(new Vector3(
                settings.origin.x + corner.x * safeTileSize.x,
                settings.origin.y + corner.y * safeTileSize.y,
                0f
            ));
        }
    }
}
