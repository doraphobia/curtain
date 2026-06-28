using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class TilePlacementGrid : MonoBehaviour
{
    public const int DefaultCellWidth = 1;
    public const int DefaultCellHeight = 5;
    public const int DefaultTileUnit = DefaultCellWidth;
    public static readonly Vector2 DefaultCellSize = new Vector2(DefaultCellWidth, DefaultCellHeight);

    public struct TileBlockInfo
    {
        public bool isValid;
        public Vector2Int cell;
        public Bounds cellBounds;
        public TilePieceDefinition definition;
        public string displayName;
        public bool hasVisualBounds;
        public Bounds visualBounds;
        public bool hasColliderBounds;
        public Bounds colliderBounds;
    }

    [Header("Grid")]
    [Min(1)]
    public int tileUnit = DefaultTileUnit;
    public Vector2 cellSize = new Vector2(DefaultCellWidth, DefaultCellHeight);
    public Vector2 origin;

    [Header("Fallback Room Seeding")]
    [Tooltip("如果场景里没有 TilePieceDefinition，可用名字匹配的 SpriteRenderer 初始化房间区域。")]
    public bool seedRendererBoundsWhenEmpty = false;
    public string seedRendererNameKeyword = "Floorplan";

    [Header("Movement Connectivity")]
    [Tooltip("玩家移动时必须沿途一直停留在已注册房间格内，避免从一个独立房间直接跨过空隙进入另一个房间。")]
    public bool requireContinuousRoomPath = true;
    [Tooltip("移动路径检测的采样间距，以一个 tile 单元为基准。0.25 表示每 1/4 tile 检查一次。")]
    [Range(0.05f, 1f)]
    public float pathSampleCellStep = 0.25f;

    [Header("Connected Room Plane")]
    [Tooltip("运行时把所有四向贴合的房间 cells 合并为整体 Mesh 平面。")]
    public bool buildConnectedRoomPlanes = true;
    [Tooltip("生成整体平面后隐藏旧的单块 SpriteRenderer，避免旧图像边缘和逻辑边缘不一致。")]
    public bool hideRoomPieceRenderersForConnectedPlane = true;
    [Tooltip("生成整体平面后禁用旧的单块 Collider2D，避免独立矩形碰撞覆盖空隙。")]
    public bool disableRoomPieceCollidersForConnectedPlane = true;
    [Tooltip("为每个连通房间整体生成 PolygonCollider2D。默认作为 trigger，实际玩家阻挡仍由网格逻辑负责。")]
    public bool buildConnectedPlaneCollider = true;
    public bool connectedPlaneColliderIsTrigger = true;
    [Tooltip("未指定时会创建一个简单的 Sprites/Default 运行时材质；之后可以在这里挂你的整体房间 shader/material。")]
    public Material connectedPlaneMaterial;
    public Color connectedPlaneFallbackColor = new Color(0.62f, 0.62f, 0.62f, 1f);
    public string connectedPlaneRootName = "__Connected Room Planes";
    public int connectedPlaneSortingOrder = 0;

    [Header("Debug")]
    public bool drawDebugOccupiedCells = true;
    public bool drawDebugExteriorEdges = true;
    public bool drawDebugVisualBounds = true;
    public bool drawDebugColliderBounds = true;
    public bool drawDebugBlockLabels = true;
    public Color debugOccupiedCellColor = new Color(0.25f, 0.9f, 1f, 0.65f);
    public Color debugExteriorEdgeColor = new Color(1f, 0.86f, 0.2f, 1f);
    public Color debugVisualBoundsColor = new Color(0.25f, 1f, 0.35f, 0.85f);
    public Color debugColliderBoundsColor = new Color(1f, 0.3f, 0.3f, 0.85f);

    private static readonly Vector2Int[] CardinalDirections =
    {
        new Vector2Int(1, 0),
        new Vector2Int(-1, 0),
        new Vector2Int(0, 1),
        new Vector2Int(0, -1)
    };

    private readonly HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> roomCells = new HashSet<Vector2Int>();
    private readonly Dictionary<Vector2Int, TilePieceDefinition> roomCellDefinitions = new Dictionary<Vector2Int, TilePieceDefinition>();
    private readonly Dictionary<Vector2Int, string> roomCellNames = new Dictionary<Vector2Int, string>();
    private bool connectedPlanesDirty;
    private Material runtimeConnectedPlaneMaterial;

    void OnValidate()
    {
        NormalizeGridSettings();
    }

    void Awake()
    {
        NormalizeGridSettings();
        RegisterExistingTiles();

        if (seedRendererBoundsWhenEmpty && roomCells.Count == 0)
            RegisterSceneRendererBoundsByName(seedRendererNameKeyword);
    }

    void Start()
    {
        QueueConnectedRoomPlaneRebuild();
    }

    void LateUpdate()
    {
        if (!connectedPlanesDirty)
            return;

        connectedPlanesDirty = false;
        RebuildConnectedRoomPlanes();
    }

    void OnDestroy()
    {
        if (runtimeConnectedPlaneMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(runtimeConnectedPlaneMaterial);
            else
                DestroyImmediate(runtimeConnectedPlaneMaterial);
        }
    }

    public Vector2Int WorldToCell(Vector3 worldPosition)
    {
        Vector2 local = (Vector2)worldPosition - origin;
        return new Vector2Int(
            Mathf.RoundToInt(local.x / Mathf.Max(0.0001f, cellSize.x)),
            Mathf.RoundToInt(local.y / Mathf.Max(0.0001f, cellSize.y))
        );
    }

    public Vector3 CellToWorld(Vector2Int cell)
    {
        return new Vector3(
            origin.x + cell.x * cellSize.x,
            origin.y + cell.y * cellSize.y,
            0f
        );
    }

    public bool CanPlace(TilePieceDefinition definition, Vector2Int anchorCell)
    {
        if (definition == null || definition.Cells == null || definition.Cells.Count == 0)
            return false;

        HashSet<Vector2Int> candidateCells = new HashSet<Vector2Int>();
        for (int i = 0; i < definition.Cells.Count; i++)
        {
            candidateCells.Add(anchorCell + definition.Cells[i]);
        }

        if (occupiedCells.Count == 0)
            return true;

        foreach (Vector2Int cell in candidateCells)
        {
            if (occupiedCells.Contains(cell))
                return true;

            if (TouchesOtherOccupiedCell(cell, candidateCells))
                return true;
        }

        return false;
    }

    public bool TryPlace(TilePieceDefinition definition, Vector2Int anchorCell)
    {
        if (!CanPlace(definition, anchorCell))
            return false;

        RegisterPiece(definition, anchorCell);
        return true;
    }

    public void RegisterPiece(TilePieceDefinition definition, Vector2Int anchorCell)
    {
        if (definition == null || definition.Cells == null)
            return;

        bool isRoomPiece = definition.placementLayer == TilePieceDefinition.PlacementLayer.Tile;
        for (int i = 0; i < definition.Cells.Count; i++)
            RegisterCell(anchorCell + definition.Cells[i], isRoomPiece, definition, GetDefinitionDisplayName(definition));

        if (isRoomPiece)
            QueueConnectedRoomPlaneRebuild();
    }

    public void RegisterCell(Vector2Int cell, bool isRoomCell = true)
    {
        RegisterCell(cell, isRoomCell, null, null);

        if (isRoomCell)
            QueueConnectedRoomPlaneRebuild();
    }

    public void RegisterWorldBounds(Bounds worldBounds)
    {
        RegisterWorldBounds(worldBounds, null);
    }

    public void RegisterWorldBounds(Bounds worldBounds, string sourceName)
    {
        Vector2Int minCell = WorldToCell(worldBounds.min);
        Vector2Int maxCell = WorldToCell(worldBounds.max);

        int minX = Mathf.Min(minCell.x, maxCell.x) - 1;
        int maxX = Mathf.Max(minCell.x, maxCell.x) + 1;
        int minY = Mathf.Min(minCell.y, maxCell.y) - 1;
        int maxY = Mathf.Max(minCell.y, maxCell.y) + 1;
        bool registeredAny = false;

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                Vector2Int cell = new Vector2Int(x, y);
                Vector3 center = CellToWorld(cell);
                if (!ContainsPoint2D(worldBounds, center))
                    continue;

                RegisterCell(cell, true, null, sourceName);
                registeredAny = true;
            }
        }

        if (!registeredAny)
            RegisterCell(WorldToCell(worldBounds.center), true, null, sourceName);

        QueueConnectedRoomPlaneRebuild();
    }

    public int OccupiedCount()
    {
        return occupiedCells.Count;
    }

    public bool HasOccupiedCells => occupiedCells.Count > 0;
    public bool HasRoomCells => roomCells.Count > 0;

    public bool IsCellOccupied(Vector2Int cell)
    {
        return occupiedCells.Contains(cell);
    }

    public bool IsRoomCell(Vector2Int cell)
    {
        return roomCells.Contains(cell);
    }

    public bool TryGetRoomCell(Vector3 worldPosition, out Vector2Int cell)
    {
        cell = WorldToCell(worldPosition);
        return roomCells.Contains(cell);
    }

    public bool TryGetBlockInfo(Vector3 worldPosition, out TileBlockInfo blockInfo)
    {
        Vector2Int cell = WorldToCell(worldPosition);
        if (!roomCells.Contains(cell))
        {
            blockInfo = default(TileBlockInfo);
            return false;
        }

        roomCellDefinitions.TryGetValue(cell, out TilePieceDefinition definition);
        blockInfo = new TileBlockInfo
        {
            isValid = true,
            cell = cell,
            cellBounds = GetCellWorldBounds(cell),
            definition = definition,
            displayName = GetCellDisplayName(cell, definition)
        };

        if (definition != null)
        {
            blockInfo.hasVisualBounds = TryGetVisualBounds(definition.gameObject, out Bounds visualBounds);
            blockInfo.visualBounds = visualBounds;
            blockInfo.hasColliderBounds = TryGetColliderBounds(definition.gameObject, out Bounds colliderBounds);
            blockInfo.colliderBounds = colliderBounds;
        }

        return true;
    }

    public bool ContainsWorldPoint(Vector3 worldPosition)
    {
        return roomCells.Contains(WorldToCell(worldPosition));
    }

    public bool ContainsWorldPoint(Vector3 worldPosition, float clearanceRadius)
    {
        if (!ContainsWorldPoint(worldPosition))
            return false;

        clearanceRadius = Mathf.Max(0f, clearanceRadius);
        if (clearanceRadius <= 0.0001f)
            return true;

        return ContainsWorldPoint(worldPosition + new Vector3(clearanceRadius, 0f, 0f)) &&
               ContainsWorldPoint(worldPosition + new Vector3(-clearanceRadius, 0f, 0f)) &&
               ContainsWorldPoint(worldPosition + new Vector3(0f, clearanceRadius, 0f)) &&
               ContainsWorldPoint(worldPosition + new Vector3(0f, -clearanceRadius, 0f)) &&
               ContainsWorldPoint(worldPosition + new Vector3(clearanceRadius * 0.7071f, clearanceRadius * 0.7071f, 0f)) &&
               ContainsWorldPoint(worldPosition + new Vector3(-clearanceRadius * 0.7071f, clearanceRadius * 0.7071f, 0f)) &&
               ContainsWorldPoint(worldPosition + new Vector3(clearanceRadius * 0.7071f, -clearanceRadius * 0.7071f, 0f)) &&
               ContainsWorldPoint(worldPosition + new Vector3(-clearanceRadius * 0.7071f, -clearanceRadius * 0.7071f, 0f));
    }

    public Vector3 ClampWorldPoint(Vector3 desiredWorldPoint, Vector3 previousWorldPoint)
    {
        return ClampWorldPoint(desiredWorldPoint, previousWorldPoint, 0f);
    }

    public Vector3 ClampWorldPoint(Vector3 desiredWorldPoint, Vector3 previousWorldPoint, float clearanceRadius)
    {
        if (roomCells.Count == 0)
            return desiredWorldPoint;

        desiredWorldPoint.z = 0f;
        previousWorldPoint.z = 0f;
        clearanceRadius = Mathf.Max(0f, clearanceRadius);

        if (ContainsWorldPoint(previousWorldPoint, clearanceRadius))
            return ClampSegmentToOccupiedArea(previousWorldPoint, desiredWorldPoint, clearanceRadius);

        if (ContainsWorldPoint(desiredWorldPoint, clearanceRadius))
            return desiredWorldPoint;

        return GetNearestOccupiedPoint(desiredWorldPoint, clearanceRadius);
    }

    public Vector3 ClampPlayerWorldPoint(Vector3 desiredWorldPoint, Vector3 previousWorldPoint, float playerRadius)
    {
        return ClampWorldPoint(desiredWorldPoint, previousWorldPoint, playerRadius);
    }

    public Bounds GetCellWorldBounds(Vector2Int cell)
    {
        Vector3 center = CellToWorld(cell);
        Vector3 size = new Vector3(Mathf.Abs(cellSize.x), Mathf.Abs(cellSize.y), 0.01f);
        return new Bounds(center, size);
    }

    [ContextMenu("Rebuild Connected Room Planes")]
    public void RebuildConnectedRoomPlanes()
    {
        DestroyConnectedRoomPlaneRoot();

        if (!buildConnectedRoomPlanes || roomCells.Count == 0)
            return;

        Transform root = CreateConnectedRoomPlaneRoot();
        List<List<Vector2Int>> components = CollectConnectedRoomComponents();
        for (int i = 0; i < components.Count; i++)
            CreateConnectedRoomPlane(root, components[i], i);

        ApplyRoomPieceVisibilityForConnectedPlanes();
    }

    private void QueueConnectedRoomPlaneRebuild()
    {
        if (!Application.isPlaying || !buildConnectedRoomPlanes)
            return;

        connectedPlanesDirty = true;
    }

    private Transform CreateConnectedRoomPlaneRoot()
    {
        GameObject rootObject = new GameObject(connectedPlaneRootName);
        Transform root = rootObject.transform;
        root.SetParent(transform, false);
        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;
        return root;
    }

    private void DestroyConnectedRoomPlaneRoot()
    {
        Transform existingRoot = transform.Find(connectedPlaneRootName);
        if (existingRoot == null)
            return;

        MeshFilter[] filters = existingRoot.GetComponentsInChildren<MeshFilter>(true);
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
            Destroy(existingRoot.gameObject);
        else
            DestroyImmediate(existingRoot.gameObject);
    }

    private List<List<Vector2Int>> CollectConnectedRoomComponents()
    {
        List<List<Vector2Int>> components = new List<List<Vector2Int>>();
        HashSet<Vector2Int> remaining = new HashSet<Vector2Int>(roomCells);
        Queue<Vector2Int> queue = new Queue<Vector2Int>();

        while (remaining.Count > 0)
        {
            Vector2Int start = default(Vector2Int);
            foreach (Vector2Int cell in remaining)
            {
                start = cell;
                break;
            }

            List<Vector2Int> component = new List<Vector2Int>();
            remaining.Remove(start);
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                component.Add(cell);

                for (int i = 0; i < CardinalDirections.Length; i++)
                {
                    Vector2Int neighbor = cell + CardinalDirections[i];
                    if (!remaining.Remove(neighbor))
                        continue;

                    queue.Enqueue(neighbor);
                }
            }

            components.Add(component);
        }

        return components;
    }

    private void CreateConnectedRoomPlane(Transform root, List<Vector2Int> component, int index)
    {
        if (component == null || component.Count == 0)
            return;

        Bounds componentBounds = GetComponentWorldBounds(component);
        List<Vector3> vertices = new List<Vector3>(component.Count * 4);
        List<Vector2> uvs = new List<Vector2>(component.Count * 4);
        List<int> triangles = new List<int>(component.Count * 6);

        for (int i = 0; i < component.Count; i++)
            AddCellQuad(component[i], componentBounds, vertices, uvs, triangles);

        Mesh mesh = new Mesh();
        mesh.name = "Connected Room Plane Mesh " + index;
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        GameObject planeObject = new GameObject("Connected Room Plane " + index);
        Transform planeTransform = planeObject.transform;
        planeTransform.SetParent(root, false);
        planeTransform.localPosition = Vector3.zero;
        planeTransform.localRotation = Quaternion.identity;
        planeTransform.localScale = Vector3.one;

        MeshFilter filter = planeObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;

        MeshRenderer renderer = planeObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = GetConnectedPlaneMaterial();
        renderer.sortingOrder = connectedPlaneSortingOrder;

        if (buildConnectedPlaneCollider)
            AddConnectedPlaneCollider(planeObject, component);
    }

    private Bounds GetComponentWorldBounds(List<Vector2Int> component)
    {
        Bounds bounds = GetCellWorldBounds(component[0]);
        for (int i = 1; i < component.Count; i++)
            bounds.Encapsulate(GetCellWorldBounds(component[i]));

        return bounds;
    }

    private void AddCellQuad(
        Vector2Int cell,
        Bounds componentBounds,
        List<Vector3> vertices,
        List<Vector2> uvs,
        List<int> triangles)
    {
        Bounds bounds = GetCellWorldBounds(cell);
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        int start = vertices.Count;

        Vector3 bottomLeft = new Vector3(min.x, min.y, 0f);
        Vector3 bottomRight = new Vector3(max.x, min.y, 0f);
        Vector3 topRight = new Vector3(max.x, max.y, 0f);
        Vector3 topLeft = new Vector3(min.x, max.y, 0f);

        vertices.Add(WorldToConnectedPlaneLocal(bottomLeft));
        vertices.Add(WorldToConnectedPlaneLocal(bottomRight));
        vertices.Add(WorldToConnectedPlaneLocal(topRight));
        vertices.Add(WorldToConnectedPlaneLocal(topLeft));

        uvs.Add(GetConnectedPlaneUv(bottomLeft, componentBounds));
        uvs.Add(GetConnectedPlaneUv(bottomRight, componentBounds));
        uvs.Add(GetConnectedPlaneUv(topRight, componentBounds));
        uvs.Add(GetConnectedPlaneUv(topLeft, componentBounds));

        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private Vector3 WorldToConnectedPlaneLocal(Vector3 worldPosition)
    {
        return transform.InverseTransformPoint(worldPosition);
    }

    private Vector2 GetConnectedPlaneUv(Vector3 worldPosition, Bounds componentBounds)
    {
        float width = Mathf.Max(0.0001f, componentBounds.size.x);
        float height = Mathf.Max(0.0001f, componentBounds.size.y);
        return new Vector2(
            (worldPosition.x - componentBounds.min.x) / width,
            (worldPosition.y - componentBounds.min.y) / height
        );
    }

    private Material GetConnectedPlaneMaterial()
    {
        if (connectedPlaneMaterial != null)
            return connectedPlaneMaterial;

        if (runtimeConnectedPlaneMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
                return null;

            runtimeConnectedPlaneMaterial = new Material(shader);
            runtimeConnectedPlaneMaterial.name = "Duo Curtain Connected Room Plane Material";
            runtimeConnectedPlaneMaterial.color = connectedPlaneFallbackColor;
        }

        return runtimeConnectedPlaneMaterial;
    }

    private void AddConnectedPlaneCollider(GameObject planeObject, List<Vector2Int> component)
    {
        List<List<Vector2>> paths = BuildBoundaryPaths(component);
        if (paths.Count == 0)
            return;

        PolygonCollider2D collider = planeObject.AddComponent<PolygonCollider2D>();
        collider.isTrigger = connectedPlaneColliderIsTrigger;
        collider.pathCount = paths.Count;

        for (int i = 0; i < paths.Count; i++)
            collider.SetPath(i, paths[i].ToArray());
    }

    private List<List<Vector2>> BuildBoundaryPaths(List<Vector2Int> component)
    {
        Dictionary<BoundaryEdge, DirectedBoundaryEdge> boundaryEdges =
            new Dictionary<BoundaryEdge, DirectedBoundaryEdge>();

        for (int i = 0; i < component.Count; i++)
        {
            Vector2Int cell = component[i];
            GridCorner bottomLeft = GetCellCorner(cell, -1, -1);
            GridCorner bottomRight = GetCellCorner(cell, 1, -1);
            GridCorner topRight = GetCellCorner(cell, 1, 1);
            GridCorner topLeft = GetCellCorner(cell, -1, 1);

            AddBoundaryEdge(boundaryEdges, bottomLeft, bottomRight);
            AddBoundaryEdge(boundaryEdges, bottomRight, topRight);
            AddBoundaryEdge(boundaryEdges, topRight, topLeft);
            AddBoundaryEdge(boundaryEdges, topLeft, bottomLeft);
        }

        HashSet<BoundaryEdge> remaining = new HashSet<BoundaryEdge>(boundaryEdges.Keys);
        List<List<Vector2>> paths = new List<List<Vector2>>();

        while (remaining.Count > 0)
        {
            DirectedBoundaryEdge startEdge = GetFirstRemainingBoundaryEdge(remaining, boundaryEdges);
            GridCorner start = startEdge.from;
            GridCorner current = startEdge.from;
            GridCorner next = startEdge.to;
            List<Vector2> path = new List<Vector2>();
            int guard = boundaryEdges.Count + 4;

            path.Add(CornerToConnectedPlaneLocal(current));

            while (guard-- > 0)
            {
                BoundaryEdge consumed = new BoundaryEdge(current, next);
                remaining.Remove(consumed);
                current = next;
                path.Add(CornerToConnectedPlaneLocal(current));

                if (current.Equals(start))
                    break;

                if (!TryGetNextBoundaryCorner(current, remaining, boundaryEdges, out next))
                    break;
            }

            if (path.Count > 1 && Approximately(path[0], path[path.Count - 1]))
                path.RemoveAt(path.Count - 1);

            if (path.Count >= 3)
                paths.Add(path);
        }

        return paths;
    }

    private void AddBoundaryEdge(
        Dictionary<BoundaryEdge, DirectedBoundaryEdge> boundaryEdges,
        GridCorner from,
        GridCorner to)
    {
        BoundaryEdge key = new BoundaryEdge(from, to);
        if (boundaryEdges.ContainsKey(key))
        {
            boundaryEdges.Remove(key);
            return;
        }

        boundaryEdges.Add(key, new DirectedBoundaryEdge(from, to));
    }

    private DirectedBoundaryEdge GetFirstRemainingBoundaryEdge(
        HashSet<BoundaryEdge> remaining,
        Dictionary<BoundaryEdge, DirectedBoundaryEdge> boundaryEdges)
    {
        foreach (BoundaryEdge key in remaining)
            return boundaryEdges[key];

        return default(DirectedBoundaryEdge);
    }

    private bool TryGetNextBoundaryCorner(
        GridCorner current,
        HashSet<BoundaryEdge> remaining,
        Dictionary<BoundaryEdge, DirectedBoundaryEdge> boundaryEdges,
        out GridCorner next)
    {
        foreach (BoundaryEdge key in remaining)
        {
            DirectedBoundaryEdge edge = boundaryEdges[key];
            if (!edge.from.Equals(current))
                continue;

            next = edge.to;
            return true;
        }

        next = default(GridCorner);
        return false;
    }

    private Vector2 CornerToConnectedPlaneLocal(GridCorner corner)
    {
        Vector3 world = new Vector3(
            origin.x + corner.x * 0.5f * cellSize.x,
            origin.y + corner.y * 0.5f * cellSize.y,
            0f
        );
        return WorldToConnectedPlaneLocal(world);
    }

    private static GridCorner GetCellCorner(Vector2Int cell, int xOffset, int yOffset)
    {
        return new GridCorner(cell.x * 2 + xOffset, cell.y * 2 + yOffset);
    }

    private void ApplyRoomPieceVisibilityForConnectedPlanes()
    {
        if (!hideRoomPieceRenderersForConnectedPlane && !disableRoomPieceCollidersForConnectedPlane)
            return;

        HashSet<TilePieceDefinition> definitions = new HashSet<TilePieceDefinition>(roomCellDefinitions.Values);
        foreach (TilePieceDefinition definition in definitions)
        {
            if (definition == null)
                continue;

            if (hideRoomPieceRenderersForConnectedPlane)
            {
                SpriteRenderer[] renderers = definition.GetComponents<SpriteRenderer>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                        renderers[i].enabled = false;
                }
            }

            if (disableRoomPieceCollidersForConnectedPlane)
            {
                Collider2D[] colliders = definition.GetComponents<Collider2D>();
                for (int i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i] != null)
                        colliders[i].enabled = false;
                }
            }
        }
    }

    private void RegisterCell(Vector2Int cell, bool isRoomCell, TilePieceDefinition definition, string sourceName)
    {
        occupiedCells.Add(cell);

        if (!isRoomCell)
            return;

        roomCells.Add(cell);

        if (definition != null)
            roomCellDefinitions[cell] = definition;

        if (!string.IsNullOrWhiteSpace(sourceName))
            roomCellNames[cell] = sourceName;
    }

    private void NormalizeGridSettings()
    {
        tileUnit = DefaultCellWidth;
        cellSize = new Vector2(
            SnapPositiveToTileMultiple(cellSize.x, DefaultCellWidth),
            SnapPositiveToTileMultiple(cellSize.y, DefaultCellHeight)
        );
        origin = new Vector2(
            SnapToTileMultiple(origin.x, DefaultCellWidth),
            SnapToTileMultiple(origin.y, DefaultCellHeight)
        );
    }

    public static float SnapPositiveToTileMultiple(float value, int unit = DefaultTileUnit)
    {
        unit = Mathf.Max(1, unit);
        int multiples = Mathf.Max(1, Mathf.RoundToInt(value / unit));
        return multiples * unit;
    }

    public static float SnapToTileMultiple(float value, int unit = DefaultTileUnit)
    {
        unit = Mathf.Max(1, unit);
        return Mathf.RoundToInt(value / unit) * unit;
    }

    private void RegisterExistingTiles()
    {
        TilePieceDefinition[] definitions = FindObjectsByType<TilePieceDefinition>(FindObjectsSortMode.None);
        for (int i = 0; i < definitions.Length; i++)
        {
            TilePieceDefinition definition = definitions[i];
            if (definition == null || !definition.registerOnStart)
                continue;

            Vector2Int anchorCell = WorldToCell(definition.transform.position);
            RegisterPiece(definition, anchorCell);
        }
    }

    private void RegisterSceneRendererBoundsByName(string keyword)
    {
        SpriteRenderer[] renderers = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        for (int i = 0; i < renderers.Length; i++)
        {
            SpriteRenderer renderer = renderers[i];
            if (renderer == null || renderer.gameObject == null)
                continue;

            if (!HasNameKeyword(renderer.transform, keyword))
                continue;

            RegisterWorldBounds(renderer.bounds, renderer.gameObject.name);
        }
    }

    private Vector3 ClampSegmentToOccupiedArea(Vector3 from, Vector3 to, float clearanceRadius)
    {
        if (!requireContinuousRoomPath && ContainsWorldPoint(to, clearanceRadius))
            return to;

        Vector3 lastValid = from;
        Vector3 firstInvalid = to;
        bool foundInvalid = false;
        int steps = GetSegmentSampleCount(from, to);

        for (int i = 1; i <= steps; i++)
        {
            Vector3 sample = Vector3.Lerp(from, to, i / (float)steps);
            if (ContainsWorldPoint(sample, clearanceRadius))
            {
                lastValid = sample;
                continue;
            }

            firstInvalid = sample;
            foundInvalid = true;
            break;
        }

        if (!foundInvalid)
            return to;

        Vector3 low = lastValid;
        Vector3 high = firstInvalid;

        for (int i = 0; i < 18; i++)
        {
            Vector3 mid = Vector3.Lerp(low, high, 0.5f);
            if (ContainsWorldPoint(mid, clearanceRadius))
                low = mid;
            else
                high = mid;
        }

        return low;
    }

    private int GetSegmentSampleCount(Vector3 from, Vector3 to)
    {
        float distance = Vector2.Distance(from, to);
        if (distance <= 0.0001f)
            return 1;

        float smallestCellSize = Mathf.Max(0.0001f, Mathf.Min(Mathf.Abs(cellSize.x), Mathf.Abs(cellSize.y)));
        float sampleDistance = Mathf.Max(0.05f, smallestCellSize * Mathf.Clamp(pathSampleCellStep, 0.05f, 1f));
        return Mathf.Max(1, Mathf.CeilToInt(distance / sampleDistance));
    }

    private Vector3 GetNearestOccupiedPoint(Vector3 desiredWorldPoint, float clearanceRadius)
    {
        bool hasCandidate = false;
        Vector3 bestPoint = desiredWorldPoint;
        float bestDistanceSqr = float.MaxValue;
        clearanceRadius = Mathf.Max(0f, clearanceRadius);

        foreach (Vector2Int cell in roomCells)
        {
            Bounds bounds = GetCellWorldBounds(cell);
            Vector3 candidate = ClampToBoundsWithClearance(desiredWorldPoint, bounds, clearanceRadius);

            if (!ContainsWorldPoint(candidate, clearanceRadius))
            {
                candidate = bounds.center;
                candidate.z = 0f;
                if (!ContainsWorldPoint(candidate, clearanceRadius))
                    continue;
            }

            float distanceSqr = ((Vector2)candidate - (Vector2)desiredWorldPoint).sqrMagnitude;
            if (!hasCandidate || distanceSqr < bestDistanceSqr)
            {
                hasCandidate = true;
                bestDistanceSqr = distanceSqr;
                bestPoint = candidate;
            }
        }

        return bestPoint;
    }

    private Vector3 ClampToBoundsWithClearance(Vector3 point, Bounds bounds, float clearanceRadius)
    {
        float minX = bounds.min.x + clearanceRadius;
        float maxX = bounds.max.x - clearanceRadius;
        float minY = bounds.min.y + clearanceRadius;
        float maxY = bounds.max.y - clearanceRadius;

        if (minX > maxX)
            minX = maxX = bounds.center.x;

        if (minY > maxY)
            minY = maxY = bounds.center.y;

        return new Vector3(
            Mathf.Clamp(point.x, minX, maxX),
            Mathf.Clamp(point.y, minY, maxY),
            0f
        );
    }

    void OnDrawGizmos()
    {
        EnsureDebugCellsAvailable();

        if (drawDebugOccupiedCells && roomCells != null && roomCells.Count > 0)
            DrawDebugRoomCells();

        if (drawDebugExteriorEdges && roomCells != null && roomCells.Count > 0)
            DrawDebugExteriorEdges();

        if (drawDebugVisualBounds || drawDebugColliderBounds)
            DrawDebugObjectBounds();

#if UNITY_EDITOR
        if (drawDebugBlockLabels && roomCells != null && roomCells.Count > 0)
            DrawDebugBlockLabels();
#endif
    }

    private void EnsureDebugCellsAvailable()
    {
        if (Application.isPlaying)
            return;

        occupiedCells.Clear();
        roomCells.Clear();
        roomCellDefinitions.Clear();
        roomCellNames.Clear();

        RegisterExistingTiles();

        if (seedRendererBoundsWhenEmpty && roomCells.Count == 0)
            RegisterSceneRendererBoundsByName(seedRendererNameKeyword);
    }

    private void DrawDebugRoomCells()
    {
        Gizmos.color = debugOccupiedCellColor;
        foreach (Vector2Int cell in roomCells)
        {
            Bounds bounds = GetCellWorldBounds(cell);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }

    private void DrawDebugExteriorEdges()
    {
        Gizmos.color = debugExteriorEdgeColor;
        foreach (Vector2Int cell in roomCells)
        {
            Bounds bounds = GetCellWorldBounds(cell);
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;

            if (!roomCells.Contains(cell + CardinalDirections[0]))
                Gizmos.DrawLine(new Vector3(max.x, min.y, 0f), new Vector3(max.x, max.y, 0f));

            if (!roomCells.Contains(cell + CardinalDirections[1]))
                Gizmos.DrawLine(new Vector3(min.x, min.y, 0f), new Vector3(min.x, max.y, 0f));

            if (!roomCells.Contains(cell + CardinalDirections[2]))
                Gizmos.DrawLine(new Vector3(min.x, max.y, 0f), new Vector3(max.x, max.y, 0f));

            if (!roomCells.Contains(cell + CardinalDirections[3]))
                Gizmos.DrawLine(new Vector3(min.x, min.y, 0f), new Vector3(max.x, min.y, 0f));
        }
    }

    private void DrawDebugObjectBounds()
    {
        if (drawDebugVisualBounds)
        {
            SpriteRenderer[] renderers = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
            Gizmos.color = debugVisualBoundsColor;
            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer renderer = renderers[i];
                if (renderer == null || !BelongsToTileDebugObject(renderer.transform))
                    continue;

                Gizmos.DrawWireCube(renderer.bounds.center, renderer.bounds.size);
            }
        }

        if (drawDebugColliderBounds)
        {
            Collider2D[] colliders = FindObjectsByType<Collider2D>(FindObjectsSortMode.None);
            Gizmos.color = debugColliderBoundsColor;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider2D collider = colliders[i];
                if (collider == null || !BelongsToTileDebugObject(collider.transform))
                    continue;

                Gizmos.DrawWireCube(collider.bounds.center, collider.bounds.size);
            }
        }
    }

#if UNITY_EDITOR
    private void DrawDebugBlockLabels()
    {
        GUIStyle style = new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = debugExteriorEdgeColor },
            alignment = TextAnchor.MiddleCenter
        };

        foreach (Vector2Int cell in roomCells)
        {
            Bounds bounds = GetCellWorldBounds(cell);
            string label = GetCellDisplayName(cell, null) + "\n" + cell.x + "," + cell.y;
            Handles.Label(bounds.center + Vector3.up * Mathf.Max(0.25f, Mathf.Abs(cellSize.y) * 0.08f), label, style);
        }
    }
#endif

    private bool TouchesOtherOccupiedCell(Vector2Int cell, HashSet<Vector2Int> candidateCells)
    {
        for (int i = 0; i < CardinalDirections.Length; i++)
        {
            Vector2Int neighbor = cell + CardinalDirections[i];
            if (candidateCells.Contains(neighbor))
                continue;

            if (occupiedCells.Contains(neighbor))
                return true;
        }

        return false;
    }

    private static bool ContainsPoint2D(Bounds bounds, Vector3 point)
    {
        const float tolerance = 0.0001f;
        return point.x >= bounds.min.x - tolerance &&
               point.x <= bounds.max.x + tolerance &&
               point.y >= bounds.min.y - tolerance &&
               point.y <= bounds.max.y + tolerance;
    }

    private bool HasNameKeyword(Transform transformToCheck, string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return true;

        Transform current = transformToCheck;
        while (current != null)
        {
            if (current.gameObject.name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            current = current.parent;
        }

        return false;
    }

    private bool BelongsToTileDebugObject(Transform transformToCheck)
    {
        if (transformToCheck == null)
            return false;

        if (transformToCheck.GetComponentInParent<TilePieceDefinition>() != null)
            return true;

        return HasNameKeyword(transformToCheck, seedRendererNameKeyword);
    }

    private string GetCellDisplayName(Vector2Int cell, TilePieceDefinition definition)
    {
        if (roomCellNames.TryGetValue(cell, out string storedName) && !string.IsNullOrWhiteSpace(storedName))
            return storedName;

        if (definition == null)
            roomCellDefinitions.TryGetValue(cell, out definition);

        if (definition != null)
            return GetDefinitionDisplayName(definition);

        return "Tile";
    }

    private string GetDefinitionDisplayName(TilePieceDefinition definition)
    {
        if (definition == null)
            return null;

        if (definition.shopData != null && !string.IsNullOrWhiteSpace(definition.shopData.displayName))
            return definition.shopData.displayName;

        return definition.gameObject != null ? definition.gameObject.name : "Tile";
    }

    private struct GridCorner : IEquatable<GridCorner>
    {
        public readonly int x;
        public readonly int y;

        public GridCorner(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public bool Equals(GridCorner other)
        {
            return x == other.x && y == other.y;
        }

        public override bool Equals(object obj)
        {
            return obj is GridCorner other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (x * 397) ^ y;
            }
        }
    }

    private struct BoundaryEdge : IEquatable<BoundaryEdge>
    {
        public readonly GridCorner a;
        public readonly GridCorner b;

        public BoundaryEdge(GridCorner first, GridCorner second)
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

        public bool Equals(BoundaryEdge other)
        {
            return a.Equals(other.a) && b.Equals(other.b);
        }

        public override bool Equals(object obj)
        {
            return obj is BoundaryEdge other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (a.GetHashCode() * 397) ^ b.GetHashCode();
            }
        }

        private static int Compare(GridCorner first, GridCorner second)
        {
            int xCompare = first.x.CompareTo(second.x);
            return xCompare != 0 ? xCompare : first.y.CompareTo(second.y);
        }
    }

    private struct DirectedBoundaryEdge
    {
        public readonly GridCorner from;
        public readonly GridCorner to;

        public DirectedBoundaryEdge(GridCorner from, GridCorner to)
        {
            this.from = from;
            this.to = to;
        }
    }

    private static bool Approximately(Vector2 a, Vector2 b)
    {
        return Mathf.Abs(a.x - b.x) <= 0.0001f &&
               Mathf.Abs(a.y - b.y) <= 0.0001f;
    }

    private static bool TryGetVisualBounds(GameObject root, out Bounds bounds)
    {
        return TryEncapsulateBounds(root != null ? root.GetComponentsInChildren<SpriteRenderer>(true) : null, out bounds);
    }

    private static bool TryGetColliderBounds(GameObject root, out Bounds bounds)
    {
        return TryEncapsulateBounds(root != null ? root.GetComponentsInChildren<Collider2D>(true) : null, out bounds);
    }

    private static bool TryEncapsulateBounds<T>(T[] components, out Bounds bounds) where T : Component
    {
        bool hasBounds = false;
        bounds = default(Bounds);

        if (components == null)
            return false;

        for (int i = 0; i < components.Length; i++)
        {
            Component component = components[i];
            if (component == null)
                continue;

            Bounds componentBounds;
            if (component is Renderer renderer)
            {
                componentBounds = renderer.bounds;
            }
            else if (component is Collider2D collider2D)
            {
                componentBounds = collider2D.bounds;
            }
            else
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = componentBounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(componentBounds);
            }
        }

        return hasBounds;
    }
}
