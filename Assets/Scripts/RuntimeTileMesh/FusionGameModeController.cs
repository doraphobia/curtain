using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DuoCurtain.RuntimeTileMesh
{
    [DefaultExecutionOrder(-40)]
    [DisallowMultipleComponent]
    public sealed class FusionGameModeController : MonoBehaviour
    {
        public enum GameMode
        {
            Player,
            Management
        }

        [Serializable]
        public sealed class BlockShopItem
        {
            public string displayName = "Block";
            public RuntimeTileMeshDraggableBlock blockPrefab;
            [Min(0)]
            public int price = 10;
            public KeyCode hotkey = KeyCode.Alpha1;
        }

        [Header("Mode")]
        public GameMode startMode = GameMode.Player;
        public GameMode currentMode = GameMode.Player;
        public KeyCode toggleModeKey = KeyCode.Return;
        public bool applyStartModeOnAwake = true;

        [Header("References")]
        public PlayerControl playerControl;
        public RuntimeTileMeshFusionSandbox fusionSandbox;
        public FusionModeCameraRig playerCamera;
        public FusionModeCameraRig managementCamera;

        [Header("Camera Transition")]
        [Min(0f)]
        public float cameraTransitionDuration = 0.65f;

        [Header("Player Mode")]
        public bool showHeadingPointInPlayerMode = true;
        public bool hideSystemCursorInPlayerMode = true;

        [Header("Management Mode")]
        public bool showSystemCursorInManagementMode = true;
        public bool placeSelectedBlockWhenLeavingManagement = true;

        [Header("Fusion Shop")]
        public bool allowFreePurchases = true;
        public bool buyAtPointerPosition = true;
        public bool dragPurchasedBlockImmediately = true;
        public List<BlockShopItem> shopItems = new List<BlockShopItem>();

        [Header("Currency")]
        public TimeCounterUI currencySource;
        public TextMeshProUGUI moneyText;
        public bool createMoneyHudIfMissing = true;
        public string moneyFormat = "Money: {0}";
        [Min(0f)]
        public float defaultStartingMoney = 100f;

        private Camera activeCamera;

        public bool IsManagementMode => currentMode == GameMode.Management;

        void Awake()
        {
            ResolveReferences();
            EnsureCurrencySource();
            EnsureMoneyHud();

            if (applyStartModeOnAwake)
                SetMode(startMode, false);
            else
                ApplyModeState(false);
        }

        void OnEnable()
        {
            ResolveReferences();
            if (currencySource != null)
                currencySource.ValueChanged += HandleCurrencyChanged;
            RefreshMoneyHud();
        }

        void OnDisable()
        {
            if (currencySource != null)
                currencySource.ValueChanged -= HandleCurrencyChanged;
        }

        void Update()
        {
            ResolveReferences();

            if (Input.GetKeyDown(toggleModeKey))
                ToggleMode();

            if (IsManagementMode)
                HandleShopHotkeys();

            RefreshMoneyHud();
        }

        public void ToggleMode()
        {
            SetMode(IsManagementMode ? GameMode.Player : GameMode.Management, true);
        }

        public void SetMode(GameMode mode)
        {
            SetMode(mode, true);
        }

        public void SetMode(GameMode mode, bool smoothCamera)
        {
            if (currentMode == mode)
            {
                ApplyModeState(smoothCamera);
                return;
            }

            currentMode = mode;
            ApplyModeState(smoothCamera);
        }

        public bool TryPurchase(BlockShopItem item)
        {
            if (item == null || item.blockPrefab == null || fusionSandbox == null)
                return false;

            if (!allowFreePurchases)
            {
                EnsureCurrencySource();
                if (currencySource == null || !currencySource.TrySpend(item.price))
                    return false;
            }

            Vector3 spawnPosition = transform.position;
            if (buyAtPointerPosition && fusionSandbox.worldCamera != null)
                spawnPosition = fusionSandbox.GetPointerWorldPosition();
            else if (PlayerControl.TryGetPlayerWorldPosition(out Vector3 playerPosition))
                spawnPosition = playerPosition;

            RuntimeTileMeshDraggableBlock spawned = fusionSandbox.SpawnBlock(
                item.blockPrefab,
                spawnPosition,
                dragPurchasedBlockImmediately);

            if (spawned != null)
                spawned.name = item.displayName;

            return spawned != null;
        }

        private void ApplyModeState(bool smoothCamera)
        {
            ResolveReferences();

            bool management = IsManagementMode;
            if (playerControl != null)
            {
                playerControl.playerInputEnabled = !management;
                playerControl.headingPointInputEnabled = !management;
                playerControl.showHeadingPoint = !management && showHeadingPointInPlayerMode;
            }

            if (fusionSandbox != null)
                fusionSandbox.SetManagementInputEnabled(management, placeSelectedBlockWhenLeavingManagement);

            ApplyCursorState(management);
            SwitchCamera(management ? managementCamera : playerCamera, smoothCamera);
        }

        private void SwitchCamera(FusionModeCameraRig targetRig, bool smoothCamera)
        {
            if (targetRig == null)
                return;

            Camera previousCamera = activeCamera != null ? activeCamera : Camera.main;
            Camera targetCamera = targetRig.Camera;
            if (targetCamera == null)
                return;

            if (previousCamera == targetCamera)
            {
                targetCamera.enabled = true;
                targetCamera.tag = "MainCamera";
                UpdateCameraReferences(targetCamera);
                return;
            }

            targetCamera.gameObject.SetActive(true);
            targetCamera.enabled = true;
            targetCamera.tag = "MainCamera";

            if (smoothCamera && previousCamera != null)
                targetRig.BeginBlendFrom(previousCamera, cameraTransitionDuration);
            else
                targetRig.SnapToDesiredPose();

            DisableOtherModeCamera(targetRig);
            activeCamera = targetCamera;
            UpdateCameraReferences(targetCamera);
        }

        private void DisableOtherModeCamera(FusionModeCameraRig targetRig)
        {
            FusionModeCameraRig otherRig = targetRig == playerCamera ? managementCamera : playerCamera;
            if (otherRig == null || otherRig.Camera == null)
                return;

            otherRig.Camera.enabled = false;
            if (otherRig.Camera.CompareTag("MainCamera"))
                otherRig.Camera.tag = "Untagged";
        }

        private void UpdateCameraReferences(Camera camera)
        {
            if (camera == null)
                return;

            if (playerControl != null)
                playerControl.targetCamera = camera;

            if (fusionSandbox != null)
                fusionSandbox.worldCamera = camera;
        }

        private void HandleShopHotkeys()
        {
            for (int i = 0; i < shopItems.Count; i++)
            {
                BlockShopItem item = shopItems[i];
                if (item == null || item.hotkey == KeyCode.None)
                    continue;

                if (Input.GetKeyDown(item.hotkey))
                    TryPurchase(item);
            }
        }

        private void ApplyCursorState(bool management)
        {
            if (management)
            {
                Cursor.visible = showSystemCursorInManagementMode;
                Cursor.lockState = CursorLockMode.None;
                return;
            }

            Cursor.visible = !hideSystemCursorInPlayerMode;
            Cursor.lockState = hideSystemCursorInPlayerMode ? CursorLockMode.Confined : CursorLockMode.None;
        }

        private void ResolveReferences()
        {
            if (playerControl == null)
                playerControl = PlayerControl.Active != null ? PlayerControl.Active : FindFirstObjectByType<PlayerControl>();

            if (fusionSandbox == null)
                fusionSandbox = FindFirstObjectByType<RuntimeTileMeshFusionSandbox>();

            if (playerCamera == null || managementCamera == null)
            {
                FusionModeCameraRig[] rigs = FindObjectsByType<FusionModeCameraRig>(FindObjectsSortMode.None);
                for (int i = 0; i < rigs.Length; i++)
                {
                    FusionModeCameraRig rig = rigs[i];
                    if (rig == null)
                        continue;

                    if (rig.mode == FusionModeCameraRig.RigMode.PlayerFollow && playerCamera == null)
                        playerCamera = rig;
                    else if (rig.mode == FusionModeCameraRig.RigMode.ManagementOverview && managementCamera == null)
                        managementCamera = rig;
                }
            }

            BindCameraRigReferences(playerCamera);
            BindCameraRigReferences(managementCamera);

            if (currencySource == null)
                currencySource = FindFirstObjectByType<TimeCounterUI>();
        }

        private void BindCameraRigReferences(FusionModeCameraRig rig)
        {
            if (rig == null)
                return;

            if (rig.playerControl == null)
                rig.playerControl = playerControl;

            if (rig.fusionSandbox == null)
                rig.fusionSandbox = fusionSandbox;
        }

        private void EnsureCurrencySource()
        {
            if (currencySource != null)
                return;

            GameObject currencyObject = new GameObject("Fusion Currency");
            currencyObject.transform.SetParent(transform, false);
            currencySource = currencyObject.AddComponent<TimeCounterUI>();
            currencySource.countUp = false;
            currencySource.startSeconds = defaultStartingMoney;
            currencySource.maxValue = Mathf.Max(defaultStartingMoney, 9999f);
        }

        private void EnsureMoneyHud()
        {
            if (moneyText != null || !createMoneyHudIfMissing)
                return;

            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObject = new GameObject(
                    "Fusion HUD Canvas",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler));
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1200;

                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            GameObject textObject = new GameObject("Money TMP", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(canvas.transform, false);

            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-32f, -24f);
            rect.sizeDelta = new Vector2(320f, 64f);

            moneyText = textObject.GetComponent<TextMeshProUGUI>();
            moneyText.alignment = TextAlignmentOptions.TopRight;
            moneyText.fontSize = 32f;
            moneyText.color = Color.white;
            moneyText.raycastTarget = false;
        }

        private void HandleCurrencyChanged(float _)
        {
            RefreshMoneyHud();
        }

        private void RefreshMoneyHud()
        {
            if (moneyText == null)
                return;

            int value = currencySource != null ? currencySource.CurrentWholeValue : Mathf.FloorToInt(defaultStartingMoney);
            moneyText.text = string.Format(moneyFormat, value);
        }
    }
}
