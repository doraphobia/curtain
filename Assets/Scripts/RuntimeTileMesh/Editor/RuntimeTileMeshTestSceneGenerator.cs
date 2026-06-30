#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace DuoCurtain.RuntimeTileMesh.Editor
{
    [InitializeOnLoad]
    public static class RuntimeTileMeshTestSceneGenerator
    {
        private const string ScenePath = "Assets/Scenes/RedScene.unity";
        private const string DemoFolder = "Assets/Scripts/RuntimeTileMesh/Demo";
        private const string WhiteMaterialPath = DemoFolder + "/RuntimeTileMesh_White.mat";
        private const string ProjectionMaterialPath = DemoFolder + "/RuntimeTileMesh_WorldProjection.mat";
        private const string DefaultFootstepFoleyPath = "Assets/Audio/Foley/DefaultFootstepFoley.asset";
        private const string NightLoopClipPath = "Assets/art/u_izj2lpy7a6-night-sounds-380287.mp3";
        private const string FusionFolder = "Assets/Fusion";
        private const string FusionPrefabFolder = FusionFolder + "/Prefabs";
        private const string FusionMaterialFolder = FusionFolder + "/Materials";
        private const string FusionBlockMaterialPath = FusionMaterialFolder + "/Fusion_Block_White.mat";
        private const string PlayerCameraPrefabPath = FusionPrefabFolder + "/Fusion_PlayerCamera.prefab";
        private const string ManagementCameraPrefabPath = FusionPrefabFolder + "/Fusion_ManagementCamera.prefab";
        private const string BlockOneByThreePrefabPath = FusionPrefabFolder + "/FusionBlock_1x3.prefab";
        private const string BlockLPrefabPath = FusionPrefabFolder + "/FusionBlock_L.prefab";
        private const string BlockTPrefabPath = FusionPrefabFolder + "/FusionBlock_T.prefab";
        private const string BlockZPrefabPath = FusionPrefabFolder + "/FusionBlock_Z.prefab";
        private const string RunOnceMarkerPath = "Temp/DuoCurtainGenerateRuntimeTileMeshTestScene.flag";
        private const string LogPrefix = "[RuntimeTileMeshTestSceneGenerator] ";
        private const bool UseProjectionMaterialByDefault = false;
        private static bool generationQueued;

        private sealed class FusionAssetSet
        {
            public Material blockMaterial;
            public RuntimeTileMeshDraggableBlock oneByThreePrefab;
            public RuntimeTileMeshDraggableBlock lPrefab;
            public RuntimeTileMeshDraggableBlock tPrefab;
            public RuntimeTileMeshDraggableBlock zPrefab;
            public FusionModeCameraRig playerCameraPrefab;
            public FusionModeCameraRig managementCameraPrefab;
        }

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

        [MenuItem("Tools/Duo Curtain/Runtime Tile Mesh/Create RedScene")]
        public static void GenerateSceneAsset()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                RequestDelayedGeneration();
                Debug.Log(LogPrefix + "Scene generation queued until Unity returns to Edit Mode.");
                return;
            }

            EnsureDemoAssets();
            FusionAssetSet fusionAssets = EnsureFusionAssets();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "RedScene";

            Material material = fusionAssets.blockMaterial != null
                ? fusionAssets.blockMaterial
                : AssetDatabase.LoadAssetAtPath<Material>(WhiteMaterialPath);
            Material projectionMaterial = UseProjectionMaterialByDefault
                ? AssetDatabase.LoadAssetAtPath<Material>(ProjectionMaterialPath)
                : null;
            CreateCameras(fusionAssets, out FusionModeCameraRig playerCamera, out FusionModeCameraRig managementCamera);
            CreateDayNightSystem(playerCamera, managementCamera);
            CreateInstructionText();
            CreateGridOverlay(material);
            RuntimeTileMeshFusionSandbox sandbox = CreateFusionSandboxController();
            if (playerCamera != null)
                sandbox.worldCamera = playerCamera.Camera;

            CreateFusionBlock("Fusion Block - L", RuntimeTileMeshDemo.DemoShape.L, new Vector3(-6f, 0f, 0f), material, projectionMaterial);
            CreateFusionBlock("Fusion Block - 1x3", RuntimeTileMeshDemo.DemoShape.OneByThree, new Vector3(-1f, -2f, 0f), material, projectionMaterial);
            CreateFusionBlock("Fusion Block - T", RuntimeTileMeshDemo.DemoShape.T, new Vector3(3f, 1f, 0f), material, projectionMaterial);
            CreateFusionBlock("Fusion Block - Z", RuntimeTileMeshDemo.DemoShape.Z, new Vector3(6f, -2f, 0f), material, projectionMaterial);
            Camera activeCamera = playerCamera != null ? playerCamera.Camera : Camera.main;
            PlayerControl playerControl = CreatePlayerControl(sandbox, activeCamera);
            BindCameraReferences(playerCamera, managementCamera, playerControl, sandbox);
            CreateBlockInfoOverlay(sandbox, activeCamera);
            CreateTopologyMap(sandbox, playerControl);
            CreateGameModeController(sandbox, playerControl, playerCamera, managementCamera, fusionAssets);

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
            File.WriteAllText(MarkerAbsolutePath(), "generate RedScene.unity");
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

        private static FusionAssetSet EnsureFusionAssets()
        {
            Directory.CreateDirectory(FusionFolder);
            Directory.CreateDirectory(FusionPrefabFolder);
            Directory.CreateDirectory(FusionMaterialFolder);

            Material blockMaterial = LoadOrCreateFusionBlockMaterial();
            FusionAssetSet assets = new FusionAssetSet
            {
                blockMaterial = blockMaterial,
                oneByThreePrefab = LoadOrCreateBlockPrefab(
                    BlockOneByThreePrefabPath,
                    "FusionBlock_1x3",
                    RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.OneByThree),
                    blockMaterial),
                lPrefab = LoadOrCreateBlockPrefab(
                    BlockLPrefabPath,
                    "FusionBlock_L",
                    RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.L),
                    blockMaterial),
                tPrefab = LoadOrCreateBlockPrefab(
                    BlockTPrefabPath,
                    "FusionBlock_T",
                    RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.T),
                    blockMaterial),
                zPrefab = LoadOrCreateBlockPrefab(
                    BlockZPrefabPath,
                    "FusionBlock_Z",
                    RuntimeTileMeshDemo.CreateShape(RuntimeTileMeshDemo.DemoShape.Z),
                    blockMaterial),
                playerCameraPrefab = LoadOrCreateCameraPrefab(
                    PlayerCameraPrefabPath,
                    "Fusion Player Camera",
                    FusionModeCameraRig.RigMode.PlayerFollow,
                    4.5f),
                managementCameraPrefab = LoadOrCreateCameraPrefab(
                    ManagementCameraPrefabPath,
                    "Fusion Management Camera",
                    FusionModeCameraRig.RigMode.ManagementOverview,
                    7f)
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return assets;
        }

        private static Material LoadOrCreateFusionBlockMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(FusionBlockMaterialPath);
            if (material != null)
                return material;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            material = new Material(shader);
            material.name = "Fusion Block White";
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);
            AssetDatabase.CreateAsset(material, FusionBlockMaterialPath);
            return material;
        }

        private static RuntimeTileMeshDraggableBlock LoadOrCreateBlockPrefab(
            string path,
            string name,
            List<Vector2Int> tiles,
            Material material)
        {
            RuntimeTileMeshDraggableBlock existing = AssetDatabase.LoadAssetAtPath<RuntimeTileMeshDraggableBlock>(path);
            if (existing != null)
                return existing;

            GameObject root = CreateFusionBlockObject(name, tiles, Vector3.zero, material, null);
            RuntimeTileMeshView view = root.GetComponent<RuntimeTileMeshView>();
            if (view != null)
                view.rebuildOnStart = true;

            RuntimeTileMeshDraggableBlock prefab = PrefabUtility.SaveAsPrefabAsset(root, path)
                .GetComponent<RuntimeTileMeshDraggableBlock>();
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static FusionModeCameraRig LoadOrCreateCameraPrefab(
            string path,
            string name,
            FusionModeCameraRig.RigMode mode,
            float orthographicSize)
        {
            FusionModeCameraRig existing = AssetDatabase.LoadAssetAtPath<FusionModeCameraRig>(path);
            if (existing != null)
            {
                EnsureCameraPrefabAudioListener(path, mode == FusionModeCameraRig.RigMode.PlayerFollow);
                return existing;
            }

            GameObject root = new GameObject(name);
            Camera camera = root.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = orthographicSize;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.enabled = false;
            root.transform.position = new Vector3(0f, 0f, -10f);
            AudioListener listener = root.AddComponent<AudioListener>();
            listener.enabled = mode == FusionModeCameraRig.RigMode.PlayerFollow;

            FusionModeCameraRig rig = root.AddComponent<FusionModeCameraRig>();
            rig.mode = mode;
            rig.orthographicSize = orthographicSize;
            rig.cameraZ = -10f;
            if (mode == FusionModeCameraRig.RigMode.PlayerFollow)
            {
                rig.followSmoothTime = 0.28f;
                rig.maxFollowSpeed = 18f;
                rig.deadZoneRadius = 0.25f;
                rig.lookAheadDistance = 0.6f;
                rig.clampToMapBounds = false;
            }
            else
            {
                rig.overviewSmoothTime = 0.35f;
                rig.overviewPadding = 1.5f;
                rig.minOverviewOrthographicSize = 4f;
                rig.maxOverviewOrthographicSize = 30f;
            }

            FusionModeCameraRig prefab = PrefabUtility.SaveAsPrefabAsset(root, path)
                .GetComponent<FusionModeCameraRig>();
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void EnsureCameraPrefabAudioListener(string path, bool enabled)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
                return;

            try
            {
                AudioListener listener = root.GetComponent<AudioListener>();
                if (listener == null)
                    listener = root.AddComponent<AudioListener>();

                listener.enabled = enabled;
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
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

        private static void CreateCameras(
            FusionAssetSet assets,
            out FusionModeCameraRig playerCamera,
            out FusionModeCameraRig managementCamera)
        {
            playerCamera = InstantiateCameraPrefab(
                assets != null ? assets.playerCameraPrefab : null,
                "Player Camera",
                FusionModeCameraRig.RigMode.PlayerFollow,
                4.5f);
            managementCamera = InstantiateCameraPrefab(
                assets != null ? assets.managementCameraPrefab : null,
                "Management Camera",
                FusionModeCameraRig.RigMode.ManagementOverview,
                7f);

            if (playerCamera != null)
            {
                playerCamera.Camera.enabled = true;
                playerCamera.Camera.tag = "MainCamera";
                playerCamera.transform.position = new Vector3(0f, 0f, -10f);
                SetSceneCameraAudioListener(playerCamera, true);
            }

            if (managementCamera != null)
            {
                managementCamera.Camera.enabled = false;
                managementCamera.Camera.tag = "Untagged";
                managementCamera.transform.position = new Vector3(0f, 0f, -10f);
                SetSceneCameraAudioListener(managementCamera, false);
            }
        }

        private static StageCycleController CreateDayNightSystem(
            FusionModeCameraRig playerCamera,
            FusionModeCameraRig managementCamera)
        {
            GameObject controllerObject = new GameObject("RedScene Day Night Cycle");
            StageCycleController stageController = controllerObject.AddComponent<StageCycleController>();
            stageController.stages = new List<StageCycleController.StageDefinition>
            {
                new StageCycleController.StageDefinition { id = StageIds.DayTop, duration = 10f },
                new StageCycleController.StageDefinition { id = StageIds.DayBottom, duration = 10f },
                new StageCycleController.StageDefinition { id = StageIds.BeforeNight, duration = 3f },
                new StageCycleController.StageDefinition { id = StageIds.Night, duration = 16f }
            };
            stageController.transitionDuration = 1f;
            stageController.startStageIndex = 0;
            stageController.nightLoopClip = AssetDatabase.LoadAssetAtPath<AudioClip>(NightLoopClipPath);
            stageController.nightLoopVolume = 1f;
            stageController.nightsRequiredForSettlement = 9999;
            stageController.settlementSceneName = string.Empty;

            ConfigureCameraWeather(playerCamera, stageController);
            ConfigureCameraWeather(managementCamera, stageController);
            ConfigureFusionBackground(playerCamera, stageController);
            ConfigureFusionBackground(managementCamera, stageController);
            return stageController;
        }

        private static void ConfigureCameraWeather(FusionModeCameraRig rig, StageCycleController stageController)
        {
            if (rig == null || rig.Camera == null)
                return;

            Camera camera = rig.Camera;
            camera.clearFlags = CameraClearFlags.SolidColor;

            DayNightCameraWeather weather = rig.GetComponent<DayNightCameraWeather>();
            if (weather == null)
                weather = rig.gameObject.AddComponent<DayNightCameraWeather>();

            weather.targetCamera = camera;
            weather.stageController = stageController;
            weather.dayColor = new Color(0.83f, 0.83f, 0.83f, 1f);
            weather.nightColor = Color.black;
            weather.cycleDuration = 10f;
            weather.transitionDuration = 1f;
            weather.stageColors = new List<DayNightCameraWeather.StageCameraColor>
            {
                new DayNightCameraWeather.StageCameraColor { stageId = StageIds.DayTop, color = new Color(0.83f, 0.83f, 0.83f, 1f) },
                new DayNightCameraWeather.StageCameraColor { stageId = StageIds.DayBottom, color = new Color(0.61f, 0.61f, 0.61f, 1f) },
                new DayNightCameraWeather.StageCameraColor { stageId = StageIds.BeforeNight, color = new Color(0.18f, 0.18f, 0.2f, 1f) },
                new DayNightCameraWeather.StageCameraColor { stageId = StageIds.Night, color = Color.black }
            };
        }

        private static void ConfigureFusionBackground(FusionModeCameraRig rig, StageCycleController stageController)
        {
            if (rig == null || rig.Camera == null)
                return;

            FusionBackgroundShaderController background =
                rig.GetComponent<FusionBackgroundShaderController>();
            if (background == null)
                background = rig.gameObject.AddComponent<FusionBackgroundShaderController>();

            background.targetCamera = rig.Camera;
            background.stageController = stageController;
            background.gridCellSize = new Vector2(1f, 5f);
            background.gridLineWidth = 0.012f;
            background.vignetteStrength = 0.16f;
            background.planeDistance = 80f;
            background.sizePadding = 1.08f;
        }

        private static void BindCameraReferences(
            FusionModeCameraRig playerCamera,
            FusionModeCameraRig managementCamera,
            PlayerControl playerControl,
            RuntimeTileMeshFusionSandbox sandbox)
        {
            ConfigureCameraReferences(playerCamera, playerControl, sandbox);
            ConfigureCameraReferences(managementCamera, playerControl, sandbox);
        }

        private static void ConfigureCameraReferences(
            FusionModeCameraRig rig,
            PlayerControl playerControl,
            RuntimeTileMeshFusionSandbox sandbox)
        {
            if (rig == null)
                return;

            rig.playerControl = playerControl;
            rig.fusionSandbox = sandbox;
        }

        private static void SetSceneCameraAudioListener(FusionModeCameraRig rig, bool enabled)
        {
            if (rig == null || rig.Camera == null)
                return;

            AudioListener listener = rig.GetComponent<AudioListener>();
            if (listener == null)
                listener = rig.gameObject.AddComponent<AudioListener>();

            listener.enabled = enabled;
        }

        private static FusionModeCameraRig InstantiateCameraPrefab(
            FusionModeCameraRig prefab,
            string fallbackName,
            FusionModeCameraRig.RigMode mode,
            float orthographicSize)
        {
            GameObject cameraObject;
            if (prefab != null)
            {
                cameraObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject);
                cameraObject.name = fallbackName;
            }
            else
            {
                cameraObject = new GameObject(fallbackName);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = orthographicSize;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                FusionModeCameraRig rig = cameraObject.AddComponent<FusionModeCameraRig>();
                rig.mode = mode;
                rig.orthographicSize = orthographicSize;
            }

            FusionModeCameraRig cameraRig = cameraObject.GetComponent<FusionModeCameraRig>();
            if (cameraRig != null)
                cameraRig.mode = mode;

            return cameraRig;
        }

        private static void CreateInstructionText()
        {
            CreateLabel(
                "Fusion Sandbox\nEnter toggles Player / Management mode. Tab toggles Block size and Type labels.\nUse 1-4 to buy preset blocks, then drag, snap, place, and merge.",
                new Vector3(-8.8f, 5.45f, -0.2f),
                0.09f,
                TextAnchor.UpperLeft,
                new Color(0.88f, 0.92f, 1f, 1f));
        }

        private static RuntimeTileMeshFusionSandbox CreateFusionSandboxController()
        {
            GameObject controllerObject = new GameObject("Fusion Sandbox Controller");
            RuntimeTileMeshFusionSandbox sandbox = controllerObject.AddComponent<RuntimeTileMeshFusionSandbox>();
            sandbox.worldCamera = Camera.main;
            sandbox.gridSize = 1f;
            sandbox.gridOrigin = Vector2.zero;
            sandbox.preserveGrabOffset = true;
            sandbox.managementInputEnabled = false;
            sandbox.snapExistingBlocksOnAwake = true;
            sandbox.mergeExistingBlocksOnAwake = true;
            sandbox.mergeAfterPlacement = true;
            sandbox.deactivateAbsorbedBlocksImmediately = true;
            sandbox.logFusionEvents = true;
            sandbox.sceneGridHalfExtents = new Vector2Int(10, 6);
            sandbox.generateDoorsOnFusion = true;
            sandbox.doorSharedEdgeCells = 3;
            sandbox.doorThickness = 0.25f;
            sandbox.doorColor = Color.black;
            sandbox.doorBlocksPlayer = true;
            sandbox.wallDebugColor = new Color(0.38f, 0.38f, 0.38f, 0.95f);
            sandbox.wallDebugLineWidth = 0.02f;

            RuntimeTileMeshFusionIntegrityMonitor integrityMonitor =
                controllerObject.AddComponent<RuntimeTileMeshFusionIntegrityMonitor>();
            integrityMonitor.fusionSandbox = sandbox;
            integrityMonitor.monitorEnabled = true;
            integrityMonitor.monitorMergeGroups = true;
            integrityMonitor.logIssuesToConsole = true;

            return sandbox;
        }

        private static PlayerControl CreatePlayerControl(RuntimeTileMeshFusionSandbox sandbox, Camera targetCamera)
        {
            EnsureEventSystem();

            GameObject canvasObject = new GameObject(
                "Player Control Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            Image playerImage = CreateControlImage(
                canvas.transform,
                "Player",
                new Vector2(28f, 28f),
                new Color(0.12f, 0.72f, 1f, 0.95f));
            Image headingImage = CreateControlImage(
                canvas.transform,
                "Heading Point",
                new Vector2(16f, 16f),
                new Color(1f, 1f, 1f, 0.65f));

            GameObject controllerObject = new GameObject("Player Control");
            PlayerControl control = controllerObject.AddComponent<PlayerControl>();
            control.targetCamera = targetCamera != null ? targetCamera : Camera.main;
            control.runtimeTileWalkableArea = sandbox;
            control.playerImage = playerImage;
            control.headingPointImage = headingImage;
            control.spawnAtRandomRuntimeTileBlockCenter = true;
            control.clampCursorToRoom = true;
            control.hideSystemCursor = true;
            control.showHeadingPoint = true;
            control.limitHeadingPointReach = true;
            control.headingPointReachRadius = 2.5f;
            control.playerCollisionRadius = 0.22f;
            control.driveCameraFromCursorOffset = false;
            control.maxCursorMoveSpeed = 5.5f;
            control.worldDistancePerFootstep = 0.68f;
            control.minSecondsBetweenFootsteps = 0.055f;
            control.footstepSurfaceIdOverride = "Concrete";

            FoleyProfile profile = AssetDatabase.LoadAssetAtPath<FoleyProfile>(DefaultFootstepFoleyPath);
            if (profile != null)
                control.footstepFoleyProfile = profile;

            FoleyPlayer foleyPlayer = controllerObject.AddComponent<FoleyPlayer>();
            FoleySurfaceResolver2D resolver = controllerObject.AddComponent<FoleySurfaceResolver2D>();
            resolver.fallbackSurfaceId = "Concrete";
            control.footstepFoleyPlayer = foleyPlayer;

            FoleyStepClock stepClock = controllerObject.AddComponent<FoleyStepClock>();
            stepClock.distancePerStep = control.worldDistancePerFootstep;
            stepClock.minSecondsBetweenSteps = control.minSecondsBetweenFootsteps;
            control.stepClock = stepClock;
            sandbox.playerControl = control;
            sandbox.carryPlayerWithSelectedBlock = true;
            return control;
        }

        private static void CreateBlockInfoOverlay(RuntimeTileMeshFusionSandbox sandbox, Camera targetCamera)
        {
            if (sandbox == null)
                return;

            RuntimeTileMeshBlockInfoOverlay overlay =
                sandbox.GetComponent<RuntimeTileMeshBlockInfoOverlay>();
            if (overlay == null)
                overlay = sandbox.gameObject.AddComponent<RuntimeTileMeshBlockInfoOverlay>();

            overlay.fusionSandbox = sandbox;
            overlay.worldCamera = targetCamera;
            overlay.autoFollowActiveFusionCamera = true;
            overlay.labelFont = BayonFontAssetBuilder.EnsureFontAsset();
            overlay.displayKey = KeyCode.None;
            overlay.displayMode = RuntimeTileMeshBlockInfoOverlay.TabDisplayMode.ToggleOnPress;
            overlay.startVisible = false;
            overlay.allowManualDisplayInput = false;
            overlay.showOnlyInManagementMode = true;
            overlay.fontSize = 30f;
            overlay.lineHeight = 30f;
            overlay.letterSpacingPercent = -5f;
            overlay.labelSize = new Vector2(82f, 51f);
            overlay.topRightInset = new Vector2(8f, -8f);
            overlay.textColor = Color.black;
            overlay.useTextOutline = true;
            overlay.outlineColor = new Color(1f, 1f, 1f, 0.92f);
            overlay.outlineWidth = 0.16f;
            overlay.useLabelBackground = true;
            overlay.labelBackgroundColor = new Color(1f, 1f, 1f, 0.72f);
        }

        private static void CreateTopologyMap(RuntimeTileMeshFusionSandbox sandbox, PlayerControl playerControl)
        {
            GameObject mapObject = new GameObject("Topology Map System");
            TopologyMapDataProvider provider = mapObject.AddComponent<TopologyMapDataProvider>();
            provider.topologyGrid = null;
            provider.fusionSandbox = sandbox;
            provider.autoFindSource = true;
            provider.useRuntimeFusionFallback = true;
            provider.refreshOnEnable = true;
            provider.pollForExternalChanges = true;

            TopologyMapRenderer renderer = mapObject.AddComponent<TopologyMapRenderer>();
            renderer.dataProvider = provider;
            renderer.placementGrid = null;
            renderer.playerControl = playerControl;
            renderer.autoBindReferences = true;
            renderer.createProviderIfMissing = true;
            renderer.createCanvasIfMissing = true;
            renderer.visible = true;
            renderer.rebuildOnEnable = true;
            renderer.renderMode = TopologyMapRenderMode.EntireBuilding;
            renderer.defaultMapSize = new Vector2(220f, 220f);
            renderer.forceSquareMap = true;
            renderer.defaultAnchoredPosition = new Vector2(-32f, -32f);
            renderer.padding = 18f;
            renderer.cellSpacing = 1.5f;
            renderer.backgroundColor = new Color(0.68f, 0.68f, 0.68f, 0.82f);
            renderer.roomColor = Color.white;
            renderer.currentRoomColor = new Color(1f, 0.94f, 0.42f, 1f);
            renderer.frameColor = Color.black;
            renderer.playerMarkerColor = Color.black;
            renderer.playerMarkerSize = 9f;
        }

        private static void CreateGameModeController(
            RuntimeTileMeshFusionSandbox sandbox,
            PlayerControl playerControl,
            FusionModeCameraRig playerCamera,
            FusionModeCameraRig managementCamera,
            FusionAssetSet assets)
        {
            GameObject controllerObject = new GameObject("Fusion Game Mode Controller");
            FusionGameModeController controller = controllerObject.AddComponent<FusionGameModeController>();
            controller.startMode = FusionGameModeController.GameMode.Player;
            controller.currentMode = FusionGameModeController.GameMode.Player;
            controller.toggleModeKey = KeyCode.Return;
            controller.playerControl = playerControl;
            controller.fusionSandbox = sandbox;
            controller.playerCamera = playerCamera;
            controller.managementCamera = managementCamera;
            controller.cameraTransitionDuration = 0.65f;
            controller.allowFreePurchases = true;
            controller.defaultStartingMoney = 100f;
            controller.moneyFormat = "Money: {0}";

            controller.shopItems = new List<FusionGameModeController.BlockShopItem>
            {
                CreateShopItem("1x3 Block", assets != null ? assets.oneByThreePrefab : null, 10, KeyCode.Alpha1),
                CreateShopItem("L Block", assets != null ? assets.lPrefab : null, 16, KeyCode.Alpha2),
                CreateShopItem("T Block", assets != null ? assets.tPrefab : null, 18, KeyCode.Alpha3),
                CreateShopItem("Z Block", assets != null ? assets.zPrefab : null, 20, KeyCode.Alpha4)
            };
        }

        private static FusionGameModeController.BlockShopItem CreateShopItem(
            string displayName,
            RuntimeTileMeshDraggableBlock prefab,
            int price,
            KeyCode hotkey)
        {
            return new FusionGameModeController.BlockShopItem
            {
                displayName = displayName,
                blockPrefab = prefab,
                price = price,
                hotkey = hotkey
            };
        }

        private static Image CreateControlImage(Transform parent, string name, Vector2 size, Color color)
        {
            GameObject imageObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            imageObject.transform.SetParent(parent, false);

            RectTransform rectTransform = imageObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = size;

            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null)
                return;

            GameObject eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<StandaloneInputModule>();
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
            Material fallbackMaterial,
            Material projectionMaterial)
        {
            GameObject root = CreateFusionBlockObject(
                label,
                RuntimeTileMeshDemo.CreateShape(shape),
                position,
                fallbackMaterial,
                projectionMaterial);

            RuntimeTileMeshDemo demo = root.AddComponent<RuntimeTileMeshDemo>();
            demo.shape = shape;
            demo.applyShapeOnStart = false;
            demo.rebuildOnValidate = false;

            RuntimeTileMeshView view = root.GetComponent<RuntimeTileMeshView>();
            RuntimeTileMeshProjectionRenderer projection = root.GetComponent<RuntimeTileMeshProjectionRenderer>();
            if (view != null)
                view.Rebuild();
            if (projection != null)
                projection.Apply();
        }

        private static GameObject CreateFusionBlockObject(
            string label,
            List<Vector2Int> tiles,
            Vector3 position,
            Material fallbackMaterial,
            Material projectionMaterial)
        {
            GameObject root = new GameObject(label);
            root.transform.position = position;
            Material displayMaterial = projectionMaterial != null ? projectionMaterial : fallbackMaterial;

            RuntimeTileMeshView view = root.AddComponent<RuntimeTileMeshView>();
            view.material = displayMaterial;
            view.tiles = tiles != null ? new List<Vector2Int>(tiles) : new List<Vector2Int> { Vector2Int.zero };
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

            RuntimeTileMeshDraggableBlock block = root.AddComponent<RuntimeTileMeshDraggableBlock>();
            block.placedColor = Color.white;
            block.hoverColor = new Color(1f, 0.08f, 0.03f, 1f);
            block.selectedColor = new Color(0.08f, 0.35f, 1f, 1f);
            block.colorLerpSpeed = 7f;

            FoleySurface2D surface = root.AddComponent<FoleySurface2D>();
            surface.surfaceId = "Concrete";

            RuntimeTileMeshProjectionRenderer projection = null;
            if (projectionMaterial != null)
            {
                projection = root.AddComponent<RuntimeTileMeshProjectionRenderer>();
                projection.visualState.material = projectionMaterial;
                projection.visualState.projectionMode = RuntimeTileMeshProjectionMode.WorldTile;
                projection.visualState.cellSize = Vector2.one;
                projection.visualState.motionTileSize = new Vector2(3f, 3f);
                projection.visualState.patternScale = 1f;
                projection.visualState.patternIntensity = 0.38f;
                projection.visualState.lineWidth = 0.055f;
                projection.captureAnchorOnEnable = false;
                projection.requireProjectionMaterial = true;
                projection.animateInPlayMode = true;
            }

            view.Rebuild();
            if (projection != null)
                projection.Apply();

            return root;
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
