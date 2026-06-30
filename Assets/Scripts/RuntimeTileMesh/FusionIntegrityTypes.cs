using System;
using System.Collections.Generic;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh
{
    public enum FusionIntegrityOperation
    {
        ManualAudit,
        BlockSpawn,
        BlockRebuild,
        BlockPlacement,
        MergeGroup,
        ConnectedRoomPlaneRebuild
    }

    [Serializable]
    public sealed class FusionIntegrityIssue
    {
        public string code;
        public string message;
        public List<Vector2Int> affectedTiles = new List<Vector2Int>();

        public FusionIntegrityIssue Clone()
        {
            return new FusionIntegrityIssue
            {
                code = code,
                message = message,
                affectedTiles = new List<Vector2Int>(affectedTiles)
            };
        }
    }

    [Serializable]
    public sealed class FusionIntegrityTileAccounting
    {
        public string triggerBlockName;
        public int triggerBlockTileCount;
        public int existingGroupBlockCount;
        public int existingGroupTileSumRaw;
        public int overlapWithinGroupCount;
        public int expectedUnionTileCount;
        public int actualMergedTileCount;
        public int extraGeneratedTileCount;
        public int missingTileCount;
        public int sandboxBeforeTileCount;
        public int sandboxAfterTileCount;
        public int sandboxOutsideGroupBeforeCount;
        public int duplicateLogicalTileEntries;
        public List<Vector2Int> triggerTiles = new List<Vector2Int>();
        public List<Vector2Int> existingGroupTiles = new List<Vector2Int>();
        public List<Vector2Int> expectedUnionTiles = new List<Vector2Int>();
        public List<Vector2Int> extraTiles = new List<Vector2Int>();
        public List<Vector2Int> missingTiles = new List<Vector2Int>();

        public FusionIntegrityTileAccounting Clone()
        {
            FusionIntegrityTileAccounting copy = new FusionIntegrityTileAccounting
            {
                triggerBlockName = triggerBlockName,
                triggerBlockTileCount = triggerBlockTileCount,
                existingGroupBlockCount = existingGroupBlockCount,
                existingGroupTileSumRaw = existingGroupTileSumRaw,
                overlapWithinGroupCount = overlapWithinGroupCount,
                expectedUnionTileCount = expectedUnionTileCount,
                actualMergedTileCount = actualMergedTileCount,
                extraGeneratedTileCount = extraGeneratedTileCount,
                missingTileCount = missingTileCount,
                sandboxBeforeTileCount = sandboxBeforeTileCount,
                sandboxAfterTileCount = sandboxAfterTileCount,
                sandboxOutsideGroupBeforeCount = sandboxOutsideGroupBeforeCount,
                duplicateLogicalTileEntries = duplicateLogicalTileEntries
            };
            copy.triggerTiles = new List<Vector2Int>(triggerTiles);
            copy.existingGroupTiles = new List<Vector2Int>(existingGroupTiles);
            copy.expectedUnionTiles = new List<Vector2Int>(expectedUnionTiles);
            copy.extraTiles = new List<Vector2Int>(extraTiles);
            copy.missingTiles = new List<Vector2Int>(missingTiles);
            return copy;
        }
    }

    [Serializable]
    public sealed class FusionIntegrityBlockSnapshot
    {
        public string blockName;
        public int instanceId;
        public bool isActive;
        public int logicalTileCount;
        public int logicalUniqueTileCount;
        public int worldTileCount;
        public int meshVertexCount;
        public int meshTriangleCount;
        public int meshComponentCount;
        public int boundaryEdgeCount;
        public int boundaryLoopCount;
        public int unconsumedBoundaryEdges;
        public bool buildSuccess;
        public bool meshCoversAllTiles;
        public List<Vector2Int> worldTiles = new List<Vector2Int>();
        public List<string> warnings = new List<string>();

        public FusionIntegrityBlockSnapshot Clone()
        {
            FusionIntegrityBlockSnapshot copy = new FusionIntegrityBlockSnapshot
            {
                blockName = blockName,
                instanceId = instanceId,
                isActive = isActive,
                logicalTileCount = logicalTileCount,
                logicalUniqueTileCount = logicalUniqueTileCount,
                worldTileCount = worldTileCount,
                meshVertexCount = meshVertexCount,
                meshTriangleCount = meshTriangleCount,
                meshComponentCount = meshComponentCount,
                boundaryEdgeCount = boundaryEdgeCount,
                boundaryLoopCount = boundaryLoopCount,
                unconsumedBoundaryEdges = unconsumedBoundaryEdges,
                buildSuccess = buildSuccess,
                meshCoversAllTiles = meshCoversAllTiles,
                worldTiles = new List<Vector2Int>(worldTiles),
                warnings = new List<string>(warnings)
            };
            return copy;
        }
    }

    [Serializable]
    public sealed class FusionIntegrityReport
    {
        public FusionIntegrityOperation operation;
        public string contextLabel;
        public float timeSinceStartup;
        public string frameLabel;
        public int expectedTileCount;
        public int actualTileCount;
        public int expectedVertexCount;
        public int actualVertexCount;
        public int issueCount;
        public FusionIntegrityTileAccounting tileAccounting;
        public List<FusionIntegrityBlockSnapshot> beforeBlocks = new List<FusionIntegrityBlockSnapshot>();
        public List<FusionIntegrityBlockSnapshot> afterBlocks = new List<FusionIntegrityBlockSnapshot>();
        public List<FusionIntegrityIssue> issues = new List<FusionIntegrityIssue>();

        public bool HasIssues => issues != null && issues.Count > 0;

        public string Summary
        {
            get
            {
                if (!HasIssues)
                    return operation + " OK | tiles=" + actualTileCount + " | " + contextLabel;

                return operation + " ISSUES=" + issueCount + " | expectedTiles=" + expectedTileCount +
                       " actualTiles=" + actualTileCount + " | " + contextLabel;
            }
        }

        public FusionIntegrityReport Clone()
        {
            FusionIntegrityReport copy = new FusionIntegrityReport
            {
                operation = operation,
                contextLabel = contextLabel,
                timeSinceStartup = timeSinceStartup,
                frameLabel = frameLabel,
                expectedTileCount = expectedTileCount,
                actualTileCount = actualTileCount,
                expectedVertexCount = expectedVertexCount,
                actualVertexCount = actualVertexCount,
                issueCount = issueCount,
                tileAccounting = tileAccounting != null ? tileAccounting.Clone() : null
            };

            for (int i = 0; i < beforeBlocks.Count; i++)
                copy.beforeBlocks.Add(beforeBlocks[i].Clone());

            for (int i = 0; i < afterBlocks.Count; i++)
                copy.afterBlocks.Add(afterBlocks[i].Clone());

            for (int i = 0; i < issues.Count; i++)
                copy.issues.Add(issues[i].Clone());

            return copy;
        }
    }
}
