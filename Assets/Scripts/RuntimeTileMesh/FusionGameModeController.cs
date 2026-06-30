using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
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
        public bool showSystemCursorInManagementMode = false;
        public bool showHeadingPointInManagementMode = true;
        public bool releaseHeadingPointRadiusInManagement = true;
        public bool placeSelectedBlockWhenLeavingManagement = true;

        [Header("Fusion Shop")]
        public bool allowFreePurchases = true;
        public bool buyAtPointerPosition = true;
        public bool dragPurchasedBlockImmediately = true;
        public List<BlockShopItem> shopItems = new List<BlockShopItem>();

        [Header("Fusion Shop UI")]
        public bool createShopPanelIfMissing = true;
        public bool showShopOnlyInManagementMode = true;
        public RectTransform shopPanelRoot;
        public RectTransform shopViewportRoot;
        public RectTransform shopContentRoot;
        public TextMeshProUGUI shopConfirmationText;
        public string confirmPurchaseFormat = "Buy {0}? Click again to confirm.";
        public string cannotAffordFormat = "Not enough money: {0}";
        [Min(120f)]
        public float shopBannerHeight = 300f;
        [Min(0f)]
        public float shopBottomInset = 0f;
        [Min(0.01f)]
        public float shopSlideDuration = 0.38f;
        public AnimationCurve shopSlideCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        public Vector2 shopCardSize = new Vector2(292f, 220f);
        [Min(0f)]
        public float shopCardSpacing = 16f;
        [Min(1f)]
        public float shopEdgeScrollZone = 180f;
        [Min(0f)]
        public float shopEdgeScrollMinSpeed = 160f;
        [Min(0f)]
        public float shopEdgeScrollMaxSpeed = 1050f;
        public Color shopPanelColor = new Color(0f, 0f, 0f, 0.72f);
        public Color shopButtonColor = new Color(1f, 1f, 1f, 0.12f);
        public Color shopPendingButtonColor = new Color(0.1f, 0.38f, 1f, 0.35f);
        public Color shopTextColor = Color.white;
        public Color shopPriceColor = new Color(1f, 0.84f, 0.36f, 1f);

        [Header("Fusion Shop Thumbnails")]
        [Range(64, 1024)]
        public int shopThumbnailResolution = 256;
        [Min(0.05f)]
        public float shopThumbnailFramingPadding = 0.35f;
        public Color shopThumbnailBackground = new Color(0f, 0f, 0f, 0f);

        [Header("Currency")]
        public TimeCounterUI currencySource;
        public TextMeshProUGUI moneyText;
        public bool createMoneyHudIfMissing = true;
        public string moneyFormat = "Money: {0}";
        [Min(0f)]
        public float defaultStartingMoney = 100f;

        private Camera activeCamera;
        private readonly List<Button> shopButtons = new List<Button>();
        private readonly List<Image> shopButtonImages = new List<Image>();
        private readonly List<RawImage> shopThumbnailImages = new List<RawImage>();
        private CanvasGroup shopCanvasGroup;
        private FusionShopThumbnailRenderer shopThumbnailRenderer;
        private RuntimeTileMeshFusionSandbox boundSandbox;
        private RuntimeTileMeshDraggableBlock pendingPurchasedBlock;
        private int pendingPurchasedPrice;
        private int pendingShopIndex = -1;
        private int shopItemsSignature;
        private float shopSlideValue;
        private float shopSlideStartValue;
        private float shopSlideTargetValue;
        private float shopSlideStartTime;
        private bool shopSlideAnimating;
        private bool cachedPlayerHeadingLimit;
        private bool hasCachedPlayerHeadingState;

        public bool IsManagementMode => currentMode == GameMode.Management;

        void Awake()
        {
            ResolveReferences();
            EnsureCurrencySource();
            EnsureShopPanel();
            EnsureMoneyHud();

            if (applyStartModeOnAwake)
                SetMode(startMode, false);
            else
                ApplyModeState(false);
        }

        void OnEnable()
        {
            ResolveReferences();
            BindSandboxEvents();
            if (currencySource != null)
                currencySource.ValueChanged += HandleCurrencyChanged;
            RefreshMoneyHud();
        }

        void OnDisable()
        {
            UnbindSandboxEvents();
            if (currencySource != null)
                currencySource.ValueChanged -= HandleCurrencyChanged;
        }

        void Update()
        {
            ResolveReferences();
            BindSandboxEvents();

            if (Input.GetKeyDown(toggleModeKey))
                ToggleMode();

            HandlePendingConfirmationOutsideClick();
            HandlePurchasedBlockCancellation();
            UpdateShopSlide();
            UpdateShopEdgeScroll();

            if (IsManagementMode && IsShopInteractive())
                HandleShopHotkeys();

            RefreshMoneyHud();
            RefreshShopPanel();
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

            int paidPrice = 0;
            if (!allowFreePurchases)
            {
                EnsureCurrencySource();
                if (currencySource == null || !currencySource.TrySpend(item.price))
                    return false;

                paidPrice = item.price;
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

            if (spawned == null)
            {
                if (paidPrice > 0 && currencySource != null)
                    currencySource.AddValue(paidPrice);
                return false;
            }

            spawned.name = item.displayName;
            pendingPurchasedBlock = spawned;
            pendingPurchasedPrice = paidPrice;
            SetShopExpanded(false, false);
            return true;
        }

        private void ApplyModeState(bool smoothCamera)
        {
            ResolveReferences();

            bool management = IsManagementMode;
            if (playerControl != null)
            {
                CachePlayerHeadingState();
                playerControl.playerInputEnabled = !management;
                playerControl.headingPointInputEnabled = management ? showHeadingPointInManagementMode : showHeadingPointInPlayerMode;
                playerControl.showHeadingPoint = management ? showHeadingPointInManagementMode : showHeadingPointInPlayerMode;
                playerControl.LimitHeadingPointReach = management && releaseHeadingPointRadiusInManagement
                    ? false
                    : cachedPlayerHeadingLimit;
            }

            if (fusionSandbox != null)
                fusionSandbox.SetManagementInputEnabled(management, placeSelectedBlockWhenLeavingManagement);

            ApplyCursorState(management);
            ApplyShopVisibility(!smoothCamera);
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
                SyncAudioListeners(targetCamera);
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
            SyncAudioListeners(targetCamera);
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

        private void SyncAudioListeners(Camera targetCamera)
        {
            if (targetCamera == null)
                return;

            SetCameraAudioListener(playerCamera != null ? playerCamera.Camera : null, targetCamera);
            SetCameraAudioListener(managementCamera != null ? managementCamera.Camera : null, targetCamera);
            SetCameraAudioListener(targetCamera, targetCamera);
        }

        private static void SetCameraAudioListener(Camera camera, Camera targetCamera)
        {
            if (camera == null)
                return;

            AudioListener listener = camera.GetComponent<AudioListener>();
            if (listener == null && camera == targetCamera)
                listener = camera.gameObject.AddComponent<AudioListener>();

            if (listener != null)
                listener.enabled = camera == targetCamera;
        }

        private void HandleShopHotkeys()
        {
            for (int i = 0; i < shopItems.Count; i++)
            {
                BlockShopItem item = shopItems[i];
                if (item == null || item.hotkey == KeyCode.None)
                    continue;

                if (Input.GetKeyDown(item.hotkey))
                    HandleShopItemClicked(i);
            }
        }

        public void HandleShopItemClicked(int index)
        {
            if (!IsManagementMode || !IsShopInteractive())
                return;

            if (index < 0 || index >= shopItems.Count)
                return;

            BlockShopItem item = shopItems[index];
            if (item == null || item.blockPrefab == null)
                return;

            if (pendingShopIndex != index)
            {
                pendingShopIndex = index;
                SetConfirmationText(string.Format(confirmPurchaseFormat, item.displayName));
                RefreshShopPanel();
                return;
            }

            if (!allowFreePurchases && currencySource != null && !currencySource.CanAfford(item.price))
            {
                SetConfirmationText(string.Format(cannotAffordFormat, item.displayName));
                RefreshShopPanel();
                return;
            }

            if (TryPurchase(item))
            {
                pendingShopIndex = -1;
                SetConfirmationText(string.Empty);
                RefreshShopPanel();
            }
        }

        private void HandlePendingConfirmationOutsideClick()
        {
            if (pendingShopIndex < 0 || !IsShopInteractive() || !Input.GetMouseButtonDown(0))
                return;

            if (IsPointerInsideRect(shopPanelRoot))
                return;

            pendingShopIndex = -1;
            SetConfirmationText(string.Empty);
            if (fusionSandbox != null)
                fusionSandbox.SuppressPointerInputForCurrentFrame();
            RefreshShopPanel();
        }

        private void HandlePurchasedBlockCancellation()
        {
            if (pendingPurchasedBlock == null || fusionSandbox == null)
                return;

            if (fusionSandbox.SelectedBlock != pendingPurchasedBlock)
                return;

            if (Input.GetMouseButtonDown(Mathf.Max(0, fusionSandbox.cancelSelectionMouseButton)))
                fusionSandbox.CancelSelectedBlock(true);
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

        private void BindSandboxEvents()
        {
            if (boundSandbox == fusionSandbox)
                return;

            UnbindSandboxEvents();
            boundSandbox = fusionSandbox;
            if (boundSandbox == null)
                return;

            boundSandbox.BlockPlaced += HandleSandboxBlockPlaced;
            boundSandbox.BlockSelectionCancelled += HandleSandboxBlockSelectionCancelled;
        }

        private void UnbindSandboxEvents()
        {
            if (boundSandbox == null)
                return;

            boundSandbox.BlockPlaced -= HandleSandboxBlockPlaced;
            boundSandbox.BlockSelectionCancelled -= HandleSandboxBlockSelectionCancelled;
            boundSandbox = null;
        }

        private void HandleSandboxBlockPlaced(RuntimeTileMeshDraggableBlock block)
        {
            if (block == null || block != pendingPurchasedBlock)
                return;

            pendingPurchasedBlock = null;
            pendingPurchasedPrice = 0;
            if (IsManagementMode)
                SetShopExpanded(true, false);
        }

        private void HandleSandboxBlockSelectionCancelled(RuntimeTileMeshDraggableBlock block, bool destroyed)
        {
            if (block == null || block != pendingPurchasedBlock)
                return;

            if (destroyed && pendingPurchasedPrice > 0 && currencySource != null)
                currencySource.AddValue(pendingPurchasedPrice);

            pendingPurchasedBlock = null;
            pendingPurchasedPrice = 0;
            if (IsManagementMode)
                SetShopExpanded(true, false);
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

        private void CachePlayerHeadingState()
        {
            if (hasCachedPlayerHeadingState || playerControl == null)
                return;

            cachedPlayerHeadingLimit = playerControl.LimitHeadingPointReach;
            hasCachedPlayerHeadingState = true;
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

        private void EnsureShopPanel()
        {
            if (shopPanelRoot != null || !createShopPanelIfMissing)
            {
                EnsureCanvasCanReceiveClicks(shopPanelRoot != null ? shopPanelRoot.GetComponentInParent<Canvas>() : null);
                EnsureEventSystem();
                if (shopPanelRoot != null)
                {
                    shopCanvasGroup = shopPanelRoot.GetComponent<CanvasGroup>();
                    if (shopCanvasGroup == null)
                        shopCanvasGroup = shopPanelRoot.gameObject.AddComponent<CanvasGroup>();
                }
                return;
            }

            GameObject existingCanvasObject = GameObject.Find("Fusion UI Canvas");
            Canvas canvas = existingCanvasObject != null ? existingCanvasObject.GetComponent<Canvas>() : null;
            if (canvas == null)
                canvas = CreateOverlayCanvas("Fusion UI Canvas", 1200);
            EnsureCanvasCanReceiveClicks(canvas);
            EnsureEventSystem();

            GameObject panelObject = new GameObject(
                "Fusion Block Shop Banner",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            panelObject.transform.SetParent(canvas.transform, false);
            shopPanelRoot = panelObject.GetComponent<RectTransform>();
            shopPanelRoot.anchorMin = new Vector2(0f, 0f);
            shopPanelRoot.anchorMax = new Vector2(1f, 0f);
            shopPanelRoot.pivot = new Vector2(0.5f, 0f);
            shopPanelRoot.sizeDelta = new Vector2(0f, shopBannerHeight);

            Image panelImage = panelObject.GetComponent<Image>();
            panelImage.color = shopPanelColor;
            panelImage.raycastTarget = true;

            shopCanvasGroup = panelObject.GetComponent<CanvasGroup>();

            VerticalLayoutGroup panelLayout = panelObject.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(24, 24, 12, 18);
            panelLayout.spacing = 8f;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            GameObject confirmObject = CreateTmpTextObject(
                "Shop Confirmation",
                panelObject.transform,
                string.Empty,
                24f,
                shopPriceColor,
                TextAlignmentOptions.Center);
            shopConfirmationText = confirmObject.GetComponent<TextMeshProUGUI>();
            LayoutElement confirmLayout = confirmObject.AddComponent<LayoutElement>();
            confirmLayout.preferredHeight = 38f;

            GameObject viewportObject = new GameObject(
                "Shop Viewport",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(RectMask2D),
                typeof(LayoutElement));
            viewportObject.transform.SetParent(panelObject.transform, false);
            shopViewportRoot = viewportObject.GetComponent<RectTransform>();
            Image viewportImage = viewportObject.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportImage.raycastTarget = false;
            LayoutElement viewportLayout = viewportObject.GetComponent<LayoutElement>();
            viewportLayout.flexibleHeight = 1f;

            GameObject contentObject = new GameObject(
                "Shop Content",
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup),
                typeof(ContentSizeFitter));
            contentObject.transform.SetParent(viewportObject.transform, false);
            shopContentRoot = contentObject.GetComponent<RectTransform>();
            shopContentRoot.anchorMin = new Vector2(0f, 0f);
            shopContentRoot.anchorMax = new Vector2(0f, 1f);
            shopContentRoot.pivot = new Vector2(0f, 0.5f);
            shopContentRoot.anchoredPosition = Vector2.zero;
            shopContentRoot.sizeDelta = new Vector2(0f, 0f);

            HorizontalLayoutGroup contentLayout = contentObject.GetComponent<HorizontalLayoutGroup>();
            contentLayout.spacing = shopCardSpacing;
            contentLayout.childControlWidth = false;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = false;
            contentLayout.childForceExpandHeight = true;

            ContentSizeFitter fitter = contentObject.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            shopSlideValue = 0f;
            shopSlideTargetValue = 0f;
            ApplyShopSlidePosition();

            RebuildShopButtons();
            ApplyShopVisibility(true);
        }

        private Canvas CreateOverlayCanvas(string name, int sortingOrder)
        {
            GameObject canvasObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private void EnsureCanvasCanReceiveClicks(Canvas canvas)
        {
            if (canvas == null)
                return;

            if (canvas.GetComponent<GraphicRaycaster>() == null)
                canvas.gameObject.AddComponent<GraphicRaycaster>();
        }

        private void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
                return;

            GameObject eventSystemObject = new GameObject(
                "EventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            eventSystemObject.transform.SetParent(transform, false);
        }

        private void RebuildShopButtons()
        {
            shopButtons.Clear();
            shopButtonImages.Clear();
            shopThumbnailImages.Clear();
            if (shopContentRoot == null)
                return;

            for (int i = shopContentRoot.childCount - 1; i >= 0; i--)
            {
                GameObject child = shopContentRoot.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            for (int i = 0; i < shopItems.Count; i++)
                CreateShopButton(i);

            shopItemsSignature = CalculateShopItemsSignature();
            LayoutRebuilder.ForceRebuildLayoutImmediate(shopContentRoot);

            if (shopThumbnailRenderer == null)
            {
                shopThumbnailRenderer = GetComponent<FusionShopThumbnailRenderer>();
                if (shopThumbnailRenderer == null)
                    shopThumbnailRenderer = gameObject.AddComponent<FusionShopThumbnailRenderer>();
            }

            shopThumbnailRenderer.Rebuild(
                shopItems,
                shopThumbnailImages,
                shopThumbnailResolution,
                shopThumbnailBackground,
                shopThumbnailFramingPadding);
        }

        private void CreateShopButton(int index)
        {
            BlockShopItem item = shopItems[index];
            GameObject buttonObject = new GameObject(
                "Shop Item - " + (item != null ? item.displayName : index.ToString()),
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement),
                typeof(VerticalLayoutGroup));
            buttonObject.transform.SetParent(shopContentRoot, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = shopCardSize;

            Image image = buttonObject.GetComponent<Image>();
            image.color = shopButtonColor;
            image.raycastTarget = true;

            Button button = buttonObject.GetComponent<Button>();
            int capturedIndex = index;
            button.onClick.AddListener(() => HandleShopItemClicked(capturedIndex));

            LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
            layout.preferredWidth = shopCardSize.x;
            layout.preferredHeight = shopCardSize.y;
            layout.minWidth = shopCardSize.x;

            VerticalLayoutGroup column = buttonObject.GetComponent<VerticalLayoutGroup>();
            column.padding = new RectOffset(14, 14, 12, 12);
            column.spacing = 8f;
            column.childControlWidth = true;
            column.childControlHeight = true;
            column.childForceExpandWidth = true;
            column.childForceExpandHeight = false;

            GameObject nameText = CreateTmpTextObject(
                "Name",
                buttonObject.transform,
                item != null ? item.displayName : "Block",
                26f,
                shopTextColor,
                TextAlignmentOptions.MidlineLeft);
            LayoutElement nameLayout = nameText.AddComponent<LayoutElement>();
            nameLayout.preferredHeight = 34f;

            GameObject bodyObject = new GameObject(
                "Thumbnail and Price",
                typeof(RectTransform),
                typeof(HorizontalLayoutGroup),
                typeof(LayoutElement));
            bodyObject.transform.SetParent(buttonObject.transform, false);
            HorizontalLayoutGroup bodyLayout = bodyObject.GetComponent<HorizontalLayoutGroup>();
            bodyLayout.spacing = 10f;
            bodyLayout.childControlWidth = true;
            bodyLayout.childControlHeight = true;
            bodyLayout.childForceExpandWidth = false;
            bodyLayout.childForceExpandHeight = true;
            LayoutElement bodySize = bodyObject.GetComponent<LayoutElement>();
            bodySize.flexibleHeight = 1f;

            GameObject thumbnailContainer = new GameObject(
                "Live Block Thumbnail Container",
                typeof(RectTransform),
                typeof(LayoutElement));
            thumbnailContainer.transform.SetParent(bodyObject.transform, false);
            LayoutElement thumbnailLayout = thumbnailContainer.GetComponent<LayoutElement>();
            thumbnailLayout.flexibleWidth = 1f;
            thumbnailLayout.minWidth = Mathf.Max(80f, shopCardSize.x - 120f);

            GameObject thumbnailObject = new GameObject(
                "Live Block Thumbnail",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage),
                typeof(AspectRatioFitter));
            thumbnailObject.transform.SetParent(thumbnailContainer.transform, false);
            RectTransform thumbnailRect = thumbnailObject.GetComponent<RectTransform>();
            thumbnailRect.anchorMin = Vector2.zero;
            thumbnailRect.anchorMax = Vector2.one;
            thumbnailRect.offsetMin = Vector2.zero;
            thumbnailRect.offsetMax = Vector2.zero;
            RawImage thumbnail = thumbnailObject.GetComponent<RawImage>();
            thumbnail.color = Color.white;
            thumbnail.raycastTarget = false;
            AspectRatioFitter aspect = thumbnailObject.GetComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 1f;

            GameObject priceText = CreateTmpTextObject(
                "Price",
                bodyObject.transform,
                item != null ? item.price.ToString() : "0",
                24f,
                shopPriceColor,
                TextAlignmentOptions.BottomRight);
            LayoutElement priceLayout = priceText.AddComponent<LayoutElement>();
            priceLayout.preferredWidth = 72f;

            shopButtons.Add(button);
            shopButtonImages.Add(image);
            shopThumbnailImages.Add(thumbnail);
        }

        private GameObject CreateTmpTextObject(
            string name,
            Transform parent,
            string text,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TextMeshProUGUI tmp = textObject.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;
            return textObject;
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

        private void RefreshShopPanel()
        {
            if (shopPanelRoot == null)
                return;

            if (shopButtons.Count != shopItems.Count || shopItemsSignature != CalculateShopItemsSignature())
                RebuildShopButtons();

            bool shopInteractive = IsShopInteractive();
            for (int i = 0; i < shopButtons.Count; i++)
            {
                Button button = shopButtons[i];
                if (button == null)
                    continue;

                BlockShopItem item = i < shopItems.Count ? shopItems[i] : null;
                button.interactable = shopInteractive && item != null && item.blockPrefab != null &&
                    (allowFreePurchases || currencySource == null || currencySource.CanAfford(item.price));

                if (i < shopButtonImages.Count && shopButtonImages[i] != null)
                    shopButtonImages[i].color = i == pendingShopIndex ? shopPendingButtonColor : shopButtonColor;
            }
        }

        private void ApplyShopVisibility(bool instant)
        {
            if (shopPanelRoot == null)
                return;

            if (!IsManagementMode)
            {
                pendingShopIndex = -1;
                SetConfirmationText(string.Empty);
            }

            bool shouldExpand = (!showShopOnlyInManagementMode || IsManagementMode) && pendingPurchasedBlock == null;
            SetShopExpanded(shouldExpand, instant);
        }

        private void SetConfirmationText(string text)
        {
            if (shopConfirmationText != null)
                shopConfirmationText.text = string.IsNullOrEmpty(text) ? " " : text;
        }

        private bool IsShopInteractive()
        {
            if (shopPanelRoot == null || shopCanvasGroup == null)
                return false;

            if (showShopOnlyInManagementMode && !IsManagementMode)
                return false;

            return pendingPurchasedBlock == null &&
                !shopSlideAnimating &&
                shopSlideTargetValue > 0.5f &&
                shopSlideValue >= 0.999f;
        }

        private void SetShopExpanded(bool expanded, bool instant)
        {
            if (shopPanelRoot == null)
                return;

            float target = expanded ? 1f : 0f;
            if (instant || shopSlideDuration <= 0.01f)
            {
                shopSlideValue = target;
                shopSlideStartValue = target;
                shopSlideTargetValue = target;
                shopSlideAnimating = false;
                ApplyShopSlidePosition();
                RefreshShopInteractionState();
                return;
            }

            if (!shopSlideAnimating && Mathf.Approximately(shopSlideValue, target))
            {
                shopSlideTargetValue = target;
                RefreshShopInteractionState();
                return;
            }

            shopSlideStartValue = shopSlideValue;
            shopSlideTargetValue = target;
            shopSlideStartTime = Time.unscaledTime;
            shopSlideAnimating = true;
            RefreshShopInteractionState();
        }

        private void UpdateShopSlide()
        {
            if (!shopSlideAnimating)
            {
                ApplyShopSlidePosition();
                RefreshShopInteractionState();
                return;
            }

            float duration = Mathf.Max(0.01f, shopSlideDuration);
            float normalized = Mathf.Clamp01((Time.unscaledTime - shopSlideStartTime) / duration);
            float eased = shopSlideCurve != null ? shopSlideCurve.Evaluate(normalized) : normalized;
            shopSlideValue = Mathf.LerpUnclamped(shopSlideStartValue, shopSlideTargetValue, eased);
            ApplyShopSlidePosition();

            if (normalized >= 1f)
            {
                shopSlideValue = shopSlideTargetValue;
                shopSlideAnimating = false;
                ApplyShopSlidePosition();
            }

            RefreshShopInteractionState();
        }

        private void ApplyShopSlidePosition()
        {
            if (shopPanelRoot == null)
                return;

            shopPanelRoot.sizeDelta = new Vector2(0f, Mathf.Max(120f, shopBannerHeight));
            float hiddenY = -Mathf.Max(120f, shopBannerHeight) - 4f;
            float shownY = Mathf.Max(0f, shopBottomInset);
            Vector2 anchored = shopPanelRoot.anchoredPosition;
            anchored.x = 0f;
            anchored.y = Mathf.Lerp(hiddenY, shownY, Mathf.Clamp01(shopSlideValue));
            shopPanelRoot.anchoredPosition = anchored;
        }

        private void RefreshShopInteractionState()
        {
            if (shopCanvasGroup == null)
                return;

            bool interactive = IsShopInteractive();
            shopCanvasGroup.interactable = interactive;
            shopCanvasGroup.blocksRaycasts = interactive;
        }

        private void UpdateShopEdgeScroll()
        {
            if (!IsShopInteractive() || shopViewportRoot == null || shopContentRoot == null)
                return;

            if (!IsPointerInsideRect(shopViewportRoot))
                return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(shopContentRoot);
            float viewportWidth = shopViewportRoot.rect.width;
            float contentWidth = Mathf.Max(shopContentRoot.rect.width, LayoutUtility.GetPreferredWidth(shopContentRoot));
            float maxOffset = Mathf.Max(0f, contentWidth - viewportWidth);
            if (maxOffset <= 0.01f)
            {
                SetShopContentX(0f, 0f);
                return;
            }

            Vector2 localPointer;
            Canvas canvas = shopViewportRoot.GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                shopViewportRoot,
                Input.mousePosition,
                eventCamera,
                out localPointer))
            {
                return;
            }

            Rect viewportRect = shopViewportRoot.rect;
            float zone = Mathf.Min(Mathf.Max(1f, shopEdgeScrollZone), viewportWidth * 0.5f);
            float direction = 0f;
            float pressure = 0f;
            if (localPointer.x < viewportRect.xMin + zone)
            {
                direction = 1f;
                pressure = Mathf.InverseLerp(viewportRect.xMin + zone, viewportRect.xMin, localPointer.x);
            }
            else if (localPointer.x > viewportRect.xMax - zone)
            {
                direction = -1f;
                pressure = Mathf.InverseLerp(viewportRect.xMax - zone, viewportRect.xMax, localPointer.x);
            }

            if (Mathf.Approximately(direction, 0f))
                return;

            pressure = Mathf.Clamp01(pressure);
            float speed = Mathf.Lerp(shopEdgeScrollMinSpeed, shopEdgeScrollMaxSpeed, pressure * pressure);
            float nextX = shopContentRoot.anchoredPosition.x + direction * speed * Time.unscaledDeltaTime;
            SetShopContentX(nextX, maxOffset);
        }

        private void SetShopContentX(float x, float maxOffset)
        {
            if (shopContentRoot == null)
                return;

            Vector2 position = shopContentRoot.anchoredPosition;
            position.x = Mathf.Clamp(x, -Mathf.Max(0f, maxOffset), 0f);
            shopContentRoot.anchoredPosition = position;
        }

        private static bool IsPointerInsideRect(RectTransform target)
        {
            if (target == null || !target.gameObject.activeInHierarchy)
                return false;

            Canvas canvas = target.GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(target, Input.mousePosition, eventCamera);
        }

        private int CalculateShopItemsSignature()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + shopItems.Count;
                for (int i = 0; i < shopItems.Count; i++)
                {
                    BlockShopItem item = shopItems[i];
                    if (item == null)
                    {
                        hash *= 31;
                        continue;
                    }

                    hash = hash * 31 + (item.displayName != null ? item.displayName.GetHashCode() : 0);
                    hash = hash * 31 + (item.blockPrefab != null ? item.blockPrefab.GetInstanceID() : 0);
                    hash = hash * 31 + item.price;
                    hash = hash * 31 + (int)item.hotkey;
                }

                return hash;
            }
        }
    }
}
