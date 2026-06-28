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
            failures += ExpectFusionConnectionRules();

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
    }
}
#endif
