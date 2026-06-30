#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using DuoCurtain.Vision;
using UnityEditor;
using UnityEngine;

namespace DuoCurtain.RuntimeTileMesh.Editor
{
    public static class RuntimeTileMeshSelfTest
    {
        [MenuItem("Tools/Duo Curtain/Runtime Tile Mesh/Run Self Test")]
        public static void Run()
        {
            int failures = 0;
            RuntimeTileMeshSettings settings = RuntimeTileMeshSettings.Default;

            failures += ExpectSuccessfulComponents("Single", RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.Single), settings, 1);
            failures += ExpectSuccessfulComponents("OneByThree", RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.OneByThree), settings, 1, expectedFirstVertexCount: 12);
            failures += ExpectSuccessfulComponents("L", RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.L), settings, 1);
            failures += ExpectSuccessfulComponents("T", RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.T), settings, 1);
            failures += ExpectSuccessfulComponents("Z", RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.Z), settings, 1);
            failures += ExpectDistinctPresetShapes();
            failures += ExpectDeterministicBuild("Z", RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.Z), settings);
            failures += ExpectSuccessfulComponents("DiagonalTouch", RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.DiagonalTouch), settings, 2);
            failures += ExpectSuccessfulComponents("RingWithHole", RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.RingWithHole), settings, 1);
            failures += ExpectDefaultFallbackMaterial();
            failures += ExpectFusionConnectionRules();
            failures += ExpectFusionBlockMergeRules();
            failures += ExpectFusionDoorRules();
            failures += ExpectSelectedBlockCarriesPlayer();
            failures += ExpectBlockInfoDescription();
            failures += ExpectTopologyMapRuntimeFusionFallback();
            failures += ExpectFusionIntegrityMerge();
            failures += ExpectFusionIntegrityTileAccounting();
            failures += ExpectFusionIntegrityUsesLatestBuild();
            failures += ExpectVisionConeAndProceduralRenderer();

            if (failures == 0)
            {
                Debug.Log("[RuntimeTileMeshSelfTest] All runtime tile mesh checks passed.");
                return;
            }

            string message = "[RuntimeTileMeshSelfTest] " + failures + " check(s) failed.";
            Debug.LogError(message);
            if (Application.isBatchMode)
                throw new Exception(message);
        }

        private static int ExpectSuccessfulComponents(
            string name,
            List<Vector2Int> tiles,
            RuntimeTileMeshSettings settings,
            int expectedComponents,
            int expectedFirstVertexCount = -1)
        {
            RuntimeTileMeshBuildResult result = RuntimeTileMeshBuilder.Build(tiles, settings);
            int failures = 0;

            if (result.components.Count != expectedComponents)
            {
                Debug.LogError("[RuntimeTileMeshSelfTest] " + name + " expected " + expectedComponents + " component(s), got " + result.components.Count + ".");
                failures++;
            }

            for (int i = 0; i < result.components.Count; i++)
            {
                RuntimeTileMeshComponentResult component = result.components[i];
                if (!component.success || component.meshData == null || component.meshData.triangles.Count == 0)
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] " + name + " component " + i + " did not generate triangles.");
                    failures++;
                    continue;
                }

                if (!FirstTriangleFacesDefault2DCamera(component.meshData))
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] " + name + " component " + i + " triangle winding faces away from the default 2D camera.");
                    failures++;
                }
            }

            if (expectedFirstVertexCount > 0 &&
                result.components.Count > 0 &&
                result.components[0].meshData != null &&
                result.components[0].meshData.vertices.Count != expectedFirstVertexCount)
            {
                Debug.LogError("[RuntimeTileMeshSelfTest] " + name + " expected first mesh to have " + expectedFirstVertexCount + " vertices, got " + result.components[0].meshData.vertices.Count + ".");
                failures++;
            }

            return failures;
        }

        private static int ExpectDistinctPresetShapes()
        {
            HashSet<Vector2Int> l = new HashSet<Vector2Int>(RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.L));
            HashSet<Vector2Int> z = new HashSet<Vector2Int>(RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.Z));
            if (!l.SetEquals(z))
                return 0;

            Debug.LogError("[RuntimeTileMeshSelfTest] L and Z presets must not share the same footprint.");
            return 1;
        }

        private static bool FirstTriangleFacesDefault2DCamera(RuntimeTileMeshData meshData)
        {
            if (meshData == null || meshData.triangles.Count < 3)
                return false;

            Vector3 a = meshData.vertices[meshData.triangles[0]];
            Vector3 b = meshData.vertices[meshData.triangles[1]];
            Vector3 c = meshData.vertices[meshData.triangles[2]];
            float z = Vector3.Cross(b - a, c - a).z;
            return z < -0.000001f;
        }

        private static int ExpectFusionConnectionRules()
        {
            int failures = 0;
            HashSet<Vector2Int> baseCells = new HashSet<Vector2Int> { Vector2Int.zero };

            failures += ExpectCellConnection("Overlap", baseCells, new HashSet<Vector2Int> { Vector2Int.zero }, true);
            failures += ExpectCellConnection("ShareEdgeOnly", baseCells, new HashSet<Vector2Int> { Vector2Int.right }, true);
            failures += ExpectCellShareEdgeOnly("ShareEdgeOnly", baseCells, new HashSet<Vector2Int> { Vector2Int.right }, true);
            failures += ExpectCellShareEdgeOnly("OverlapDoesNotShareEdge", baseCells, new HashSet<Vector2Int> { Vector2Int.zero }, false);
            failures += ExpectCellConnection("VerticalEdge", baseCells, new HashSet<Vector2Int> { Vector2Int.up }, true);
            failures += ExpectCellConnection("DiagonalCorner", baseCells, new HashSet<Vector2Int> { Vector2Int.one }, false);
            failures += ExpectCellConnection("OneCellGap", baseCells, new HashSet<Vector2Int> { new Vector2Int(2, 0) }, false);
            return failures;
        }

        private static int ExpectCellShareEdgeOnly(
            string name,
            HashSet<Vector2Int> ownCells,
            HashSet<Vector2Int> otherCells,
            bool expected)
        {
            bool actual = RuntimeTileMeshDraggableBlock.CellSetsShareEdge(ownCells, otherCells);
            if (actual == expected)
                return 0;

            Debug.LogError("[RuntimeTileMeshSelfTest] Fusion edge-only rule " + name + " expected " + expected + ", got " + actual + ".");
            return 1;
        }

        private static int ExpectDeterministicBuild(
            string name,
            List<Vector2Int> tiles,
            RuntimeTileMeshSettings settings)
        {
            RuntimeTileMeshBuildResult baseline = RuntimeTileMeshBuilder.Build(tiles, settings);
            if (baseline.components.Count == 0 || !baseline.components[0].success)
            {
                Debug.LogError("[RuntimeTileMeshSelfTest] " + name + " deterministic baseline build failed.");
                return 1;
            }

            RuntimeTileMeshData baselineMesh = baseline.components[0].meshData;
            int baselineVertices = baselineMesh.vertices.Count;
            int baselineTriangles = baselineMesh.triangles.Count;

            for (int attempt = 0; attempt < 12; attempt++)
            {
                List<Vector2Int> shuffled = new List<Vector2Int>(tiles);
                Shuffle(shuffled, attempt + 17);
                RuntimeTileMeshBuildResult result = RuntimeTileMeshBuilder.Build(shuffled, settings);
                if (result.components.Count == 0 || !result.components[0].success || result.components[0].meshData == null)
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] " + name + " deterministic build failed on attempt " + attempt + ".");
                    return 1;
                }

                RuntimeTileMeshData meshData = result.components[0].meshData;
                if (meshData.vertices.Count != baselineVertices || meshData.triangles.Count != baselineTriangles)
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] " + name + " deterministic build changed mesh topology on attempt " + attempt + ".");
                    return 1;
                }
            }

            return 0;
        }

        private static void Shuffle(List<Vector2Int> values, int seed)
        {
            System.Random random = new System.Random(seed);
            for (int i = values.Count - 1; i > 0; i--)
            {
                int swapIndex = random.Next(i + 1);
                Vector2Int temp = values[i];
                values[i] = values[swapIndex];
                values[swapIndex] = temp;
            }
        }

        private static int ExpectCellConnection(
            string name,
            HashSet<Vector2Int> ownCells,
            HashSet<Vector2Int> otherCells,
            bool expected)
        {
            bool actual = RuntimeTileMeshDraggableBlock.CellSetsOverlapOrShareEdge(ownCells, otherCells);
            if (actual == expected)
                return 0;

            Debug.LogError("[RuntimeTileMeshSelfTest] Fusion connection rule " + name + " expected " + expected + ", got " + actual + ".");
            return 1;
        }

        private static int ExpectDefaultFallbackMaterial()
        {
            GameObject root = new GameObject("RuntimeTileMeshSelfTest Default Visual");
            try
            {
                RuntimeTileMeshView view = root.AddComponent<RuntimeTileMeshView>();
                view.material = null;
                view.tiles = RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.Single);
                view.tileSize = Vector2.one;
                view.rebuildOnStart = false;
                view.Rebuild();

                List<Renderer> renderers = new List<Renderer>();
                view.CollectGeneratedRenderers(renderers);
                if (renderers.Count == 0)
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Default fallback material test generated no renderers.");
                    return 1;
                }

                Material material = renderers[0] != null ? renderers[0].sharedMaterial : null;
                if (material == null || material.shader == null)
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Default fallback material test expected a non-null white fallback material.");
                    return 1;
                }

                if (material.HasProperty("_BaseColor") && material.GetColor("_BaseColor") != Color.white)
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Default fallback material _BaseColor was not white.");
                    return 1;
                }

                if (material.HasProperty("_Color") && material.GetColor("_Color") != Color.white)
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Default fallback material _Color was not white.");
                    return 1;
                }

                return 0;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static int ExpectFusionBlockMergeRules()
        {
            int failures = 0;
            GameObject controllerObject = new GameObject("RuntimeTileMeshSelfTest Fusion Controller");
            RuntimeTileMeshFusionSandbox sandbox = controllerObject.AddComponent<RuntimeTileMeshFusionSandbox>();
            sandbox.gridSize = 1f;
            sandbox.gridOrigin = Vector2.zero;
            sandbox.mergeAfterPlacement = true;
            sandbox.mergeExistingBlocksOnAwake = false;
            sandbox.snapExistingBlocksOnAwake = false;
            sandbox.deactivateAbsorbedBlocksImmediately = true;

            List<GameObject> cleanup = new List<GameObject> { controllerObject };
            try
            {
                failures += ExpectBlockMerge(
                    sandbox,
                    cleanup,
                    "Overlap",
                    new[] { CreateBlockSpec(Vector2Int.zero) },
                    new[] { CreateBlockSpec(Vector2Int.zero) },
                    new HashSet<Vector2Int> { Vector2Int.zero },
                    expectedAbsorbed: 1,
                    expectedRemainingBlocks: 1);

                failures += ExpectBlockMerge(
                    sandbox,
                    cleanup,
                    "SharedEdge",
                    new[] { CreateBlockSpec(Vector2Int.zero) },
                    new[] { CreateBlockSpec(Vector2Int.right) },
                    new HashSet<Vector2Int> { Vector2Int.zero, Vector2Int.right },
                    expectedAbsorbed: 1,
                    expectedRemainingBlocks: 1);

                failures += ExpectBlockMerge(
                    sandbox,
                    cleanup,
                    "ConnectedChain",
                    new[] { CreateBlockSpec(Vector2Int.zero) },
                    new[] { CreateBlockSpec(Vector2Int.right), CreateBlockSpec(new Vector2Int(2, 0)) },
                    new HashSet<Vector2Int> { Vector2Int.zero, Vector2Int.right, new Vector2Int(2, 0) },
                    expectedAbsorbed: 2,
                    expectedRemainingBlocks: 1);

                failures += ExpectBlockMerge(
                    sandbox,
                    cleanup,
                    "DiagonalDoesNotMerge",
                    new[] { CreateBlockSpec(Vector2Int.zero) },
                    new[] { CreateBlockSpec(Vector2Int.one) },
                    new HashSet<Vector2Int> { Vector2Int.zero },
                    expectedAbsorbed: 0,
                    expectedRemainingBlocks: 2);
            }
            finally
            {
                for (int i = cleanup.Count - 1; i >= 0; i--)
                {
                    if (cleanup[i] != null)
                        UnityEngine.Object.DestroyImmediate(cleanup[i]);
                }
            }

            return failures;
        }

        private static int ExpectSelectedBlockCarriesPlayer()
        {
            GameObject controllerObject = new GameObject("RuntimeTileMeshSelfTest Player Carry Controller");
            GameObject playerObject = new GameObject("RuntimeTileMeshSelfTest Player");
            List<GameObject> cleanup = new List<GameObject> { controllerObject, playerObject };

            try
            {
                RuntimeTileMeshFusionSandbox sandbox = controllerObject.AddComponent<RuntimeTileMeshFusionSandbox>();
                sandbox.gridSize = 1f;
                sandbox.gridOrigin = Vector2.zero;
                sandbox.managementInputEnabled = true;
                sandbox.mergeAfterPlacement = false;
                sandbox.carryPlayerWithSelectedBlock = true;

                PlayerControl player = playerObject.AddComponent<PlayerControl>();
                player.SetWorldPositionImmediate(new Vector3(0.25f, 0.75f, 0f));
                sandbox.playerControl = player;

                RuntimeTileMeshDraggableBlock block = CreateSelfTestBlock(
                    "Player Carry Block",
                    new[] { CreateBlockSpec(Vector2Int.zero) },
                    cleanup);

                sandbox.BeginDraggingBlock(block, new Vector3(4f, 3f, 0f), false);
                Vector3 expectedPlayerPosition = new Vector3(4.25f, 3.75f, 0f);
                if (!sandbox.IsCarryingPlayer ||
                    Vector3.Distance(player.PlayerWorldPosition, expectedPlayerPosition) > 0.0001f)
                {
                    Debug.LogError(
                        "[RuntimeTileMeshSelfTest] Selected block should carry the player while preserving the player's local offset. Expected " +
                        expectedPlayerPosition + ", got " + player.PlayerWorldPosition + ".");
                    return 1;
                }

                sandbox.SetManagementInputEnabled(false, true);
                if (sandbox.IsCarryingPlayer ||
                    Vector3.Distance(player.PlayerWorldPosition, expectedPlayerPosition) > 0.0001f)
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Player carry should release on placement without changing the final player position.");
                    return 1;
                }

                return 0;
            }
            finally
            {
                for (int i = cleanup.Count - 1; i >= 0; i--)
                {
                    if (cleanup[i] != null)
                        UnityEngine.Object.DestroyImmediate(cleanup[i]);
                }
            }
        }

        private static int ExpectBlockInfoDescription()
        {
            List<GameObject> cleanup = new List<GameObject>();
            try
            {
                RuntimeTileMeshDraggableBlock block = CreateSelfTestBlock(
                    "Block Info Description",
                    new[]
                    {
                        CreateBlockSpec(Vector2Int.zero),
                        CreateBlockSpec(new Vector2Int(1, 2))
                    },
                    cleanup);
                block.blockType = string.Empty;

                string fallbackDescription = RuntimeTileMeshBlockInfoOverlay.BuildDescription(
                    block,
                    "DEFAULT",
                    "UNIT",
                    true);
                if (fallbackDescription != "2X3 UNIT\nDEFAULT")
                {
                    Debug.LogError(
                        "[RuntimeTileMeshSelfTest] Block info fallback description was incorrect: " +
                        fallbackDescription);
                    return 1;
                }

                block.blockType = "Kitchen";
                string typedDescription = RuntimeTileMeshBlockInfoOverlay.BuildDescription(
                    block,
                    "DEFAULT",
                    "UNIT",
                    true);
                if (typedDescription != "2X3 UNIT\nKITCHEN")
                {
                    Debug.LogError(
                        "[RuntimeTileMeshSelfTest] Block info typed description was incorrect: " +
                        typedDescription);
                    return 1;
                }

                return 0;
            }
            finally
            {
                for (int i = cleanup.Count - 1; i >= 0; i--)
                {
                    if (cleanup[i] != null)
                        UnityEngine.Object.DestroyImmediate(cleanup[i]);
                }
            }
        }

        private static int ExpectTopologyMapRuntimeFusionFallback()
        {
            List<GameObject> cleanup = new List<GameObject>();
            try
            {
                GameObject controllerObject = new GameObject("RuntimeTileMeshSelfTest Topology Provider");
                cleanup.Add(controllerObject);
                RuntimeTileMeshFusionSandbox sandbox = controllerObject.AddComponent<RuntimeTileMeshFusionSandbox>();
                sandbox.gridSize = 1f;
                sandbox.gridOrigin = Vector2.zero;

                CreateSelfTestBlock(
                    "Topology Runtime Block A",
                    new[]
                    {
                        CreateBlockSpec(new Vector2Int(111, 222)),
                        CreateBlockSpec(new Vector2Int(112, 222))
                    },
                    cleanup);
                CreateSelfTestBlock(
                    "Topology Runtime Block B",
                    new[] { CreateBlockSpec(new Vector2Int(111, 223)) },
                    cleanup);

                TopologyMapDataProvider provider = controllerObject.AddComponent<TopologyMapDataProvider>();
                provider.autoFindSource = false;
                provider.topologyGrid = null;
                provider.fusionSandbox = sandbox;
                provider.Refresh(false);

                if (!provider.HasTopology ||
                    !provider.IsRoomCell(new Vector2Int(111, 222)) ||
                    !provider.IsRoomCell(new Vector2Int(112, 222)) ||
                    !provider.IsRoomCell(new Vector2Int(111, 223)))
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Topology provider did not collect runtime fusion cells.");
                    return 1;
                }

                if (!provider.TryGetRoomCell(new Vector3(111.25f, 222.25f, 0f), out Vector2Int roomCell) ||
                    roomCell != new Vector2Int(111, 222))
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Topology provider did not resolve a runtime fusion world point to the expected cell.");
                    return 1;
                }

                if (!provider.TryGetWorldLogicalPosition(
                        new Vector3(111.5f, 222.5f, 0f),
                        out Vector2 logicalPosition,
                        out _) ||
                    Vector2.Distance(logicalPosition, new Vector2(111f, 222f)) > 0.0001f)
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Topology provider did not project runtime fusion world position to map space.");
                    return 1;
                }

                return 0;
            }
            finally
            {
                for (int i = cleanup.Count - 1; i >= 0; i--)
                {
                    if (cleanup[i] != null)
                        UnityEngine.Object.DestroyImmediate(cleanup[i]);
                }
            }
        }

        private static int ExpectFusionDoorRules()
        {
            int failures = 0;
            GameObject controllerObject = new GameObject("RuntimeTileMeshSelfTest Door Controller");
            RuntimeTileMeshFusionSandbox sandbox = controllerObject.AddComponent<RuntimeTileMeshFusionSandbox>();
            sandbox.gridSize = 1f;
            sandbox.gridOrigin = Vector2.zero;
            sandbox.mergeAfterPlacement = true;
            sandbox.mergeExistingBlocksOnAwake = false;
            sandbox.snapExistingBlocksOnAwake = false;
            sandbox.deactivateAbsorbedBlocksImmediately = true;
            sandbox.generateDoorsOnFusion = true;
            sandbox.doorSharedEdgeCells = 3;
            sandbox.doorOpenAngleDegrees = 90f;

            List<GameObject> cleanup = new List<GameObject> { controllerObject };
            try
            {
                failures += ExpectDoorMerge(
                    sandbox,
                    cleanup,
                    "OneByThreeAgainstTwoByThree",
                    new[]
                    {
                        CreateBlockSpec(new Vector2Int(0, 0)),
                        CreateBlockSpec(new Vector2Int(0, 1)),
                        CreateBlockSpec(new Vector2Int(0, 2))
                    },
                    new[]
                    {
                        CreateBlockSpec(new Vector2Int(1, 0)),
                        CreateBlockSpec(new Vector2Int(1, 1)),
                        CreateBlockSpec(new Vector2Int(1, 2)),
                        CreateBlockSpec(new Vector2Int(2, 0)),
                        CreateBlockSpec(new Vector2Int(2, 1)),
                        CreateBlockSpec(new Vector2Int(2, 2))
                    },
                    expectedDoorCount: 1,
                    expectedDoorCenter: new Vector2(1f, 1.5f));

                failures += ExpectDoorMerge(
                    sandbox,
                    cleanup,
                    "FourCellSharedEdgeDoesNotCreateDoor",
                    new[]
                    {
                        CreateBlockSpec(new Vector2Int(5, 0)),
                        CreateBlockSpec(new Vector2Int(5, 1)),
                        CreateBlockSpec(new Vector2Int(5, 2)),
                        CreateBlockSpec(new Vector2Int(5, 3))
                    },
                    new[]
                    {
                        CreateBlockSpec(new Vector2Int(6, 0)),
                        CreateBlockSpec(new Vector2Int(6, 1)),
                        CreateBlockSpec(new Vector2Int(6, 2)),
                        CreateBlockSpec(new Vector2Int(6, 3))
                    },
                    expectedDoorCount: 0,
                    expectedDoorCenter: Vector2.zero);

                failures += ExpectDoorWallSpanExtendsAfterLaterMerge(sandbox, cleanup);
            }
            finally
            {
                for (int i = cleanup.Count - 1; i >= 0; i--)
                {
                    if (cleanup[i] != null)
                        UnityEngine.Object.DestroyImmediate(cleanup[i]);
                }
            }

            return failures;
        }

        private static int ExpectDoorWallSpanExtendsAfterLaterMerge(
            RuntimeTileMeshFusionSandbox sandbox,
            List<GameObject> cleanup)
        {
            RuntimeTileMeshDraggableBlock survivor = CreateSelfTestBlock(
                "Door Span Extension Survivor",
                new[]
                {
                    CreateBlockSpec(new Vector2Int(20, 0)),
                    CreateBlockSpec(new Vector2Int(20, 1)),
                    CreateBlockSpec(new Vector2Int(20, 2))
                },
                cleanup);
            RuntimeTileMeshDraggableBlock candidate = CreateSelfTestBlock(
                "Door Span Extension Candidate",
                new[]
                {
                    CreateBlockSpec(new Vector2Int(21, 0)),
                    CreateBlockSpec(new Vector2Int(21, 1)),
                    CreateBlockSpec(new Vector2Int(21, 2)),
                    CreateBlockSpec(new Vector2Int(22, 0)),
                    CreateBlockSpec(new Vector2Int(22, 1)),
                    CreateBlockSpec(new Vector2Int(22, 2))
                },
                cleanup);

            List<RuntimeTileMeshDraggableBlock> blocks = new List<RuntimeTileMeshDraggableBlock> { survivor, candidate };
            sandbox.MergeConnectedBlocks(survivor, blocks);

            RuntimeTileMeshFusionDoor[] doors = survivor.GetComponentsInChildren<RuntimeTileMeshFusionDoor>(true);
            if (doors.Length != 1)
            {
                Debug.LogError("[RuntimeTileMeshSelfTest] Door span extension expected one initial door, got " + doors.Length + ".");
                return 1;
            }

            RuntimeTileMeshDraggableBlock extension = CreateSelfTestBlock(
                "Door Span Extension Later Merge",
                new[]
                {
                    CreateBlockSpec(new Vector2Int(20, 3)),
                    CreateBlockSpec(new Vector2Int(21, 3))
                },
                cleanup);

            blocks = new List<RuntimeTileMeshDraggableBlock> { survivor, extension };
            sandbox.MergeConnectedBlocks(survivor, blocks);
            doors = survivor.GetComponentsInChildren<RuntimeTileMeshFusionDoor>(true);

            int failures = 0;
            if (doors.Length != 1)
            {
                Debug.LogError("[RuntimeTileMeshSelfTest] Door span extension should preserve the original door count, got " + doors.Length + ".");
                failures++;
            }

            if (doors.Length > 0)
            {
                RuntimeTileMeshFusionDoor door = doors[0];
                if (door.wallVariableStart != 0 || door.wallCellLength != 4 || door.doorVariableOffset != 1)
                {
                    Debug.LogError(
                        "[RuntimeTileMeshSelfTest] Door span extension expected wall start 0, length 4, door offset 1; got start " +
                        door.wallVariableStart + ", length " + door.wallCellLength + ", offset " + door.doorVariableOffset + ".");
                    failures++;
                }

                if (!sandbox.TryBlockDoorMovement(new Vector3(20.75f, 3.5f, 0f), new Vector3(21.25f, 3.5f, 0f), 0.05f))
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Door span extension expected the extended upper wall segment to block movement.");
                    failures++;
                }
            }

            return failures;
        }

        private static int ExpectDoorMerge(
            RuntimeTileMeshFusionSandbox sandbox,
            List<GameObject> cleanup,
            string name,
            IEnumerable<BlockSpec> survivorSpecs,
            IEnumerable<BlockSpec> candidateSpecs,
            int expectedDoorCount,
            Vector2 expectedDoorCenter)
        {
            RuntimeTileMeshDraggableBlock survivor = CreateSelfTestBlock(name + " Survivor", survivorSpecs, cleanup);
            RuntimeTileMeshDraggableBlock candidate = CreateSelfTestBlock(name + " Candidate", candidateSpecs, cleanup);
            List<RuntimeTileMeshDraggableBlock> blocks = new List<RuntimeTileMeshDraggableBlock> { survivor, candidate };

            sandbox.MergeConnectedBlocks(survivor, blocks);
            RuntimeTileMeshFusionDoor[] doors = survivor.GetComponentsInChildren<RuntimeTileMeshFusionDoor>(true);
            int failures = 0;
            if (doors.Length != expectedDoorCount)
            {
                Debug.LogError("[RuntimeTileMeshSelfTest] Fusion door rule " + name + " expected " + expectedDoorCount + " door(s), got " + doors.Length + ".");
                failures++;
            }

            if (expectedDoorCount > 0 && doors.Length > 0)
            {
                RuntimeTileMeshFusionDoor door = doors[0];
                door.toggleCooldown = 0f;
                float distance = Vector2.Distance(door.seamCenter, expectedDoorCenter);
                if (distance > 0.0001f)
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Fusion door rule " + name + " expected door center " + expectedDoorCenter + ", got " + door.seamCenter + ".");
                    failures++;
                }

                if (!sandbox.TryBlockDoorMovement(new Vector3(0.75f, 0.5f, 0f), new Vector3(1.25f, 0.5f, 0f), 0.05f))
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Fusion door rule " + name + " expected the sandbox-level lower safe wall segment to block movement.");
                    failures++;
                }

                if (!sandbox.TryBlockDoorMovement(new Vector3(0.75f, 1.5f, 0f), new Vector3(1.25f, 1.5f, 0f), 0.05f))
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Fusion door rule " + name + " expected the sandbox-level closed door segment to block and open.");
                    failures++;
                }

                if (!door.IsOpen)
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Fusion door rule " + name + " expected the door to open after sandbox-level player collision.");
                    failures++;
                }

                if (sandbox.TryBlockDoorMovement(new Vector3(0.75f, 1.5f, 0f), new Vector3(1.25f, 1.5f, 0f), 0.05f))
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Fusion door rule " + name + " expected the sandbox-level open door segment to allow movement.");
                    failures++;
                }

                if (!sandbox.TryBlockDoorMovement(new Vector3(1.5f, 0.65f, 0f), new Vector3(1.5f, 1.05f, 0f), 0.05f))
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Fusion door rule " + name + " expected collision with the open door panel to close and block movement.");
                    failures++;
                }

                if (door.IsOpen)
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Fusion door rule " + name + " expected the door to be closed after hitting the open panel.");
                    failures++;
                }

                if (!sandbox.TryBlockDoorMovement(new Vector3(0.75f, 1.1f, 0f), new Vector3(1.25f, 1.1f, 0f), 0.05f))
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Fusion door rule " + name + " expected lower-half collision with the closed door to open and block movement.");
                    failures++;
                }

                if (door.HingeEnd != RuntimeTileMeshFusionDoor.DoorHingeEnd.Positive)
                {
                    Debug.LogError("[RuntimeTileMeshSelfTest] Fusion door rule " + name + " expected lower-half collision to choose the upper wall-connected endpoint as the hinge.");
                    failures++;
                }
            }

            return failures;
        }

        private static int ExpectBlockMerge(
            RuntimeTileMeshFusionSandbox sandbox,
            List<GameObject> cleanup,
            string name,
            IEnumerable<BlockSpec> survivorSpecs,
            IEnumerable<BlockSpec> candidateSpecs,
            HashSet<Vector2Int> expectedSurvivorWorldCells,
            int expectedAbsorbed,
            int expectedRemainingBlocks)
        {
            RuntimeTileMeshDraggableBlock survivor = CreateSelfTestBlock(name + " Survivor", survivorSpecs, cleanup);
            List<RuntimeTileMeshDraggableBlock> blocks = new List<RuntimeTileMeshDraggableBlock> { survivor };
            foreach (BlockSpec spec in candidateSpecs)
                blocks.Add(CreateSelfTestBlock(name + " Candidate", new[] { spec }, cleanup));

            int absorbed = sandbox.MergeConnectedBlocks(survivor, blocks);
            int failures = 0;

            if (absorbed != expectedAbsorbed)
            {
                Debug.LogError("[RuntimeTileMeshSelfTest] Fusion block merge " + name + " expected " + expectedAbsorbed + " absorbed block(s), got " + absorbed + ".");
                failures++;
            }

            if (blocks.Count != expectedRemainingBlocks)
            {
                Debug.LogError("[RuntimeTileMeshSelfTest] Fusion block merge " + name + " expected " + expectedRemainingBlocks + " remaining block(s), got " + blocks.Count + ".");
                failures++;
            }

            HashSet<Vector2Int> actualCells = survivor.GetWorldCells(sandbox.gridSize, sandbox.gridOrigin);
            if (!CellSetsEqual(actualCells, expectedSurvivorWorldCells))
            {
                Debug.LogError("[RuntimeTileMeshSelfTest] Fusion block merge " + name + " produced wrong survivor cells. Expected " + FormatCells(expectedSurvivorWorldCells) + ", got " + FormatCells(actualCells) + ".");
                failures++;
            }

            return failures;
        }

        private static RuntimeTileMeshDraggableBlock CreateSelfTestBlock(
            string name,
            IEnumerable<BlockSpec> specs,
            List<GameObject> cleanup)
        {
            List<BlockSpec> specList = new List<BlockSpec>(specs);
            Vector2Int rootCell = specList.Count > 0 ? specList[0].worldCell : Vector2Int.zero;
            List<Vector2Int> localCells = new List<Vector2Int>();
            for (int i = 0; i < specList.Count; i++)
                localCells.Add(specList[i].worldCell - rootCell);

            GameObject root = new GameObject(name);
            root.transform.position = new Vector3(rootCell.x, rootCell.y, 0f);
            cleanup.Add(root);

            RuntimeTileMeshView view = root.AddComponent<RuntimeTileMeshView>();
            view.tiles = localCells;
            view.tileSize = Vector2.one;
            view.rebuildOnStart = false;
            view.buildPolygonCollider2D = false;

            return root.AddComponent<RuntimeTileMeshDraggableBlock>();
        }

        private static BlockSpec CreateBlockSpec(Vector2Int worldCell)
        {
            return new BlockSpec { worldCell = worldCell };
        }

        private static bool CellSetsEqual(HashSet<Vector2Int> a, HashSet<Vector2Int> b)
        {
            if (a == null || b == null || a.Count != b.Count)
                return false;

            foreach (Vector2Int cell in a)
            {
                if (!b.Contains(cell))
                    return false;
            }

            return true;
        }

        private static string FormatCells(HashSet<Vector2Int> cells)
        {
            List<Vector2Int> sorted = new List<Vector2Int>(cells);
            sorted.Sort((a, b) =>
            {
                int yCompare = a.y.CompareTo(b.y);
                return yCompare != 0 ? yCompare : a.x.CompareTo(b.x);
            });

            List<string> formatted = new List<string>(sorted.Count);
            for (int i = 0; i < sorted.Count; i++)
                formatted.Add("(" + sorted[i].x + "," + sorted[i].y + ")");

            return string.Join(", ", formatted);
        }

        private static int ExpectFusionIntegrityMerge()
        {
            GameObject controllerObject = new GameObject("RuntimeTileMeshSelfTest Integrity Controller");
            RuntimeTileMeshFusionSandbox sandbox = controllerObject.AddComponent<RuntimeTileMeshFusionSandbox>();
            RuntimeTileMeshFusionIntegrityMonitor monitor =
                controllerObject.GetComponent<RuntimeTileMeshFusionIntegrityMonitor>();
            if (monitor == null)
                monitor = controllerObject.AddComponent<RuntimeTileMeshFusionIntegrityMonitor>();
            sandbox.gridSize = 1f;
            sandbox.gridOrigin = Vector2.zero;
            sandbox.mergeAfterPlacement = true;
            sandbox.mergeExistingBlocksOnAwake = false;
            sandbox.snapExistingBlocksOnAwake = false;
            sandbox.recordFusionIntegrity = true;
            monitor.fusionSandbox = sandbox;
            monitor.monitorEnabled = true;
            monitor.monitorMergeGroups = true;
            monitor.logIssuesToConsole = false;

            List<GameObject> cleanup = new List<GameObject> { controllerObject };
            try
            {
                RuntimeTileMeshDraggableBlock zA = CreateSelfTestBlock(
                    "Integrity Z A",
                    RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.Z),
                    cleanup,
                    new Vector2Int(0, 0));
                RuntimeTileMeshDraggableBlock zB = CreateSelfTestBlock(
                    "Integrity Z B",
                    RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.Z),
                    cleanup,
                    new Vector2Int(1, 0));

                HashSet<Vector2Int> expected = RuntimeTileMeshFusionIntegrityAnalyzer.CollectUnionTiles(
                    new[] { zA, zB },
                    sandbox.gridSize,
                    sandbox.gridOrigin);
                List<RuntimeTileMeshDraggableBlock> blocks = new List<RuntimeTileMeshDraggableBlock> { zA, zB };
                sandbox.MergeConnectedBlocks(zB, blocks);

                if (monitor.IssueReportCount > 0)
                {
                    monitor.TryGetLatestIssueReport(out FusionIntegrityReport report);
                    Debug.LogError(
                        "[RuntimeTileMeshSelfTest] Fusion integrity merge reported issues: " +
                        RuntimeTileMeshFusionIntegrityAnalyzer.FormatReport(report));
                    return 1;
                }

                HashSet<Vector2Int> actual = zB.GetWorldCells(sandbox.gridSize, sandbox.gridOrigin);
                if (expected.Count != actual.Count)
                {
                    Debug.LogError(
                        "[RuntimeTileMeshSelfTest] Fusion integrity merge tile count mismatch. Expected " +
                        expected.Count + ", got " + actual.Count + ".");
                    return 1;
                }

                return 0;
            }
            finally
            {
                for (int i = cleanup.Count - 1; i >= 0; i--)
                {
                    if (cleanup[i] != null)
                        UnityEngine.Object.DestroyImmediate(cleanup[i]);
                }
            }
        }

        private static int ExpectFusionIntegrityTileAccounting()
        {
            GameObject controllerObject = new GameObject("RuntimeTileMeshSelfTest Tile Accounting Controller");
            RuntimeTileMeshFusionSandbox sandbox = controllerObject.AddComponent<RuntimeTileMeshFusionSandbox>();
            RuntimeTileMeshFusionIntegrityMonitor monitor =
                controllerObject.GetComponent<RuntimeTileMeshFusionIntegrityMonitor>();
            if (monitor == null)
                monitor = controllerObject.AddComponent<RuntimeTileMeshFusionIntegrityMonitor>();
            sandbox.gridSize = 1f;
            sandbox.gridOrigin = Vector2.zero;
            sandbox.mergeAfterPlacement = true;
            sandbox.mergeExistingBlocksOnAwake = false;
            sandbox.snapExistingBlocksOnAwake = false;
            sandbox.recordFusionIntegrity = true;
            monitor.fusionSandbox = sandbox;
            monitor.monitorEnabled = true;
            monitor.monitorMergeGroups = true;
            monitor.logIssuesToConsole = false;

            List<GameObject> cleanup = new List<GameObject> { controllerObject };
            try
            {
                List<Vector2Int> duplicateLocalShape = new List<Vector2Int>
                {
                    Vector2Int.zero,
                    Vector2Int.zero,
                    new Vector2Int(1, 0)
                };

                RuntimeTileMeshDraggableBlock blockA = CreateSelfTestBlock(
                    "Accounting A",
                    duplicateLocalShape,
                    cleanup,
                    new Vector2Int(0, 0));
                RuntimeTileMeshDraggableBlock blockB = CreateSelfTestBlock(
                    "Accounting B",
                    RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.Single),
                    cleanup,
                    new Vector2Int(2, 0));

                HashSet<Vector2Int> expectedUnion = RuntimeTileMeshFusionIntegrityAnalyzer.CollectUnionTiles(
                    new[] { blockA, blockB },
                    sandbox.gridSize,
                    sandbox.gridOrigin);
                List<RuntimeTileMeshDraggableBlock> blocks = new List<RuntimeTileMeshDraggableBlock> { blockA, blockB };
                sandbox.MergeConnectedBlocks(blockB, blocks);

                if (monitor.IssueReportCount > 0)
                {
                    monitor.TryGetLatestIssueReport(out FusionIntegrityReport issueReport);
                    Debug.LogError(
                        "[RuntimeTileMeshSelfTest] Tile accounting merge reported issues: " +
                        RuntimeTileMeshFusionIntegrityAnalyzer.FormatReport(issueReport));
                    return 1;
                }

                HashSet<Vector2Int> actual = blockB.GetWorldCells(sandbox.gridSize, sandbox.gridOrigin);
                if (expectedUnion.Count != actual.Count)
                {
                    Debug.LogError(
                        "[RuntimeTileMeshSelfTest] Tile accounting count mismatch. Expected union " +
                        expectedUnion.Count + ", got " + actual.Count + ".");
                    return 1;
                }

                RuntimeTileMeshView view = blockB.View;
                int uniqueLocal = RuntimeTileMeshFusionIntegrityAnalyzer.CountUniqueLocalTiles(view.tiles);
                if (view.tiles.Count != uniqueLocal)
                {
                    Debug.LogError(
                        "[RuntimeTileMeshSelfTest] Merge should dedupe local tiles. Listed " +
                        view.tiles.Count + ", unique " + uniqueLocal + ".");
                    return 1;
                }

                FusionIntegrityReport latest = monitor.Reports.Count > 0
                    ? monitor.Reports[monitor.Reports.Count - 1]
                    : null;
                FusionIntegrityTileAccounting accounting = latest != null ? latest.tileAccounting : null;
                if (accounting == null ||
                    accounting.triggerBlockTileCount != 1 ||
                    accounting.expectedUnionTileCount != expectedUnion.Count ||
                    accounting.extraGeneratedTileCount != 0)
                {
                    Debug.LogError(
                        "[RuntimeTileMeshSelfTest] Tile accounting fields incorrect. " +
                        (accounting != null
                            ? "trigger=" + accounting.triggerBlockTileCount +
                              " expectedUnion=" + accounting.expectedUnionTileCount +
                              " extra=" + accounting.extraGeneratedTileCount
                            : "accounting=null"));
                    return 1;
                }

                return 0;
            }
            finally
            {
                for (int i = cleanup.Count - 1; i >= 0; i--)
                {
                    if (cleanup[i] != null)
                        UnityEngine.Object.DestroyImmediate(cleanup[i]);
                }
            }
        }

        private static int ExpectFusionIntegrityUsesLatestBuild()
        {
            List<GameObject> cleanup = new List<GameObject>();
            Mesh staleMesh = null;
            try
            {
                RuntimeTileMeshDraggableBlock block = CreateSelfTestBlock(
                    "Integrity Latest Build",
                    RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.Z),
                    cleanup,
                    Vector2Int.zero);

                GameObject staleVisual = new GameObject("Stale Serialized Visual");
                staleVisual.transform.SetParent(block.transform, false);
                cleanup.Add(staleVisual);
                MeshFilter filter = staleVisual.AddComponent<MeshFilter>();
                staleMesh = new Mesh { name = "Legacy Six Vertex Mesh" };
                staleMesh.vertices = new[]
                {
                    new Vector3(0f, 0f, 0f),
                    new Vector3(3f, 0f, 0f),
                    new Vector3(3f, 1f, 0f),
                    new Vector3(1f, 1f, 0f),
                    new Vector3(1f, 3f, 0f),
                    new Vector3(0f, 3f, 0f)
                };
                staleMesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 5, 4 };
                filter.sharedMesh = staleMesh;

                FusionIntegrityBlockSnapshot snapshot =
                    RuntimeTileMeshFusionIntegrityAnalyzer.CaptureBlockSnapshot(
                        block,
                        1f,
                        Vector2.zero);
                int expectedVertices = block.View.tiles.Count * 4;
                if (snapshot.meshVertexCount == expectedVertices)
                    return 0;

                Debug.LogError(
                    "[RuntimeTileMeshSelfTest] Integrity audit should use the latest build result instead of stale child meshes. Expected " +
                    expectedVertices + " vertices, got " + snapshot.meshVertexCount + ".");
                return 1;
            }
            finally
            {
                if (staleMesh != null)
                    UnityEngine.Object.DestroyImmediate(staleMesh);

                for (int i = cleanup.Count - 1; i >= 0; i--)
                {
                    if (cleanup[i] != null)
                        UnityEngine.Object.DestroyImmediate(cleanup[i]);
                }
            }
        }

        private static int ExpectVisionConeAndProceduralRenderer()
        {
            GameObject rendererObject = null;
            try
            {
                VisionSnapshot snapshot = new VisionSnapshot();
                RadialVisionSampler2D sampler = new RadialVisionSampler2D();
                sampler.Sample(
                    snapshot,
                    Vector2.zero,
                    Vector2.right,
                    90f,
                    5f,
                    16,
                    64,
                    2,
                    0.35f,
                    0,
                    false,
                    null);

                if (!snapshot.ContainsWorldPoint(new Vector2(2f, 0.1f)) ||
                    snapshot.ContainsWorldPoint(new Vector2(-1f, 0f)) ||
                    snapshot.ContainsWorldPoint(new Vector2(1f, 3f)))
                {
                    Debug.LogError(
                        "[RuntimeTileMeshSelfTest] Vision cone containment should include points ahead and reject points behind or outside the cone.");
                    return 1;
                }

                rendererObject = new GameObject("Vision Renderer Self Test");
                ProceduralMeshVisionRenderer visionRenderer =
                    rendererObject.AddComponent<ProceduralMeshVisionRenderer>();
                visionRenderer.Initialize(new VisionRendererContext(
                    rendererObject,
                    rendererObject.transform,
                    0,
                    0,
                    0f));
                visionRenderer.Render(snapshot, new VisionRenderParameters());

                Transform output = rendererObject.transform.Find("Procedural Vision Mesh");
                MeshFilter filter = output != null ? output.GetComponent<MeshFilter>() : null;
                int expectedVertexCount = snapshot.SampleCount + 1;
                int actualVertexCount = filter != null && filter.sharedMesh != null
                    ? filter.sharedMesh.vertexCount
                    : 0;
                if (actualVertexCount == expectedVertexCount)
                    return 0;

                Debug.LogError(
                    "[RuntimeTileMeshSelfTest] Procedural vision mesh expected " +
                    expectedVertexCount + " vertices, got " + actualVertexCount + ".");
                return 1;
            }
            finally
            {
                if (rendererObject != null)
                    UnityEngine.Object.DestroyImmediate(rendererObject);
            }
        }

        private static RuntimeTileMeshDraggableBlock CreateSelfTestBlock(
            string name,
            List<Vector2Int> localTiles,
            List<GameObject> cleanup,
            Vector2Int rootCell)
        {
            GameObject root = new GameObject(name);
            root.transform.position = new Vector3(rootCell.x, rootCell.y, 0f);
            cleanup.Add(root);

            RuntimeTileMeshView view = root.AddComponent<RuntimeTileMeshView>();
            view.tiles = new List<Vector2Int>(localTiles);
            view.tileSize = Vector2.one;
            view.rebuildOnStart = false;
            view.buildPolygonCollider2D = false;
            view.Rebuild();

            return root.AddComponent<RuntimeTileMeshDraggableBlock>();
        }

        private struct BlockSpec
        {
            public Vector2Int worldCell;
        }
    }
}
#endif
