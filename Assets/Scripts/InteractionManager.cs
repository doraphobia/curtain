using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public class InteractionManager : MonoBehaviour
{
    [Header("Core References")]
    public PlayerControl playerControl;
    public CameraDragPan cameraDragPan;
    public TilePlacementGrid placementGrid;
    public TilePlacementManager tilePlacementManager;
    public TileShopPanelUI tileShopPanelUI;
    public TimeCounterUI currencyCounter;
    public CursorHazardZone[] cursorHazardZones;

    [Header("Auto Bind")]
    public bool autoBindOnAwake = true;
    public bool autoFindHazardZones = true;

    [Header("Player Control Bridge")]
    public bool applyCursorSettingsToPlayerControl = true;
    public bool hideSystemCursor = true;
    public bool confineSystemCursorToWindow = true;
    public bool clampPlayerToRoomBounds = true;

    [Header("Legacy UI Cursor")]
    public bool useLegacyUICursorImage = false;
    public bool autoFindLegacyUICursorImage = true;
    public string legacyUICursorObjectName = "drag";
    public Image legacyUICursorImage;

    [Header("Room Seed")]
    public bool seedRoomCellsFromRenderers = true;
    public bool autoFindRoomSeedRenderers = true;
    public string roomSeedNameKeyword = "Floorplan";
    public SpriteRenderer[] roomSeedRenderers;

    private RectTransform legacyUICursorRectTransform;
    private Canvas legacyUICursorCanvas;

    void Awake()
    {
        if (autoBindOnAwake)
            BindDependencies();
    }

    void Start()
    {
        if (autoBindOnAwake)
            BindDependencies();
    }

    void Update()
    {
        DeveloperModeState.TryHandleHotkey();
        UpdateLegacyUICursorVisual();
    }

    [ContextMenu("Bind Dependencies")]
    public void BindDependencies()
    {
        if (playerControl == null)
            playerControl = FindFirstObjectByType<PlayerControl>();

        if (cameraDragPan == null || !cameraDragPan.isActiveAndEnabled)
            cameraDragPan = FindPrimaryCameraDragPan();

        if (placementGrid == null)
            placementGrid = FindFirstObjectByType<TilePlacementGrid>();

        if (tilePlacementManager == null)
            tilePlacementManager = FindFirstObjectByType<TilePlacementManager>();

        if (tileShopPanelUI == null)
            tileShopPanelUI = FindFirstObjectByType<TileShopPanelUI>();

        if (currencyCounter == null)
            currencyCounter = FindFirstObjectByType<TimeCounterUI>();

        if (autoFindHazardZones && (cursorHazardZones == null || cursorHazardZones.Length == 0))
            cursorHazardZones = FindObjectsByType<CursorHazardZone>(FindObjectsSortMode.None);

        EnsureRoomSeedRenderersResolved();
        ResolveLegacyUICursorImage();

        Camera resolvedCamera = ResolveMainCamera();
        ApplyPlayerControlSettings(resolvedCamera);

        if (cameraDragPan != null && cameraDragPan.targetCamera == null)
            cameraDragPan.targetCamera = resolvedCamera;

        SeedRoomCellsIfNeeded();
        BindTilePlacement(resolvedCamera);
        BindShopPanel();
        BindHazardZones(resolvedCamera);
    }

    private void ApplyPlayerControlSettings(Camera resolvedCamera)
    {
        if (!applyCursorSettingsToPlayerControl || playerControl == null)
            return;

        if (playerControl.targetCamera == null)
            playerControl.targetCamera = resolvedCamera;

        if (placementGrid != null)
            playerControl.roomGrid = placementGrid;

        playerControl.hideSystemCursor = hideSystemCursor;
        playerControl.confineSystemCursorToWindow = confineSystemCursorToWindow;
        playerControl.clampCursorToRoom = clampPlayerToRoomBounds;
    }

    private void BindTilePlacement(Camera resolvedCamera)
    {
        if (tilePlacementManager == null)
            return;

        if (tilePlacementManager.targetCamera == null)
            tilePlacementManager.targetCamera = resolvedCamera;

        if (placementGrid != null)
            tilePlacementManager.placementGrid = placementGrid;

        if (currencyCounter != null)
            tilePlacementManager.currencySource = currencyCounter;
    }

    private void BindShopPanel()
    {
        if (tileShopPanelUI == null)
            return;

        if (currencyCounter != null)
            tileShopPanelUI.currencySource = currencyCounter;

        if (tilePlacementManager != null)
            tileShopPanelUI.placementManager = tilePlacementManager;

        tileShopPanelUI.Refresh();
    }

    private void BindHazardZones(Camera resolvedCamera)
    {
        if (cursorHazardZones == null || resolvedCamera == null)
            return;

        for (int i = 0; i < cursorHazardZones.Length; i++)
        {
            CursorHazardZone hazardZone = cursorHazardZones[i];
            if (hazardZone == null)
                continue;

            if (hazardZone.targetCamera == null)
                hazardZone.targetCamera = resolvedCamera;
        }
    }

    private Camera ResolveMainCamera()
    {
        if (playerControl != null && playerControl.targetCamera != null)
            return playerControl.targetCamera;

        if (cameraDragPan != null && cameraDragPan.targetCamera != null)
            return cameraDragPan.targetCamera;

        Camera sceneMainCamera = Camera.main;
        if (sceneMainCamera != null)
            return sceneMainCamera;

        return FindFirstObjectByType<Camera>();
    }

    private void SeedRoomCellsIfNeeded()
    {
        if (!seedRoomCellsFromRenderers || placementGrid == null)
            return;

        EnsureRoomSeedRenderersResolved();

        if (roomSeedRenderers == null)
            return;

        for (int i = 0; i < roomSeedRenderers.Length; i++)
        {
            SpriteRenderer renderer = roomSeedRenderers[i];
            if (renderer == null)
                continue;

            placementGrid.RegisterWorldBounds(renderer.bounds);
        }
    }

    private void EnsureRoomSeedRenderersResolved()
    {
        if (roomSeedRenderers != null && roomSeedRenderers.Length > 0)
            return;

        if (!autoFindRoomSeedRenderers)
            return;

        roomSeedRenderers = FindRoomSeedRenderersByName(roomSeedNameKeyword);
    }

    private CameraDragPan FindPrimaryCameraDragPan()
    {
        CameraDragPan[] pans = FindObjectsByType<CameraDragPan>(FindObjectsSortMode.None);
        if (pans == null || pans.Length == 0)
            return null;

        CameraDragPan firstEnabled = null;
        for (int i = 0; i < pans.Length; i++)
        {
            CameraDragPan candidate = pans[i];
            if (candidate == null || !candidate.isActiveAndEnabled)
                continue;

            if (candidate.targetCamera != null && candidate.targetCamera == Camera.main)
                return candidate;

            if (firstEnabled == null)
                firstEnabled = candidate;
        }

        return firstEnabled != null ? firstEnabled : pans[0];
    }

    private SpriteRenderer[] FindRoomSeedRenderersByName(string keyword)
    {
        SpriteRenderer[] allRenderers = FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None);
        if (allRenderers == null || allRenderers.Length == 0)
            return new SpriteRenderer[0];

        if (string.IsNullOrWhiteSpace(keyword))
            return allRenderers;

        System.Collections.Generic.List<SpriteRenderer> matches = new System.Collections.Generic.List<SpriteRenderer>();
        for (int i = 0; i < allRenderers.Length; i++)
        {
            SpriteRenderer renderer = allRenderers[i];
            if (renderer == null || renderer.gameObject == null)
                continue;

            if (renderer.gameObject.name.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0)
                matches.Add(renderer);
        }

        return matches.ToArray();
    }

    private void ResolveLegacyUICursorImage()
    {
        if (!useLegacyUICursorImage)
            return;

        if (legacyUICursorImage == null && autoFindLegacyUICursorImage)
            legacyUICursorImage = FindLegacyUICursorImageByName(legacyUICursorObjectName);

        if (legacyUICursorImage == null)
            return;

        legacyUICursorRectTransform = legacyUICursorImage.rectTransform;
        legacyUICursorCanvas = legacyUICursorImage.GetComponentInParent<Canvas>();
        legacyUICursorImage.raycastTarget = false;
    }

    private Image FindLegacyUICursorImageByName(string objectName)
    {
        Image[] images = FindObjectsByType<Image>(FindObjectsSortMode.None);
        if (images == null || images.Length == 0)
            return null;

        for (int i = 0; i < images.Length; i++)
        {
            Image image = images[i];
            if (image == null || image.gameObject == null)
                continue;

            if (image.gameObject.name.Equals(objectName, System.StringComparison.OrdinalIgnoreCase))
                return image;
        }

        return null;
    }

    private void UpdateLegacyUICursorVisual()
    {
        if (!useLegacyUICursorImage || legacyUICursorImage == null)
            return;

        Vector2 screenPoint;
        if (!PlayerControl.TryGetInteractionScreenPosition(out screenPoint))
            screenPoint = Input.mousePosition;

        if (legacyUICursorRectTransform == null)
            legacyUICursorRectTransform = legacyUICursorImage.rectTransform;

        RectTransform parentRect = legacyUICursorRectTransform != null ? legacyUICursorRectTransform.parent as RectTransform : null;
        if (parentRect == null || legacyUICursorRectTransform == null)
            return;

        Camera eventCamera = null;
        Camera renderCamera = ResolveMainCamera();
        if (legacyUICursorCanvas != null && legacyUICursorCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            eventCamera = legacyUICursorCanvas.worldCamera != null ? legacyUICursorCanvas.worldCamera : renderCamera;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, eventCamera, out Vector2 localPoint))
            legacyUICursorRectTransform.anchoredPosition = localPoint;
    }
}
