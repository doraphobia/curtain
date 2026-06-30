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
        public RectTransform shopContentRoot;
        public TextMeshProUGUI shopConfirmationText;
        public string confirmPurchaseFormat = "你是否要购买 {0}? 再次点击确认";
        public string cannotAffordFormat = "金钱不足: {0}";
        public Vector2 shopPanelSize = new Vector2(360f, 460f);
        public Vector2 shopPanelAnchoredPosition = new Vector2(-32f, -112f);
        public Color shopPanelColor = new Color(0f, 0f, 0f, 0.72f);
        public Color shopButtonColor = new Color(1f, 1f, 1f, 0.12f);
        public Color shopPendingButtonColor = new Color(0.1f, 0.38f, 1f, 0.35f);
        public Color shopTextColor = Color.white;
        public Color shopPriceColor = new Color(1f, 0.84f, 0.36f, 1f);

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
        private int pendingShopIndex = -1;
        private bool cachedPlayerHeadingLimit;
        private bool hasCachedPlayerHeadingState;

        public bool IsManagementMode => currentMode == GameMode.Management;

        void Awake()
        {
            ResolveReferences();
            EnsureCurrencySource();
            EnsureMoneyHud();
            EnsureShopPanel();

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
            ApplyShopVisibility();
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
                    HandleShopItemClicked(i);
            }
        }

        public void HandleShopItemClicked(int index)
        {
            if (!IsManagementMode)
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
                return;
            }

            Canvas canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
                canvas = CreateOverlayCanvas("Fusion UI Canvas", 1200);
            else
                EnsureCanvasCanReceiveClicks(canvas);
            EnsureEventSystem();

            GameObject panelObject = new GameObject("Fusion Block Shop Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panelObject.transform.SetParent(canvas.transform, false);
            shopPanelRoot = panelObject.GetComponent<RectTransform>();
            shopPanelRoot.anchorMin = new Vector2(1f, 1f);
            shopPanelRoot.anchorMax = new Vector2(1f, 1f);
            shopPanelRoot.pivot = new Vector2(1f, 1f);
            shopPanelRoot.anchoredPosition = shopPanelAnchoredPosition;
            shopPanelRoot.sizeDelta = shopPanelSize;

            Image panelImage = panelObject.GetComponent<Image>();
            panelImage.color = shopPanelColor;
            panelImage.raycastTarget = true;

            VerticalLayoutGroup panelLayout = panelObject.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(16, 16, 14, 16);
            panelLayout.spacing = 10f;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = false;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            GameObject titleObject = CreateTmpTextObject("Shop Title", panelObject.transform, "Blocks", 30f, shopTextColor, TextAlignmentOptions.TopLeft);
            LayoutElement titleLayout = titleObject.AddComponent<LayoutElement>();
            titleLayout.preferredHeight = 42f;

            GameObject confirmObject = CreateTmpTextObject("Shop Confirmation", panelObject.transform, string.Empty, 22f, shopPriceColor, TextAlignmentOptions.TopLeft);
            shopConfirmationText = confirmObject.GetComponent<TextMeshProUGUI>();
            LayoutElement confirmLayout = confirmObject.AddComponent<LayoutElement>();
            confirmLayout.preferredHeight = 64f;

            GameObject contentObject = new GameObject("Shop Content", typeof(RectTransform), typeof(VerticalLayoutGroup));
            contentObject.transform.SetParent(panelObject.transform, false);
            shopContentRoot = contentObject.GetComponent<RectTransform>();
            VerticalLayoutGroup contentLayout = contentObject.GetComponent<VerticalLayoutGroup>();
            contentLayout.spacing = 8f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = false;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            LayoutElement contentLayoutElement = contentObject.AddComponent<LayoutElement>();
            contentLayoutElement.flexibleHeight = 1f;

            RebuildShopButtons();
            ApplyShopVisibility();
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
            if (shopContentRoot == null)
                return;

            for (int i = shopContentRoot.childCount - 1; i >= 0; i--)
                Destroy(shopContentRoot.GetChild(i).gameObject);

            for (int i = 0; i < shopItems.Count; i++)
                CreateShopButton(i);
        }

        private void CreateShopButton(int index)
        {
            BlockShopItem item = shopItems[index];
            GameObject buttonObject = new GameObject("Shop Item - " + (item != null ? item.displayName : index.ToString()), typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(shopContentRoot, false);

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(0f, 64f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = shopButtonColor;
            image.raycastTarget = true;

            Button button = buttonObject.GetComponent<Button>();
            int capturedIndex = index;
            button.onClick.AddListener(() => HandleShopItemClicked(capturedIndex));

            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 64f;

            HorizontalLayoutGroup row = buttonObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(14, 14, 8, 8);
            row.spacing = 10f;
            row.childControlWidth = true;
            row.childControlHeight = true;
            row.childForceExpandWidth = false;
            row.childForceExpandHeight = true;

            GameObject nameText = CreateTmpTextObject("Name", buttonObject.transform, item != null ? item.displayName : "Block", 24f, shopTextColor, TextAlignmentOptions.MidlineLeft);
            LayoutElement nameLayout = nameText.AddComponent<LayoutElement>();
            nameLayout.flexibleWidth = 1f;

            GameObject priceText = CreateTmpTextObject("Price", buttonObject.transform, item != null ? item.price.ToString() : "0", 22f, shopPriceColor, TextAlignmentOptions.MidlineRight);
            LayoutElement priceLayout = priceText.AddComponent<LayoutElement>();
            priceLayout.preferredWidth = 88f;

            shopButtons.Add(button);
            shopButtonImages.Add(image);
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

            if (shopButtons.Count != shopItems.Count)
                RebuildShopButtons();

            for (int i = 0; i < shopButtons.Count; i++)
            {
                Button button = shopButtons[i];
                if (button == null)
                    continue;

                BlockShopItem item = i < shopItems.Count ? shopItems[i] : null;
                button.interactable = IsManagementMode && item != null && item.blockPrefab != null &&
                    (allowFreePurchases || currencySource == null || currencySource.CanAfford(item.price));

                if (i < shopButtonImages.Count && shopButtonImages[i] != null)
                    shopButtonImages[i].color = i == pendingShopIndex ? shopPendingButtonColor : shopButtonColor;
            }
        }

        private void ApplyShopVisibility()
        {
            if (shopPanelRoot == null)
                return;

            if (!IsManagementMode)
            {
                pendingShopIndex = -1;
                SetConfirmationText(string.Empty);
            }

            shopPanelRoot.gameObject.SetActive(!showShopOnlyInManagementMode || IsManagementMode);
        }

        private void SetConfirmationText(string text)
        {
            if (shopConfirmationText != null)
                shopConfirmationText.text = text;
        }
    }
}
