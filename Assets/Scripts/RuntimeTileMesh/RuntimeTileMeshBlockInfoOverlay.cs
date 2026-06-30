using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DuoCurtain.RuntimeTileMesh
{
    [DisallowMultipleComponent]
    public sealed class RuntimeTileMeshBlockInfoOverlay : MonoBehaviour
    {
        public enum TabDisplayMode
        {
            ToggleOnPress,
            HoldToShow
        }

        [Header("Input")]
        public KeyCode displayKey = KeyCode.Tab;
        public TabDisplayMode displayMode = TabDisplayMode.ToggleOnPress;
        public bool startVisible = false;

        [Header("References")]
        public RuntimeTileMeshFusionSandbox fusionSandbox;
        public Camera worldCamera;
        public TMP_FontAsset labelFont;
        public string resourcesFontPath = "Fonts/Bayon-Regular SDF";

        [Header("Figma Typography")]
        [Min(1f)]
        public float fontSize = 30f;
        [Min(1f)]
        public float lineHeight = 30f;
        [Range(-100f, 100f)]
        public float letterSpacingPercent = -5f;
        public Color textColor = Color.black;
        public Vector2 labelSize = new Vector2(82f, 51f);
        public Vector2 topRightInset = new Vector2(-8f, -8f);
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

        private readonly Dictionary<RuntimeTileMeshDraggableBlock, TextMeshProUGUI> labels =
            new Dictionary<RuntimeTileMeshDraggableBlock, TextMeshProUGUI>();
        private readonly List<RuntimeTileMeshDraggableBlock> staleBlocks =
            new List<RuntimeTileMeshDraggableBlock>();

        private Canvas overlayCanvas;
        private RectTransform overlayCanvasRect;
        private RectTransform labelRoot;
        private float nextRefreshTime;
        private bool isVisible;

        public bool IsVisible => isVisible;

        void Awake()
        {
            ResolveReferences();
            EnsureOverlayCanvas();
            SetVisible(startVisible, true);
        }

        void Update()
        {
            ResolveReferences();
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
                SetVisible(Input.GetKey(displayKey), false);
                return;
            }

            if (Input.GetKeyDown(displayKey))
                SetVisible(!isVisible, false);
        }

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

            if (fusionSandbox != null && fusionSandbox.worldCamera != null)
                worldCamera = fusionSandbox.worldCamera;
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
                if (!labels.TryGetValue(block, out TextMeshProUGUI label) || label == null)
                {
                    label = CreateLabel(block);
                    labels[block] = label;
                }

                ApplyLabelTypography(label);
                label.text = FormatWithLineHeight(GetDescription(block));
            }

            staleBlocks.Clear();
            foreach (KeyValuePair<RuntimeTileMeshDraggableBlock, TextMeshProUGUI> pair in labels)
            {
                if (pair.Key == null || !activeSet.Contains(pair.Key))
                    staleBlocks.Add(pair.Key);
            }

            for (int i = 0; i < staleBlocks.Count; i++)
            {
                RuntimeTileMeshDraggableBlock staleBlock = staleBlocks[i];
                if (labels.TryGetValue(staleBlock, out TextMeshProUGUI label) && label != null)
                    Destroy(label.gameObject);
                labels.Remove(staleBlock);
            }
        }

        private TextMeshProUGUI CreateLabel(RuntimeTileMeshDraggableBlock block)
        {
            GameObject labelObject = new GameObject(
                "Block Info - " + (block != null ? block.name : "Missing"),
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(labelRoot, false);

            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = labelSize;

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.raycastTarget = false;
            ApplyLabelTypography(label);
            return label;
        }

        private void ApplyLabelTypography(TextMeshProUGUI label)
        {
            if (label == null)
                return;

            if (labelFont != null)
                label.font = labelFont;
            label.fontStyle = FontStyles.Normal;
            label.fontSize = fontSize;
            label.characterSpacing = letterSpacingPercent;
            label.lineSpacing = 0f;
            label.color = textColor;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
            label.richText = true;
            label.margin = Vector4.zero;

            RectTransform rect = label.rectTransform;
            rect.sizeDelta = labelSize;
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

            foreach (KeyValuePair<RuntimeTileMeshDraggableBlock, TextMeshProUGUI> pair in labels)
            {
                RuntimeTileMeshDraggableBlock block = pair.Key;
                TextMeshProUGUI label = pair.Value;
                if (block == null || label == null)
                    continue;

                if (!TryGetBlockTopRightWorld(block, out Vector3 worldCorner))
                {
                    label.enabled = false;
                    continue;
                }

                Vector3 screen = worldCamera.WorldToScreenPoint(worldCorner);
                bool inFront = screen.z >= 0f;
                bool onScreen = screen.x >= 0f && screen.x <= Screen.width &&
                                screen.y >= 0f && screen.y <= Screen.height;
                label.enabled = inFront && (!hideLabelsOutsideScreen || onScreen);
                if (!label.enabled)
                    continue;

                Vector2 adjustedScreen = new Vector2(screen.x, screen.y) + topRightInset;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    overlayCanvasRect,
                    adjustedScreen,
                    null,
                    out Vector2 localPoint))
                {
                    label.rectTransform.anchoredPosition = localPoint;
                }
            }
        }

        private bool TryGetBlockTopRightWorld(
            RuntimeTileMeshDraggableBlock block,
            out Vector3 worldCorner)
        {
            worldCorner = Vector3.zero;
            if (block == null || fusionSandbox == null)
                return false;

            HashSet<Vector2Int> cells = block.GetWorldCells(
                fusionSandbox.gridSize,
                fusionSandbox.gridOrigin);
            if (cells.Count == 0)
                return false;

            bool hasCell = false;
            Vector2Int maximum = Vector2Int.zero;
            foreach (Vector2Int cell in cells)
            {
                maximum = hasCell ? Vector2Int.Max(maximum, cell) : cell;
                hasCell = true;
            }

            float gridSize = Mathf.Max(0.0001f, Mathf.Abs(fusionSandbox.gridSize));
            worldCorner = new Vector3(
                fusionSandbox.gridOrigin.x + (maximum.x + 1) * gridSize,
                fusionSandbox.gridOrigin.y + (maximum.y + 1) * gridSize,
                block.transform.position.z);
            return true;
        }
    }
}
