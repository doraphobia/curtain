using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class TilePlacementGrid : MonoBehaviour
{
    public const int DefaultTileUnit = 5;

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
    public Vector2 cellSize = new Vector2(DefaultTileUnit, DefaultTileUnit);
    public Vector2 origin;

    [Header("Fallback Room Seeding")]
    [Tooltip("如果场景里没有 TilePieceDefinition，可用名字匹配的 SpriteRenderer 初始化房间区域。")]
    public bool seedRendererBoundsWhenEmpty = true;
    public string seedRendererNameKeyword = "Floorplan";

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
    }

    public void RegisterCell(Vector2Int cell, bool isRoomCell = true)
    {
        RegisterCell(cell, isRoomCell, null, null);
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

        if (ContainsWorldPoint(desiredWorldPoint, clearanceRadius))
            return desiredWorldPoint;

        if (ContainsWorldPoint(previousWorldPoint, clearanceRadius))
            return ClampSegmentToOccupiedArea(previousWorldPoint, desiredWorldPoint, clearanceRadius);

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
        tileUnit = Mathf.Max(1, Mathf.RoundToInt(tileUnit));
        cellSize = new Vector2(
            SnapPositiveToTileMultiple(cellSize.x, tileUnit),
            SnapPositiveToTileMultiple(cellSize.y, tileUnit)
        );
        origin = new Vector2(
            SnapToTileMultiple(origin.x, tileUnit),
            SnapToTileMultiple(origin.y, tileUnit)
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
        Vector3 low = from;
        Vector3 high = to;

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
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                if (x == 0 && y == 0)
                    continue;

                Vector2Int neighbor = new Vector2Int(cell.x + x, cell.y + y);
                if (candidateCells.Contains(neighbor))
                    continue;

                if (occupiedCells.Contains(neighbor))
                    return true;
            }
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
