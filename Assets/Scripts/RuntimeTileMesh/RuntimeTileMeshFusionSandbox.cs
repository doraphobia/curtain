using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    [DisallowMultipleComponent]
    public class RuntimeTileMeshFusionSandbox : MonoBehaviour
    {
        [Header("Input")]
        public Camera worldCamera;
        public LayerMask blockLayerMask = ~0;
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

        void Awake()
        {
            if (worldCamera == null)
                worldCamera = Camera.main;

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

            selectedThisFrame = false;
            Vector3 mouseWorld = ScreenToWorld(Input.mousePosition);

            if (selectedBlock != null)
            {
                MoveSelectedBlock(mouseWorld);
                if (Input.GetMouseButtonDown(0) && !selectedThisFrame)
                    PlaceSelectedBlock();
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
            MoveSelectedBlock(mouseWorld);
        }

        private void MoveSelectedBlock(Vector3 mouseWorld)
        {
            if (selectedBlock == null)
                return;

            Vector3 desired = preserveGrabOffset ? mouseWorld + grabOffset : mouseWorld;
            Vector3 snapped = SnapWorldPosition(desired, selectedBlock.transform.position.z);
            selectedBlock.transform.position = snapped;
        }

        private void PlaceSelectedBlock()
        {
            RuntimeTileMeshDraggableBlock placed = selectedBlock;
            selectedBlock = null;

            placed.SetSelected(false);
            placed.SetSortingOrder(normalSortingOrder);
            placed.SnapRootToGrid(gridSize, gridOrigin);

            if (mergeAfterPlacement)
                MergeConnectedBlocks(placed);
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
            HashSet<Vector2Int> mergedCells = seed.GetWorldCells(gridSize, gridOrigin);
            group.Add(seed);

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
                    foreach (Vector2Int cell in candidateCells)
                        mergedCells.Add(cell);

                    expanded = true;
                }
            }
            while (expanded);

            if (group.Count <= 1)
                return 0;

            seed.SetHovered(false);
            seed.SetSelected(false);
            seed.SetSortingOrder(normalSortingOrder);
            seed.ApplyWorldCells(mergedCells, gridSize, gridOrigin);

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
    }
}
