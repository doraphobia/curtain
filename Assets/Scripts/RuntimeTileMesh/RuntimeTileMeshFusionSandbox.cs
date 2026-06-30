using System;
using System.Collections.Generic;
using DuoCurtain.Vision;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DuoCurtain.RuntimeTileMesh
{
    [DisallowMultipleComponent]
    public class RuntimeTileMeshFusionSandbox : MonoBehaviour, IVisibilitySegmentSource
    {
        [Header("Input")]
        public Camera worldCamera;
        public LayerMask blockLayerMask = ~0;
        public bool managementInputEnabled = true;
        public bool ignorePointerOverUI = true;
        public int cancelSelectionMouseButton = 1;
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

        [Header("Integrity Monitoring")]
        public bool recordFusionIntegrity = true;

        [Header("Visibility")]
        public bool registerFusionBlocksForVisibility = true;
        public bool includeSelectedBlockInVisibility = true;
        public bool logVisibilitySourceSegments = false;

        [Header("Fusion Doors")]
        public bool generateDoorsOnFusion = true;
        [Min(1)]
        public int doorSharedEdgeCells = 3;
        [Min(0.01f)]
        public float doorThickness = 0.25f;
        [Range(1f, 179f)]
        public float doorOpenAngleDegrees = 90f;
        [Header("Fusion Door Animation")]
        public bool animateDoors = true;
        [Min(0f)]
        public float doorOpenDuration = 0.25f;
        [Min(0f)]
        public float doorCloseDuration = 0.2f;
        public AnimationCurve doorSwingCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Range(0f, 1f)]
        public float doorPassableOpenAmount = 0.82f;
        public bool useDoorEndWobble = true;
        [Min(0f)]
        public float doorEndWobbleDuration = 0.18f;
        [Min(0f)]
        public float doorEndWobbleAmplitudeDegrees = 6f;
        [Min(0.5f)]
        public float doorEndWobbleOscillations = 2.5f;
        public Color doorColor = Color.black;
        public bool doorBlocksPlayer = true;
        public bool allowHeadingPointDoorInteraction = true;
        public int doorInteractionMouseButton = 0;
        [Min(0f)]
        public float doorInteractionRadius = 0.12f;

        [Header("Fusion Wall Visual")]
        public GameObject wallVisualPrefab;
        public Color wallDebugColor = new Color(0.38f, 0.38f, 0.38f, 0.95f);
        [Min(0.005f)]
        public float wallDebugLineWidth = 0.02f;

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

        [Header("Runtime Grid Overlay")]
        public bool renderRuntimeGridInGame = true;
        public bool disableLegacyRuntimeGridOverlay = true;
        public Color runtimeGridColor = new Color(1f, 1f, 1f, 0.42f);
        [Min(0.001f)]
        public float runtimeGridLineWidth = 0.012f;
        public int runtimeGridSortingOrder = -45;
        [Min(0f)]
        public float runtimeGridCameraPaddingCells = 2f;
        public Material runtimeGridMaterial;

        private readonly List<RuntimeTileMeshDraggableBlock> blocks =
            new List<RuntimeTileMeshDraggableBlock>();
        private readonly List<LineRenderer> runtimeGridLines = new List<LineRenderer>();

        private RuntimeTileMeshDraggableBlock hoveredBlock;
        private RuntimeTileMeshDraggableBlock selectedBlock;
        private Vector3 selectedStartPosition;
        private Vector3 grabOffset;
        private bool selectedThisFrame;
        private RuntimeTileMeshDraggableBlock playerCarrierBlock;
        private Vector3 playerCarrierLocalOffset;
        private readonly List<RuntimeTileMeshFusionDoor> registeredRuntimeDoors =
            new List<RuntimeTileMeshFusionDoor>();
        private readonly List<RuntimeTileMeshFusionDoor> doorBuffer = new List<RuntimeTileMeshFusionDoor>();
        private readonly HashSet<RuntimeTileMeshFusionDoor> playerContactingDoors =
            new HashSet<RuntimeTileMeshFusionDoor>();
        private readonly List<RuntimeTileMeshFusionDoor> playerDoorContactRemovalBuffer =
            new List<RuntimeTileMeshFusionDoor>();
        private int suppressPointerInputFrame = -1;
        private Transform runtimeGridRoot;
        private Material runtimeGridRuntimeMaterial;
        private bool legacyRuntimeGridChecked;

        public bool HasWalkableCells => CollectWalkableCells().Count > 0;
        public bool ManagementInputEnabled => managementInputEnabled;
        public bool IsCarryingPlayer => playerCarrierBlock != null;
        public RuntimeTileMeshDraggableBlock SelectedBlock => selectedBlock;

        public event Action<RuntimeTileMeshDraggableBlock> BlockPlaced;
        public event Action<RuntimeTileMeshDraggableBlock, bool> BlockSelectionCancelled;

        public struct FusionWallEdgePlacement
        {
            public RuntimeTileMeshFusionDoor.DoorAxis axis;
            public Vector2 center;
            public Vector2 normal;
            public Vector2 tangent;
            public Vector2Int ownerCell;
            public int edgeCoordinate;
            public int variable;
            public float distance;
        }

        void Awake()
        {
            if (worldCamera == null)
                worldCamera = Camera.main;

            ResolvePlayerControl();
            EnsureIntegrityMonitor();

            RefreshBlocks();
            if (snapExistingBlocksOnAwake)
                SnapAllBlocksToGrid();

            if (mergeExistingBlocksOnAwake)
                MergeAllConnectedBlocks();

            MarkVisibilityDirty();
        }

        void OnEnable()
        {
            VisibilityWorld.GetOrCreate().RegisterSource(this);
        }

        void OnDisable()
        {
            if (VisibilityWorld.Instance != null)
                VisibilityWorld.Instance.UnregisterSource(this);
        }

        void Update()
        {
            if (worldCamera == null)
                return;

            if (PauseManager.IsGamePaused)
                return;

            if (suppressPointerInputFrame == Time.frameCount)
            {
                ClearHover();
                return;
            }

            if (!managementInputEnabled)
            {
                HandlePlayerDoorContact();
                HandleDoorInteractionInput();
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
                if (Input.GetMouseButtonDown(Mathf.Max(0, cancelSelectionMouseButton)))
                {
                    CancelSelectedBlock(false);
                    return;
                }

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

        void LateUpdate()
        {
            UpdateRuntimeGridOverlay();
        }

        void OnDestroy()
        {
            if (runtimeGridRuntimeMaterial != null)
            {
                if (Application.isPlaying)
                    Destroy(runtimeGridRuntimeMaterial);
                else
                    DestroyImmediate(runtimeGridRuntimeMaterial);
            }
        }

        private void HandleDoorInteractionInput()
        {
            if (!allowHeadingPointDoorInteraction)
                return;

            if (!Input.GetMouseButtonDown(Mathf.Max(0, doorInteractionMouseButton)))
                return;

            if (ignorePointerOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            Vector3 interactionWorldPoint;
            if (!PlayerControl.TryGetInteractionWorldPosition(out interactionWorldPoint))
                interactionWorldPoint = GetPointerWorldPosition();

            Vector3 playerWorldPoint;
            if (!PlayerControl.TryGetPlayerWorldPosition(out playerWorldPoint))
                playerWorldPoint = interactionWorldPoint;

            List<RuntimeTileMeshFusionDoor> doors = CollectActiveDoors();
            for (int i = 0; i < doors.Count; i++)
            {
                RuntimeTileMeshFusionDoor door = doors[i];
                if (door != null && door.isActiveAndEnabled &&
                    door.TryInteract(interactionWorldPoint, playerWorldPoint, doorInteractionRadius))
                {
                    break;
                }
            }
        }

        private void HandlePlayerDoorContact()
        {
            if (!doorBlocksPlayer)
            {
                playerContactingDoors.Clear();
                return;
            }

            if (!PlayerControl.TryGetPlayerWorldPosition(out Vector3 playerWorldPoint))
            {
                playerContactingDoors.Clear();
                return;
            }

            float playerRadius = playerControl != null
                ? playerControl.PlayerCollisionRadius
                : (PlayerControl.Active != null ? PlayerControl.Active.PlayerCollisionRadius : 0f);

            playerDoorContactRemovalBuffer.Clear();
            foreach (RuntimeTileMeshFusionDoor trackedDoor in playerContactingDoors)
            {
                if (trackedDoor == null ||
                    !trackedDoor.isActiveAndEnabled ||
                    !trackedDoor.IsPointTouchingInteractionArea(playerWorldPoint, playerRadius))
                {
                    playerDoorContactRemovalBuffer.Add(trackedDoor);
                }
            }

            for (int i = 0; i < playerDoorContactRemovalBuffer.Count; i++)
                playerContactingDoors.Remove(playerDoorContactRemovalBuffer[i]);

            List<RuntimeTileMeshFusionDoor> doors = CollectActiveDoors();
            for (int i = 0; i < doors.Count; i++)
            {
                RuntimeTileMeshFusionDoor door = doors[i];
                if (door == null || !door.isActiveAndEnabled)
                    continue;

                if (!door.IsPointTouchingInteractionArea(playerWorldPoint, playerRadius))
                    continue;

                if (playerContactingDoors.Contains(door))
                    continue;

                door.TryToggleFromPlayerContact(playerWorldPoint, playerRadius);
                playerContactingDoors.Add(door);
            }
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

            CancelSelectedBlock(false);
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

            MarkVisibilityDirty();
            return block;
        }

        public void SuppressPointerInputForCurrentFrame()
        {
            suppressPointerInputFrame = Time.frameCount;
        }

        public bool CancelSelectedBlock(bool destroyBlock)
        {
            RuntimeTileMeshDraggableBlock cancelled = selectedBlock;
            if (cancelled == null)
                return false;

            selectedBlock = null;
            cancelled.SetSelected(false);
            cancelled.SetSortingOrder(normalSortingOrder);

            if (!destroyBlock)
            {
                cancelled.transform.position = selectedStartPosition;
                MoveCarriedPlayerTo(cancelled);
            }

            ReleaseCarriedPlayer();
            BlockSelectionCancelled?.Invoke(cancelled, destroyBlock);

            if (destroyBlock)
            {
                cancelled.gameObject.SetActive(false);
                blocks.Remove(cancelled);
                if (Application.isPlaying)
                    Destroy(cancelled.gameObject);
                else
                    DestroyImmediate(cancelled.gameObject);
            }

            MarkVisibilityDirty();
            return true;
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
            selectedStartPosition = block.transform.position;
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
            selectedStartPosition = block.transform.position;
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

            MarkVisibilityDirty();
            BlockPlaced?.Invoke(placed);
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

        private void MoveCarriedPlayerTo(RuntimeTileMeshDraggableBlock block)
        {
            if (playerCarrierBlock == null || playerCarrierBlock != block)
                return;

            ResolvePlayerControl();
            if (playerControl == null)
                return;

            Vector3 carriedWorldPosition = block.transform.position + playerCarrierLocalOffset;
            carriedWorldPosition.z = 0f;
            playerControl.SetWorldPositionImmediate(carriedWorldPosition);
        }

        private void ReleaseCarriedPlayer()
        {
            playerCarrierBlock = null;
            playerCarrierLocalOffset = Vector3.zero;
        }

        private void EnsureIntegrityMonitor()
        {
            if (!recordFusionIntegrity)
                return;

            RuntimeTileMeshFusionIntegrityMonitor monitor = GetComponent<RuntimeTileMeshFusionIntegrityMonitor>();
            if (monitor == null)
                monitor = gameObject.AddComponent<RuntimeTileMeshFusionIntegrityMonitor>();

            if (monitor.fusionSandbox == null)
                monitor.fusionSandbox = this;
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
            if (absorbedTotal > 0)
                MarkVisibilityDirty();
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

            MarkVisibilityDirty();
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
            MarkVisibilityDirty();
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

            HashSet<Vector2Int> preSandboxUnion = CollectActiveBlockUnion(activeBlocks);
            FusionIntegrityMergeContext mergeContext = new FusionIntegrityMergeContext
            {
                triggerBlock = seed,
                groupCellSets = CloneGroupCellSets(groupCellSets),
                preSandboxUnion = preSandboxUnion
            };

            List<RuntimeTileMeshFusionDoor> carriedDoors = DetachGroupDoors(group);
            List<FusionWallAttachment> carriedWallAttachments = DetachGroupWallAttachments(group);
            List<DoorCandidate> doorCandidates = generateDoorsOnFusion
                ? CollectDoorCandidates(groupCellSets)
                : null;

            seed.SetHovered(false);
            seed.SetSelected(false);
            seed.SetSortingOrder(normalSortingOrder);
            seed.ApplyWorldCells(mergedCells, gridSize, gridOrigin);
            if (!seed.WorldCellsMatch(mergedCells, gridSize, gridOrigin))
            {
                Debug.LogWarning(
                    "[RuntimeTileMeshFusionSandbox] Post-merge world cells do not match merged union on " +
                    seed.name + ". Expected " + mergedCells.Count + " cell(s).",
                    seed);
            }

            AttachDoorsToSeed(carriedDoors, seed);
            AttachWallAttachmentsToSeed(carriedWallAttachments, seed);
            CreateFusionDoors(seed, doorCandidates);
            RefreshFusionDoorWallSpans(seed, mergedCells);

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

            mergeContext.postSandboxUnion = CollectActiveBlockUnion(activeBlocks);
            RecordFusionIntegrityMerge(group, seed, mergedCells, mergeContext);

            if (logFusionEvents)
                Debug.Log("[RuntimeTileMeshFusionSandbox] Merged " + (absorbed + 1) + " block(s) into " + seed.name + " with " + mergedCells.Count + " occupied cell(s).", seed);

            MarkVisibilityDirty();
            return absorbed;
        }

        private void RecordFusionIntegrityMerge(
            HashSet<RuntimeTileMeshDraggableBlock> group,
            RuntimeTileMeshDraggableBlock seed,
            HashSet<Vector2Int> mergedCells,
            FusionIntegrityMergeContext mergeContext)
        {
            if (!recordFusionIntegrity || group == null || seed == null || mergedCells == null)
                return;

            RuntimeTileMeshFusionIntegrityMonitor monitor = RuntimeTileMeshFusionIntegrityMonitor.Instance;
            if (monitor == null)
                monitor = FindFirstObjectByType<RuntimeTileMeshFusionIntegrityMonitor>();
            if (monitor == null)
                return;

            List<RuntimeTileMeshDraggableBlock> groupBlocks = new List<RuntimeTileMeshDraggableBlock>(group.Count);
            foreach (RuntimeTileMeshDraggableBlock block in group)
            {
                if (block != null)
                    groupBlocks.Add(block);
            }

            RuntimeTileMeshView seedView = seed.View;
            RuntimeTileMeshBuildResult buildResult = seedView != null ? seedView.LastBuildResult : null;
            monitor.RecordMergeGroup(
                groupBlocks,
                seed,
                mergedCells,
                buildResult,
                "MergeGroup -> " + seed.name + " (" + groupBlocks.Count + " blocks, " + mergedCells.Count + " cells)",
                mergeContext);
        }

        private HashSet<Vector2Int> CollectActiveBlockUnion(List<RuntimeTileMeshDraggableBlock> activeBlocks)
        {
            return RuntimeTileMeshFusionIntegrityAnalyzer.CollectUnionTiles(activeBlocks, gridSize, gridOrigin);
        }

        private static Dictionary<RuntimeTileMeshDraggableBlock, HashSet<Vector2Int>> CloneGroupCellSets(
            Dictionary<RuntimeTileMeshDraggableBlock, HashSet<Vector2Int>> source)
        {
            Dictionary<RuntimeTileMeshDraggableBlock, HashSet<Vector2Int>> clone =
                new Dictionary<RuntimeTileMeshDraggableBlock, HashSet<Vector2Int>>();
            if (source == null)
                return clone;

            foreach (KeyValuePair<RuntimeTileMeshDraggableBlock, HashSet<Vector2Int>> pair in source)
            {
                if (pair.Key == null || pair.Value == null)
                    continue;

                clone[pair.Key] = new HashSet<Vector2Int>(pair.Value);
            }

            return clone;
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

        public Vector3 ResolvePlayerWorldPoint(
            Vector3 desiredWorldPoint,
            Vector3 previousWorldPoint,
            float playerRadius,
            bool allowOutdoorMovement)
        {
            if (!allowOutdoorMovement)
                return ClampPlayerWorldPoint(desiredWorldPoint, previousWorldPoint, playerRadius);

            HashSet<Vector2Int> walkableCells = CollectWalkableCells();
            if (walkableCells.Count == 0)
                return desiredWorldPoint;

            desiredWorldPoint.z = 0f;
            previousWorldPoint.z = 0f;
            playerRadius = Mathf.Max(0f, playerRadius);

            bool previousInside = ContainsWorldPoint(previousWorldPoint, playerRadius, walkableCells);
            bool desiredInside = ContainsWorldPoint(desiredWorldPoint, playerRadius, walkableCells);

            if (previousInside && desiredInside)
                return ClampSegmentToWalkableArea(previousWorldPoint, desiredWorldPoint, playerRadius, walkableCells);

            if (previousInside != desiredInside)
            {
                if (AllowsDoorwayBoundaryPassage(previousWorldPoint, desiredWorldPoint, playerRadius))
                    return desiredWorldPoint;

                TryBlockDoorMovement(previousWorldPoint, desiredWorldPoint, playerRadius);
                return previousInside
                    ? ClampSegmentToWalkableArea(previousWorldPoint, desiredWorldPoint, playerRadius, walkableCells)
                    : ClampSegmentToOutdoorArea(previousWorldPoint, desiredWorldPoint, playerRadius, walkableCells);
            }

            if (!previousInside && !desiredInside &&
                SegmentTouchesWalkableArea(previousWorldPoint, desiredWorldPoint, playerRadius, walkableCells, out Vector3 lastOutdoorPoint))
            {
                if (AllowsDoorwayBoundaryPassage(previousWorldPoint, desiredWorldPoint, playerRadius))
                    return desiredWorldPoint;

                TryBlockDoorMovement(previousWorldPoint, desiredWorldPoint, playerRadius);
                return lastOutdoorPoint;
            }

            return TryBlockDoorMovement(previousWorldPoint, desiredWorldPoint, playerRadius)
                ? previousWorldPoint
                : desiredWorldPoint;
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

            RuntimeTileMeshDraggableBlock spawnBlock = activeBlocks[UnityEngine.Random.Range(0, activeBlocks.Count)];
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

        public bool TryFindNearestExteriorWallEdge(
            Vector3 worldPoint,
            float maxDistance,
            out FusionWallEdgePlacement placement)
        {
            placement = default;
            HashSet<Vector2Int> walkableCells = CollectWalkableCells();
            if (walkableCells.Count == 0)
                return false;

            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            float maxDistanceSqr = maxDistance > 0f ? maxDistance * maxDistance : float.PositiveInfinity;
            bool found = false;
            float bestDistanceSqr = float.MaxValue;
            Vector2 point = worldPoint;

            foreach (Vector2Int cell in walkableCells)
            {
                TryConsiderExteriorEdge(
                    cell,
                    Vector2Int.right,
                    RuntimeTileMeshFusionDoor.DoorAxis.Vertical,
                    cell.x + 1,
                    cell.y,
                    Vector2.right,
                    safeGridSize,
                    point,
                    walkableCells,
                    maxDistanceSqr,
                    ref found,
                    ref bestDistanceSqr,
                    ref placement);

                TryConsiderExteriorEdge(
                    cell,
                    Vector2Int.left,
                    RuntimeTileMeshFusionDoor.DoorAxis.Vertical,
                    cell.x,
                    cell.y,
                    Vector2.left,
                    safeGridSize,
                    point,
                    walkableCells,
                    maxDistanceSqr,
                    ref found,
                    ref bestDistanceSqr,
                    ref placement);

                TryConsiderExteriorEdge(
                    cell,
                    Vector2Int.up,
                    RuntimeTileMeshFusionDoor.DoorAxis.Horizontal,
                    cell.y + 1,
                    cell.x,
                    Vector2.up,
                    safeGridSize,
                    point,
                    walkableCells,
                    maxDistanceSqr,
                    ref found,
                    ref bestDistanceSqr,
                    ref placement);

                TryConsiderExteriorEdge(
                    cell,
                    Vector2Int.down,
                    RuntimeTileMeshFusionDoor.DoorAxis.Horizontal,
                    cell.y,
                    cell.x,
                    Vector2.down,
                    safeGridSize,
                    point,
                    walkableCells,
                    maxDistanceSqr,
                    ref found,
                    ref bestDistanceSqr,
                    ref placement);
            }

            return found;
        }

        public bool TryFindPurchasableExteriorWallEdge(
            Vector3 worldPoint,
            float maxDistance,
            FusionGameModeController.WallAttachmentCategory category,
            out FusionWallEdgePlacement placement)
        {
            placement = default;
            if (!TryFindNearestExteriorWallEdge(worldPoint, maxDistance, out placement))
                return false;

            return category == FusionGameModeController.WallAttachmentCategory.Door
                ? CanPlaceExteriorFusionDoor(placement)
                : CanPlaceExteriorWindow(placement);
        }

        public bool CanPlaceExteriorWindow(FusionWallEdgePlacement placement)
        {
            return TryFindBlockOwningCell(placement.ownerCell, out _);
        }

        public bool CanPlaceExteriorFusionDoor(FusionWallEdgePlacement placement)
        {
            if (!TryFindBlockOwningCell(placement.ownerCell, out _))
                return false;

            float duplicateEpsilon = Mathf.Max(0.001f, Mathf.Abs(gridSize) * 0.05f);
            return !HasFusionDoorAt(placement.axis, placement.center, duplicateEpsilon);
        }

        public bool TryFindBlockOwningCell(Vector2Int cell, out RuntimeTileMeshDraggableBlock owner)
        {
            owner = null;
            RefreshBlocks();
            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));

            for (int i = 0; i < blocks.Count; i++)
            {
                RuntimeTileMeshDraggableBlock block = blocks[i];
                if (block == null || !block.isActiveAndEnabled)
                    continue;

                HashSet<Vector2Int> blockCells = block.GetWorldCells(safeGridSize, gridOrigin);
                if (blockCells.Contains(cell))
                {
                    owner = block;
                    return true;
                }
            }

            return false;
        }

        public bool HasFusionDoorAt(
            RuntimeTileMeshFusionDoor.DoorAxis axis,
            Vector2 center,
            float epsilon)
        {
            PruneRegisteredDoors();
            for (int i = 0; i < registeredRuntimeDoors.Count; i++)
            {
                RuntimeTileMeshFusionDoor door = registeredRuntimeDoors[i];
                if (door != null && door.IsSameDoor(axis, center, epsilon))
                    return true;
            }

            RefreshBlocks();
            for (int i = 0; i < blocks.Count; i++)
            {
                RuntimeTileMeshDraggableBlock block = blocks[i];
                if (block == null)
                    continue;

                RuntimeTileMeshFusionDoor[] blockDoors =
                    block.GetComponentsInChildren<RuntimeTileMeshFusionDoor>(true);
                if (HasDoorAt(blockDoors, axis, center, epsilon))
                    return true;
            }

            return false;
        }

        public bool TryPlaceExteriorFusionDoor(
            FusionWallEdgePlacement placement,
            string displayName,
            out RuntimeTileMeshFusionDoor door)
        {
            door = null;
            if (!CanPlaceExteriorFusionDoor(placement))
                return false;

            if (!TryFindBlockOwningCell(placement.ownerCell, out RuntimeTileMeshDraggableBlock owner))
                return false;

            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            string key = "Exterior:" + placement.axis + ":" + placement.edgeCoordinate + ":" + placement.variable;
            string resolvedName = string.IsNullOrWhiteSpace(displayName) ? "Door" : displayName.Trim();

            GameObject doorObject = new GameObject("Fusion Door - " + resolvedName);
            doorObject.transform.SetParent(owner.transform, true);

            door = doorObject.AddComponent<RuntimeTileMeshFusionDoor>();
            door.includeWallVisual = false;
            door.Configure(
                placement.axis,
                placement.center,
                safeGridSize,
                key,
                doorThickness,
                doorOpenAngleDegrees,
                doorColor,
                placement.edgeCoordinate,
                placement.variable,
                1,
                null,
                wallDebugColor,
                wallDebugLineWidth);
            door.ConfigureExteriorSwing(placement.normal);
            ApplySandboxDoorAnimationSettings(door);
            RegisterRuntimeDoor(door);
            MarkVisibilityDirty();
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

        private List<FusionWallAttachment> DetachGroupWallAttachments(HashSet<RuntimeTileMeshDraggableBlock> group)
        {
            List<FusionWallAttachment> attachments = new List<FusionWallAttachment>();
            if (group == null)
                return attachments;

            foreach (RuntimeTileMeshDraggableBlock block in group)
            {
                if (block == null)
                    continue;

                FusionWallAttachment[] blockAttachments = block.GetComponentsInChildren<FusionWallAttachment>(true);
                for (int i = 0; i < blockAttachments.Length; i++)
                {
                    FusionWallAttachment attachment = blockAttachments[i];
                    if (attachment == null || attachments.Contains(attachment))
                        continue;

                    attachment.transform.SetParent(transform, true);
                    attachments.Add(attachment);
                }
            }

            return attachments;
        }

        private static void AttachWallAttachmentsToSeed(
            List<FusionWallAttachment> attachments,
            RuntimeTileMeshDraggableBlock seed)
        {
            if (attachments == null || seed == null)
                return;

            for (int i = 0; i < attachments.Count; i++)
            {
                FusionWallAttachment attachment = attachments[i];
                if (attachment != null)
                    attachment.transform.SetParent(seed.transform, true);
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
                    doorOpenAngleDegrees,
                    doorColor,
                    candidate.edgeCoordinate,
                    candidate.variableStart,
                    candidate.runLength,
                    wallVisualPrefab,
                    wallDebugColor,
                    wallDebugLineWidth);
                ApplySandboxDoorAnimationSettings(door);
                RegisterRuntimeDoor(door);
            }
        }

        private void RegisterRuntimeDoor(RuntimeTileMeshFusionDoor door)
        {
            if (door == null || registeredRuntimeDoors.Contains(door))
                return;

            registeredRuntimeDoors.Add(door);
            MarkVisibilityDirty();
        }

        private void PruneRegisteredDoors()
        {
            for (int i = registeredRuntimeDoors.Count - 1; i >= 0; i--)
            {
                RuntimeTileMeshFusionDoor door = registeredRuntimeDoors[i];
                if (door == null || !IsGameplayDoor(door))
                    registeredRuntimeDoors.RemoveAt(i);
            }
        }

        private void ApplySandboxDoorAnimationSettings(RuntimeTileMeshFusionDoor door)
        {
            if (door == null)
                return;

            door.animateDoor = animateDoors;
            door.openDuration = doorOpenDuration;
            door.closeDuration = doorCloseDuration;
            door.swingCurve = doorSwingCurve;
            door.doorwayPassableOpenAmount = doorPassableOpenAmount;
            door.useEndWobble = useDoorEndWobble;
            door.endWobbleDuration = doorEndWobbleDuration;
            door.endWobbleAmplitudeDegrees = doorEndWobbleAmplitudeDegrees;
            door.endWobbleOscillations = doorEndWobbleOscillations;
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

        private static void RefreshFusionDoorWallSpans(
            RuntimeTileMeshDraggableBlock seed,
            HashSet<Vector2Int> mergedCells)
        {
            if (seed == null || mergedCells == null || mergedCells.Count == 0)
                return;

            RuntimeTileMeshFusionDoor[] doors = seed.GetComponentsInChildren<RuntimeTileMeshFusionDoor>(true);
            for (int i = 0; i < doors.Length; i++)
            {
                if (doors[i] != null)
                    doors[i].RefreshWallSpanFromCells(mergedCells);
            }
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
            return TryBlockDoorMovement(from, to, playerRadius);
        }

        private Vector3 ClampSegmentToOutdoorArea(
            Vector3 from,
            Vector3 to,
            float clearanceRadius,
            HashSet<Vector2Int> walkableCells)
        {
            if (!SegmentTouchesWalkableArea(from, to, clearanceRadius, walkableCells, out Vector3 lastOutdoorPoint))
                return to;

            return lastOutdoorPoint;
        }

        private bool SegmentTouchesWalkableArea(
            Vector3 from,
            Vector3 to,
            float clearanceRadius,
            HashSet<Vector2Int> walkableCells,
            out Vector3 lastOutdoorPoint)
        {
            lastOutdoorPoint = from;
            int steps = GetSegmentSampleCount(from, to);
            for (int i = 1; i <= steps; i++)
            {
                Vector3 sample = Vector3.Lerp(from, to, i / (float)steps);
                if (ContainsWorldPoint(sample, clearanceRadius, walkableCells))
                    return true;

                lastOutdoorPoint = sample;
            }

            return false;
        }

        private bool AllowsDoorwayBoundaryPassage(Vector3 from, Vector3 to, float playerRadius)
        {
            List<RuntimeTileMeshFusionDoor> doors = CollectActiveDoors();
            for (int i = 0; i < doors.Count; i++)
            {
                RuntimeTileMeshFusionDoor door = doors[i];
                if (door != null && door.isActiveAndEnabled &&
                    door.AllowsMovementThroughDoorway(from, to, playerRadius))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryBlockDoorMovement(Vector3 from, Vector3 to, float playerRadius)
        {
            if (!doorBlocksPlayer)
                return false;

            List<RuntimeTileMeshFusionDoor> doors = CollectActiveDoors();
            for (int i = 0; i < doors.Count; i++)
            {
                RuntimeTileMeshFusionDoor door = doors[i];
                if (door != null && door.isActiveAndEnabled && door.TryBlockMovement(from, to, playerRadius))
                    return true;
            }

            return false;
        }

        private List<RuntimeTileMeshFusionDoor> CollectActiveDoors()
        {
            doorBuffer.Clear();
            PruneRegisteredDoors();
            for (int i = 0; i < registeredRuntimeDoors.Count; i++)
                AddDoorIfGameplay(registeredRuntimeDoors[i]);

            if (blocks.Count == 0)
                RefreshBlocks();

            for (int i = 0; i < blocks.Count; i++)
            {
                RuntimeTileMeshDraggableBlock block = blocks[i];
                if (block == null || !block.isActiveAndEnabled)
                    continue;

                RuntimeTileMeshFusionDoor[] blockDoors = block.GetComponentsInChildren<RuntimeTileMeshFusionDoor>(true);
                for (int j = 0; j < blockDoors.Length; j++)
                {
                    AddDoorIfGameplay(blockDoors[j]);
                }
            }

            RuntimeTileMeshFusionDoor[] sandboxDoors = GetComponentsInChildren<RuntimeTileMeshFusionDoor>(true);
            for (int i = 0; i < sandboxDoors.Length; i++)
            {
                AddDoorIfGameplay(sandboxDoors[i]);
            }

            return doorBuffer;
        }

        private void AddDoorIfGameplay(RuntimeTileMeshFusionDoor door)
        {
            if (!IsGameplayDoor(door) || doorBuffer.Contains(door))
                return;

            ApplySandboxDoorAnimationSettings(door);
            doorBuffer.Add(door);
        }

        private bool IsGameplayDoor(RuntimeTileMeshFusionDoor door)
        {
            if (door == null || !door.isActiveAndEnabled)
                return false;

            HideFlags flags = door.gameObject.hideFlags;
            if ((flags & HideFlags.HideAndDontSave) == HideFlags.HideAndDontSave ||
                (flags & HideFlags.DontSaveInEditor) == HideFlags.DontSaveInEditor ||
                (flags & HideFlags.DontSaveInBuild) == HideFlags.DontSaveInBuild)
            {
                return false;
            }

            return door.gameObject.scene.IsValid();
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

        private void TryConsiderExteriorEdge(
            Vector2Int ownerCell,
            Vector2Int neighborOffset,
            RuntimeTileMeshFusionDoor.DoorAxis axis,
            int edgeCoordinate,
            int variable,
            Vector2 normal,
            float safeGridSize,
            Vector2 point,
            HashSet<Vector2Int> walkableCells,
            float maxDistanceSqr,
            ref bool found,
            ref float bestDistanceSqr,
            ref FusionWallEdgePlacement placement)
        {
            if (walkableCells.Contains(ownerCell + neighborOffset))
                return;

            Vector2 start;
            Vector2 end;
            if (axis == RuntimeTileMeshFusionDoor.DoorAxis.Vertical)
            {
                float x = gridOrigin.x + edgeCoordinate * safeGridSize;
                float y = gridOrigin.y + variable * safeGridSize;
                start = new Vector2(x, y);
                end = new Vector2(x, y + safeGridSize);
            }
            else
            {
                float x = gridOrigin.x + variable * safeGridSize;
                float y = gridOrigin.y + edgeCoordinate * safeGridSize;
                start = new Vector2(x, y);
                end = new Vector2(x + safeGridSize, y);
            }

            Vector2 closest = ClosestPointOnSegment(point, start, end);
            float distanceSqr = (point - closest).sqrMagnitude;
            if (distanceSqr > maxDistanceSqr || distanceSqr >= bestDistanceSqr)
                return;

            found = true;
            bestDistanceSqr = distanceSqr;
            placement = new FusionWallEdgePlacement
            {
                axis = axis,
                center = (start + end) * 0.5f,
                normal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector2.up,
                tangent = axis == RuntimeTileMeshFusionDoor.DoorAxis.Vertical ? Vector2.up : Vector2.right,
                ownerCell = ownerCell,
                edgeCoordinate = edgeCoordinate,
                variable = variable,
                distance = Mathf.Sqrt(distanceSqr)
            };
        }

        private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSqr = segment.sqrMagnitude;
            if (lengthSqr <= 0.000001f)
                return start;

            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSqr);
            return start + segment * t;
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

            RuntimeTileMeshView view = block.View;
            if (view != null)
                view.ClearGeneratedMeshImmediate();

            if (deactivateAbsorbedBlocksImmediately)
                block.gameObject.SetActive(false);

            if (Application.isPlaying)
                Destroy(block.gameObject);
            else
                DestroyImmediate(block.gameObject);
            MarkVisibilityDirty();
        }

        public void MarkVisibilityDirty()
        {
            VisibilityWorld.MarkActiveWorldDirty();
        }

        public void CollectVisibilitySegments(List<VisibilitySegment> results)
        {
            if (!registerFusionBlocksForVisibility || results == null)
                return;

            RefreshBlocks();
            int before = results.Count;
            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            List<RuntimeTileMeshFusionDoor> doors = CollectActiveDoors();

            for (int i = 0; i < blocks.Count; i++)
            {
                RuntimeTileMeshDraggableBlock block = blocks[i];
                if (block == null || !block.isActiveAndEnabled)
                    continue;

                if (!includeSelectedBlockInVisibility && block == selectedBlock)
                    continue;

                HashSet<Vector2Int> blockCells = block.GetWorldCells(safeGridSize, gridOrigin);
                foreach (Vector2Int cell in blockCells)
                {
                    AddVisibilityBoundaryIfExterior(
                        results,
                        block,
                        blockCells,
                        cell,
                        Vector2Int.right,
                        RuntimeTileMeshFusionDoor.DoorAxis.Vertical,
                        cell.x + 1,
                        cell.y,
                        safeGridSize,
                        doors);
                    AddVisibilityBoundaryIfExterior(
                        results,
                        block,
                        blockCells,
                        cell,
                        Vector2Int.left,
                        RuntimeTileMeshFusionDoor.DoorAxis.Vertical,
                        cell.x,
                        cell.y,
                        safeGridSize,
                        doors);
                    AddVisibilityBoundaryIfExterior(
                        results,
                        block,
                        blockCells,
                        cell,
                        Vector2Int.up,
                        RuntimeTileMeshFusionDoor.DoorAxis.Horizontal,
                        cell.y + 1,
                        cell.x,
                        safeGridSize,
                        doors);
                    AddVisibilityBoundaryIfExterior(
                        results,
                        block,
                        blockCells,
                        cell,
                        Vector2Int.down,
                        RuntimeTileMeshFusionDoor.DoorAxis.Horizontal,
                        cell.y,
                        cell.x,
                        safeGridSize,
                        doors);
                }
            }

            if (logVisibilitySourceSegments)
            {
                Debug.Log(
                    "[VisibilitySource] " + name +
                    " fusionBlockBoundarySegments=" + (results.Count - before) +
                    " blocks=" + blocks.Count,
                    this);
            }
        }

        private void AddVisibilityBoundaryIfExterior(
            List<VisibilitySegment> results,
            RuntimeTileMeshDraggableBlock sourceBlock,
            HashSet<Vector2Int> blockCells,
            Vector2Int cell,
            Vector2Int neighborOffset,
            RuntimeTileMeshFusionDoor.DoorAxis axis,
            int edgeCoordinate,
            int variable,
            float safeGridSize,
            List<RuntimeTileMeshFusionDoor> doors)
        {
            if (blockCells.Contains(cell + neighborOffset))
                return;

            Vector2 start;
            Vector2 end;
            if (axis == RuntimeTileMeshFusionDoor.DoorAxis.Vertical)
            {
                float x = gridOrigin.x + edgeCoordinate * safeGridSize;
                float y = gridOrigin.y + variable * safeGridSize;
                start = new Vector2(x, y);
                end = new Vector2(x, y + safeGridSize);
            }
            else
            {
                float x = gridOrigin.x + variable * safeGridSize;
                float y = gridOrigin.y + edgeCoordinate * safeGridSize;
                start = new Vector2(x, y);
                end = new Vector2(x + safeGridSize, y);
            }

            if (IsExteriorDoorwaySegment(axis, start, end, doors, safeGridSize))
                return;

            results.Add(new VisibilitySegment(
                start,
                end,
                VisibilitySegmentType.Wall,
                sourceBlock.gameObject,
                sourceBlock));
        }

        private static bool IsExteriorDoorwaySegment(
            RuntimeTileMeshFusionDoor.DoorAxis axis,
            Vector2 start,
            Vector2 end,
            List<RuntimeTileMeshFusionDoor> doors,
            float safeGridSize)
        {
            if (doors == null || doors.Count == 0)
                return false;

            float epsilon = Mathf.Max(0.001f, safeGridSize * 0.03f);
            float lineCoordinate = axis == RuntimeTileMeshFusionDoor.DoorAxis.Vertical ? start.x : start.y;
            float segmentCenterAlong = axis == RuntimeTileMeshFusionDoor.DoorAxis.Vertical
                ? (start.y + end.y) * 0.5f
                : (start.x + end.x) * 0.5f;

            for (int i = 0; i < doors.Count; i++)
            {
                RuntimeTileMeshFusionDoor door = doors[i];
                if (door == null || !door.isActiveAndEnabled || door.axis != axis)
                    continue;

                float doorLine = axis == RuntimeTileMeshFusionDoor.DoorAxis.Vertical
                    ? door.seamCenter.x
                    : door.seamCenter.y;
                float doorCenterAlong = axis == RuntimeTileMeshFusionDoor.DoorAxis.Vertical
                    ? door.seamCenter.y
                    : door.seamCenter.x;

                if (Mathf.Abs(lineCoordinate - doorLine) <= epsilon &&
                    Mathf.Abs(segmentCenterAlong - doorCenterAlong) <= epsilon)
                {
                    return true;
                }
            }

            return false;
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

        private void UpdateRuntimeGridOverlay()
        {
            if (!Application.isPlaying)
                return;

            if (disableLegacyRuntimeGridOverlay && !legacyRuntimeGridChecked)
            {
                legacyRuntimeGridChecked = true;
                GameObject legacy = GameObject.Find("Runtime Grid Overlay");
                if (legacy != null && legacy.transform != runtimeGridRoot)
                    legacy.SetActive(false);
            }

            if (!renderRuntimeGridInGame || worldCamera == null)
            {
                if (runtimeGridRoot != null)
                    runtimeGridRoot.gameObject.SetActive(false);
                return;
            }

            EnsureRuntimeGridRoot();
            if (runtimeGridRoot == null)
                return;

            runtimeGridRoot.gameObject.SetActive(true);
            float safeGridSize = Mathf.Max(0.0001f, Mathf.Abs(gridSize));
            float halfHeight = worldCamera.orthographic
                ? worldCamera.orthographicSize
                : Mathf.Max(1f, Vector3.Distance(worldCamera.transform.position, Vector3.zero) * 0.5f);
            float halfWidth = halfHeight * Mathf.Max(0.01f, worldCamera.aspect);
            Vector3 center = worldCamera.transform.position;
            float padding = Mathf.Max(0f, runtimeGridCameraPaddingCells) * safeGridSize;

            float minWorldX = center.x - halfWidth - padding;
            float maxWorldX = center.x + halfWidth + padding;
            float minWorldY = center.y - halfHeight - padding;
            float maxWorldY = center.y + halfHeight + padding;

            int minX = Mathf.FloorToInt((minWorldX - gridOrigin.x) / safeGridSize);
            int maxX = Mathf.CeilToInt((maxWorldX - gridOrigin.x) / safeGridSize);
            int minY = Mathf.FloorToInt((minWorldY - gridOrigin.y) / safeGridSize);
            int maxY = Mathf.CeilToInt((maxWorldY - gridOrigin.y) / safeGridSize);

            int lineIndex = 0;
            for (int x = minX; x <= maxX; x++)
            {
                float worldX = gridOrigin.x + x * safeGridSize;
                LineRenderer line = GetRuntimeGridLine(lineIndex++);
                ConfigureRuntimeGridLine(
                    line,
                    new Vector3(worldX, gridOrigin.y + minY * safeGridSize, 0f),
                    new Vector3(worldX, gridOrigin.y + maxY * safeGridSize, 0f));
            }

            for (int y = minY; y <= maxY; y++)
            {
                float worldY = gridOrigin.y + y * safeGridSize;
                LineRenderer line = GetRuntimeGridLine(lineIndex++);
                ConfigureRuntimeGridLine(
                    line,
                    new Vector3(gridOrigin.x + minX * safeGridSize, worldY, 0f),
                    new Vector3(gridOrigin.x + maxX * safeGridSize, worldY, 0f));
            }

            for (int i = lineIndex; i < runtimeGridLines.Count; i++)
            {
                if (runtimeGridLines[i] != null)
                    runtimeGridLines[i].gameObject.SetActive(false);
            }
        }

        private void EnsureRuntimeGridRoot()
        {
            if (runtimeGridRoot != null)
                return;

            GameObject root = new GameObject("Fusion Runtime Camera Grid");
            root.transform.SetParent(transform, false);
            runtimeGridRoot = root.transform;
        }

        private LineRenderer GetRuntimeGridLine(int index)
        {
            while (runtimeGridLines.Count <= index)
            {
                GameObject lineObject = new GameObject("Runtime Grid Line " + runtimeGridLines.Count);
                lineObject.transform.SetParent(runtimeGridRoot, false);
                LineRenderer line = lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.numCapVertices = 0;
                line.numCornerVertices = 0;
                runtimeGridLines.Add(line);
            }

            LineRenderer renderer = runtimeGridLines[index];
            renderer.gameObject.SetActive(true);
            return renderer;
        }

        private void ConfigureRuntimeGridLine(LineRenderer line, Vector3 start, Vector3 end)
        {
            if (line == null)
                return;

            line.sharedMaterial = GetRuntimeGridMaterial();
            line.startColor = runtimeGridColor;
            line.endColor = runtimeGridColor;
            line.widthMultiplier = Mathf.Max(0.001f, runtimeGridLineWidth);
            line.sortingOrder = runtimeGridSortingOrder;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
        }

        private Material GetRuntimeGridMaterial()
        {
            if (runtimeGridMaterial != null)
                return runtimeGridMaterial;

            if (runtimeGridRuntimeMaterial != null)
                return runtimeGridRuntimeMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
                return null;

            runtimeGridRuntimeMaterial = new Material(shader);
            runtimeGridRuntimeMaterial.name = "Fusion Runtime Camera Grid";
            if (runtimeGridRuntimeMaterial.HasProperty("_BaseColor"))
                runtimeGridRuntimeMaterial.SetColor("_BaseColor", Color.white);
            if (runtimeGridRuntimeMaterial.HasProperty("_Color"))
                runtimeGridRuntimeMaterial.SetColor("_Color", Color.white);
            return runtimeGridRuntimeMaterial;
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
