using System;
using System.Collections.Generic;
using DuoCurtain.RuntimeTileMesh;
using UnityEngine;

[DisallowMultipleComponent]
public class TopologyMapDataProvider : MonoBehaviour
{
    [Header("Topology Source")]
    public TilePlacementGrid topologyGrid;
    public RuntimeTileMeshFusionSandbox fusionSandbox;
    public bool autoFindSource = true;
    public bool useRuntimeFusionFallback = true;

    [Header("Runtime")]
    public bool refreshOnEnable = true;
    public bool pollForExternalChanges = true;

    private readonly List<Vector2Int> roomCells = new List<Vector2Int>();
    private readonly HashSet<Vector2Int> roomCellLookup = new HashSet<Vector2Int>();
    private RectInt roomCellBounds;
    private bool hasTopology;
    private bool usingRuntimeFusionSource;
    private int runtimeTopologyHash;

    public event Action<TopologyMapDataProvider> TopologyChanged;

    public IReadOnlyList<Vector2Int> RoomCells => roomCells;
    public RectInt RoomCellBounds => roomCellBounds;
    public bool HasTopology => hasTopology;
    public int RoomCellCount => roomCells.Count;
    public int TopologyVersion { get; private set; } = -1;
    public TilePlacementGrid Source => topologyGrid;
    public RuntimeTileMeshFusionSandbox RuntimeSource => fusionSandbox;

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

            if (topologyGrid == null && fusionSandbox == null)
                return;
        }

        if (pollForExternalChanges && topologyGrid != null && topologyGrid.TopologyVersion != TopologyVersion)
            Refresh(true);
        else if (pollForExternalChanges && topologyGrid == null && fusionSandbox != null)
            Refresh(true);
    }

    public void Bind(TilePlacementGrid source)
    {
        if (topologyGrid == source)
            return;

        UnsubscribeFromSource();
        topologyGrid = source;
        if (source != null)
            fusionSandbox = null;
        if (isActiveAndEnabled)
            SubscribeToSource();
        Refresh(true);
    }

    public void Bind(RuntimeTileMeshFusionSandbox source)
    {
        if (fusionSandbox == source && topologyGrid == null)
            return;

        UnsubscribeFromSource();
        topologyGrid = null;
        fusionSandbox = source;
        Refresh(true);
    }

    public void Refresh(bool notifyListeners)
    {
        int previousVersion = TopologyVersion;
        bool previousHasTopology = hasTopology;
        bool previousRuntimeSource = usingRuntimeFusionSource;

        roomCells.Clear();
        roomCellLookup.Clear();
        hasTopology = false;
        roomCellBounds = default(RectInt);
        usingRuntimeFusionSource = false;

        if (topologyGrid != null)
        {
            topologyGrid.CopyRoomCells(roomCells);
            hasTopology = topologyGrid.TryGetRoomCellBounds(out roomCellBounds);
            TopologyVersion = topologyGrid.TopologyVersion;
            runtimeTopologyHash = 0;
            CopyCellsToLookup();
        }
        else if (useRuntimeFusionFallback && fusionSandbox != null)
        {
            CopyRuntimeFusionCells(roomCells);
            SortCells(roomCells);
            CopyCellsToLookup();
            hasTopology = TryGetCellBounds(roomCells, out roomCellBounds);
            usingRuntimeFusionSource = true;

            int hash = CalculateCellHash(roomCells);
            if (TopologyVersion < 0 || hash != runtimeTopologyHash || !previousRuntimeSource)
                TopologyVersion = Mathf.Max(0, TopologyVersion + 1);
            runtimeTopologyHash = hash;
        }
        else
        {
            TopologyVersion = -1;
            runtimeTopologyHash = 0;
        }

        bool changed = previousVersion != TopologyVersion ||
                       previousHasTopology != hasTopology ||
                       previousRuntimeSource != usingRuntimeFusionSource;
        if (notifyListeners && changed)
            TopologyChanged?.Invoke(this);
    }

    public bool TryGetRoomCell(Vector3 worldPosition, out Vector2Int cell)
    {
        if (topologyGrid != null)
            return topologyGrid.TryGetRoomCell(worldPosition, out cell);

        if (usingRuntimeFusionSource && fusionSandbox != null)
        {
            cell = RuntimeWorldPointToCell(worldPosition);
            return roomCellLookup.Contains(cell);
        }

        cell = default(Vector2Int);
        return false;
    }

    public bool TryGetWorldLogicalPosition(Vector3 worldPosition, out Vector2 logicalPosition, out Vector2Int cell)
    {
        logicalPosition = default(Vector2);
        cell = default(Vector2Int);

        if (topologyGrid == null)
        {
            if (!usingRuntimeFusionSource || fusionSandbox == null)
                return false;

            cell = RuntimeWorldPointToCell(worldPosition);
            if (!roomCellLookup.Contains(cell))
                return false;

            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(fusionSandbox.gridSize));
            Vector2 runtimeMin = fusionSandbox.gridOrigin + (Vector2)cell * safeGridSize;
            float runtimeNormalizedX = Mathf.InverseLerp(runtimeMin.x, runtimeMin.x + safeGridSize, worldPosition.x);
            float runtimeNormalizedY = Mathf.InverseLerp(runtimeMin.y, runtimeMin.y + safeGridSize, worldPosition.y);
            logicalPosition = new Vector2(
                cell.x - 0.5f + Mathf.Clamp01(runtimeNormalizedX),
                cell.y - 0.5f + Mathf.Clamp01(runtimeNormalizedY));
            return true;
        }

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
        if (topologyGrid != null)
            return topologyGrid.IsRoomCell(cell);

        return roomCellLookup.Contains(cell);
    }

    private bool ResolveSource()
    {
        if (topologyGrid != null || !autoFindSource)
            return topologyGrid != null;

        if (topologyGrid == null)
            topologyGrid = FindFirstObjectByType<TilePlacementGrid>();

        if (topologyGrid == null && useRuntimeFusionFallback && fusionSandbox == null)
            fusionSandbox = FindFirstObjectByType<RuntimeTileMeshFusionSandbox>();

        return topologyGrid != null || fusionSandbox != null;
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

    private void CopyRuntimeFusionCells(List<Vector2Int> results)
    {
        if (results == null || fusionSandbox == null)
            return;

        HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
        RuntimeTileMeshDraggableBlock[] blocks =
            FindObjectsByType<RuntimeTileMeshDraggableBlock>(FindObjectsSortMode.None);

        for (int i = 0; i < blocks.Length; i++)
        {
            RuntimeTileMeshDraggableBlock block = blocks[i];
            if (block == null || !block.isActiveAndEnabled)
                continue;

            HashSet<Vector2Int> blockCells = block.GetWorldCells(
                fusionSandbox.gridSize,
                fusionSandbox.gridOrigin);
            foreach (Vector2Int cell in blockCells)
                cells.Add(cell);
        }

        foreach (Vector2Int cell in cells)
            results.Add(cell);
    }

    private void CopyCellsToLookup()
    {
        roomCellLookup.Clear();
        for (int i = 0; i < roomCells.Count; i++)
            roomCellLookup.Add(roomCells[i]);
    }

    private Vector2Int RuntimeWorldPointToCell(Vector3 worldPosition)
    {
        float safeGridSize = fusionSandbox != null
            ? Mathf.Max(0.0001f, Mathf.Abs(fusionSandbox.gridSize))
            : 1f;
        Vector2 origin = fusionSandbox != null ? fusionSandbox.gridOrigin : Vector2.zero;
        return new Vector2Int(
            Mathf.FloorToInt((worldPosition.x - origin.x) / safeGridSize),
            Mathf.FloorToInt((worldPosition.y - origin.y) / safeGridSize));
    }

    private static bool TryGetCellBounds(IReadOnlyList<Vector2Int> cells, out RectInt bounds)
    {
        if (cells == null || cells.Count == 0)
        {
            bounds = default(RectInt);
            return false;
        }

        Vector2Int min = cells[0];
        Vector2Int max = cells[0];
        for (int i = 1; i < cells.Count; i++)
        {
            min = Vector2Int.Min(min, cells[i]);
            max = Vector2Int.Max(max, cells[i]);
        }

        bounds = new RectInt(min.x, min.y, max.x - min.x + 1, max.y - min.y + 1);
        return true;
    }

    private static int CalculateCellHash(IReadOnlyList<Vector2Int> cells)
    {
        unchecked
        {
            int hash = 17;
            if (cells != null)
            {
                for (int i = 0; i < cells.Count; i++)
                {
                    hash = hash * 31 + cells[i].x;
                    hash = hash * 31 + cells[i].y;
                }
            }

            return hash;
        }
    }

    private static void SortCells(List<Vector2Int> cells)
    {
        if (cells == null)
            return;

        cells.Sort(CompareCells);
    }

    private static int CompareCells(Vector2Int a, Vector2Int b)
    {
        int yCompare = a.y.CompareTo(b.y);
        return yCompare != 0 ? yCompare : a.x.CompareTo(b.x);
    }
}
