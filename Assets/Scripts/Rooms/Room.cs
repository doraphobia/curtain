using UnityEngine;

/// <summary>
/// Minimal room metadata for enemy AI: one window, one exterior door, interior area.
/// Place on a room root object. Interior is detected via <see cref="roomAreaCollider"/> or grid fallback.
/// </summary>
[DisallowMultipleComponent]
public class Room : MonoBehaviour
{
    [Header("References")]
    public WindowPortal window;
    public BreakableExteriorDoor exteriorDoor;
    [Tooltip("Trigger collider covering the white floor / interior walkable area.")]
    public Collider2D roomAreaCollider;

    [Header("Identity")]
    public string roomId;

    public bool HasExteriorDoor => exteriorDoor != null;
    public bool HasWindow => window != null;

    void Awake()
    {
        if (string.IsNullOrWhiteSpace(roomId))
            roomId = name;

        if (window != null && window.ownerRoom == null)
            window.ownerRoom = this;

        if (exteriorDoor != null && exteriorDoor.ownerRoom == null)
            exteriorDoor.ownerRoom = this;

        RoomManager.Register(this);
    }

    void OnDestroy()
    {
        RoomManager.Unregister(this);
    }

    void OnValidate()
    {
        if (window == null)
            window = GetComponentInChildren<WindowPortal>(true);

        if (exteriorDoor == null)
            exteriorDoor = GetComponentInChildren<BreakableExteriorDoor>(true);

        if (roomAreaCollider == null)
            roomAreaCollider = GetComponentInChildren<Collider2D>(true);
    }

    public bool ContainsWorldPoint(Vector2 worldPoint)
    {
        if (roomAreaCollider != null)
            return roomAreaCollider.OverlapPoint(worldPoint);

        return false;
    }
}
