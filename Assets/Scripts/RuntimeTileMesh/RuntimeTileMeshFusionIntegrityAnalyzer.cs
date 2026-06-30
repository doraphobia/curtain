using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    public sealed class FusionIntegrityMergeContext
    {
        public RuntimeTileMeshDraggableBlock triggerBlock;
        public Dictionary<RuntimeTileMeshDraggableBlock, HashSet<Vector2Int>> groupCellSets;
        public HashSet<Vector2Int> preSandboxUnion;
        public HashSet<Vector2Int> postSandboxUnion;
    }

    public static class RuntimeTileMeshFusionIntegrityAnalyzer
    {
        public const string IssueMergeTileLoss = "MERGE_TILE_LOSS";
        public const string IssueMergeTileGain = "MERGE_TILE_GAIN";
        public const string IssueExtraGeneratedTiles = "EXTRA_GENERATED_TILES";
        public const string IssueMeshMissing = "MESH_MISSING";
        public const string IssueMeshEmpty = "MESH_EMPTY";
        public const string IssueMeshCoverageGap = "MESH_COVERAGE_GAP";
        public const string IssueMeshVertexMismatch = "MESH_VERTEX_MISMATCH";
        public const string IssueBuildFailed = "BUILD_FAILED";
        public const string IssueBoundaryEdgeLoss = "BOUNDARY_EDGE_LOSS";
        public const string IssueLogicalWorldTileMismatch = "LOGICAL_WORLD_TILE_MISMATCH";
        public const string IssueDuplicateLogicalTiles = "DUPLICATE_LOGICAL_TILES";
        public const string IssueInactiveBlockWithTiles = "INACTIVE_BLOCK_WITH_TILES";
        public const string IssueSandboxTileCountChanged = "SANDBOX_TILE_COUNT_CHANGED";

        public static HashSet<Vector2Int> CollectUnionTiles(
            IEnumerable<RuntimeTileMeshDraggableBlock> blocks,
            float gridSize,
            Vector2 gridOrigin)
        {
            HashSet<Vector2Int> union = new HashSet<Vector2Int>();
            if (blocks == null)
                return union;

            foreach (RuntimeTileMeshDraggableBlock block in blocks)
            {
                if (block == null || !block.isActiveAndEnabled)
                    continue;

                HashSet<Vector2Int> cells = block.GetWorldCells(gridSize, gridOrigin);
                foreach (Vector2Int cell in cells)
                    union.Add(cell);
            }

            return union;
        }

        public static HashSet<Vector2Int> CollectUnionTilesFromSets(
            IEnumerable<HashSet<Vector2Int>> cellSets)
        {
            HashSet<Vector2Int> union = new HashSet<Vector2Int>();
            if (cellSets == null)
                return union;

            foreach (HashSet<Vector2Int> cells in cellSets)
            {
                if (cells == null)
                    continue;

                foreach (Vector2Int cell in cells)
                    union.Add(cell);
            }

            return union;
        }

        public static int CountUniqueLocalTiles(IList<Vector2Int> localTiles)
        {
            if (localTiles == null || localTiles.Count == 0)
                return 0;

            return TileOccupancy.ToSet(localTiles).Count;
        }

        public static FusionIntegrityBlockSnapshot CaptureBlockSnapshot(
            RuntimeTileMeshDraggableBlock block,
            float gridSize,
            Vector2 gridOrigin,
            RuntimeTileMeshBuildResult buildResult = null)
        {
            FusionIntegrityBlockSnapshot snapshot = new FusionIntegrityBlockSnapshot();
            if (block == null)
                return snapshot;

            RuntimeTileMeshView view = block.View;
            snapshot.blockName = block.name;
            snapshot.instanceId = block.GetInstanceID();
            snapshot.isActive = block.isActiveAndEnabled;
            snapshot.buildSuccess = true;
            snapshot.logicalTileCount = view != null && view.tiles != null ? view.tiles.Count : 0;
            snapshot.logicalUniqueTileCount = view != null && view.tiles != null
                ? CountUniqueLocalTiles(view.tiles)
                : 0;

            HashSet<Vector2Int> worldCells = block.GetWorldCells(gridSize, gridOrigin);
            snapshot.worldTileCount = worldCells.Count;
            snapshot.worldTiles = SortTiles(worldCells);

            if (buildResult != null)
            {
                for (int i = 0; i < buildResult.warnings.Count; i++)
                    snapshot.warnings.Add(buildResult.warnings[i]);

                for (int i = 0; i < buildResult.components.Count; i++)
                {
                    RuntimeTileMeshComponentResult component = buildResult.components[i];
                    if (component == null)
                        continue;

                    if (!component.success)
                        snapshot.buildSuccess = false;

                    for (int warningIndex = 0; warningIndex < component.warnings.Count; warningIndex++)
                        snapshot.warnings.Add(component.warnings[warningIndex]);

                    if (component.meshData == null)
                        continue;

                    snapshot.meshComponentCount++;
                    snapshot.meshVertexCount += component.meshData.vertices.Count;
                    snapshot.meshTriangleCount += component.meshData.triangles.Count / 3;
                }
            }
            else
            {
                CaptureRenderedMeshCounts(block, snapshot);
            }

            if (view != null)
            {
                RuntimeTileMeshSettings settings = view.CreateSettings();
                List<Vector2Int> localTiles = view.tiles ?? new List<Vector2Int>();
                RuntimeTileMeshData probeMesh = new RuntimeTileMeshData();
                if (TileGridMeshGenerator.TryBuild(localTiles, settings, probeMesh, out string warning))
                {
                    snapshot.meshCoversAllTiles = TileGridMeshGenerator.CoversAllTiles(localTiles, settings, probeMesh);
                    if (!snapshot.buildSuccess && probeMesh.triangles.Count > 0)
                        snapshot.buildSuccess = true;

                    if (snapshot.meshVertexCount <= 0)
                    {
                        snapshot.meshVertexCount = probeMesh.vertices.Count;
                        snapshot.meshTriangleCount = probeMesh.triangles.Count / 3;
                    }
                }
                else if (!string.IsNullOrEmpty(warning))
                {
                    snapshot.warnings.Add(warning);
                }

                CaptureBoundaryStats(TileOccupancy.ToSet(localTiles), snapshot);
            }

            return snapshot;
        }

        public static void AnalyzeBlockSnapshot(
            FusionIntegrityBlockSnapshot snapshot,
            RuntimeTileMeshSettings settings,
            List<FusionIntegrityIssue> issues)
        {
            if (snapshot == null || issues == null)
                return;

            if (snapshot.logicalTileCount > snapshot.logicalUniqueTileCount)
            {
                AddIssue(issues, IssueDuplicateLogicalTiles,
                    snapshot.blockName + " logical tile list contains " +
                    (snapshot.logicalTileCount - snapshot.logicalUniqueTileCount) +
                    " duplicate local entr(ies) (" + snapshot.logicalTileCount + " listed, " +
                    snapshot.logicalUniqueTileCount + " unique).",
                    snapshot.worldTiles);
            }

            if (snapshot.worldTileCount > 0 && !snapshot.isActive)
            {
                AddIssue(issues, IssueInactiveBlockWithTiles,
                    snapshot.blockName + " has " + snapshot.worldTileCount + " world tile(s) but is inactive.",
                    snapshot.worldTiles);
            }

            if (snapshot.logicalUniqueTileCount != snapshot.worldTileCount)
            {
                AddIssue(issues, IssueLogicalWorldTileMismatch,
                    snapshot.blockName + " unique logical tile count (" + snapshot.logicalUniqueTileCount +
                    ") differs from world tile count (" + snapshot.worldTileCount + ").",
                    snapshot.worldTiles);
            }

            if (snapshot.worldTileCount > 0 && snapshot.meshTriangleCount <= 0)
            {
                AddIssue(issues, IssueMeshMissing,
                    snapshot.blockName + " has occupied tiles but no render triangles.",
                    snapshot.worldTiles);
            }

            int expectedVertices = snapshot.worldTileCount * 4;
            if (snapshot.worldTileCount > 0 &&
                snapshot.meshVertexCount > 0 &&
                snapshot.meshVertexCount != expectedVertices)
            {
                AddIssue(issues, IssueMeshVertexMismatch,
                    snapshot.blockName + " expected " + expectedVertices + " grid vertices for " +
                    snapshot.worldTileCount + " tile(s), got " + snapshot.meshVertexCount + ".",
                    snapshot.worldTiles);
            }

            if (snapshot.worldTileCount > 0 && !snapshot.meshCoversAllTiles)
            {
                List<Vector2Int> uncovered = TileGridMeshGenerator.FindUncoveredTiles(
                    snapshot.worldTiles,
                    settings,
                    BuildProbeMesh(snapshot.worldTiles, settings));
                AddIssue(issues, IssueMeshCoverageGap,
                    snapshot.blockName + " mesh does not cover " + uncovered.Count + " tile center(s).",
                    uncovered);
            }

            if (snapshot.unconsumedBoundaryEdges > 0)
            {
                AddIssue(issues, IssueBoundaryEdgeLoss,
                    snapshot.blockName + " has " + snapshot.unconsumedBoundaryEdges +
                    " unconsumed boundary edge(s) after loop reconstruction.",
                    snapshot.worldTiles);
            }

            if (snapshot.worldTileCount > 0 && !snapshot.buildSuccess)
            {
                AddIssue(issues, IssueBuildFailed,
                    snapshot.blockName + " rebuild did not report success.",
                    snapshot.worldTiles);
            }
        }

        public static FusionIntegrityReport AnalyzeMergeGroup(
            string contextLabel,
            IList<RuntimeTileMeshDraggableBlock> groupBlocks,
            RuntimeTileMeshDraggableBlock seed,
            HashSet<Vector2Int> mergedCells,
            float gridSize,
            Vector2 gridOrigin,
            RuntimeTileMeshBuildResult seedBuildResult = null,
            FusionIntegrityMergeContext mergeContext = null)
        {
            FusionIntegrityReport report = CreateReport(FusionIntegrityOperation.MergeGroup, contextLabel);
            report.tileAccounting = BuildTileAccounting(
                mergeContext,
                seed,
                groupBlocks,
                mergedCells,
                gridSize,
                gridOrigin);

            report.expectedTileCount = report.tileAccounting.expectedUnionTileCount;
            report.actualTileCount = report.tileAccounting.actualMergedTileCount;
            report.expectedVertexCount = report.expectedTileCount * 4;

            for (int i = 0; i < groupBlocks.Count; i++)
            {
                RuntimeTileMeshDraggableBlock block = groupBlocks[i];
                RuntimeTileMeshBuildResult buildResult = block == seed ? seedBuildResult : null;
                report.beforeBlocks.Add(CaptureBlockSnapshot(block, gridSize, gridOrigin, buildResult));
            }

            if (seed != null)
            {
                FusionIntegrityBlockSnapshot after = CaptureBlockSnapshot(seed, gridSize, gridOrigin, seedBuildResult);
                report.afterBlocks.Add(after);
                report.actualVertexCount = after.meshVertexCount;
            }

            ApplyTileAccountingIssues(report.tileAccounting, seed != null ? seed.name : "seed", report.issues);

            if (seed != null && report.afterBlocks.Count > 0)
            {
                FusionIntegrityBlockSnapshot after = report.afterBlocks[0];
                if (after.worldTileCount != report.tileAccounting.actualMergedTileCount)
                {
                    AddIssue(report.issues, IssueLogicalWorldTileMismatch,
                        seed.name + " post-ApplyWorldCells world tile count (" + after.worldTileCount +
                        ") differs from merged cell set (" + report.tileAccounting.actualMergedTileCount + ").",
                        after.worldTiles);
                }
            }

            if (seed != null && seed.View != null)
                AnalyzeBlockSnapshot(report.afterBlocks[0], seed.View.CreateSettings(), report.issues);

            report.issueCount = report.issues.Count;
            return report;
        }

        public static FusionIntegrityTileAccounting BuildTileAccounting(
            FusionIntegrityMergeContext mergeContext,
            RuntimeTileMeshDraggableBlock seed,
            IList<RuntimeTileMeshDraggableBlock> groupBlocks,
            HashSet<Vector2Int> mergedCells,
            float gridSize,
            Vector2 gridOrigin)
        {
            FusionIntegrityTileAccounting accounting = new FusionIntegrityTileAccounting();
            RuntimeTileMeshDraggableBlock trigger = mergeContext != null && mergeContext.triggerBlock != null
                ? mergeContext.triggerBlock
                : seed;

            Dictionary<RuntimeTileMeshDraggableBlock, HashSet<Vector2Int>> groupCellSets =
                mergeContext != null && mergeContext.groupCellSets != null
                    ? mergeContext.groupCellSets
                    : BuildGroupCellSets(groupBlocks, gridSize, gridOrigin);

            HashSet<Vector2Int> expectedUnion = CollectUnionTilesFromSets(groupCellSets.Values);
            HashSet<Vector2Int> actualCells = mergedCells != null
                ? new HashSet<Vector2Int>(mergedCells)
                : new HashSet<Vector2Int>();

            if (trigger != null && groupCellSets.TryGetValue(trigger, out HashSet<Vector2Int> triggerCells))
            {
                accounting.triggerBlockName = trigger.name;
                accounting.triggerBlockTileCount = triggerCells.Count;
                accounting.triggerTiles = SortTiles(triggerCells);
            }

            int rawSum = 0;
            int existingGroupBlocks = 0;
            HashSet<Vector2Int> existingOnly = new HashSet<Vector2Int>(expectedUnion);
            if (trigger != null && groupCellSets.TryGetValue(trigger, out HashSet<Vector2Int> triggerSet))
            {
                foreach (Vector2Int cell in triggerSet)
                    existingOnly.Remove(cell);
            }

            foreach (KeyValuePair<RuntimeTileMeshDraggableBlock, HashSet<Vector2Int>> pair in groupCellSets)
            {
                if (pair.Key == null || pair.Value == null)
                    continue;

                rawSum += pair.Value.Count;
                if (pair.Key != trigger)
                    existingGroupBlocks++;
            }

            accounting.existingGroupBlockCount = existingGroupBlocks;
            accounting.existingGroupTileSumRaw = rawSum - accounting.triggerBlockTileCount;
            accounting.existingGroupTiles = SortTiles(existingOnly);
            accounting.overlapWithinGroupCount = rawSum - expectedUnion.Count;
            accounting.expectedUnionTileCount = expectedUnion.Count;
            accounting.expectedUnionTiles = SortTiles(expectedUnion);
            accounting.actualMergedTileCount = actualCells.Count;

            List<Vector2Int> missing = new List<Vector2Int>();
            List<Vector2Int> extra = new List<Vector2Int>();
            foreach (Vector2Int cell in expectedUnion)
            {
                if (!actualCells.Contains(cell))
                    missing.Add(cell);
            }

            foreach (Vector2Int cell in actualCells)
            {
                if (!expectedUnion.Contains(cell))
                    extra.Add(cell);
            }

            accounting.missingTileCount = missing.Count;
            accounting.missingTiles = SortTiles(missing);
            accounting.extraGeneratedTileCount = extra.Count;
            accounting.extraTiles = SortTiles(extra);

            if (mergeContext != null)
            {
                accounting.sandboxBeforeTileCount = mergeContext.preSandboxUnion != null
                    ? mergeContext.preSandboxUnion.Count
                    : 0;
                accounting.sandboxAfterTileCount = mergeContext.postSandboxUnion != null
                    ? mergeContext.postSandboxUnion.Count
                    : 0;

                if (mergeContext.preSandboxUnion != null && mergeContext.groupCellSets != null)
                {
                    HashSet<Vector2Int> groupUnion = CollectUnionTilesFromSets(mergeContext.groupCellSets.Values);
                    HashSet<Vector2Int> outside = new HashSet<Vector2Int>(mergeContext.preSandboxUnion);
                    foreach (Vector2Int cell in groupUnion)
                        outside.Remove(cell);
                    accounting.sandboxOutsideGroupBeforeCount = outside.Count;
                }
            }

            if (trigger != null && trigger.View != null && trigger.View.tiles != null)
            {
                accounting.duplicateLogicalTileEntries =
                    trigger.View.tiles.Count - CountUniqueLocalTiles(trigger.View.tiles);
            }

            return accounting;
        }

        public static void ApplyTileAccountingIssues(
            FusionIntegrityTileAccounting accounting,
            string seedName,
            List<FusionIntegrityIssue> issues)
        {
            if (accounting == null || issues == null)
                return;

            if (accounting.missingTileCount > 0)
            {
                AddIssue(issues, IssueMergeTileLoss,
                    seedName + " merge lost " + accounting.missingTileCount +
                    " tile(s). Trigger=" + accounting.triggerBlockTileCount +
                    ", existingGroup=" + accounting.existingGroupTileSumRaw +
                    ", expectedUnion=" + accounting.expectedUnionTileCount +
                    ", actual=" + accounting.actualMergedTileCount + ".",
                    accounting.missingTiles);
            }

            if (accounting.extraGeneratedTileCount > 0)
            {
                AddIssue(issues, IssueExtraGeneratedTiles,
                    seedName + " merge generated " + accounting.extraGeneratedTileCount +
                    " extra tile(s). Trigger=" + accounting.triggerBlockTileCount +
                    ", existingGroup=" + accounting.existingGroupTileSumRaw +
                    ", overlapWithinGroup=" + accounting.overlapWithinGroupCount +
                    ", expectedUnion=" + accounting.expectedUnionTileCount +
                    ", actual=" + accounting.actualMergedTileCount + ".",
                    accounting.extraTiles);

                AddIssue(issues, IssueMergeTileGain,
                    seedName + " merge result contains " + accounting.extraGeneratedTileCount +
                    " tile(s) not present in the pre-merge union.",
                    accounting.extraTiles);
            }

            if (accounting.sandboxBeforeTileCount > 0 &&
                accounting.sandboxAfterTileCount != accounting.sandboxBeforeTileCount)
            {
                int delta = accounting.sandboxAfterTileCount - accounting.sandboxBeforeTileCount;
                AddIssue(issues, IssueSandboxTileCountChanged,
                    "Sandbox world tile count changed from " + accounting.sandboxBeforeTileCount +
                    " to " + accounting.sandboxAfterTileCount + " (" + (delta > 0 ? "+" : "") + delta +
                    "). Merge should preserve the global occupied set.",
                    delta > 0 ? accounting.extraTiles : accounting.missingTiles);
            }

            if (accounting.duplicateLogicalTileEntries > 0)
            {
                AddIssue(issues, IssueDuplicateLogicalTiles,
                    seedName + " carries " + accounting.duplicateLogicalTileEntries +
                    " duplicate local tile entries after merge.",
                    accounting.extraTiles.Count > 0 ? accounting.extraTiles : accounting.expectedUnionTiles);
            }
        }

        public static FusionIntegrityReport AnalyzeRebuild(
            RuntimeTileMeshDraggableBlock block,
            RuntimeTileMeshBuildResult buildResult,
            float gridSize,
            Vector2 gridOrigin,
            string contextLabel)
        {
            FusionIntegrityReport report = CreateReport(FusionIntegrityOperation.BlockRebuild, contextLabel);
            FusionIntegrityBlockSnapshot snapshot = CaptureBlockSnapshot(block, gridSize, gridOrigin, buildResult);
            report.afterBlocks.Add(snapshot);
            report.expectedTileCount = snapshot.worldTileCount;
            report.actualTileCount = snapshot.worldTileCount;
            report.expectedVertexCount = snapshot.worldTileCount * 4;
            report.actualVertexCount = snapshot.meshVertexCount;

            if (block != null && block.View != null)
                AnalyzeBlockSnapshot(snapshot, block.View.CreateSettings(), report.issues);

            report.issueCount = report.issues.Count;
            return report;
        }

        public static FusionIntegrityReport AnalyzeSandbox(
            RuntimeTileMeshFusionSandbox sandbox,
            string contextLabel)
        {
            FusionIntegrityReport report = CreateReport(FusionIntegrityOperation.ManualAudit, contextLabel);
            if (sandbox == null)
            {
                AddIssue(report.issues, IssueBuildFailed, "Sandbox reference was null.", null);
                report.issueCount = report.issues.Count;
                return report;
            }

            RuntimeTileMeshDraggableBlock[] blocks =
                Object.FindObjectsByType<RuntimeTileMeshDraggableBlock>(FindObjectsSortMode.None);
            HashSet<Vector2Int> union = new HashSet<Vector2Int>();

            for (int i = 0; i < blocks.Length; i++)
            {
                RuntimeTileMeshDraggableBlock block = blocks[i];
                if (block == null || !block.isActiveAndEnabled)
                    continue;

                FusionIntegrityBlockSnapshot snapshot = CaptureBlockSnapshot(
                    block,
                    sandbox.gridSize,
                    sandbox.gridOrigin);
                report.afterBlocks.Add(snapshot);
                report.actualVertexCount += snapshot.meshVertexCount;

                if (block.View != null)
                    AnalyzeBlockSnapshot(snapshot, block.View.CreateSettings(), report.issues);

                foreach (Vector2Int cell in snapshot.worldTiles)
                    union.Add(cell);
            }

            report.expectedTileCount = union.Count;
            report.actualTileCount = union.Count;
            report.expectedVertexCount = union.Count * 4;
            report.issueCount = report.issues.Count;
            return report;
        }

        public static string FormatReport(FusionIntegrityReport report)
        {
            if (report == null)
                return string.Empty;

            StringBuilder builder = new StringBuilder();
            builder.Append("[FusionIntegrity] ").Append(report.Summary);
            builder.Append("\nFrame: ").Append(report.frameLabel);
            builder.Append("\nBefore blocks: ").Append(report.beforeBlocks.Count);
            builder.Append(" | After blocks: ").Append(report.afterBlocks.Count);

            if (report.tileAccounting != null)
            {
                FusionIntegrityTileAccounting a = report.tileAccounting;
                builder.Append("\nTile Accounting:");
                builder.Append("\n  trigger=").Append(a.triggerBlockName)
                    .Append(" (").Append(a.triggerBlockTileCount).Append(")");
                builder.Append("\n  existingGroupBlocks=").Append(a.existingGroupBlockCount)
                    .Append(" existingGroupTileSumRaw=").Append(a.existingGroupTileSumRaw);
                builder.Append("\n  overlapWithinGroup=").Append(a.overlapWithinGroupCount);
                builder.Append("\n  expectedUnion=").Append(a.expectedUnionTileCount)
                    .Append(" actualMerged=").Append(a.actualMergedTileCount);
                builder.Append("\n  extra=").Append(a.extraGeneratedTileCount)
                    .Append(" missing=").Append(a.missingTileCount);
                builder.Append("\n  sandboxBefore=").Append(a.sandboxBeforeTileCount)
                    .Append(" sandboxAfter=").Append(a.sandboxAfterTileCount)
                    .Append(" outsideGroupBefore=").Append(a.sandboxOutsideGroupBeforeCount);
                if (a.triggerTiles.Count > 0)
                    builder.Append("\n  triggerTiles=").Append(FormatTileList(a.triggerTiles, 16));
                if (a.existingGroupTiles.Count > 0)
                    builder.Append("\n  existingGroupTiles=").Append(FormatTileList(a.existingGroupTiles, 16));
                if (a.expectedUnionTiles.Count > 0)
                    builder.Append("\n  expectedUnionTiles=").Append(FormatTileList(a.expectedUnionTiles, 16));
                if (a.extraTiles.Count > 0)
                    builder.Append("\n  extraTiles=").Append(FormatTileList(a.extraTiles, 16));
                if (a.missingTiles.Count > 0)
                    builder.Append("\n  missingTiles=").Append(FormatTileList(a.missingTiles, 16));
            }

            for (int i = 0; i < report.issues.Count; i++)
            {
                FusionIntegrityIssue issue = report.issues[i];
                builder.Append("\n  [").Append(issue.code).Append("] ").Append(issue.message);
                if (issue.affectedTiles != null && issue.affectedTiles.Count > 0)
                    builder.Append(" | tiles=").Append(FormatTileList(issue.affectedTiles, 12));
            }

            return builder.ToString();
        }

        private static Dictionary<RuntimeTileMeshDraggableBlock, HashSet<Vector2Int>> BuildGroupCellSets(
            IList<RuntimeTileMeshDraggableBlock> groupBlocks,
            float gridSize,
            Vector2 gridOrigin)
        {
            Dictionary<RuntimeTileMeshDraggableBlock, HashSet<Vector2Int>> groupCellSets =
                new Dictionary<RuntimeTileMeshDraggableBlock, HashSet<Vector2Int>>();
            if (groupBlocks == null)
                return groupCellSets;

            for (int i = 0; i < groupBlocks.Count; i++)
            {
                RuntimeTileMeshDraggableBlock block = groupBlocks[i];
                if (block == null)
                    continue;

                groupCellSets[block] = block.GetWorldCells(gridSize, gridOrigin);
            }

            return groupCellSets;
        }

        private static FusionIntegrityReport CreateReport(FusionIntegrityOperation operation, string contextLabel)
        {
            return new FusionIntegrityReport
            {
                operation = operation,
                contextLabel = contextLabel ?? string.Empty,
                timeSinceStartup = Time.time,
                frameLabel = "t=" + Time.time.ToString("0.000") + " f=" + Time.frameCount
            };
        }

        private static void CompareTileSets(
            HashSet<Vector2Int> expectedUnion,
            HashSet<Vector2Int> actualCells,
            string seedName,
            List<FusionIntegrityIssue> issues)
        {
            if (expectedUnion == null || actualCells == null)
                return;

            List<Vector2Int> missing = new List<Vector2Int>();
            foreach (Vector2Int cell in expectedUnion)
            {
                if (!actualCells.Contains(cell))
                    missing.Add(cell);
            }

            if (missing.Count > 0)
            {
                AddIssue(issues, IssueMergeTileLoss,
                    seedName + " merge lost " + missing.Count + " tile(s) from the pre-merge union.",
                    missing);
            }

            List<Vector2Int> unexpected = new List<Vector2Int>();
            foreach (Vector2Int cell in actualCells)
            {
                if (!expectedUnion.Contains(cell))
                    unexpected.Add(cell);
            }

            if (unexpected.Count > 0)
            {
                AddIssue(issues, IssueMergeTileGain,
                    seedName + " merge gained " + unexpected.Count + " unexpected tile(s).",
                    unexpected);
            }
        }

        private static void CaptureRenderedMeshCounts(
            RuntimeTileMeshDraggableBlock block,
            FusionIntegrityBlockSnapshot snapshot)
        {
            if (block == null || snapshot == null)
                return;

            MeshFilter[] filters = block.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                MeshFilter filter = filters[i];
                if (filter == null || filter.sharedMesh == null)
                    continue;

                snapshot.meshComponentCount++;
                snapshot.meshVertexCount += filter.sharedMesh.vertexCount;
                snapshot.meshTriangleCount += filter.sharedMesh.triangles.Length / 3;
                snapshot.buildSuccess = snapshot.buildSuccess || filter.sharedMesh.triangles.Length > 0;
            }
        }

        private static void CaptureBoundaryStats(IEnumerable<Vector2Int> tiles, FusionIntegrityBlockSnapshot snapshot)
        {
            if (tiles == null || snapshot == null)
                return;

            HashSet<Vector2Int> tileSet = tiles as HashSet<Vector2Int> ?? TileOccupancy.ToSet(tiles);
            if (tileSet.Count == 0)
                return;

            List<DirectedTileEdge> boundaryEdges = TileBoundaryExtractor.ExtractBoundaryEdges(tileSet);
            List<List<Vector2Int>> loops = PolygonLoopBuilder.BuildLoops(boundaryEdges);
            snapshot.boundaryEdgeCount = boundaryEdges.Count;
            snapshot.boundaryLoopCount = loops.Count;

            int consumed = 0;
            for (int i = 0; i < loops.Count; i++)
                consumed += loops[i].Count;

            snapshot.unconsumedBoundaryEdges = Mathf.Max(0, boundaryEdges.Count - consumed);
        }

        private static RuntimeTileMeshData BuildProbeMesh(IList<Vector2Int> tiles, RuntimeTileMeshSettings settings)
        {
            RuntimeTileMeshData meshData = new RuntimeTileMeshData();
            TileGridMeshGenerator.TryBuild(tiles, settings, meshData, out _);
            return meshData;
        }

        private static List<Vector2Int> SortTiles(IEnumerable<Vector2Int> tiles)
        {
            List<Vector2Int> sorted = new List<Vector2Int>();
            if (tiles == null)
                return sorted;

            foreach (Vector2Int tile in tiles)
                sorted.Add(tile);

            sorted.Sort((a, b) =>
            {
                int yCompare = a.y.CompareTo(b.y);
                return yCompare != 0 ? yCompare : a.x.CompareTo(b.x);
            });
            return sorted;
        }

        private static void AddIssue(
            List<FusionIntegrityIssue> issues,
            string code,
            string message,
            IList<Vector2Int> affectedTiles)
        {
            if (issues == null)
                return;

            FusionIntegrityIssue issue = new FusionIntegrityIssue
            {
                code = code,
                message = message
            };

            if (affectedTiles != null)
            {
                for (int i = 0; i < affectedTiles.Count; i++)
                    issue.affectedTiles.Add(affectedTiles[i]);
            }

            issues.Add(issue);
        }

        private static string FormatTileList(IList<Vector2Int> tiles, int maxCount)
        {
            if (tiles == null || tiles.Count == 0)
                return "[]";

            int count = Mathf.Min(maxCount, tiles.Count);
            StringBuilder builder = new StringBuilder();
            builder.Append('[');
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    builder.Append(", ");

                builder.Append('(').Append(tiles[i].x).Append(',').Append(tiles[i].y).Append(')');
            }

            if (tiles.Count > count)
                builder.Append(", ...+").Append(tiles.Count - count);

            builder.Append(']');
            return builder.ToString();
        }
    }
}
