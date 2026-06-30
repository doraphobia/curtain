using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry for <see cref="Room"/> instances and spatial queries used by enemy AI.
/// Falls back to <see cref="TilePlacementGrid"/> room cells when no Room collider matches.
/// </summary>
[DisallowMultipleComponent]
public class RoomManager : MonoBehaviour
{
    public static RoomManager Instance { get; private set; }

    [Header("Fallback")]
    public TilePlacementGrid tilePlacementGrid;

    private static readonly List<Room> RegisteredRooms = new List<Room>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        if (tilePlacementGrid == null)
            tilePlacementGrid = FindFirstObjectByType<TilePlacementGrid>();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public static void Register(Room room)
    {
        if (room == null || RegisteredRooms.Contains(room))
            return;

        RegisteredRooms.Add(room);
    }

    public static void Unregister(Room room)
    {
        if (room == null)
            return;

        RegisteredRooms.Remove(room);
    }

    public static IReadOnlyList<Room> AllRooms => RegisteredRooms;

    public static Room GetRoomAtPosition(Vector3 worldPosition)
    {
        Vector2 point = worldPosition;
        for (int i = 0; i < RegisteredRooms.Count; i++)
        {
            Room room = RegisteredRooms[i];
            if (room != null && room.ContainsWorldPoint(point))
                return room;
        }

        TilePlacementGrid grid = Instance != null ? Instance.tilePlacementGrid : null;
        if (grid == null)
            grid = FindFirstObjectByType<TilePlacementGrid>();

        if (grid != null && grid.HasRoomCells)
        {
            Vector2Int cell = grid.WorldToCell(worldPosition);
            if (grid.IsRoomCell(cell))
                return FindRoomCoveringCell(grid, cell);
        }

        return null;
    }

    public static bool IsInsideAnyRoom(Vector3 worldPosition)
    {
        return GetRoomAtPosition(worldPosition) != null;
    }

    public static bool IsValidEnemySpawnPosition(
        Vector3 position,
        float nearPlayerRadius,
        float maxSpawnDistanceFromPlayer)
    {
        if (IsInsideAnyRoom(position))
            return false;

        if (!PlayerControl.TryGetPlayerWorldPosition(out Vector3 playerPosition))
            return true;

        float distance = Vector2.Distance(position, playerPosition);
        if (maxSpawnDistanceFromPlayer > 0f && distance > maxSpawnDistanceFromPlayer)
            return false;

        if (nearPlayerRadius > 0f && distance < nearPlayerRadius)
            return false;

        return true;
    }

    private static Room FindRoomCoveringCell(TilePlacementGrid grid, Vector2Int cell)
    {
        for (int i = 0; i < RegisteredRooms.Count; i++)
        {
            Room room = RegisteredRooms[i];
            if (room == null)
                continue;

            Bounds bounds = grid.GetCellWorldBounds(cell);
            if (room.ContainsWorldPoint(bounds.center))
                return room;
        }

        return RegisteredRooms.Count > 0 ? RegisteredRooms[0] : null;
    }
}
