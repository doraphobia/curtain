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
