using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DuoCurtain.RuntimeTileMesh
{
    [DisallowMultipleComponent]
    public sealed class RuntimeTileMeshBlockInfoOverlay : MonoBehaviour
    {
        private const float DisplayToggleDebounceSeconds = 0.12f;

        public enum TabDisplayMode
        {
            ToggleOnPress,
            HoldToShow
        }

        [Header("Input")]
        public KeyCode displayKey = KeyCode.Tab;
        public TabDisplayMode displayMode = TabDisplayMode.ToggleOnPress;
        public bool startVisible = false;
        public bool allowManualDisplayInput = false;

        [Header("References")]
        public RuntimeTileMeshFusionSandbox fusionSandbox;
        public FusionGameModeController modeController;
        public Camera worldCamera;
        public bool autoFollowActiveFusionCamera = true;
        public TMP_FontAsset labelFont;
        public string resourcesFontPath = "Fonts/Bayon-Regular SDF";

        [Header("Visibility")]
        public bool showOnlyInManagementMode = true;

        [Header("Figma Typography")]
        [Min(1f)]
        public float fontSize = 30f;
        [Min(1f)]
        public float lineHeight = 30f;
        [Range(-100f, 100f)]
        public float letterSpacingPercent = -5f;
        public Color textColor = Color.black;
        public bool useTextOutline = true;
        public Color outlineColor = new Color(1f, 1f, 1f, 0.92f);
        [Range(0f, 0.5f)]
        public float outlineWidth = 0.16f;
        public bool useLabelBackground = true;
        public Color labelBackgroundColor = new Color(1f, 1f, 1f, 0.72f);
        public Vector2 labelSize = new Vector2(82f, 51f);
        public Vector2 topRightInset = new Vector2(8f, -8f);
        public string defaultBlockType = "DEFAULT";
        public string unitSuffix = "UNIT";
        public bool uppercaseType = true;

        [Header("Overlay")]
        public int canvasSortingOrder = 1150;
        public Vector2 referenceResolution = new Vector2(1920f, 1080f);
        [Range(0f, 1f)]
        public float canvasScaleMatch = 0.5f;
        [Min(0.05f)]
        public float blockRefreshInterval = 0.2f;
        public bool hideLabelsOutsideScreen = true;

        private readonly Dictionary<RuntimeTileMeshDraggableBlock, LabelHandle> labels =
            new Dictionary<RuntimeTileMeshDraggableBlock, LabelHandle>();
        private readonly List<RuntimeTileMeshDraggableBlock> staleBlocks =
            new List<RuntimeTileMeshDraggableBlock>();

        private Canvas overlayCanvas;
        private RectTransform overlayCanvasRect;
        private RectTransform labelRoot;
        private float nextRefreshTime;
        private bool isVisible;
        private int lastDisplayToggleFrame = -1;
        private float lastDisplayToggleTime = -100f;

        public bool IsVisible => isVisible;

        void Awake()
        {
            ResolveReferences();
            EnsureOverlayCanvas();
            SetVisible(showOnlyInManagementMode ? false : startVisible, true);
        }

        void Update()
        {
            ResolveReferences();
            if (showOnlyInManagementMode)
                SetVisible(modeController != null && modeController.IsManagementMode, false);
            else if (allowManualDisplayInput)
                HandleDisplayInput();

            if (!isVisible || Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, blockRefreshInterval);
            RefreshLabels();
        }

        void LateUpdate()
        {
            if (!isVisible)
                return;

            UpdateLabelPositions();
        }

        void OnGUI()
        {
            if (showOnlyInManagementMode || !allowManualDisplayInput)
                return;

            if (displayKey == KeyCode.None)
                return;

            Event currentEvent = Event.current;
            if (currentEvent == null || currentEvent.keyCode != displayKey)
                return;

            if (displayMode == TabDisplayMode.HoldToShow)
            {
                if (currentEvent.type == EventType.KeyDown)
                {
                    SetVisible(true, false);
                    currentEvent.Use();
                }
                else if (currentEvent.type == EventType.KeyUp)
                {
                    SetVisible(false, false);
                    currentEvent.Use();
                }

                return;
            }

            if (currentEvent.type == EventType.KeyDown)
            {
                ToggleVisibleFromInput();
                currentEvent.Use();
            }
        }

        void OnDestroy()
        {
            if (overlayCanvas == null)
                return;

            if (Application.isPlaying)
                Destroy(overlayCanvas.gameObject);
            else
                DestroyImmediate(overlayCanvas.gameObject);
        }

        public void SetVisible(bool visible)
        {
            SetVisible(visible, false);
        }

        public string GetDescription(RuntimeTileMeshDraggableBlock block)
        {
            return BuildDescription(block, defaultBlockType, unitSuffix, uppercaseType);
        }

        public static string BuildDescription(
            RuntimeTileMeshDraggableBlock block,
            string fallbackType,
            string suffix,
            bool forceUppercase)
        {
            if (block == null || !block.TryGetLogicalBounds(out _, out _, out Vector2Int size))
                return string.Empty;

            string resolvedType = string.IsNullOrWhiteSpace(block.blockType)
                ? fallbackType
                : block.blockType.Trim();
            if (string.IsNullOrWhiteSpace(resolvedType))
                resolvedType = "DEFAULT";
            if (forceUppercase)
                resolvedType = resolvedType.ToUpperInvariant();

            string resolvedSuffix = string.IsNullOrWhiteSpace(suffix) ? "UNIT" : suffix.Trim();
            return size.x + "X" + size.y + " " + resolvedSuffix + "\n" + resolvedType;
        }

        private void HandleDisplayInput()
        {
            if (displayMode == TabDisplayMode.HoldToShow)
            {
                SetVisible(IsDisplayKeyHeld(), false);
                return;
            }

            if (WasDisplayKeyPressedThisFrame())
                ToggleVisibleFromInput();
        }

        private void ToggleVisibleFromInput()
        {
            if (lastDisplayToggleFrame == Time.frameCount)
                return;

            if (Time.unscaledTime - lastDisplayToggleTime < DisplayToggleDebounceSeconds)
                return;

            lastDisplayToggleFrame = Time.frameCount;
            lastDisplayToggleTime = Time.unscaledTime;
            SetVisible(!isVisible, false);
        }

        private bool WasDisplayKeyPressedThisFrame()
        {
            if (displayKey == KeyCode.None)
                return false;

            bool pressed = Input.GetKeyDown(displayKey);
#if ENABLE_INPUT_SYSTEM
            pressed |= IsInputSystemDisplayKeyPressed();
#endif
            return pressed;
        }

        private bool IsDisplayKeyHeld()
        {
            if (displayKey == KeyCode.None)
                return false;

            bool held = Input.GetKey(displayKey);
#if ENABLE_INPUT_SYSTEM
            held |= IsInputSystemDisplayKeyHeld();
#endif
            return held;
        }

#if ENABLE_INPUT_SYSTEM
        private bool IsInputSystemDisplayKeyPressed()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return false;

            if (displayKey == KeyCode.Tab)
                return keyboard.tabKey.wasPressedThisFrame;
            if (displayKey == KeyCode.Space)
                return keyboard.spaceKey.wasPressedThisFrame;
            if (displayKey == KeyCode.Return)
                return keyboard.enterKey.wasPressedThisFrame;
            if (displayKey == KeyCode.Escape)
                return keyboard.escapeKey.wasPressedThisFrame;

            return false;
        }

        private bool IsInputSystemDisplayKeyHeld()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return false;

            if (displayKey == KeyCode.Tab)
                return keyboard.tabKey.isPressed;
            if (displayKey == KeyCode.Space)
                return keyboard.spaceKey.isPressed;
            if (displayKey == KeyCode.Return)
                return keyboard.enterKey.isPressed;
            if (displayKey == KeyCode.Escape)
                return keyboard.escapeKey.isPressed;

            return false;
        }
#endif

        private void SetVisible(bool visible, bool force)
        {
            if (!force && isVisible == visible)
                return;

            isVisible = visible;
            EnsureOverlayCanvas();
            if (labelRoot != null)
                labelRoot.gameObject.SetActive(isVisible);

            if (!isVisible)
                return;

            RefreshLabels();
            UpdateLabelPositions();
        }

        private void ResolveReferences()
        {
            if (fusionSandbox == null)
                fusionSandbox = GetComponent<RuntimeTileMeshFusionSandbox>();
            if (fusionSandbox == null)
                fusionSandbox = FindFirstObjectByType<RuntimeTileMeshFusionSandbox>();
            if (modeController == null)
                modeController = FindFirstObjectByType<FusionGameModeController>();

            if (autoFollowActiveFusionCamera)
            {
                Camera activeFusionCamera = ResolveActiveFusionCamera();
                if (activeFusionCamera != null)
                    worldCamera = activeFusionCamera;
                else if (fusionSandbox != null && fusionSandbox.worldCamera != null)
                    worldCamera = fusionSandbox.worldCamera;
            }
            else if (fusionSandbox != null && fusionSandbox.worldCamera != null)
            {
                worldCamera = fusionSandbox.worldCamera;
            }

            if (worldCamera == null)
                worldCamera = Camera.main;

            if (labelFont == null && !string.IsNullOrWhiteSpace(resourcesFontPath))
                labelFont = Resources.Load<TMP_FontAsset>(resourcesFontPath);
            if (labelFont == null)
                labelFont = TMP_Settings.defaultFontAsset;
        }

        private void EnsureOverlayCanvas()
        {
            if (overlayCanvas != null)
                return;

            GameObject canvasObject = new GameObject(
                "Fusion Block Info Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            overlayCanvas = canvasObject.GetComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = canvasSortingOrder;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.matchWidthOrHeight = canvasScaleMatch;

            overlayCanvasRect = canvasObject.GetComponent<RectTransform>();

            GameObject rootObject = new GameObject("Block Labels", typeof(RectTransform));
            rootObject.transform.SetParent(canvasObject.transform, false);
            labelRoot = rootObject.GetComponent<RectTransform>();
            labelRoot.anchorMin = Vector2.zero;
            labelRoot.anchorMax = Vector2.one;
            labelRoot.offsetMin = Vector2.zero;
            labelRoot.offsetMax = Vector2.zero;
        }

        private void RefreshLabels()
        {
            EnsureOverlayCanvas();
            RuntimeTileMeshDraggableBlock[] activeBlocks =
                FindObjectsByType<RuntimeTileMeshDraggableBlock>(FindObjectsSortMode.None);
            HashSet<RuntimeTileMeshDraggableBlock> activeSet =
                new HashSet<RuntimeTileMeshDraggableBlock>();

            for (int i = 0; i < activeBlocks.Length; i++)
            {
                RuntimeTileMeshDraggableBlock block = activeBlocks[i];
                if (block == null || !block.isActiveAndEnabled)
                    continue;

                activeSet.Add(block);
                if (!labels.TryGetValue(block, out LabelHandle label) || label == null || label.text == null)
                {
                    label = CreateLabel(block);
                    labels[block] = label;
                }

                ApplyLabelTypography(label);
                label.text.text = FormatWithLineHeight(GetDescription(block));
            }

            staleBlocks.Clear();
            foreach (KeyValuePair<RuntimeTileMeshDraggableBlock, LabelHandle> pair in labels)
            {
                if (pair.Key == null || !activeSet.Contains(pair.Key))
                    staleBlocks.Add(pair.Key);
            }

            for (int i = 0; i < staleBlocks.Count; i++)
            {
                RuntimeTileMeshDraggableBlock staleBlock = staleBlocks[i];
                if (labels.TryGetValue(staleBlock, out LabelHandle label) && label != null && label.root != null)
                    Destroy(label.root.gameObject);
                labels.Remove(staleBlock);
            }
        }

        private LabelHandle CreateLabel(RuntimeTileMeshDraggableBlock block)
        {
            GameObject labelObject = new GameObject(
                "Block Info - " + (block != null ? block.name : "Missing"),
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            labelObject.transform.SetParent(labelRoot, false);

            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = labelSize;

            Image background = labelObject.GetComponent<Image>();
            background.raycastTarget = false;

            GameObject textObject = new GameObject(
                "Text",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textObject.transform.SetParent(labelObject.transform, false);

            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            TextMeshProUGUI label = textObject.GetComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            LabelHandle handle = new LabelHandle(rect, background, label);
            ApplyLabelTypography(handle);
            return handle;
        }

        private void ApplyLabelTypography(LabelHandle handle)
        {
            if (handle == null || handle.text == null)
                return;

            TextMeshProUGUI label = handle.text;
            if (labelFont != null)
                label.font = labelFont;
            label.fontStyle = FontStyles.Normal;
            label.fontSize = fontSize;
            label.characterSpacing = letterSpacingPercent;
            label.lineSpacing = 0f;
            label.color = textColor;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.richText = true;
            label.margin = Vector4.zero;
            label.outlineColor = outlineColor;
            label.outlineWidth = useTextOutline ? outlineWidth : 0f;

            handle.root.sizeDelta = labelSize;
            RectTransform rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            if (handle.background != null)
            {
                handle.background.enabled = useLabelBackground;
                handle.background.color = labelBackgroundColor;
            }
        }

        private string FormatWithLineHeight(string description)
        {
            if (string.IsNullOrEmpty(description))
                return string.Empty;

            return "<line-height=" + Mathf.Max(1f, lineHeight).ToString("0.###") + "px>" + description;
        }

        private void UpdateLabelPositions()
        {
            if (worldCamera == null || overlayCanvasRect == null)
                return;

            foreach (KeyValuePair<RuntimeTileMeshDraggableBlock, LabelHandle> pair in labels)
            {
                RuntimeTileMeshDraggableBlock block = pair.Key;
                LabelHandle label = pair.Value;
                if (block == null || label == null || label.text == null)
                    continue;

                if (!TryGetBlockLabelAnchorWorld(block, out Vector3 worldAnchor))
                {
                    SetLabelVisible(label, false);
                    continue;
                }

                Vector3 screen = worldCamera.WorldToScreenPoint(worldAnchor);
                bool inFront = screen.z >= 0f;
                bool onScreen = screen.x >= 0f && screen.x <= Screen.width &&
                                screen.y >= 0f && screen.y <= Screen.height;
                bool labelVisible = inFront && (!hideLabelsOutsideScreen || onScreen);
                SetLabelVisible(label, labelVisible);
                if (!labelVisible)
                    continue;

                Vector2 adjustedScreen = new Vector2(screen.x, screen.y) + topRightInset;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    overlayCanvasRect,
                    adjustedScreen,
                    null,
                    out Vector2 localPoint))
                {
                    label.root.anchoredPosition = localPoint;
                }
            }
        }

        private void SetLabelVisible(LabelHandle label, bool visible)
        {
            if (label == null)
                return;

            if (label.text != null)
                label.text.enabled = visible;
            if (label.background != null)
                label.background.enabled = visible && useLabelBackground;
        }

        private Camera ResolveActiveFusionCamera()
        {
            FusionModeCameraRig[] rigs = FindObjectsByType<FusionModeCameraRig>(FindObjectsSortMode.None);
            for (int i = 0; i < rigs.Length; i++)
            {
                FusionModeCameraRig rig = rigs[i];
                if (rig == null || rig.Camera == null)
                    continue;

                if (rig.gameObject.activeInHierarchy && rig.Camera.enabled)
                    return rig.Camera;
            }

            return null;
        }

        private bool TryGetBlockLabelAnchorWorld(
            RuntimeTileMeshDraggableBlock block,
            out Vector3 worldAnchor)
        {
            worldAnchor = Vector3.zero;
            if (block == null || fusionSandbox == null)
                return false;

            HashSet<Vector2Int> cells = block.GetWorldCells(
                fusionSandbox.gridSize,
                fusionSandbox.gridOrigin);
            if (cells.Count == 0)
                return false;

            bool hasCell = false;
            Vector2Int anchorCell = Vector2Int.zero;
            foreach (Vector2Int cell in cells)
            {
                if (!hasCell || cell.y > anchorCell.y || (cell.y == anchorCell.y && cell.x > anchorCell.x))
                    anchorCell = cell;

                hasCell = true;
            }

            float gridSize = Mathf.Max(0.0001f, Mathf.Abs(fusionSandbox.gridSize));
            worldAnchor = new Vector3(
                fusionSandbox.gridOrigin.x + anchorCell.x * gridSize,
                fusionSandbox.gridOrigin.y + (anchorCell.y + 1) * gridSize,
                block.transform.position.z);
            return true;
        }

        private sealed class LabelHandle
        {
            public readonly RectTransform root;
            public readonly Image background;
            public readonly TextMeshProUGUI text;

            public LabelHandle(RectTransform root, Image background, TextMeshProUGUI text)
            {
                this.root = root;
                this.background = background;
                this.text = text;
            }
        }
    }
}
