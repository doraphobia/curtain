using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TilePlacementGrid : MonoBehaviour
{
    [Header("Grid")]
    public Vector2 cellSize = Vector2.one;
    public Vector2 origin;

    [Header("Fallback Room Seeding")]
    [Tooltip("如果场景里没有 TilePieceDefinition，可用名字匹配的 SpriteRenderer 初始化房间区域。")]
    public bool seedRendererBoundsWhenEmpty = true;
    public string seedRendererNameKeyword = "Floorplan";

    [Header("Debug")]
    public bool drawDebugOccupiedCells = true;
    public Color debugOccupiedCellColor = new Color(0.25f, 0.9f, 1f, 0.65f);

    private readonly HashSet<Vector2Int> occupiedCells = new HashSet<Vector2Int>();
    private readonly HashSet<Vector2Int> roomCells = new HashSet<Vector2Int>();

    void Awake()
    {
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
            RegisterCell(anchorCell + definition.Cells[i], isRoomPiece);
    }

    public void RegisterCell(Vector2Int cell, bool isRoomCell = true)
    {
        occupiedCells.Add(cell);

        if (isRoomCell)
            roomCells.Add(cell);
    }

    public void RegisterWorldBounds(Bounds worldBounds)
    {
        Vector2Int minCell = WorldToCell(worldBounds.min);
        Vector2Int maxCell = WorldToCell(worldBounds.max);

        int minX = Mathf.Min(minCell.x, maxCell.x);
        int maxX = Mathf.Max(minCell.x, maxCell.x);
        int minY = Mathf.Min(minCell.y, maxCell.y);
        int maxY = Mathf.Max(minCell.y, maxCell.y);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
                RegisterCell(new Vector2Int(x, y));
        }
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

    public bool ContainsWorldPoint(Vector3 worldPosition)
    {
        return roomCells.Contains(WorldToCell(worldPosition));
    }

    public Vector3 ClampWorldPoint(Vector3 desiredWorldPoint, Vector3 previousWorldPoint)
    {
        if (roomCells.Count == 0)
            return desiredWorldPoint;

        desiredWorldPoint.z = 0f;
        previousWorldPoint.z = 0f;

        if (ContainsWorldPoint(desiredWorldPoint))
            return desiredWorldPoint;

        if (ContainsWorldPoint(previousWorldPoint))
            return ClampSegmentToOccupiedArea(previousWorldPoint, desiredWorldPoint);

        return GetNearestOccupiedPoint(desiredWorldPoint);
    }

    public Bounds GetCellWorldBounds(Vector2Int cell)
    {
        Vector3 center = CellToWorld(cell);
        Vector3 size = new Vector3(Mathf.Abs(cellSize.x), Mathf.Abs(cellSize.y), 0.01f);
        return new Bounds(center, size);
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

            if (!string.IsNullOrWhiteSpace(keyword) &&
                renderer.gameObject.name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            RegisterWorldBounds(renderer.bounds);
        }
    }

    private Vector3 ClampSegmentToOccupiedArea(Vector3 from, Vector3 to)
    {
        Vector3 low = from;
        Vector3 high = to;

        for (int i = 0; i < 16; i++)
        {
            Vector3 mid = Vector3.Lerp(low, high, 0.5f);
            if (ContainsWorldPoint(mid))
                low = mid;
            else
                high = mid;
        }

        return low;
    }

    private Vector3 GetNearestOccupiedPoint(Vector3 desiredWorldPoint)
    {
        bool hasCandidate = false;
        Vector3 bestPoint = desiredWorldPoint;
        float bestDistanceSqr = float.MaxValue;

        foreach (Vector2Int cell in roomCells)
        {
            Bounds bounds = GetCellWorldBounds(cell);
            Vector3 candidate = new Vector3(
                Mathf.Clamp(desiredWorldPoint.x, bounds.min.x, bounds.max.x),
                Mathf.Clamp(desiredWorldPoint.y, bounds.min.y, bounds.max.y),
                0f
            );

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

    void OnDrawGizmos()
    {
        if (!drawDebugOccupiedCells || roomCells == null || roomCells.Count == 0)
            return;

        Gizmos.color = debugOccupiedCellColor;
        foreach (Vector2Int cell in roomCells)
        {
            Bounds bounds = GetCellWorldBounds(cell);
            Gizmos.DrawWireCube(bounds.center, bounds.size);
        }
    }

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
}
