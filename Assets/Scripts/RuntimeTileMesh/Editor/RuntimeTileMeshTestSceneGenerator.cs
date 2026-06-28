#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DuoCurtain.RuntimeTileMesh.Editor
{
    [InitializeOnLoad]
    public static class RuntimeTileMeshTestSceneGenerator
    {
        private const string ScenePath = "Assets/Scenes/RuntimeTileMeshTest.unity";
        private const string DemoFolder = "Assets/Scripts/RuntimeTileMesh/Demo";
        private const string WhiteMaterialPath = DemoFolder + "/RuntimeTileMesh_White.mat";
        private const string ProjectionMaterialPath = DemoFolder + "/RuntimeTileMesh_WorldProjection.mat";
        private const string RunOnceMarkerPath = "Temp/DuoCurtainGenerateRuntimeTileMeshTestScene.flag";
        private const string LogPrefix = "[RuntimeTileMeshTestSceneGenerator] ";
        private static bool generationQueued;

        static RuntimeTileMeshTestSceneGenerator()
        {
            EditorApplication.update -= WatchMarker;
            EditorApplication.update += WatchMarker;
            GenerateSceneIfRequested();
        }

        [InitializeOnLoadMethod]
        private static void GenerateSceneIfRequested()
        {
            if (!MarkerExists())
                return;

            QueueMarkerGeneration();
        }

        private static void WatchMarker()
        {
            if (!MarkerExists())
                return;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (!generationQueued)
                    QueueMarkerGeneration();
                return;
            }

            TryGenerateSceneFromMarker();
        }

        [MenuItem("Tools/Duo Curtain/Runtime Tile Mesh/Create Test Scene")]
        public static void GenerateSceneAsset()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                RequestDelayedGeneration();
                Debug.Log(LogPrefix + "Scene generation queued until Unity returns to Edit Mode.");
                return;
            }

            EnsureDemoAssets();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "RuntimeTileMeshTest";

            Material material = AssetDatabase.LoadAssetAtPath<Material>(WhiteMaterialPath);
            Material projectionMaterial = AssetDatabase.LoadAssetAtPath<Material>(ProjectionMaterialPath);
            CreateCamera();
            CreateInstructionText();
            CreateGridOverlay(material);
            CreateFusionSandboxController();

            CreateFusionBlock("Fusion Block - L", RuntimeTileMeshDemo.DemoShape.L, new Vector3(-6f, 0f, 0f), projectionMaterial);
            CreateFusionBlock("Fusion Block - 1x3", RuntimeTileMeshDemo.DemoShape.OneByThree, new Vector3(-1f, -2f, 0f), projectionMaterial);
            CreateFusionBlock("Fusion Block - T", RuntimeTileMeshDemo.DemoShape.T, new Vector3(3f, 1f, 0f), projectionMaterial);
            CreateFusionBlock("Fusion Block - Z", RuntimeTileMeshDemo.DemoShape.Z, new Vector3(6f, -2f, 0f), projectionMaterial);

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log(LogPrefix + "Created test scene at " + ScenePath);
        }

        private static void QueueMarkerGeneration()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            if (generationQueued)
                return;

            generationQueued = true;
            EditorApplication.delayCall += TryGenerateSceneFromMarker;
        }

        private static void TryGenerateSceneFromMarker()
        {
            generationQueued = false;
            if (!MarkerExists())
                return;

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.Log(LogPrefix + "Waiting for Edit Mode before regenerating " + ScenePath + ".");
                return;
            }

            string markerPath = MarkerAbsolutePath();
            File.Delete(markerPath);
            GenerateSceneAsset();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !MarkerExists())
                return;

            QueueMarkerGeneration();
        }

        private static void RequestDelayedGeneration()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MarkerAbsolutePath()));
            File.WriteAllText(MarkerAbsolutePath(), "generate RuntimeTileMeshTest.unity");
            QueueMarkerGeneration();
        }

        private static bool MarkerExists()
        {
            return File.Exists(MarkerAbsolutePath());
        }

        private static string MarkerAbsolutePath()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, RunOnceMarkerPath);
        }

        private static void EnsureDemoAssets()
        {
            Directory.CreateDirectory(DemoFolder);
            CreateWhiteMaterial();
            CreateProjectionMaterial();
        }

        private static void CreateWhiteMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Texture");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(WhiteMaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, WhiteMaterialPath);
            }
            else if (shader != null)
            {
                material.shader = shader;
            }

            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", null);
            if (material.HasProperty("_MainTex"))
                material.SetTexture("_MainTex", null);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 0f);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0f);

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        private static void CreateProjectionMaterial()
        {
            Shader shader = Shader.Find("Duo Curtain/Runtime Tile Projection Unlit");
            if (shader == null)
            {
                Debug.LogWarning(LogPrefix + "Projection shader was not found. Falling back to the white material.");
                return;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(ProjectionMaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, ProjectionMaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);
            if (material.HasProperty("_ProjectionMode"))
                material.SetFloat("_ProjectionMode", (float)RuntimeTileMeshProjectionMode.WorldTile);
            if (material.HasProperty("_PatternCellSize"))
                material.SetVector("_PatternCellSize", new Vector4(1f, 1f, 0f, 0f));
            if (material.HasProperty("_MotionTileSize"))
                material.SetVector("_MotionTileSize", new Vector4(3f, 3f, 0f, 0f));
            if (material.HasProperty("_PatternScale"))
                material.SetFloat("_PatternScale", 1f);
            if (material.HasProperty("_PatternIntensity"))
                material.SetFloat("_PatternIntensity", 0.38f);
            if (material.HasProperty("_PatternLineWidth"))
                material.SetFloat("_PatternLineWidth", 0.055f);

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        private static void CreateCamera()
        {
            GameObject cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 6f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.tag = "MainCamera";
        }

        private static void CreateInstructionText()
        {
            CreateLabel(
                "Fusion Sandbox\nWorldTile projection: mesh reveals a stable infinite 3x3 motion tile.\nHover fades red. Click to pick up, snap, place, and merge by overlap or shared edge.",
                new Vector3(-8.8f, 5.45f, -0.2f),
                0.09f,
                TextAnchor.UpperLeft,
                new Color(0.88f, 0.92f, 1f, 1f));
        }

        private static void CreateFusionSandboxController()
        {
            GameObject controllerObject = new GameObject("Fusion Sandbox Controller");
            RuntimeTileMeshFusionSandbox sandbox = controllerObject.AddComponent<RuntimeTileMeshFusionSandbox>();
            sandbox.worldCamera = Camera.main;
            sandbox.gridSize = 1f;
            sandbox.gridOrigin = Vector2.zero;
            sandbox.preserveGrabOffset = true;
            sandbox.snapExistingBlocksOnAwake = true;
            sandbox.mergeExistingBlocksOnAwake = true;
            sandbox.mergeAfterPlacement = true;
            sandbox.deactivateAbsorbedBlocksImmediately = true;
            sandbox.logFusionEvents = true;
            sandbox.sceneGridHalfExtents = new Vector2Int(10, 6);
        }

        private static void CreateGridOverlay(Material material)
        {
            GameObject root = new GameObject("Runtime Grid Overlay");
            const int halfWidth = 10;
            const int halfHeight = 6;
            const float lineWidth = 0.015f;
            Color lineColor = new Color(0.28f, 0.3f, 0.34f, 0.85f);

            for (int x = -halfWidth; x <= halfWidth; x++)
                CreateGridLine(root.transform, "Grid X " + x, material, lineColor, lineWidth, new Vector3(x, -halfHeight, 0.25f), new Vector3(x, halfHeight, 0.25f));

            for (int y = -halfHeight; y <= halfHeight; y++)
                CreateGridLine(root.transform, "Grid Y " + y, material, lineColor, lineWidth, new Vector3(-halfWidth, y, 0.25f), new Vector3(halfWidth, y, 0.25f));
        }

        private static void CreateGridLine(
            Transform parent,
            string name,
            Material material,
            Color color,
            float width,
            Vector3 start,
            Vector3 end)
        {
            GameObject lineObject = new GameObject(name);
            lineObject.transform.SetParent(parent, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.positionCount = 2;
            line.useWorldSpace = false;
            line.widthMultiplier = width;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.startColor = color;
            line.endColor = color;
            line.sortingOrder = -10;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
        }

        private static void CreateFusionBlock(
            string label,
            RuntimeTileMeshDemo.DemoShape shape,
            Vector3 position,
            Material material)
        {
            GameObject root = new GameObject(label);
            root.transform.position = position;

            RuntimeTileMeshView view = root.AddComponent<RuntimeTileMeshView>();
            view.material = material;
            view.tiles = RuntimeTileMeshDemo.CreateShape(shape);
            view.tileSize = Vector2.one;
            view.uvMode = RuntimeTileMeshUVMode.Bounds;
            view.uvTilingScale = Vector2.one;
            view.buildPolygonCollider2D = true;
            view.polygonColliderIsTrigger = true;
            view.rebuildOnStart = false;
            view.sortingOrder = 0;
            view.drawDebugTiles = true;
            view.drawDebugBoundaryEdges = true;
            view.drawDebugLoopPoints = true;

            RuntimeTileMeshDemo demo = root.AddComponent<RuntimeTileMeshDemo>();
            demo.shape = shape;
            demo.applyShapeOnStart = false;
            demo.rebuildOnValidate = false;

            RuntimeTileMeshDraggableBlock block = root.AddComponent<RuntimeTileMeshDraggableBlock>();
            block.placedColor = Color.white;
            block.hoverColor = new Color(1f, 0.08f, 0.03f, 1f);
            block.selectedColor = new Color(0.08f, 0.35f, 1f, 1f);
            block.colorLerpSpeed = 7f;

            RuntimeTileMeshProjectionRenderer projection = root.AddComponent<RuntimeTileMeshProjectionRenderer>();
            projection.visualState.material = material;
            projection.visualState.projectionMode = RuntimeTileMeshProjectionMode.WorldTile;
            projection.visualState.cellSize = Vector2.one;
            projection.visualState.motionTileSize = new Vector2(3f, 3f);
            projection.visualState.patternScale = 1f;
            projection.visualState.patternIntensity = 0.38f;
            projection.visualState.lineWidth = 0.055f;
            projection.captureAnchorOnEnable = false;
            projection.animateInPlayMode = true;

            view.Rebuild();
            projection.Apply();
        }

        private static void CreateLabel(
            string text,
            Vector3 position,
            float size,
            TextAnchor anchor,
            Color color)
        {
            GameObject labelObject = new GameObject("Label - " + text.Split('\n')[0]);
            labelObject.transform.position = position;
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = text;
            label.fontSize = 48;
            label.characterSize = size;
            label.anchor = anchor;
            label.color = color;
        }
    }
}
#endif
