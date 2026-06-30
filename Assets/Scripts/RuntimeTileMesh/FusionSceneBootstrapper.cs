using UnityEngine;
using UnityEngine.SceneManagement;

namespace DuoCurtain.RuntimeTileMesh
{
    public static class FusionSceneBootstrapper
    {
        private const string SandboxObjectName = "Fusion Sandbox";
        private const string PlayerControlObjectName = "Player Control";
        private const string PauseManagerObjectName = "Pause";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
            InstallForCurrentScene();
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            InstallForCurrentScene();
        }

        private static void InstallForCurrentScene()
        {
            Camera mainCamera = Camera.main;
            bool hasRoomGrid = Object.FindFirstObjectByType<TilePlacementGrid>() != null;
            bool hasFusionBlock = Object.FindFirstObjectByType<RuntimeTileMeshDraggableBlock>() != null;
            bool hasFusionModeController = Object.FindFirstObjectByType<FusionGameModeController>() != null;
            bool hasPlayerControl = Object.FindFirstObjectByType<PlayerControl>() != null;
            bool hasGameplaySurface = hasRoomGrid || hasFusionBlock || hasFusionModeController || hasPlayerControl;

            RuntimeTileMeshFusionSandbox sandbox = EnsureFusionSandbox(hasGameplaySurface, mainCamera);
            PlayerControl playerControl = EnsurePlayerControl(hasGameplaySurface, mainCamera, sandbox);
            ConfigurePauseManagers(hasGameplaySurface, mainCamera);
            BindFusionReferences(sandbox, playerControl, mainCamera);
        }

        private static RuntimeTileMeshFusionSandbox EnsureFusionSandbox(bool hasGameplaySurface, Camera mainCamera)
        {
            RuntimeTileMeshFusionSandbox sandbox = Object.FindFirstObjectByType<RuntimeTileMeshFusionSandbox>();
            if (sandbox == null && hasGameplaySurface)
            {
                GameObject sandboxObject = new GameObject(SandboxObjectName);
                sandbox = sandboxObject.AddComponent<RuntimeTileMeshFusionSandbox>();
                sandbox.managementInputEnabled = false;
                sandbox.generateDoorsOnFusion = true;
                sandbox.doorBlocksPlayer = true;
                sandbox.allowHeadingPointDoorInteraction = true;
                sandbox.doorOpenAngleDegrees = 90f;
            }

            if (sandbox != null && sandbox.worldCamera == null)
                sandbox.worldCamera = mainCamera;

            return sandbox;
        }

        private static PlayerControl EnsurePlayerControl(
            bool hasGameplaySurface,
            Camera mainCamera,
            RuntimeTileMeshFusionSandbox sandbox)
        {
            PlayerControl playerControl = PlayerControl.Active != null
                ? PlayerControl.Active
                : Object.FindFirstObjectByType<PlayerControl>();

            if (playerControl == null && hasGameplaySurface)
            {
                GameObject playerObject = new GameObject(PlayerControlObjectName);
                playerControl = playerObject.AddComponent<PlayerControl>();
            }

            if (playerControl == null)
                return null;

            if (playerControl.targetCamera == null)
                playerControl.targetCamera = mainCamera;
            if (playerControl.runtimeTileWalkableArea == null)
                playerControl.runtimeTileWalkableArea = sandbox;
            playerControl.preferRuntimeTileWalkableArea = true;
            return playerControl;
        }

        private static void ConfigurePauseManagers(bool hasGameplaySurface, Camera mainCamera)
        {
            PauseManager[] pauseManagers = Object.FindObjectsByType<PauseManager>(FindObjectsSortMode.None);
            if (pauseManagers.Length == 0 && hasGameplaySurface)
            {
                GameObject pauseObject = new GameObject(PauseManagerObjectName);
                PauseManager pauseManager = pauseObject.AddComponent<PauseManager>();
                ConfigurePauseManager(pauseManager, mainCamera);
                return;
            }

            for (int i = 0; i < pauseManagers.Length; i++)
                ConfigurePauseManager(pauseManagers[i], mainCamera);
        }

        private static void ConfigurePauseManager(PauseManager pauseManager, Camera mainCamera)
        {
            if (pauseManager == null)
                return;

            pauseManager.toggleKey = KeyCode.Space;
            pauseManager.captureWorldCameraOnly = true;
            pauseManager.blurCanvasSortingOrder = -100;
            if (pauseManager.blurSourceCamera == null)
                pauseManager.blurSourceCamera = mainCamera;
        }

        private static void BindFusionReferences(
            RuntimeTileMeshFusionSandbox sandbox,
            PlayerControl playerControl,
            Camera mainCamera)
        {
            FusionGameModeController modeController = Object.FindFirstObjectByType<FusionGameModeController>();
            if (modeController != null)
            {
                if (modeController.fusionSandbox == null)
                    modeController.fusionSandbox = sandbox;
                if (modeController.playerControl == null)
                    modeController.playerControl = playerControl;
            }

            FusionModeCameraRig[] cameraRigs = Object.FindObjectsByType<FusionModeCameraRig>(FindObjectsSortMode.None);
            for (int i = 0; i < cameraRigs.Length; i++)
            {
                FusionModeCameraRig rig = cameraRigs[i];
                if (rig == null)
                    continue;

                if (rig.fusionSandbox == null)
                    rig.fusionSandbox = sandbox;
                if (rig.playerControl == null)
                    rig.playerControl = playerControl;
            }

            RuntimeTileMeshBlockInfoOverlay overlay = Object.FindFirstObjectByType<RuntimeTileMeshBlockInfoOverlay>();
            if (overlay != null)
            {
                if (overlay.fusionSandbox == null)
                    overlay.fusionSandbox = sandbox;
                if (overlay.worldCamera == null)
                    overlay.worldCamera = mainCamera;
            }
        }
    }
}
