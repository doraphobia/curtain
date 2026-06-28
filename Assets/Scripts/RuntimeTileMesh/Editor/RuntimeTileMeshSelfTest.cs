#if UNITY_EDITOR
using System;
using System.Collections.Generic;
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
            failures += ExpectSuccessfulComponents("OneByThree", RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.OneByThree), settings, 1, expectedFirstVertexCount: 4);
            failures += ExpectSuccessfulComponents("L", RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.L), settings, 1);
            failures += ExpectSuccessfulComponents("T", RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.T), settings, 1);
            failures += ExpectSuccessfulComponents("Z", RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.Z), settings, 1);
            failures += ExpectSuccessfulComponents("DiagonalTouch", RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.DiagonalTouch), settings, 2);
            failures += ExpectHoleWarning("RingWithHole", RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.RingWithHole), settings);
            failures += ExpectDefaultFallbackMaterial();
            failures += ExpectFusionConnectionRules();
            failures += ExpectFusionBlockMergeRules();

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
            failures += ExpectCellConnection("HorizontalEdge", baseCells, new HashSet<Vector2Int> { Vector2Int.right }, true);
            failures += ExpectCellConnection("VerticalEdge", baseCells, new HashSet<Vector2Int> { Vector2Int.up }, true);
            failures += ExpectCellConnection("DiagonalCorner", baseCells, new HashSet<Vector2Int> { Vector2Int.one }, false);
            failures += ExpectCellConnection("OneCellGap", baseCells, new HashSet<Vector2Int> { new Vector2Int(2, 0) }, false);
            return failures;
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

        private static int ExpectHoleWarning(
            string name,
            List<Vector2Int> tiles,
            RuntimeTileMeshSettings settings)
        {
            RuntimeTileMeshBuildResult result = RuntimeTileMeshBuilder.Build(tiles, settings);
            bool sawHoleWarning = false;
            for (int i = 0; i < result.warnings.Count; i++)
            {
                if (result.warnings[i].Contains("Hole loops were detected"))
                {
                    sawHoleWarning = true;
                    break;
                }
            }

            if (result.components.Count == 1 && !result.components[0].success && sawHoleWarning)
                return 0;

            Debug.LogError("[RuntimeTileMeshSelfTest] " + name + " should report an unsupported-hole warning instead of filling the hole.");
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

        private struct BlockSpec
        {
            public Vector2Int worldCell;
        }
    }
}
#endif
