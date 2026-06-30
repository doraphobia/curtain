using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class TopologyMapDataProvider : MonoBehaviour
{
    [Header("Topology Source")]
    public TilePlacementGrid topologyGrid;
    public bool autoFindSource = true;

    [Header("Runtime")]
    public bool refreshOnEnable = true;
    public bool pollForExternalChanges = true;

    private readonly List<Vector2Int> roomCells = new List<Vector2Int>();
    private RectInt roomCellBounds;
    private bool hasTopology;

    public event Action<TopologyMapDataProvider> TopologyChanged;

    public IReadOnlyList<Vector2Int> RoomCells => roomCells;
    public RectInt RoomCellBounds => roomCellBounds;
    public bool HasTopology => hasTopology;
    public int RoomCellCount => roomCells.Count;
    public int TopologyVersion { get; private set; } = -1;
    public TilePlacementGrid Source => topologyGrid;

    void OnEnable()
    {
        ResolveSource();
        SubscribeToSource();

        if (refreshOnEnable)
            Refresh(true);
    }

    void OnDisable()
    {
        UnsubscribeFromSource();
    }

    void Update()
    {
        if (topologyGrid == null)
        {
            if (autoFindSource && ResolveSource())
            {
                SubscribeToSource();
                Refresh(true);
            }

            return;
        }

        if (pollForExternalChanges && topologyGrid.TopologyVersion != TopologyVersion)
            Refresh(true);
    }

    public void Bind(TilePlacementGrid source)
    {
        if (topologyGrid == source)
            return;

        UnsubscribeFromSource();
        topologyGrid = source;
        if (isActiveAndEnabled)
            SubscribeToSource();
        Refresh(true);
    }

    public void Refresh(bool notifyListeners)
    {
        roomCells.Clear();
        hasTopology = false;
        roomCellBounds = default(RectInt);

        if (topologyGrid != null)
        {
            topologyGrid.CopyRoomCells(roomCells);
            hasTopology = topologyGrid.TryGetRoomCellBounds(out roomCellBounds);
            TopologyVersion = topologyGrid.TopologyVersion;
        }
        else
        {
            TopologyVersion = -1;
        }

        if (notifyListeners)
            TopologyChanged?.Invoke(this);
    }

    public bool TryGetRoomCell(Vector3 worldPosition, out Vector2Int cell)
    {
        if (topologyGrid != null)
            return topologyGrid.TryGetRoomCell(worldPosition, out cell);

        cell = default(Vector2Int);
        return false;
    }

    public bool TryGetWorldLogicalPosition(Vector3 worldPosition, out Vector2 logicalPosition, out Vector2Int cell)
    {
        logicalPosition = default(Vector2);
        cell = default(Vector2Int);

        if (topologyGrid == null)
            return false;

        cell = topologyGrid.WorldToCell(worldPosition);
        Bounds bounds = topologyGrid.GetCellWorldBounds(cell);
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        float normalizedX = Mathf.InverseLerp(min.x, max.x, worldPosition.x);
        float normalizedY = Mathf.InverseLerp(min.y, max.y, worldPosition.y);
        normalizedX = Mathf.Clamp01(normalizedX);
        normalizedY = Mathf.Clamp01(normalizedY);

        logicalPosition = new Vector2(
            cell.x - 0.5f + normalizedX,
            cell.y - 0.5f + normalizedY);
        return true;
    }

    public bool IsRoomCell(Vector2Int cell)
    {
        return topologyGrid != null && topologyGrid.IsRoomCell(cell);
    }

    private bool ResolveSource()
    {
        if (topologyGrid != null || !autoFindSource)
            return topologyGrid != null;

        topologyGrid = FindFirstObjectByType<TilePlacementGrid>();
        return topologyGrid != null;
    }

    private void SubscribeToSource()
    {
        if (topologyGrid != null)
            topologyGrid.TopologyChanged += HandleSourceTopologyChanged;
    }

    private void UnsubscribeFromSource()
    {
        if (topologyGrid != null)
            topologyGrid.TopologyChanged -= HandleSourceTopologyChanged;
    }

    private void HandleSourceTopologyChanged(TilePlacementGrid source)
    {
        Refresh(true);
    }
}
