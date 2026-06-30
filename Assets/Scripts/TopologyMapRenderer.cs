using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum TopologyMapRenderMode
{
    EntireBuilding,
    LocalAroundPlayer
}

[DisallowMultipleComponent]
public class TopologyMapRenderer : MonoBehaviour
{
    private sealed class CellView
    {
        public Vector2Int cell;
        public RectTransform rectTransform;
        public Image image;
        public CanvasGroup canvasGroup;
        public Color currentColor;
        public float appearProgress;
    }

    [Header("Topology Source")]
    public TopologyMapDataProvider dataProvider;
    public TilePlacementGrid placementGrid;
    public PlayerControl playerControl;
    public bool autoBindReferences = true;
    public bool createProviderIfMissing = true;

    [Header("Rendering")]
    public Canvas targetCanvas;
    public RectTransform mapRoot;
    public bool createCanvasIfMissing = true;
    public bool visible = true;
    public bool rebuildOnEnable = true;

    [Header("Display Mode")]
    public TopologyMapRenderMode renderMode = TopologyMapRenderMode.EntireBuilding;
    [Min(0.5f)]
    public float localZoomDistance = 5f;

    [Header("Layout")]
    public Vector2 defaultMapSize = new Vector2(220f, 180f);
    public Vector2 defaultAnchoredPosition = new Vector2(-32f, -32f);
    [Min(0f)]
    public float padding = 18f;
    [Min(0f)]
    public float cellSpacing = 1.5f;
    public Vector2 mapCenterOffset = Vector2.zero;

    [Header("Colors")]
    public Color backgroundColor = new Color(0.68f, 0.68f, 0.68f, 0.82f);
    public Color roomColor = Color.white;
    public Color currentRoomColor = new Color(1f, 0.94f, 0.42f, 1f);
    public Color frameColor = Color.black;

    [Header("Player Marker")]
    public bool showPlayerMarker = true;
    public Color playerMarkerColor = Color.black;
    [Min(1f)]
    public float playerMarkerSize = 9f;

    [Header("Highlight")]
    public bool highlightCurrentRoom = true;

    [Header("Animation")]
    public bool useUnscaledTime = false;
    [Min(0f)]
    public float roomAppearDuration = 0.18f;
    [Min(0.0001f)]
    public float highlightFadeDuration = 0.16f;
    [Min(0.0001f)]
    public float markerSmoothTime = 0.08f;
    [Min(0.0001f)]
    public float topologyScaleSmoothTime = 0.2f;
    [Min(0.0001f)]
    public float localScrollSmoothTime = 0.12f;

    [Header("Scale")]
    [Min(0.1f)]
    public float minimumCellPixelSize = 2f;
    [Min(1f)]
    public float localMaximumCellPixelSize = 48f;

    [Header("Frame")]
    public bool showBackground = true;
    public bool showFrame = true;
    [Min(0.5f)]
    public float frameLineThickness = 1.5f;
    [Min(2f)]
    public float frameCornerLength = 26f;
    public bool showFullBorder = true;
    public bool showCornerMarkers = true;

    [Header("Runtime")]
    public bool autoRefreshWhenTopologyChanges = true;

    [Header("Performance")]
    [Min(0)]
    public int initialPoolSize = 32;

    [Header("Debug")]
    public bool showDebugOverlay = false;
    public bool createDebugTextIfMissing = false;
    public bool logRebuilds = false;
    public TextMeshProUGUI debugText;

    private readonly Dictionary<Vector2Int, CellView> activeCells = new Dictionary<Vector2Int, CellView>();
    private readonly Queue<CellView> pooledCells = new Queue<CellView>();
    private readonly HashSet<Vector2Int> visibleCellSet = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> staleCells = new List<Vector2Int>();

    private RectTransform backgroundRect;
    private Image backgroundImage;
    private RectTransform cellRoot;
    private RectTransform markerRect;
    private Image markerImage;
    private RectTransform frameRoot;
    private readonly List<Image> frameLines = new List<Image>();
    private CanvasGroup mapCanvasGroup;

    private TopologyMapDataProvider subscribedProvider;
    private Texture2D runtimeCircleTexture;
    private Sprite runtimeCircleSprite;

    private bool topologyDirty = true;
    private bool frameDirty = true;
    private bool hasLayoutState;
    private bool hasMarkerPosition;
    private int observedTopologyVersion = int.MinValue;
    private TopologyMapRenderMode observedRenderMode;
    private Vector2Int observedLocalAnchorCell;
    private bool hasObservedLocalAnchorCell;
    private Vector2 targetLogicalCenter;
    private Vector2 displayedLogicalCenter;
    private Vector2 displayedCenterVelocity;
    private float targetCellStep = 1f;
    private float displayedCellStep = 1f;
    private float displayedCellStepVelocity;
    private Vector2 markerVelocity;
    private Vector2 markerPosition;
    private Vector2 lastMapSize;

    void Awake()
    {
        ResolveReferences();
        EnsurePresentation();
        WarmPool();
    }

    void OnEnable()
    {
        ResolveReferences();
        EnsurePresentation();
        SubscribeToProvider();

        if (rebuildOnEnable)
            RequestTopologyRebuild();
    }

    void OnDisable()
    {
        UnsubscribeFromProvider();
    }

    void OnDestroy()
    {
        DestroyRuntimeCircleSprite();
    }

    void OnValidate()
    {
        padding = Mathf.Max(0f, padding);
        cellSpacing = Mathf.Max(0f, cellSpacing);
        localZoomDistance = Mathf.Max(0.5f, localZoomDistance);
        roomAppearDuration = Mathf.Max(0f, roomAppearDuration);
        highlightFadeDuration = Mathf.Max(0.0001f, highlightFadeDuration);
        markerSmoothTime = Mathf.Max(0.0001f, markerSmoothTime);
        topologyScaleSmoothTime = Mathf.Max(0.0001f, topologyScaleSmoothTime);
        localScrollSmoothTime = Mathf.Max(0.0001f, localScrollSmoothTime);
        minimumCellPixelSize = Mathf.Max(0.1f, minimumCellPixelSize);
        localMaximumCellPixelSize = Mathf.Max(1f, localMaximumCellPixelSize);
        frameLineThickness = Mathf.Max(0.5f, frameLineThickness);
        frameCornerLength = Mathf.Max(2f, frameCornerLength);

        frameDirty = true;
        topologyDirty = true;
    }

    void Update()
    {
        ResolveReferences();
        EnsurePresentation();
        SubscribeToProvider();
        ApplyVisibility();

        if (!visible || mapRoot == null)
            return;

        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        Vector2 mapSize = mapRoot.rect.size;
        if (mapSize != lastMapSize)
        {
            lastMapSize = mapSize;
            frameDirty = true;
        }

        if (dataProvider != null && dataProvider.TopologyVersion != observedTopologyVersion)
            topologyDirty = true;

        bool hasCurrentRoom = TryGetCurrentPlayerRoom(out Vector2Int currentRoom);
        Vector2Int localAnchorCell = hasCurrentRoom ? currentRoom : observedLocalAnchorCell;
        bool localAnchorChanged = renderMode == TopologyMapRenderMode.LocalAroundPlayer &&
            (!hasObservedLocalAnchorCell || localAnchorCell != observedLocalAnchorCell);

        if (renderMode != observedRenderMode || topologyDirty || localAnchorChanged)
            RebuildCellViews(hasCurrentRoom, currentRoom);

        UpdateTargetLayout(hasCurrentRoom, currentRoom);
        UpdateLayoutState(deltaTime);
        UpdateCellViews(deltaTime, hasCurrentRoom, currentRoom);
        UpdatePlayerMarker(deltaTime);
        UpdateFrame();
        UpdateDebugOverlay(hasCurrentRoom, currentRoom);
    }

    void OnRectTransformDimensionsChange()
    {
        frameDirty = true;
    }

    public void RequestTopologyRebuild()
    {
        topologyDirty = true;
    }

    public void SetRenderMode(TopologyMapRenderMode mode)
    {
        if (renderMode == mode)
            return;

        renderMode = mode;
        topologyDirty = true;
    }

    private void ResolveReferences()
    {
        if (!autoBindReferences)
            return;

        if (placementGrid == null)
            placementGrid = dataProvider != null && dataProvider.Source != null
                ? dataProvider.Source
                : FindFirstObjectByType<TilePlacementGrid>();

        if (dataProvider == null)
        {
            dataProvider = GetComponent<TopologyMapDataProvider>();
            if (dataProvider == null && createProviderIfMissing)
                dataProvider = gameObject.AddComponent<TopologyMapDataProvider>();
        }

        if (dataProvider != null && placementGrid != null && dataProvider.Source != placementGrid)
            dataProvider.Bind(placementGrid);

        if (playerControl == null)
            playerControl = PlayerControl.Active != null ? PlayerControl.Active : FindFirstObjectByType<PlayerControl>();

        if (targetCanvas == null)
            targetCanvas = GetComponentInParent<Canvas>();
    }

    private void SubscribeToProvider()
    {
        if (subscribedProvider == dataProvider)
            return;

        UnsubscribeFromProvider();
        subscribedProvider = dataProvider;
        if (subscribedProvider != null)
            subscribedProvider.TopologyChanged += HandleProviderTopologyChanged;
    }

    private void UnsubscribeFromProvider()
    {
        if (subscribedProvider != null)
            subscribedProvider.TopologyChanged -= HandleProviderTopologyChanged;

        subscribedProvider = null;
    }

    private void HandleProviderTopologyChanged(TopologyMapDataProvider provider)
    {
        if (autoRefreshWhenTopologyChanges)
            topologyDirty = true;
    }

    private void EnsurePresentation()
    {
        EnsureMapRoot();
        if (mapRoot == null)
            return;

        mapCanvasGroup = mapRoot.GetComponent<CanvasGroup>();
        if (mapCanvasGroup == null)
            mapCanvasGroup = mapRoot.gameObject.AddComponent<CanvasGroup>();

        EnsureBackground();
        EnsureCellRoot();
        EnsureMarker();
        EnsureFrameRoot();
        EnsureDebugText();
        ApplyHierarchyOrder();
    }

    private void EnsureMapRoot()
    {
        if (mapRoot != null)
            return;

        RectTransform ownRect = transform as RectTransform;
        if (ownRect != null)
        {
            mapRoot = ownRect;
            return;
        }

        Canvas canvas = ResolveCanvasForGeneratedMap();
        if (canvas == null)
            return;

        GameObject mapObject = new GameObject("Topology Map", typeof(RectTransform));
        mapObject.transform.SetParent(canvas.transform, false);

        mapRoot = mapObject.GetComponent<RectTransform>();
        mapRoot.anchorMin = new Vector2(1f, 1f);
        mapRoot.anchorMax = new Vector2(1f, 1f);
        mapRoot.pivot = new Vector2(1f, 1f);
        mapRoot.sizeDelta = defaultMapSize;
        mapRoot.anchoredPosition = defaultAnchoredPosition;
    }

    private Canvas ResolveCanvasForGeneratedMap()
    {
        if (targetCanvas != null)
            return targetCanvas;

        targetCanvas = FindFirstObjectByType<Canvas>();
        if (targetCanvas != null || !createCanvasIfMissing)
            return targetCanvas;

        GameObject canvasObject = new GameObject(
            "Topology Map Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));

        targetCanvas = canvasObject.GetComponent<Canvas>();
        targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        targetCanvas.sortingOrder = 900;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return targetCanvas;
    }

    private void EnsureBackground()
    {
        if (backgroundRect != null && backgroundImage != null)
            return;

        GameObject backgroundObject = CreateUIObject("Topology Map Background", mapRoot);
        backgroundRect = backgroundObject.GetComponent<RectTransform>();
        backgroundImage = backgroundObject.AddComponent<Image>();
        backgroundImage.raycastTarget = false;
    }

    private void EnsureCellRoot()
    {
        if (cellRoot != null)
            return;

        GameObject rootObject = CreateUIObject("Topology Cells", mapRoot);
        cellRoot = rootObject.GetComponent<RectTransform>();
        StretchToParent(cellRoot);
    }

    private void EnsureMarker()
    {
        if (markerRect != null && markerImage != null)
            return;

        GameObject markerObject = CreateUIObject("Topology Player Marker", mapRoot);
        markerRect = markerObject.GetComponent<RectTransform>();
        markerRect.anchorMin = new Vector2(0.5f, 0.5f);
        markerRect.anchorMax = new Vector2(0.5f, 0.5f);
        markerRect.pivot = new Vector2(0.5f, 0.5f);

        markerImage = markerObject.AddComponent<Image>();
        markerImage.sprite = GetRuntimeCircleSprite();
        markerImage.raycastTarget = false;
    }

    private void EnsureFrameRoot()
    {
        if (frameRoot != null)
            return;

        GameObject rootObject = CreateUIObject("Topology Frame", mapRoot);
        frameRoot = rootObject.GetComponent<RectTransform>();
        StretchToParent(frameRoot);
    }

    private void EnsureDebugText()
    {
        if (!createDebugTextIfMissing || debugText != null || mapRoot == null)
            return;

        GameObject textObject = CreateUIObject("Topology Debug Text", mapRoot);
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0f, 1f);
        textRect.anchorMax = new Vector2(0f, 1f);
        textRect.pivot = new Vector2(0f, 1f);
        textRect.anchoredPosition = new Vector2(8f, -8f);
        textRect.sizeDelta = new Vector2(180f, 80f);

        debugText = textObject.AddComponent<TextMeshProUGUI>();
        debugText.fontSize = 12f;
        debugText.color = Color.black;
        debugText.raycastTarget = false;
    }

    private void ApplyHierarchyOrder()
    {
        if (backgroundRect != null)
            backgroundRect.SetAsFirstSibling();
        if (cellRoot != null)
            cellRoot.SetSiblingIndex(Mathf.Min(1, mapRoot.childCount - 1));
        if (markerRect != null)
            markerRect.SetAsLastSibling();
        if (frameRoot != null)
            frameRoot.SetAsLastSibling();
        if (debugText != null)
            debugText.transform.SetAsLastSibling();
    }

    private void ApplyVisibility()
    {
        if (mapCanvasGroup == null)
            return;

        mapCanvasGroup.alpha = visible ? 1f : 0f;
        mapCanvasGroup.interactable = false;
        mapCanvasGroup.blocksRaycasts = false;
    }

    private void WarmPool()
    {
        if (cellRoot == null)
            return;

        while (pooledCells.Count < initialPoolSize)
        {
            CellView view = CreateCellView();
            view.rectTransform.gameObject.SetActive(false);
            pooledCells.Enqueue(view);
        }
    }

    private void RebuildCellViews(bool hasCurrentRoom, Vector2Int currentRoom)
    {
        if (dataProvider == null || !dataProvider.HasTopology)
        {
            ReleaseAllCells();
            observedTopologyVersion = dataProvider != null ? dataProvider.TopologyVersion : int.MinValue;
            observedRenderMode = renderMode;
            topologyDirty = false;
            return;
        }

        visibleCellSet.Clear();
        IReadOnlyList<Vector2Int> cells = dataProvider.RoomCells;

        if (renderMode == TopologyMapRenderMode.LocalAroundPlayer)
        {
            Vector2Int anchorCell = hasCurrentRoom ? currentRoom : GetFallbackLocalAnchor();
            int radius = Mathf.CeilToInt(localZoomDistance) + 1;
            for (int i = 0; i < cells.Count; i++)
            {
                Vector2Int cell = cells[i];
                if (Mathf.Abs(cell.x - anchorCell.x) <= radius &&
                    Mathf.Abs(cell.y - anchorCell.y) <= radius)
                {
                    visibleCellSet.Add(cell);
                }
            }

            observedLocalAnchorCell = anchorCell;
            hasObservedLocalAnchorCell = true;
        }
        else
        {
            for (int i = 0; i < cells.Count; i++)
                visibleCellSet.Add(cells[i]);

            hasObservedLocalAnchorCell = false;
        }

        staleCells.Clear();
        foreach (KeyValuePair<Vector2Int, CellView> pair in activeCells)
        {
            if (!visibleCellSet.Contains(pair.Key))
                staleCells.Add(pair.Key);
        }

        for (int i = 0; i < staleCells.Count; i++)
            ReleaseCell(staleCells[i]);

        foreach (Vector2Int cell in visibleCellSet)
        {
            if (!activeCells.ContainsKey(cell))
                activeCells[cell] = AcquireCell(cell);
        }

        observedTopologyVersion = dataProvider.TopologyVersion;
        observedRenderMode = renderMode;
        topologyDirty = false;

        if (logRebuilds)
            Debug.Log("[TopologyMapRenderer] Rebuilt " + activeCells.Count + " visible cell(s) in " + renderMode + " mode.", this);
    }

    private Vector2Int GetFallbackLocalAnchor()
    {
        if (hasObservedLocalAnchorCell)
            return observedLocalAnchorCell;

        if (dataProvider != null && dataProvider.HasTopology)
        {
            RectInt bounds = dataProvider.RoomCellBounds;
            return new Vector2Int(
                Mathf.RoundToInt(bounds.xMin + (bounds.width - 1) * 0.5f),
                Mathf.RoundToInt(bounds.yMin + (bounds.height - 1) * 0.5f));
        }

        return Vector2Int.zero;
    }

    private CellView AcquireCell(Vector2Int cell)
    {
        CellView view = pooledCells.Count > 0 ? pooledCells.Dequeue() : CreateCellView();
        view.cell = cell;
        view.appearProgress = 0f;
        view.currentColor = roomColor;
        view.image.color = roomColor;
        view.canvasGroup.alpha = 0f;
        view.rectTransform.localScale = Vector3.zero;
        view.rectTransform.gameObject.SetActive(true);
        view.rectTransform.SetParent(cellRoot, false);
        return view;
    }

    private void ReleaseCell(Vector2Int cell)
    {
        if (!activeCells.TryGetValue(cell, out CellView view))
            return;

        activeCells.Remove(cell);
        view.rectTransform.gameObject.SetActive(false);
        pooledCells.Enqueue(view);
    }

    private void ReleaseAllCells()
    {
        staleCells.Clear();
        foreach (KeyValuePair<Vector2Int, CellView> pair in activeCells)
            staleCells.Add(pair.Key);

        for (int i = 0; i < staleCells.Count; i++)
            ReleaseCell(staleCells[i]);
    }

    private CellView CreateCellView()
    {
        GameObject cellObject = CreateUIObject("Topology Room Cell", cellRoot != null ? cellRoot : mapRoot);
        RectTransform rect = cellObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        Image image = cellObject.AddComponent<Image>();
        image.raycastTarget = false;

        CanvasGroup canvasGroup = cellObject.AddComponent<CanvasGroup>();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        return new CellView
        {
            rectTransform = rect,
            image = image,
            canvasGroup = canvasGroup,
            currentColor = roomColor
        };
    }

    private void UpdateTargetLayout(bool hasCurrentRoom, Vector2Int currentRoom)
    {
        Vector2 contentSize = GetContentSize();
        if (dataProvider == null || !dataProvider.HasTopology)
        {
            targetLogicalCenter = Vector2.zero;
            targetCellStep = Mathf.Max(minimumCellPixelSize, Mathf.Min(contentSize.x, contentSize.y));
            return;
        }

        if (renderMode == TopologyMapRenderMode.LocalAroundPlayer)
        {
            if (!TryGetPlayerLogicalPosition(out targetLogicalCenter))
                targetLogicalCenter = hasCurrentRoom ? (Vector2)currentRoom : GetBoundsCenter(dataProvider.RoomCellBounds);

            float diameter = Mathf.Max(1f, localZoomDistance * 2f + 1f);
            targetCellStep = Mathf.Min(contentSize.x, contentSize.y) / diameter;
            targetCellStep = Mathf.Min(targetCellStep, localMaximumCellPixelSize);
            targetCellStep = Mathf.Max(minimumCellPixelSize, targetCellStep);
        }
        else
        {
            RectInt bounds = dataProvider.RoomCellBounds;
            targetLogicalCenter = GetBoundsCenter(bounds);
            float widthStep = contentSize.x / Mathf.Max(1, bounds.width);
            float heightStep = contentSize.y / Mathf.Max(1, bounds.height);
            targetCellStep = Mathf.Max(0.1f, Mathf.Min(widthStep, heightStep));
        }
    }

    private void UpdateLayoutState(float deltaTime)
    {
        if (!hasLayoutState || deltaTime <= 0f)
        {
            displayedLogicalCenter = targetLogicalCenter;
            displayedCellStep = targetCellStep;
            displayedCenterVelocity = Vector2.zero;
            displayedCellStepVelocity = 0f;
            hasLayoutState = true;
            return;
        }

        float centerSmoothTime = renderMode == TopologyMapRenderMode.LocalAroundPlayer
            ? localScrollSmoothTime
            : topologyScaleSmoothTime;

        displayedLogicalCenter = Vector2.SmoothDamp(
            displayedLogicalCenter,
            targetLogicalCenter,
            ref displayedCenterVelocity,
            Mathf.Max(0.0001f, centerSmoothTime),
            Mathf.Infinity,
            deltaTime);

        displayedCellStep = Mathf.SmoothDamp(
            displayedCellStep,
            targetCellStep,
            ref displayedCellStepVelocity,
            Mathf.Max(0.0001f, topologyScaleSmoothTime),
            Mathf.Infinity,
            deltaTime);
    }

    private void UpdateCellViews(float deltaTime, bool hasCurrentRoom, Vector2Int currentRoom)
    {
        float appliedSpacing = Mathf.Min(cellSpacing, displayedCellStep * 0.4f);
        float renderSize = Mathf.Max(0.1f, displayedCellStep - appliedSpacing);
        float colorT = 1f - Mathf.Exp(-deltaTime / Mathf.Max(0.0001f, highlightFadeDuration));
        float appearStep = roomAppearDuration <= 0.0001f
            ? 1f
            : deltaTime / Mathf.Max(0.0001f, roomAppearDuration);

        foreach (KeyValuePair<Vector2Int, CellView> pair in activeCells)
        {
            CellView view = pair.Value;
            Vector2 position = ProjectLogicalPosition(new Vector2(view.cell.x, view.cell.y));
            view.rectTransform.anchoredPosition = position;
            view.rectTransform.sizeDelta = new Vector2(renderSize, renderSize);

            bool isCurrentRoom = highlightCurrentRoom && hasCurrentRoom && view.cell == currentRoom;
            Color targetColor = isCurrentRoom ? currentRoomColor : roomColor;
            view.currentColor = Color.Lerp(view.currentColor, targetColor, colorT);
            view.image.color = view.currentColor;

            view.appearProgress = Mathf.Clamp01(view.appearProgress + appearStep);
            float appear = Mathf.SmoothStep(0f, 1f, view.appearProgress);
            view.canvasGroup.alpha = appear;
            view.rectTransform.localScale = new Vector3(appear, appear, 1f);
        }
    }

    private void UpdatePlayerMarker(float deltaTime)
    {
        if (markerRect == null || markerImage == null)
            return;

        if (!showPlayerMarker || !TryGetPlayerLogicalPosition(out Vector2 playerLogical))
        {
            markerRect.gameObject.SetActive(false);
            hasMarkerPosition = false;
            return;
        }

        markerRect.gameObject.SetActive(true);
        markerImage.color = playerMarkerColor;
        markerImage.sprite = GetRuntimeCircleSprite();
        markerRect.sizeDelta = new Vector2(playerMarkerSize, playerMarkerSize);

        Vector2 targetPosition = ProjectLogicalPosition(playerLogical);
        if (!hasMarkerPosition || deltaTime <= 0f)
        {
            markerPosition = targetPosition;
            markerVelocity = Vector2.zero;
            hasMarkerPosition = true;
        }
        else
        {
            markerPosition = Vector2.SmoothDamp(
                markerPosition,
                targetPosition,
                ref markerVelocity,
                markerSmoothTime,
                Mathf.Infinity,
                deltaTime);
        }

        markerRect.anchoredPosition = markerPosition;
    }

    private void UpdateFrame()
    {
        if (!frameDirty || mapRoot == null)
            return;

        frameDirty = false;

        if (backgroundRect != null && backgroundImage != null)
        {
            StretchToParent(backgroundRect);
            backgroundImage.enabled = showBackground;
            backgroundImage.color = backgroundColor;
        }

        if (frameRoot == null)
            return;

        frameRoot.gameObject.SetActive(showFrame);
        if (!showFrame)
            return;

        StretchToParent(frameRoot);
        EnsureFrameLineCount(12);

        Color color = frameColor;
        float t = frameLineThickness;
        float length = Mathf.Max(frameCornerLength, t);

        SetLine(frameLines[0], new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, t), Vector2.zero, showFullBorder);
        SetLine(frameLines[1], new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, t), Vector2.zero, showFullBorder);
        SetLine(frameLines[2], new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(t, 0f), Vector2.zero, showFullBorder);
        SetLine(frameLines[3], new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(t, 0f), Vector2.zero, showFullBorder);

        SetLine(frameLines[4], new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(length, t), new Vector2(length * 0.5f, 0f), showCornerMarkers);
        SetLine(frameLines[5], new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(t, length), new Vector2(0f, -length * 0.5f), showCornerMarkers);
        SetLine(frameLines[6], new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(length, t), new Vector2(-length * 0.5f, 0f), showCornerMarkers);
        SetLine(frameLines[7], new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(t, length), new Vector2(0f, -length * 0.5f), showCornerMarkers);
        SetLine(frameLines[8], new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(length, t), new Vector2(length * 0.5f, 0f), showCornerMarkers);
        SetLine(frameLines[9], new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(t, length), new Vector2(0f, length * 0.5f), showCornerMarkers);
        SetLine(frameLines[10], new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(length, t), new Vector2(-length * 0.5f, 0f), showCornerMarkers);
        SetLine(frameLines[11], new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(t, length), new Vector2(0f, length * 0.5f), showCornerMarkers);

        for (int i = 0; i < frameLines.Count; i++)
            frameLines[i].color = color;
    }

    private void UpdateDebugOverlay(bool hasCurrentRoom, Vector2Int currentRoom)
    {
        if (debugText == null)
            return;

        debugText.gameObject.SetActive(showDebugOverlay);
        if (!showDebugOverlay)
            return;

        string boundsText = "none";
        if (dataProvider != null && dataProvider.HasTopology)
        {
            RectInt bounds = dataProvider.RoomCellBounds;
            boundsText = bounds.xMin + "," + bounds.yMin + " " + bounds.width + "x" + bounds.height;
        }

        string currentText = hasCurrentRoom ? currentRoom.x + "," + currentRoom.y : "none";
        int count = dataProvider != null ? dataProvider.RoomCellCount : 0;
        debugText.text =
            "Mode: " + renderMode +
            "\nCells: " + count +
            "\nBounds: " + boundsText +
            "\nCurrent: " + currentText;
    }

    private bool TryGetCurrentPlayerRoom(out Vector2Int cell)
    {
        if (dataProvider == null || !TryGetPlayerWorldPosition(out Vector3 worldPosition))
        {
            cell = default(Vector2Int);
            return false;
        }

        return dataProvider.TryGetRoomCell(worldPosition, out cell);
    }

    private bool TryGetPlayerLogicalPosition(out Vector2 logicalPosition)
    {
        logicalPosition = default(Vector2);
        if (dataProvider == null || !TryGetPlayerWorldPosition(out Vector3 worldPosition))
            return false;

        if (!dataProvider.TryGetRoomCell(worldPosition, out Vector2Int roomCell))
            return false;

        return dataProvider.TryGetWorldLogicalPosition(worldPosition, out logicalPosition, out roomCell);
    }

    private bool TryGetPlayerWorldPosition(out Vector3 worldPosition)
    {
        if (playerControl != null && playerControl.HasPlayerWorldPosition)
        {
            worldPosition = playerControl.PlayerWorldPosition;
            return true;
        }

        return PlayerControl.TryGetPlayerWorldPosition(out worldPosition);
    }

    private Vector2 GetContentSize()
    {
        if (mapRoot == null)
            return Vector2.one;

        Rect rect = mapRoot.rect;
        return new Vector2(
            Mathf.Max(1f, Mathf.Abs(rect.width) - padding * 2f),
            Mathf.Max(1f, Mathf.Abs(rect.height) - padding * 2f));
    }

    private Vector2 ProjectLogicalPosition(Vector2 logicalPosition)
    {
        Vector2 offset = logicalPosition - displayedLogicalCenter;
        return offset * displayedCellStep + mapCenterOffset;
    }

    private static Vector2 GetBoundsCenter(RectInt bounds)
    {
        return new Vector2(
            bounds.xMin + (bounds.width - 1) * 0.5f,
            bounds.yMin + (bounds.height - 1) * 0.5f);
    }

    private static GameObject CreateUIObject(string objectName, Transform parent)
    {
        GameObject gameObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer));
        if (parent != null)
            gameObject.transform.SetParent(parent, false);

        return gameObject;
    }

    private static void StretchToParent(RectTransform rectTransform)
    {
        if (rectTransform == null)
            return;

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.localScale = Vector3.one;
    }

    private void EnsureFrameLineCount(int count)
    {
        while (frameLines.Count < count)
        {
            GameObject lineObject = CreateUIObject("Topology Frame Line", frameRoot);
            Image image = lineObject.AddComponent<Image>();
            image.raycastTarget = false;
            frameLines.Add(image);
        }
    }

    private static void SetLine(
        Image image,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 sizeDelta,
        Vector2 anchoredPosition,
        bool enabled)
    {
        if (image == null)
            return;

        image.enabled = enabled;
        RectTransform rectTransform = image.rectTransform;
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = pivot;
        rectTransform.sizeDelta = sizeDelta;
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.localScale = Vector3.one;
    }

    private Sprite GetRuntimeCircleSprite()
    {
        if (runtimeCircleSprite != null)
            return runtimeCircleSprite;

        const int size = 64;
        runtimeCircleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        runtimeCircleTexture.name = "Topology Player Marker";
        runtimeCircleTexture.hideFlags = HideFlags.HideAndDontSave;

        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.45f;
        float feather = 1.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01((radius - distance) / feather);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        runtimeCircleTexture.SetPixels(pixels);
        runtimeCircleTexture.Apply(false, true);

        runtimeCircleSprite = Sprite.Create(
            runtimeCircleTexture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size);
        runtimeCircleSprite.name = "Topology Player Marker";
        runtimeCircleSprite.hideFlags = HideFlags.HideAndDontSave;
        return runtimeCircleSprite;
    }

    private void DestroyRuntimeCircleSprite()
    {
        if (runtimeCircleSprite != null)
        {
            if (Application.isPlaying)
                Destroy(runtimeCircleSprite);
            else
                DestroyImmediate(runtimeCircleSprite);
        }

        if (runtimeCircleTexture != null)
        {
            if (Application.isPlaying)
                Destroy(runtimeCircleTexture);
            else
                DestroyImmediate(runtimeCircleTexture);
        }
    }
}
