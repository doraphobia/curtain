using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DuoCurtain.RuntimeTileMesh
{
    [DisallowMultipleComponent]
    public class RuntimeTileMeshFusionSandbox : MonoBehaviour
    {
        [Header("Input")]
        public Camera worldCamera;
        public LayerMask blockLayerMask = ~0;
        public bool managementInputEnabled = true;
        public bool ignorePointerOverUI = true;
        public bool preserveGrabOffset = true;
        public bool snapExistingBlocksOnAwake = true;
        public bool mergeExistingBlocksOnAwake = true;

        [Header("Grid")]
        [Min(0.0001f)]
        public float gridSize = 1f;
        public Vector2 gridOrigin = Vector2.zero;

        [Header("Fusion")]
        public bool mergeAfterPlacement = true;
        public bool deactivateAbsorbedBlocksImmediately = true;
        public bool logFusionEvents = false;

        [Header("Fusion Doors")]
        public bool generateDoorsOnFusion = true;
        [Min(1)]
        public int doorSharedEdgeCells = 3;
        [Min(0.01f)]
        public float doorThickness = 0.5f;
        public Color doorColor = Color.black;
        public bool doorBlocksPlayer = true;

        [Header("Fusion Wall Visual")]
        public GameObject wallVisualPrefab;
        public Color wallDebugColor = new Color(0f, 0f, 0f, 0.9f);
        [Min(0.005f)]
        public float wallDebugLineWidth = 0.08f;

        [Header("Player Walkable Area")]
        public bool excludeSelectedBlockFromWalkableArea = true;
        public bool requireContinuousWalkablePath = true;
        [Range(0.05f, 1f)]
        public float pathSampleCellStep = 0.25f;

        [Header("Player Carry")]
        public PlayerControl playerControl;
        public bool carryPlayerWithSelectedBlock = true;

        [Header("Visual")]
        public int normalSortingOrder = 0;
        public int selectedSortingOrder = 10;
        public bool drawSceneGrid = true;
        public Vector2Int sceneGridHalfExtents = new Vector2Int(12, 7);
        public Color sceneGridColor = new Color(0.45f, 0.45f, 0.5f, 0.35f);

        private readonly List<RuntimeTileMeshDraggableBlock> blocks =
            new List<RuntimeTileMeshDraggableBlock>();

        private RuntimeTileMeshDraggableBlock hoveredBlock;
        private RuntimeTileMeshDraggableBlock selectedBlock;
        private Vector3 grabOffset;
        private bool selectedThisFrame;
        private RuntimeTileMeshDraggableBlock playerCarrierBlock;
        private Vector3 playerCarrierLocalOffset;

        public bool HasWalkableCells => CollectWalkableCells().Count > 0;
        public bool ManagementInputEnabled => managementInputEnabled;
        public bool IsCarryingPlayer => playerCarrierBlock != null;

        void Awake()
        {
            if (worldCamera == null)
                worldCamera = Camera.main;

            ResolvePlayerControl();

            RefreshBlocks();
            if (snapExistingBlocksOnAwake)
                SnapAllBlocksToGrid();

            if (mergeExistingBlocksOnAwake)
                MergeAllConnectedBlocks();
        }

        void Update()
        {
            if (worldCamera == null)
                return;

            if (!managementInputEnabled)
            {
                ClearHover();
                ReleaseCarriedPlayer();
                return;
            }

            if (selectedBlock == null && playerCarrierBlock != null)
                ReleaseCarriedPlayer();

            selectedThisFrame = false;
            Vector3 mouseWorld = ScreenToWorld(Input.mousePosition);
            bool pointerOverUI = ignorePointerOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

            if (selectedBlock != null)
            {
                MoveSelectedBlock(mouseWorld);
                if (!pointerOverUI && Input.GetMouseButtonDown(0) && !selectedThisFrame)
                    PlaceSelectedBlock();
                return;
            }

            if (pointerOverUI)
            {
                ClearHover();
                return;
            }

            UpdateHover(mouseWorld);
            if (Input.GetMouseButtonDown(0) && hoveredBlock != null)
                PickUpBlock(hoveredBlock, mouseWorld);
        }

        public void RefreshBlocks()
        {
            blocks.Clear();
            RuntimeTileMeshDraggableBlock[] foundBlocks = FindObjectsByType<RuntimeTileMeshDraggableBlock>(FindObjectsSortMode.None);
            for (int i = 0; i < foundBlocks.Length; i++)
            {
                if (foundBlocks[i] != null)
                    blocks.Add(foundBlocks[i]);
            }
        }

        public void SetManagementInputEnabled(bool enabled)
        {
            SetManagementInputEnabled(enabled, true);
        }

        public void SetManagementInputEnabled(bool enabled, bool placeSelectedBlockWhenDisabling)
        {
            if (managementInputEnabled == enabled)
                return;

            managementInputEnabled = enabled;
            if (enabled)
                return;

            ClearHover();
            if (selectedBlock == null)
                return;

            if (placeSelectedBlockWhenDisabling)
            {
                PlaceSelectedBlock();
                return;
            }

            selectedBlock.SetSelected(false);
            selectedBlock.SetSortingOrder(normalSortingOrder);
            selectedBlock = null;
            ReleaseCarriedPlayer();
        }

        public RuntimeTileMeshDraggableBlock SpawnBlock(
            RuntimeTileMeshDraggableBlock prefab,
            Vector3 worldPosition,
            bool beginDragging)
        {
            if (prefab == null)
                return null;

            Vector3 spawnPosition = SnapWorldPosition(worldPosition, prefab.transform.position.z);
            RuntimeTileMeshDraggableBlock block = Instantiate(prefab, spawnPosition, prefab.transform.rotation);
            block.name = prefab.name;
            block.gameObject.SetActive(true);
            block.SnapRootToGrid(gridSize, gridOrigin);
            block.RebuildAndRefresh();

            RefreshBlocks();
            if (!blocks.Contains(block))
                blocks.Add(block);

            if (beginDragging && managementInputEnabled)
                BeginDraggingBlockInternal(block, worldPosition, false, false);

            return block;
        }

        public void BeginDraggingBlock(RuntimeTileMeshDraggableBlock block, Vector3 pointerWorld, bool useGrabOffset)
        {
            BeginDraggingBlockInternal(block, pointerWorld, useGrabOffset, true);
        }

        private void BeginDraggingBlockInternal(
            RuntimeTileMeshDraggableBlock block,
            Vector3 pointerWorld,
            bool useGrabOffset,
            bool allowPlayerCarry)
        {
            if (!managementInputEnabled || block == null)
                return;

            if (selectedBlock != null && selectedBlock != block)
                PlaceSelectedBlock();

            ClearHover();
            selectedBlock = block;
            selectedBlock.SetSelected(true);
            selectedBlock.SetSortingOrder(selectedSortingOrder);
            grabOffset = useGrabOffset ? selectedBlock.transform.position - pointerWorld : Vector3.zero;
            selectedThisFrame = true;
            BindPlayerToSelectedBlock(allowPlayerCarry);
            MoveSelectedBlock(pointerWorld);
        }

        public Vector3 GetPointerWorldPosition()
        {
            if (worldCamera == null)
                worldCamera = Camera.main;

            return worldCamera != null ? ScreenToWorld(Input.mousePosition) : Vector3.zero;
        }

        public bool TryGetWorldBounds(out Bounds bounds)
        {
            if (blocks.Count == 0)
                RefreshBlocks();

            bool hasBounds = false;
            bounds = default(Bounds);
            for (int i = 0; i < blocks.Count; i++)
            {
                RuntimeTileMeshDraggableBlock block = blocks[i];
                if (block == null || !block.isActiveAndEnabled)
                    continue;

                HashSet<Vector2Int> blockCells = block.GetWorldCells(gridSize, gridOrigin);
                foreach (Vector2Int cell in blockCells)
                {
                    Bounds cellBounds = GetCellWorldBounds(cell);
                    if (!hasBounds)
                    {
                        bounds = cellBounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(cellBounds);
                    }
                }
            }

            return hasBounds;
        }

        private void UpdateHover(Vector3 mouseWorld)
        {
            RuntimeTileMeshDraggableBlock nextHover = FindBlockAt(mouseWorld);
            if (hoveredBlock == nextHover)
                return;

            if (hoveredBlock != null)
                hoveredBlock.SetHovered(false);

            hoveredBlock = nextHover;
            if (hoveredBlock != null)
                hoveredBlock.SetHovered(true);
        }

        private void ClearHover()
        {
            if (hoveredBlock == null)
                return;

            hoveredBlock.SetHovered(false);
            hoveredBlock = null;
        }

        private void PickUpBlock(RuntimeTileMeshDraggableBlock block, Vector3 mouseWorld)
        {
            if (block == null)
                return;

            if (hoveredBlock != null)
            {
                hoveredBlock.SetHovered(false);
                hoveredBlock = null;
            }

            selectedBlock = block;
            selectedBlock.SetSelected(true);
            selectedBlock.SetSortingOrder(selectedSortingOrder);
            grabOffset = preserveGrabOffset ? selectedBlock.transform.position - mouseWorld : Vector3.zero;
            selectedThisFrame = true;
            BindPlayerToSelectedBlock(true);
            MoveSelectedBlock(mouseWorld);
        }

        private void MoveSelectedBlock(Vector3 mouseWorld)
        {
            if (selectedBlock == null)
                return;

            Vector3 desired = preserveGrabOffset ? mouseWorld + grabOffset : mouseWorld;
            Vector3 snapped = SnapWorldPosition(desired, selectedBlock.transform.position.z);
            selectedBlock.transform.position = snapped;
            MoveCarriedPlayerWithSelectedBlock();
        }

        private void PlaceSelectedBlock()
        {
            RuntimeTileMeshDraggableBlock placed = selectedBlock;
            selectedBlock = null;
            ReleaseCarriedPlayer();

            placed.SetSelected(false);
            placed.SetSortingOrder(normalSortingOrder);
            placed.SnapRootToGrid(gridSize, gridOrigin);

            if (mergeAfterPlacement)
                MergeConnectedBlocks(placed);
        }

        private void BindPlayerToSelectedBlock(bool allowPlayerCarry)
        {
            ReleaseCarriedPlayer();
            if (!allowPlayerCarry || !carryPlayerWithSelectedBlock || selectedBlock == null)
                return;

            ResolvePlayerControl();
            if (playerControl == null || !playerControl.HasPlayerWorldPosition)
                return;

            Vector3 playerWorldPosition = playerControl.PlayerWorldPosition;
            HashSet<Vector2Int> selectedCells = selectedBlock.GetWorldCells(gridSize, gridOrigin);
            if (!selectedCells.Contains(WorldPointToOccupiedCell(playerWorldPosition)))
                return;

            playerCarrierBlock = selectedBlock;
            playerCarrierLocalOffset = playerWorldPosition - selectedBlock.transform.position;
        }

        private void MoveCarriedPlayerWithSelectedBlock()
        {
            if (playerCarrierBlock == null || playerCarrierBlock != selectedBlock)
                return;

            ResolvePlayerControl();
            if (playerControl == null)
            {
                ReleaseCarriedPlayer();
                return;
            }

            Vector3 carriedWorldPosition = playerCarrierBlock.transform.position + playerCarrierLocalOffset;
            carriedWorldPosition.z = 0f;
            playerControl.SetWorldPositionImmediate(carriedWorldPosition);
        }

        private void ReleaseCarriedPlayer()
        {
            playerCarrierBlock = null;
            playerCarrierLocalOffset = Vector3.zero;
        }

        private void ResolvePlayerControl()
        {
            if (playerControl == null)
                playerControl = PlayerControl.Active != null ? PlayerControl.Active : FindFirstObjectByType<PlayerControl>();
        }

        [ContextMenu("Merge All Connected Blocks")]
        public int MergeAllConnectedBlocks()
        {
            RefreshBlocks();
            return MergeAllConnectedBlocks(blocks);
        }

        public int MergeAllConnectedBlocks(IList<RuntimeTileMeshDraggableBlock> sourceBlocks)
        {
            if (sourceBlocks == null || sourceBlocks.Count == 0)
                return 0;

            List<RuntimeTileMeshDraggableBlock> activeBlocks = BuildActiveBlockList(sourceBlocks);
            int absorbedTotal = 0;

            bool mergedThisPass;
            do
            {
                mergedThisPass = false;
                for (int i = 0; i < activeBlocks.Count; i++)
                {
                    RuntimeTileMeshDraggableBlock seed = activeBlocks[i];
                    if (seed == null)
                        continue;

                    int absorbed = MergeConnectedBlockGroup(seed, activeBlocks);
                    if (absorbed > 0)
                    {
                        absorbedTotal += absorbed;
                        mergedThisPass = true;
                        break;
                    }
                }
            }
            while (mergedThisPass);

            sourceBlocks.Clear();
            for (int i = 0; i < activeBlocks.Count; i++)
            {
                if (activeBlocks[i] != null)
                    sourceBlocks.Add(activeBlocks[i]);
            }

            blocks.Clear();
            blocks.AddRange(activeBlocks);
            return absorbedTotal;
        }

        public int MergeConnectedBlocks(RuntimeTileMeshDraggableBlock placed)
        {
            if (placed == null)
                return 0;

            RefreshBlocks();
            if (!blocks.Contains(placed))
                blocks.Add(placed);

            int absorbed = MergeConnectedBlockGroup(placed, blocks);

            if (!blocks.Contains(placed))
                blocks.Add(placed);

            return absorbed;
        }

        public int MergeConnectedBlocks(
            RuntimeTileMeshDraggableBlock placed,
            IList<RuntimeTileMeshDraggableBlock> sourceBlocks)
        {
            if (placed == null || sourceBlocks == null)
                return 0;

            List<RuntimeTileMeshDraggableBlock> activeBlocks = BuildActiveBlockList(sourceBlocks);
            if (!activeBlocks.Contains(placed))
                activeBlocks.Add(placed);

            int absorbed = MergeConnectedBlockGroup(placed, activeBlocks);

            sourceBlocks.Clear();
            for (int i = 0; i < activeBlocks.Count; i++)
            {
                if (activeBlocks[i] != null)
                    sourceBlocks.Add(activeBlocks[i]);
            }

            blocks.Clear();
            blocks.AddRange(activeBlocks);
            return absorbed;
        }

        private int MergeConnectedBlockGroup(
            RuntimeTileMeshDraggableBlock seed,
            List<RuntimeTileMeshDraggableBlock> activeBlocks)
        {
            if (seed == null || activeBlocks == null || activeBlocks.Count <= 1)
                return 0;

            HashSet<RuntimeTileMeshDraggableBlock> group = new HashSet<RuntimeTileMeshDraggableBlock>();
            Dictionary<RuntimeTileMeshDraggableBlock, HashSet<Vector2Int>> groupCellSets =
                new Dictionary<RuntimeTileMeshDraggableBlock, HashSet<Vector2Int>>();
            HashSet<Vector2Int> seedCells = seed.GetWorldCells(gridSize, gridOrigin);
            HashSet<Vector2Int> mergedCells = new HashSet<Vector2Int>(seedCells);
            group.Add(seed);
            groupCellSets[seed] = seedCells;

            bool expanded;
            do
            {
                expanded = false;
                for (int i = 0; i < activeBlocks.Count; i++)
                {
                    RuntimeTileMeshDraggableBlock candidate = activeBlocks[i];
                    if (candidate == null || group.Contains(candidate))
                        continue;

                    HashSet<Vector2Int> candidateCells = candidate.GetWorldCells(gridSize, gridOrigin);
                    if (!RuntimeTileMeshDraggableBlock.CellSetsOverlapOrShareEdge(mergedCells, candidateCells))
                        continue;

                    group.Add(candidate);
                    groupCellSets[candidate] = candidateCells;
                    foreach (Vector2Int cell in candidateCells)
                        mergedCells.Add(cell);

                    expanded = true;
                }
            }
            while (expanded);

            if (group.Count <= 1)
                return 0;

            List<RuntimeTileMeshFusionDoor> carriedDoors = DetachGroupDoors(group);
            List<DoorCandidate> doorCandidates = generateDoorsOnFusion
                ? CollectDoorCandidates(groupCellSets)
                : null;

            seed.SetHovered(false);
            seed.SetSelected(false);
            seed.SetSortingOrder(normalSortingOrder);
            seed.ApplyWorldCells(mergedCells, gridSize, gridOrigin);
            AttachDoorsToSeed(carriedDoors, seed);
            CreateFusionDoors(seed, doorCandidates);

            int absorbed = 0;
            for (int i = activeBlocks.Count - 1; i >= 0; i--)
            {
                RuntimeTileMeshDraggableBlock candidate = activeBlocks[i];
                if (candidate == null || candidate == seed || !group.Contains(candidate))
                    continue;

                activeBlocks.RemoveAt(i);
                RemoveAbsorbedBlock(candidate);
                absorbed++;
            }

            if (logFusionEvents)
                Debug.Log("[RuntimeTileMeshFusionSandbox] Merged " + (absorbed + 1) + " block(s) into " + seed.name + " with " + mergedCells.Count + " occupied cell(s).", seed);

            return absorbed;
        }

        public bool ContainsWorldPoint(Vector3 worldPosition)
        {
            return ContainsWorldPoint(worldPosition, 0f);
        }

        public bool ContainsWorldPoint(Vector3 worldPosition, float clearanceRadius)
        {
            return ContainsWorldPoint(worldPosition, clearanceRadius, CollectWalkableCells());
        }

        public Vector3 ClampPlayerWorldPoint(Vector3 desiredWorldPoint, Vector3 previousWorldPoint, float playerRadius)
        {
            HashSet<Vector2Int> walkableCells = CollectWalkableCells();
            if (walkableCells.Count == 0)
                return desiredWorldPoint;

            desiredWorldPoint.z = 0f;
            previousWorldPoint.z = 0f;
            playerRadius = Mathf.Max(0f, playerRadius);

            if (ContainsWorldPoint(previousWorldPoint, playerRadius, walkableCells))
                return ClampSegmentToWalkableArea(previousWorldPoint, desiredWorldPoint, playerRadius, walkableCells);

            if (ContainsWorldPoint(desiredWorldPoint, playerRadius, walkableCells))
                return desiredWorldPoint;

            return GetNearestWalkablePoint(desiredWorldPoint, playerRadius, walkableCells);
        }

        public bool TryGetRandomBlockCenter(out Vector3 worldPosition)
        {
            if (blocks.Count == 0)
                RefreshBlocks();

            List<RuntimeTileMeshDraggableBlock> activeBlocks = BuildActiveBlockList(blocks);
            for (int i = activeBlocks.Count - 1; i >= 0; i--)
            {
                RuntimeTileMeshDraggableBlock block = activeBlocks[i];
                if (block == null || !block.isActiveAndEnabled)
                    activeBlocks.RemoveAt(i);
            }

            if (activeBlocks.Count == 0)
            {
                worldPosition = Vector3.zero;
                return false;
            }

            RuntimeTileMeshDraggableBlock spawnBlock = activeBlocks[Random.Range(0, activeBlocks.Count)];
            HashSet<Vector2Int> cells = spawnBlock.GetWorldCells(gridSize, gridOrigin);
            if (cells.Count == 0)
            {
                worldPosition = spawnBlock.transform.position;
                worldPosition.z = 0f;
                return true;
            }

            worldPosition = GetCellSetCenterPoint(cells);
            if (!ContainsWorldPoint(worldPosition, 0f, cells))
                worldPosition = GetNearestWalkablePoint(worldPosition, 0f, cells);

            worldPosition.z = 0f;
            return true;
        }

        private List<RuntimeTileMeshFusionDoor> DetachGroupDoors(HashSet<RuntimeTileMeshDraggableBlock> group)
        {
            List<RuntimeTileMeshFusionDoor> doors = new List<RuntimeTileMeshFusionDoor>();
            if (group == null)
                return doors;

            foreach (RuntimeTileMeshDraggableBlock block in group)
            {
                if (block == null)
                    continue;

                RuntimeTileMeshFusionDoor[] blockDoors = block.GetComponentsInChildren<RuntimeTileMeshFusionDoor>(true);
                for (int i = 0; i < blockDoors.Length; i++)
                {
                    RuntimeTileMeshFusionDoor door = blockDoors[i];
                    if (door == null || doors.Contains(door))
                        continue;

                    door.transform.SetParent(transform, true);
                    doors.Add(door);
                }
            }

            return doors;
        }

        private static void AttachDoorsToSeed(List<RuntimeTileMeshFusionDoor> doors, RuntimeTileMeshDraggableBlock seed)
        {
            if (doors == null || seed == null)
                return;

            for (int i = 0; i < doors.Count; i++)
            {
                RuntimeTileMeshFusionDoor door = doors[i];
                if (door != null)
                    door.transform.SetParent(seed.transform, true);
            }
        }

        private void CreateFusionDoors(RuntimeTileMeshDraggableBlock seed, List<DoorCandidate> candidates)
        {
            if (seed == null || candidates == null || candidates.Count == 0)
                return;

            RuntimeTileMeshFusionDoor[] existingDoors = seed.GetComponentsInChildren<RuntimeTileMeshFusionDoor>(true);
            float duplicateEpsilon = Mathf.Max(0.001f, Mathf.Abs(gridSize) * 0.05f);

            for (int i = 0; i < candidates.Count; i++)
            {
                DoorCandidate candidate = candidates[i];
                if (HasDoorAt(existingDoors, candidate.axis, candidate.center, duplicateEpsilon))
                    continue;

                GameObject doorObject = new GameObject("Fusion Door - " + candidate.key);
                doorObject.transform.SetParent(seed.transform, true);

                RuntimeTileMeshFusionDoor door = doorObject.AddComponent<RuntimeTileMeshFusionDoor>();
                door.Configure(
                    candidate.axis,
                    candidate.center,
                    Mathf.Max(0.0001f, Mathf.Abs(gridSize)),
                    candidate.key,
                    doorThickness,
                    doorColor,
                    candidate.edgeCoordinate,
                    candidate.variableStart,
                    candidate.runLength,
                    wallVisualPrefab,
                    wallDebugColor,
                    wallDebugLineWidth);
            }
        }

        private static bool HasDoorAt(
            RuntimeTileMeshFusionDoor[] existingDoors,
            RuntimeTileMeshFusionDoor.DoorAxis axis,
            Vector2 center,
            float epsilon)
        {
            if (existingDoors == null)
                return false;

            for (int i = 0; i < existingDoors.Length; i++)
            {
                RuntimeTileMeshFusionDoor door = existingDoors[i];
                if (door != null && door.IsSameDoor(axis, center, epsilon))
                    return true;
            }

            return false;
        }

        private List<DoorCandidate> CollectDoorCandidates(
            Dictionary<RuntimeTileMeshDraggableBlock, HashSet<Vector2Int>> groupCellSets)
        {
            List<DoorCandidate> candidates = new List<DoorCandidate>();
            if (groupCellSets == null || groupCellSets.Count <= 1)
                return candidates;

            List<HashSet<Vector2Int>> cellSets = new List<HashSet<Vector2Int>>(groupCellSets.Values);
            HashSet<string> candidateKeys = new HashSet<string>();
            for (int i = 0; i < cellSets.Count; i++)
            {
                for (int j = i + 1; j < cellSets.Count; j++)
                    CollectDoorCandidatesBetween(cellSets[i], cellSets[j], candidates, candidateKeys);
            }

            return candidates;
        }

        private void CollectDoorCandidatesBetween(
            HashSet<Vector2Int> firstCells,
            HashSet<Vector2Int> secondCells,
            List<DoorCandidate> candidates,
            HashSet<string> candidateKeys)
        {
            if (firstCells == null || secondCells == null || candidates == null || candidateKeys == null)
                return;

            Dictionary<string, List<SharedEdgeSegment>> segmentsByLine =
                new Dictionary<string, List<SharedEdgeSegment>>();
            HashSet<string> segmentKeys = new HashSet<string>();

            foreach (Vector2Int cell in firstCells)
            {
                AddSharedEdgeSegment(cell, cell + Vector2Int.right, secondCells, segmentsByLine, segmentKeys);
                AddSharedEdgeSegment(cell, cell + Vector2Int.left, secondCells, segmentsByLine, segmentKeys);
                AddSharedEdgeSegment(cell, cell + Vector2Int.up, secondCells, segmentsByLine, segmentKeys);
                AddSharedEdgeSegment(cell, cell + Vector2Int.down, secondCells, segmentsByLine, segmentKeys);
            }

            foreach (KeyValuePair<string, List<SharedEdgeSegment>> pair in segmentsByLine)
            {
                List<SharedEdgeSegment> segments = pair.Value;
                segments.Sort((a, b) => a.variable.CompareTo(b.variable));

                int runStartIndex = 0;
                while (runStartIndex < segments.Count)
                {
                    int runEndIndex = runStartIndex;
                    while (runEndIndex + 1 < segments.Count &&
                           segments[runEndIndex + 1].variable == segments[runEndIndex].variable + 1)
                    {
                        runEndIndex++;
                    }

                    int runLength = runEndIndex - runStartIndex + 1;
                    if (runLength == Mathf.Max(1, doorSharedEdgeCells))
                    {
                        SharedEdgeSegment first = segments[runStartIndex];
                        int variableStart = first.variable;
                        float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
                        Vector2 center = first.axis == RuntimeTileMeshFusionDoor.DoorAxis.Vertical
                            ? new Vector2(
                                gridOrigin.x + first.edgeCoordinate * safeGridSize,
                                gridOrigin.y + (variableStart + runLength * 0.5f) * safeGridSize)
                            : new Vector2(
                                gridOrigin.x + (variableStart + runLength * 0.5f) * safeGridSize,
                                gridOrigin.y + first.edgeCoordinate * safeGridSize);

                        string key = first.axis + ":" + first.edgeCoordinate + ":" + variableStart;
                        if (candidateKeys.Add(key))
                        {
                            candidates.Add(new DoorCandidate
                            {
                                axis = first.axis,
                                center = center,
                                key = key,
                                edgeCoordinate = first.edgeCoordinate,
                                variableStart = variableStart,
                                runLength = runLength
                            });
                        }
                    }

                    runStartIndex = runEndIndex + 1;
                }
            }
        }

        private static void AddSharedEdgeSegment(
            Vector2Int sourceCell,
            Vector2Int neighborCell,
            HashSet<Vector2Int> neighborCells,
            Dictionary<string, List<SharedEdgeSegment>> segmentsByLine,
            HashSet<string> segmentKeys)
        {
            if (!neighborCells.Contains(neighborCell))
                return;

            RuntimeTileMeshFusionDoor.DoorAxis axis;
            int edgeCoordinate;
            int variable;

            if (neighborCell.x != sourceCell.x)
            {
                axis = RuntimeTileMeshFusionDoor.DoorAxis.Vertical;
                edgeCoordinate = Mathf.Max(sourceCell.x, neighborCell.x);
                variable = sourceCell.y;
            }
            else
            {
                axis = RuntimeTileMeshFusionDoor.DoorAxis.Horizontal;
                edgeCoordinate = Mathf.Max(sourceCell.y, neighborCell.y);
                variable = sourceCell.x;
            }

            string lineKey = axis + ":" + edgeCoordinate;
            string segmentKey = lineKey + ":" + variable;
            if (!segmentKeys.Add(segmentKey))
                return;

            if (!segmentsByLine.TryGetValue(lineKey, out List<SharedEdgeSegment> segments))
            {
                segments = new List<SharedEdgeSegment>();
                segmentsByLine[lineKey] = segments;
            }

            segments.Add(new SharedEdgeSegment
            {
                axis = axis,
                edgeCoordinate = edgeCoordinate,
                variable = variable
            });
        }

        private HashSet<Vector2Int> CollectWalkableCells()
        {
            if (blocks.Count == 0)
                RefreshBlocks();

            HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
            for (int i = 0; i < blocks.Count; i++)
            {
                RuntimeTileMeshDraggableBlock block = blocks[i];
                if (block == null || !block.isActiveAndEnabled)
                    continue;

                if (excludeSelectedBlockFromWalkableArea && block == selectedBlock)
                    continue;

                HashSet<Vector2Int> blockCells = block.GetWorldCells(gridSize, gridOrigin);
                foreach (Vector2Int cell in blockCells)
                    cells.Add(cell);
            }

            return cells;
        }

        private bool ContainsWorldPoint(
            Vector3 worldPosition,
            float clearanceRadius,
            HashSet<Vector2Int> walkableCells)
        {
            if (walkableCells == null || walkableCells.Count == 0)
                return false;

            if (!walkableCells.Contains(WorldPointToOccupiedCell(worldPosition)))
                return false;

            clearanceRadius = Mathf.Max(0f, clearanceRadius);
            if (clearanceRadius <= 0.0001f)
                return true;

            float diagonal = clearanceRadius * 0.7071f;
            return walkableCells.Contains(WorldPointToOccupiedCell(worldPosition + new Vector3(clearanceRadius, 0f, 0f))) &&
                   walkableCells.Contains(WorldPointToOccupiedCell(worldPosition + new Vector3(-clearanceRadius, 0f, 0f))) &&
                   walkableCells.Contains(WorldPointToOccupiedCell(worldPosition + new Vector3(0f, clearanceRadius, 0f))) &&
                   walkableCells.Contains(WorldPointToOccupiedCell(worldPosition + new Vector3(0f, -clearanceRadius, 0f))) &&
                   walkableCells.Contains(WorldPointToOccupiedCell(worldPosition + new Vector3(diagonal, diagonal, 0f))) &&
                   walkableCells.Contains(WorldPointToOccupiedCell(worldPosition + new Vector3(-diagonal, diagonal, 0f))) &&
                   walkableCells.Contains(WorldPointToOccupiedCell(worldPosition + new Vector3(diagonal, -diagonal, 0f))) &&
                   walkableCells.Contains(WorldPointToOccupiedCell(worldPosition + new Vector3(-diagonal, -diagonal, 0f)));
        }

        private Vector3 ClampSegmentToWalkableArea(
            Vector3 from,
            Vector3 to,
            float clearanceRadius,
            HashSet<Vector2Int> walkableCells)
        {
            if (!requireContinuousWalkablePath && ContainsWorldPoint(to, clearanceRadius, walkableCells))
                return to;

            Vector3 lastValid = from;
            Vector3 firstInvalid = to;
            bool foundInvalid = false;
            int steps = GetSegmentSampleCount(from, to);

            for (int i = 1; i <= steps; i++)
            {
                Vector3 sample = Vector3.Lerp(from, to, i / (float)steps);
                if (TryToggleDoorFromMovement(lastValid, sample, clearanceRadius))
                    return lastValid;

                if (ContainsWorldPoint(sample, clearanceRadius, walkableCells))
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
                if (ContainsWorldPoint(mid, clearanceRadius, walkableCells))
                    low = mid;
                else
                    high = mid;
            }

            return low;
        }

        private bool TryToggleDoorFromMovement(Vector3 from, Vector3 to, float playerRadius)
        {
            if (!doorBlocksPlayer)
                return false;

            RuntimeTileMeshFusionDoor[] doors = GetComponentsInChildren<RuntimeTileMeshFusionDoor>(true);
            for (int i = 0; i < doors.Length; i++)
            {
                RuntimeTileMeshFusionDoor door = doors[i];
                if (door != null && door.isActiveAndEnabled && door.TryBlockMovement(from, to, playerRadius))
                    return true;
            }

            return false;
        }

        private int GetSegmentSampleCount(Vector3 from, Vector3 to)
        {
            float distance = Vector2.Distance(from, to);
            if (distance <= 0.0001f)
                return 1;

            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            float sampleDistance = Mathf.Max(0.05f, safeGridSize * Mathf.Clamp(pathSampleCellStep, 0.05f, 1f));
            return Mathf.Max(1, Mathf.CeilToInt(distance / sampleDistance));
        }

        private Vector3 GetNearestWalkablePoint(
            Vector3 desiredWorldPoint,
            float clearanceRadius,
            HashSet<Vector2Int> walkableCells)
        {
            bool hasCandidate = false;
            Vector3 bestPoint = desiredWorldPoint;
            float bestDistanceSqr = float.MaxValue;

            foreach (Vector2Int cell in walkableCells)
            {
                Bounds bounds = GetCellWorldBounds(cell);
                Vector3 candidate = ClampToBoundsWithClearance(desiredWorldPoint, bounds, clearanceRadius);

                if (!ContainsWorldPoint(candidate, clearanceRadius, walkableCells))
                {
                    candidate = bounds.center;
                    candidate.z = 0f;
                    if (!ContainsWorldPoint(candidate, clearanceRadius, walkableCells))
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

        private Bounds GetCellWorldBounds(Vector2Int cell)
        {
            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            Vector3 min = new Vector3(
                gridOrigin.x + cell.x * safeGridSize,
                gridOrigin.y + cell.y * safeGridSize,
                0f);
            Vector3 size = new Vector3(safeGridSize, safeGridSize, 0.01f);
            return new Bounds(min + size * 0.5f, size);
        }

        private Vector3 ClampToBoundsWithClearance(Vector3 point, Bounds bounds, float clearanceRadius)
        {
            clearanceRadius = Mathf.Max(0f, clearanceRadius);
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
                0f);
        }

        private Vector3 GetCellSetCenterPoint(HashSet<Vector2Int> cells)
        {
            bool hasValue = false;
            Vector2Int min = Vector2Int.zero;
            Vector2Int max = Vector2Int.zero;
            foreach (Vector2Int cell in cells)
            {
                if (!hasValue)
                {
                    min = cell;
                    max = cell;
                    hasValue = true;
                    continue;
                }

                min = new Vector2Int(Mathf.Min(min.x, cell.x), Mathf.Min(min.y, cell.y));
                max = new Vector2Int(Mathf.Max(max.x, cell.x), Mathf.Max(max.y, cell.y));
            }

            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            return new Vector3(
                gridOrigin.x + (min.x + max.x + 1f) * safeGridSize * 0.5f,
                gridOrigin.y + (min.y + max.y + 1f) * safeGridSize * 0.5f,
                0f);
        }

        private Vector2Int WorldPointToOccupiedCell(Vector3 worldPosition)
        {
            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            return new Vector2Int(
                Mathf.FloorToInt((worldPosition.x - gridOrigin.x) / safeGridSize),
                Mathf.FloorToInt((worldPosition.y - gridOrigin.y) / safeGridSize));
        }

        private static List<RuntimeTileMeshDraggableBlock> BuildActiveBlockList(IList<RuntimeTileMeshDraggableBlock> sourceBlocks)
        {
            List<RuntimeTileMeshDraggableBlock> activeBlocks = new List<RuntimeTileMeshDraggableBlock>();
            for (int i = 0; i < sourceBlocks.Count; i++)
            {
                RuntimeTileMeshDraggableBlock block = sourceBlocks[i];
                if (block != null && !activeBlocks.Contains(block))
                    activeBlocks.Add(block);
            }

            return activeBlocks;
        }

        private void RemoveAbsorbedBlock(RuntimeTileMeshDraggableBlock block)
        {
            if (block == null)
                return;

            block.SetHovered(false);
            block.SetSelected(false);

            if (deactivateAbsorbedBlocksImmediately)
                block.gameObject.SetActive(false);

            if (Application.isPlaying)
                Destroy(block.gameObject);
            else
                DestroyImmediate(block.gameObject);
        }

        private void SnapAllBlocksToGrid()
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                RuntimeTileMeshDraggableBlock block = blocks[i];
                if (block != null)
                    block.SnapRootToGrid(gridSize, gridOrigin);
            }
        }

        private RuntimeTileMeshDraggableBlock FindBlockAt(Vector3 worldPoint)
        {
            bool oldQueriesHitTriggers = Physics2D.queriesHitTriggers;
            Physics2D.queriesHitTriggers = true;
            Collider2D[] hits = Physics2D.OverlapPointAll(worldPoint, blockLayerMask);
            Physics2D.queriesHitTriggers = oldQueriesHitTriggers;

            RuntimeTileMeshDraggableBlock bestBlock = null;
            int bestSortingOrder = int.MinValue;
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i];
                if (hit == null)
                    continue;

                RuntimeTileMeshDraggableBlock block = hit.GetComponentInParent<RuntimeTileMeshDraggableBlock>();
                if (block == null || block == selectedBlock)
                    continue;

                RuntimeTileMeshView view = block.View;
                int sortingOrder = view != null ? view.sortingOrder : 0;
                if (bestBlock != null && sortingOrder < bestSortingOrder)
                    continue;

                bestBlock = block;
                bestSortingOrder = sortingOrder;
            }

            return bestBlock;
        }

        private Vector3 ScreenToWorld(Vector3 screenPosition)
        {
            Vector3 world = worldCamera.ScreenToWorldPoint(screenPosition);
            world.z = 0f;
            return world;
        }

        private Vector3 SnapWorldPosition(Vector3 worldPosition, float z)
        {
            Vector2Int cell = RuntimeTileMeshDraggableBlock.WorldToCell(worldPosition, gridSize, gridOrigin);
            return RuntimeTileMeshDraggableBlock.CellToWorld(cell, gridSize, gridOrigin, z);
        }

        void OnDrawGizmos()
        {
            if (!drawSceneGrid)
                return;

            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            Gizmos.color = sceneGridColor;

            int minX = -Mathf.Abs(sceneGridHalfExtents.x);
            int maxX = Mathf.Abs(sceneGridHalfExtents.x);
            int minY = -Mathf.Abs(sceneGridHalfExtents.y);
            int maxY = Mathf.Abs(sceneGridHalfExtents.y);

            for (int x = minX; x <= maxX; x++)
            {
                Vector3 a = new Vector3(gridOrigin.x + x * safeGridSize, gridOrigin.y + minY * safeGridSize, 0f);
                Vector3 b = new Vector3(gridOrigin.x + x * safeGridSize, gridOrigin.y + maxY * safeGridSize, 0f);
                Gizmos.DrawLine(a, b);
            }

            for (int y = minY; y <= maxY; y++)
            {
                Vector3 a = new Vector3(gridOrigin.x + minX * safeGridSize, gridOrigin.y + y * safeGridSize, 0f);
                Vector3 b = new Vector3(gridOrigin.x + maxX * safeGridSize, gridOrigin.y + y * safeGridSize, 0f);
                Gizmos.DrawLine(a, b);
            }
        }

        private struct SharedEdgeSegment
        {
            public RuntimeTileMeshFusionDoor.DoorAxis axis;
            public int edgeCoordinate;
            public int variable;
        }

        private struct DoorCandidate
        {
            public RuntimeTileMeshFusionDoor.DoorAxis axis;
            public Vector2 center;
            public string key;
            public int edgeCoordinate;
            public int variableStart;
            public int runLength;
        }
    }
}
