using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using TMPro;
using UnityEngine.UI;
using DuoCurtain.GameplayVisuals;
using DuoCurtain.RuntimeTileMesh;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public class PlayerControl : MonoBehaviour
{
    public enum CursorMovementMode
    {
        DirectScreenPosition,
        PointerOffsetVelocity
    }

    public enum CursorVisualMode
    {
        ImageSprite,
        Animator
    }

    public static PlayerControl Active { get; private set; }

    [Header("References")]
    public Camera targetCamera;
    public TilePlacementGrid roomGrid;
    public RuntimeTileMeshFusionSandbox runtimeTileWalkableArea;
    public bool preferRuntimeTileWalkableArea = true;
    [FormerlySerializedAs("uiCursorImage")]
    public Image playerImage;
    [FormerlySerializedAs("uiCursorObjectName")]
    public string playerObjectName = "Player";

    [Header("System Cursor")]
    public bool hideSystemCursor = true;
    public bool confineSystemCursorToWindow = true;
    public bool restoreSystemCursorOnDisable = true;

    [Header("Player Point")]
    public bool playerInputEnabled = true;
    public bool clampCursorToRoom = true;
    public bool allowOutdoorMovementFromRuntimeRooms = true;
    public bool freezeWorldCursorWhenPointerOverUI = true;
    public bool showPlayerAtHeadingPointOverUI = false;
    public bool spawnAtRandomRuntimeTileBlockCenter = false;

    [Header("Startup Safety")]
    public bool waitForRuntimeTileSpawnBeforeMouseFallback = true;
    public bool requireRuntimeTileSpawnForRandomStart = true;
    [Min(0f)]
    public float runtimeTileSpawnWaitSeconds = 3f;
    [Min(0f)]
    public float startupInputLockSeconds = 0.35f;

    [Header("Player Collision")]
    public bool usePlayerCollisionRadius = true;
    [Min(0f)]
    public float playerCollisionRadius = 0.35f;
    public bool drawDebugPlayerIndicator = true;
    public Color debugPlayerColor = new Color(0.1f, 0.75f, 1f, 1f);
    public Color debugHeadingColor = new Color(1f, 0.82f, 0.2f, 1f);
    public Color debugBlockedPlayerColor = new Color(1f, 0.25f, 0.25f, 1f);

    [Header("Heading Point")]
    public bool headingPointInputEnabled = true;
    public bool showHeadingPoint = true;
    public bool createHeadingPointIfMissing = true;
    public Image headingPointImage;
    public string headingPointObjectName = "Heading Point";
    public Sprite headingPointSprite;
    public Color headingPointColor = Color.white;
    [Range(0f, 1f)]
    public float headingPointAlpha = 0.65f;
    [Min(1f)]
    public float headingPointSize = 14f;
    public bool headingPointUseDedicatedOverlayCanvas = true;
    public int headingPointCanvasSortingOrder = 6500;
    public bool headingPointUseScreenInvertShader = true;
    public bool headingPointUseAdaptiveVisualRenderer = false;
    public bool invertHeadingPointColorWhenPlayerInputDisabled = true;
    public bool limitHeadingPointReach = true;
    [Min(0f)]
    public float headingPointReachRadius = 2.5f;
    public bool drawDebugHeadingReach = true;
    public Color debugHeadingReachColor = new Color(1f, 0.82f, 0.2f, 0.24f);

    [Header("Player Movement")]
    public CursorMovementMode movementMode = CursorMovementMode.PointerOffsetVelocity;
    [Min(0f)]
    public float maxCursorMoveSpeed = 16f;
    [Min(0f)]
    public float cursorInputDeadZonePixels = 18f;
    [Min(0f)]
    public float cursorInputFullSpeedPixels = 0f;
    [Min(0.01f)]
    public float cursorInputResponsePower = 1f;
    public bool normalizePointerOffsetByScreenSize = true;

    [Header("Outdoor Movement")]
    [Range(0.05f, 1f)]
    public float outsideMoveSpeedMultiplier = 0.5f;
    public bool showOutdoorWarning = true;
    public string outdoorWarningMessage = "type: 现在你在屋子外面，目前的你很脆弱！";
    public string outdoorWarningMessageEnglish = "type: You are outside. You are vulnerable!";
    public TMP_FontAsset outdoorWarningFont;
    public string outdoorWarningFontResourcesPath = CjkUiFontUtility.DefaultResourcesFontPath;
    [Min(1f)]
    public float outdoorWarningFontSize = 34f;
    public Color outdoorWarningColor = new Color(1f, 0.92f, 0.68f, 1f);
    public Vector2 outdoorWarningAnchoredPosition = new Vector2(0f, -64f);

    [Header("Player Visual")]
    public CursorVisualMode cursorVisualMode = CursorVisualMode.ImageSprite;
    public Sprite cursorSprite;
    public RuntimeAnimatorController cursorAnimatorController;
    public bool driveAnimatorParameters = true;
    public string movingAnimatorBool = "IsMoving";
    public string speedAnimatorFloat = "MoveSpeed";
    public string stepPhaseAnimatorFloat = "StepPhase";
    public string stepRateAnimatorFloat = "StepRate";
    public string stepAnimatorTrigger = "Step";
    public bool fireStepAnimatorTrigger = true;
    public bool driveAnimatorPlaybackSpeedFromStepClock = true;
    [Min(0.01f)]
    public float animatorReferenceStepsPerSecond = 2f;
    public Vector2 animatorPlaybackSpeedRange = new Vector2(0.5f, 2.25f);

    [Header("Camera Pan")]
    public bool driveCameraFromCursorOffset = true;
    public float maxCameraPanSpeed = 12f;
    public float panDeadZonePixels = 24f;
    public bool useUnscaledTime = false;

    [Header("Footsteps")]
    public bool playFootstepSounds = true;
    public AudioSource footstepAudioSource;
    public AudioClip[] footstepClips;
    [Min(0.01f)]
    public float worldDistancePerFootstep = 1.2f;
    [Min(0f)]
    public float minSecondsBetweenFootsteps = 0.08f;
    [Range(0f, 1f)]
    public float footstepVolume = 1f;
    public Vector2 footstepPitchRange = new Vector2(0.95f, 1.05f);

    [Header("Unified Step Clock")]
    public bool useUnifiedStepClock = true;
    public FoleyStepClock stepClock;
    public bool syncFootstepSettingsToStepClock = true;
    [Min(0f)]
    public float runSpeedThreshold = 8f;
    [Min(0.01f)]
    public float speedForFullFootstepCadence = 16f;
    [Min(0f)]
    public float slowHeelToeDelay = 0.18f;
    [Min(0f)]
    public float fastHeelToeDelay = 0f;

    [Header("Foley System")]
    public bool useFoleyProfileForFootsteps = true;
    public FoleyPlayer footstepFoleyPlayer;
    public FoleyProfile footstepFoleyProfile;
    public string footstepSurfaceIdOverride;

    private RectTransform playerRectTransform;
    private Canvas playerCanvas;
    private Animator playerAnimator;
    private GameplayVisualRenderer playerAdaptiveVisual;
    private RectTransform headingPointRectTransform;
    private Canvas headingPointCanvas;
    private Canvas headingPointOverlayCanvas;
    private GameplayVisualRenderer headingPointAdaptiveVisual;
    private Material headingPointInvertMaterial;
    private Texture2D runtimeCursorTexture;
    private Sprite runtimeCursorSprite;
    private Texture2D runtimeHeadingPointTexture;
    private Sprite runtimeHeadingPointSprite;
    private Vector3 currentWorldPosition;
    private Vector3 headingWorldPosition;
    private Vector2 headingScreenPosition;
    private bool hasWorldPosition;
    private bool hasHeadingWorldPosition;
    private float currentCursorSpeed;
    private bool previousCursorVisible;
    private CursorLockMode previousCursorLockMode;
    private bool cursorStateApplied;
    private bool warnedAboutMissingRoomArea;
    private float footstepDistanceAccumulator;
    private float nextFootstepTime;
    private int lastFootstepClipIndex = -1;
    private bool stepTriggeredThisFrame;
    private bool lastPointerOverUI;
    private FoleyStepClock.StepData currentStepData;
    private bool playerIsOutsideRuntimeRoom;
    private TextMeshProUGUI outdoorWarningText;
    private Canvas outdoorWarningCanvas;
    private bool runtimeSpawnWaitStarted;
    private float runtimeSpawnWaitDeadline;
    private float startupInputUnlockTime;
    private bool warnedAboutRuntimeSpawnFallbackBlocked;

    public Vector3 PlayerWorldPosition => currentWorldPosition;
    public Vector3 CurrentWorldPosition => PlayerWorldPosition;
    public Vector3 HeadingWorldPosition => headingWorldPosition;
    public Vector2 HeadingScreenPosition => headingScreenPosition;
    public bool HasWorldPosition => hasWorldPosition;
    public bool HasPlayerWorldPosition => hasWorldPosition;
    public bool HasHeadingWorldPosition => hasHeadingWorldPosition;
    public float PlayerCollisionRadius => usePlayerCollisionRadius ? Mathf.Max(0f, playerCollisionRadius) : 0f;
    public float CurrentCursorSpeed => currentCursorSpeed;
    public bool IsOutsideRuntimeRoom => playerIsOutsideRuntimeRoom;
    public bool LimitHeadingPointReach
    {
        get => limitHeadingPointReach;
        set => limitHeadingPointReach = value;
    }

    public static bool IsRunning => Active != null && Active.isActiveAndEnabled;
    public static bool HasActive => IsRunning && Active.hasWorldPosition;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateDefaultControllerIfNeeded()
    {
        if (FindFirstObjectByType<PlayerControl>() != null)
            return;

        if (FindFirstObjectByType<TilePlacementGrid>() == null &&
            FindFirstObjectByType<RuntimeTileMeshFusionSandbox>() == null)
            return;

        GameObject controllerObject = new GameObject("Player Control");
        controllerObject.AddComponent<PlayerControl>();
    }

    void Awake()
    {
        if (Active != null && Active != this)
        {
            enabled = false;
            return;
        }

        Active = this;
        ResolveReferences();
        ApplySystemCursorState();
        InitializeWorldPosition();
    }

    void OnEnable()
    {
        Active = this;
        ResolveReferences();
        ApplySystemCursorState();
    }

    void Start()
    {
        ResolveReferences();
        InitializeWorldPosition();
    }

    void Update()
    {
        DeveloperModeState.TryHandleHotkey();
        if (PauseManager.IsGamePaused)
        {
            currentCursorSpeed = 0f;
            stepTriggeredThisFrame = false;
            return;
        }

        ResolveReferences();

        lastPointerOverUI = IsPointerOverUI();
        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        if (headingPointInputEnabled && hasWorldPosition)
        {
            UpdateHeadingPoint();
        }
        else
        {
            hasHeadingWorldPosition = false;
        }

        float movedDistance = 0f;
        if (playerInputEnabled)
        {
            movedDistance = UpdateLogicalWorldPosition(lastPointerOverUI, deltaTime);
        }
        else
        {
            currentCursorSpeed = 0f;
            InitializeWorldPosition();
        }

        UpdateStepClock(movedDistance, deltaTime);
        if (playerInputEnabled)
            ApplyCameraPan(lastPointerOverUI, deltaTime);
        UpdateCursorMotionEffects(movedDistance);
        UpdateOutdoorWarningVisual();
    }

    void LateUpdate()
    {
        if (PauseManager.IsGamePaused)
            return;

        ResolveReferences();
        UpdatePlayerVisual(lastPointerOverUI);
        UpdateHeadingPointVisual();
    }

    void OnDisable()
    {
        if (Active == this)
            Active = null;

        if (restoreSystemCursorOnDisable)
            RestoreSystemCursorState();

        DestroyRuntimeHeadingPointAssets();
    }

    void OnDestroy()
    {
        if (Active == this)
            Active = null;

        if (restoreSystemCursorOnDisable)
            RestoreSystemCursorState();

        DestroyRuntimeHeadingPointAssets();
    }

    public static bool TryGetPlayerWorldPosition(out Vector3 worldPosition)
    {
        if (HasActive)
        {
            worldPosition = Active.currentWorldPosition;
            return true;
        }

        worldPosition = default(Vector3);
        return false;
    }

    public static bool TryGetWorldPosition(out Vector3 worldPosition)
    {
        return TryGetPlayerWorldPosition(out worldPosition);
    }

    public static bool TryGetPlayerScreenPosition(out Vector2 screenPosition)
    {
        if (HasActive && Active.targetCamera != null)
        {
            screenPosition = Active.targetCamera.WorldToScreenPoint(Active.currentWorldPosition);
            return true;
        }

        screenPosition = default(Vector2);
        return false;
    }

    public static bool TryGetScreenPosition(out Vector2 screenPosition)
    {
        return TryGetPlayerScreenPosition(out screenPosition);
    }

    public static bool TryGetHeadingWorldPosition(out Vector3 worldPosition)
    {
        if (IsRunning && Active.hasHeadingWorldPosition)
        {
            worldPosition = Active.headingWorldPosition;
            return true;
        }

        worldPosition = default(Vector3);
        return false;
    }

    public static bool TryGetHeadingScreenPosition(out Vector2 screenPosition)
    {
        if (IsRunning && Active.hasHeadingWorldPosition)
        {
            screenPosition = Active.headingScreenPosition;
            return true;
        }

        screenPosition = default(Vector2);
        return false;
    }

    public static bool TryGetInteractionWorldPosition(out Vector3 worldPosition)
    {
        if (TryGetHeadingWorldPosition(out worldPosition))
            return true;

        return TryGetPlayerWorldPosition(out worldPosition);
    }

    public static bool TryGetInteractionScreenPosition(out Vector2 screenPosition)
    {
        if (TryGetHeadingScreenPosition(out screenPosition))
            return true;

        return TryGetPlayerScreenPosition(out screenPosition);
    }

    public void SetWorldPositionImmediate(Vector3 worldPosition)
    {
        worldPosition.z = 0f;
        currentWorldPosition = worldPosition;
        hasWorldPosition = true;
        currentCursorSpeed = 0f;
        playerIsOutsideRuntimeRoom = IsOutsideRuntimeRoomAt(currentWorldPosition);
    }

    private void ResolveReferences()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (roomGrid == null)
            roomGrid = FindFirstObjectByType<TilePlacementGrid>();

        if (runtimeTileWalkableArea == null)
            runtimeTileWalkableArea = FindFirstObjectByType<RuntimeTileMeshFusionSandbox>();

        if (playerImage == null)
            playerImage = FindUIImageByName(playerObjectName);

        if (playerImage != null)
        {
            playerImage.raycastTarget = false;
            playerRectTransform = playerImage.rectTransform;
            playerCanvas = playerImage.GetComponentInParent<Canvas>();
            if (playerAdaptiveVisual == null)
            {
                playerAdaptiveVisual = GameplayVisualRenderer.Ensure(
                    playerImage,
                    GameplayVisualPriority.Player);
                if (playerAdaptiveVisual != null)
                {
                    playerAdaptiveVisual.collectTargetsAutomatically = false;
                    playerAdaptiveVisual.graphics = new Graphic[] { playerImage };
                    playerAdaptiveVisual.contrastStrength = 1.35f;
                    playerAdaptiveVisual.edgeContrast = 0.75f;
                    playerAdaptiveVisual.enableOutline = true;
                    playerAdaptiveVisual.outlineWidth = 1.2f;
                    playerAdaptiveVisual.outlineStrength = 0.72f;
                    playerAdaptiveVisual.Refresh();
                }
            }
            ApplyCursorVisualSettings();
        }

        ResolveHeadingPointVisual();
        ResolveStepClock();
        ResolveFootstepFoleyPlayer();
        ResolveFootstepAudioSource();
    }

    private bool InitializeWorldPosition()
    {
        if (hasWorldPosition || targetCamera == null)
            return hasWorldPosition;

        Vector3 startWorld;
        if (spawnAtRandomRuntimeTileBlockCenter &&
            runtimeTileWalkableArea != null &&
            runtimeTileWalkableArea.TryGetRandomBlockCenter(out Vector3 runtimeSpawnWorld))
        {
            CompleteWorldPositionInitialization(runtimeSpawnWorld, true);
            return true;
        }

        if (ShouldWaitForRuntimeTileSpawn())
        {
            currentCursorSpeed = 0f;
            return false;
        }

        if (ShouldRequireRuntimeSpawnBeforeInitialization())
        {
            currentCursorSpeed = 0f;
            if (!warnedAboutRuntimeSpawnFallbackBlocked && runtimeSpawnWaitStarted)
            {
                Debug.LogWarning(
                    "[PlayerControl] Waiting for RuntimeTileMesh room cells before spawning Player. " +
                    "Mouse fallback is disabled so startup pointer position cannot move the player outside a floor block.",
                    this);
                warnedAboutRuntimeSpawnFallbackBlocked = true;
            }

            return false;
        }

        startWorld = ScreenToWorld(Input.mousePosition);
        if (clampCursorToRoom && ShouldUseRuntimeTileWalkableAreaFirst())
        {
            if (spawnAtRandomRuntimeTileBlockCenter && runtimeTileWalkableArea.TryGetRandomBlockCenter(out Vector3 spawnWorld))
                startWorld = spawnWorld;
            else
                startWorld = runtimeTileWalkableArea.ClampPlayerWorldPoint(startWorld, startWorld, PlayerCollisionRadius);
        }
        else if (clampCursorToRoom && roomGrid != null && roomGrid.HasRoomCells)
        {
            startWorld = roomGrid.ClampPlayerWorldPoint(startWorld, startWorld, PlayerCollisionRadius);
        }
        else if (clampCursorToRoom && runtimeTileWalkableArea != null && runtimeTileWalkableArea.HasWalkableCells)
        {
            if (spawnAtRandomRuntimeTileBlockCenter && runtimeTileWalkableArea.TryGetRandomBlockCenter(out Vector3 spawnWorld))
                startWorld = spawnWorld;
            else
                startWorld = runtimeTileWalkableArea.ClampPlayerWorldPoint(startWorld, startWorld, PlayerCollisionRadius);
        }

        CompleteWorldPositionInitialization(startWorld, true);
        return true;
    }

    private bool ShouldWaitForRuntimeTileSpawn()
    {
        if (!waitForRuntimeTileSpawnBeforeMouseFallback ||
            !spawnAtRandomRuntimeTileBlockCenter ||
            runtimeTileWalkableArea == null ||
            runtimeTileSpawnWaitSeconds <= 0f)
        {
            return false;
        }

        if (runtimeTileWalkableArea.HasWalkableCells)
            return false;

        if (!runtimeSpawnWaitStarted)
        {
            runtimeSpawnWaitStarted = true;
            runtimeSpawnWaitDeadline = Time.unscaledTime + runtimeTileSpawnWaitSeconds;
        }

        return Time.unscaledTime <= runtimeSpawnWaitDeadline;
    }

    private bool ShouldRequireRuntimeSpawnBeforeInitialization()
    {
        return requireRuntimeTileSpawnForRandomStart &&
               spawnAtRandomRuntimeTileBlockCenter &&
               runtimeTileWalkableArea != null;
    }

    private void CompleteWorldPositionInitialization(Vector3 worldPosition, bool lockStartupInput)
    {
        worldPosition.z = 0f;
        currentWorldPosition = worldPosition;
        hasWorldPosition = true;
        playerIsOutsideRuntimeRoom = IsOutsideRuntimeRoomAt(currentWorldPosition);
        runtimeSpawnWaitStarted = false;
        warnedAboutRuntimeSpawnFallbackBlocked = false;

        if (lockStartupInput && startupInputLockSeconds > 0f)
            startupInputUnlockTime = Mathf.Max(startupInputUnlockTime, Time.unscaledTime + startupInputLockSeconds);
    }

    private void UpdateHeadingPoint()
    {
        if (targetCamera == null)
        {
            hasHeadingWorldPosition = false;
            return;
        }

        headingScreenPosition = Input.mousePosition;
        headingWorldPosition = ClampHeadingPointWorldPosition(ScreenToWorld(headingScreenPosition));
        if (targetCamera != null)
            headingScreenPosition = targetCamera.WorldToScreenPoint(headingWorldPosition);
        hasHeadingWorldPosition = true;
    }

    private Vector3 ClampHeadingPointWorldPosition(Vector3 desiredWorldPosition)
    {
        desiredWorldPosition.z = 0f;
        if (!limitHeadingPointReach || !hasWorldPosition)
            return desiredWorldPosition;

        float radius = Mathf.Max(0f, headingPointReachRadius);
        if (radius <= 0.0001f)
            return currentWorldPosition;

        Vector2 offset = (Vector2)(desiredWorldPosition - currentWorldPosition);
        if (offset.sqrMagnitude <= radius * radius)
            return desiredWorldPosition;

        Vector2 clamped = (Vector2)currentWorldPosition + offset.normalized * radius;
        return new Vector3(clamped.x, clamped.y, 0f);
    }

    private float UpdateLogicalWorldPosition(bool pointerOverUI, float deltaTime)
    {
        if (targetCamera == null)
            return 0f;

        if (!InitializeWorldPosition())
        {
            currentCursorSpeed = 0f;
            return 0f;
        }

        if (IsStartupInputLocked())
        {
            currentCursorSpeed = 0f;
            return 0f;
        }

        if (pointerOverUI && freezeWorldCursorWhenPointerOverUI)
        {
            currentCursorSpeed = 0f;
            return 0f;
        }

        bool hadWorldPosition = hasWorldPosition;
        Vector3 previousWorldPosition = currentWorldPosition;
        Vector3 desiredWorld = GetDesiredWorldPosition(deltaTime);
        Vector3 nextWorld = desiredWorld;

        if (clampCursorToRoom)
        {
            if (ShouldAllowOutdoorRuntimeMovement())
            {
                nextWorld = runtimeTileWalkableArea.ResolvePlayerWorldPoint(
                    desiredWorld,
                    currentWorldPosition,
                    PlayerCollisionRadius,
                    true);
                warnedAboutMissingRoomArea = false;
            }
            else if (ShouldUseRuntimeTileWalkableAreaFirst())
            {
                nextWorld = runtimeTileWalkableArea.ClampPlayerWorldPoint(desiredWorld, currentWorldPosition, PlayerCollisionRadius);
                warnedAboutMissingRoomArea = false;
            }
            else if (roomGrid != null && roomGrid.HasRoomCells)
            {
                nextWorld = roomGrid.ClampPlayerWorldPoint(desiredWorld, currentWorldPosition, PlayerCollisionRadius);
                warnedAboutMissingRoomArea = false;
            }
            else if (runtimeTileWalkableArea != null && runtimeTileWalkableArea.HasWalkableCells)
            {
                nextWorld = runtimeTileWalkableArea.ClampPlayerWorldPoint(desiredWorld, currentWorldPosition, PlayerCollisionRadius);
                warnedAboutMissingRoomArea = false;
            }
            else if (!warnedAboutMissingRoomArea)
            {
                Debug.LogWarning("[PlayerControl] No occupied room cells found. Player clamp is inactive until room cells are registered.");
                warnedAboutMissingRoomArea = true;
            }
        }

        nextWorld.z = 0f;
        currentWorldPosition = nextWorld;
        hasWorldPosition = true;
        playerIsOutsideRuntimeRoom = IsOutsideRuntimeRoomAt(currentWorldPosition);
        float movedDistance = hadWorldPosition ? Vector2.Distance(previousWorldPosition, currentWorldPosition) : 0f;
        currentCursorSpeed = deltaTime > 0f ? movedDistance / deltaTime : 0f;
        return movedDistance;
    }

    public bool TryGetCurrentBlock(out TilePlacementGrid.TileBlockInfo blockInfo)
    {
        if (roomGrid != null && hasWorldPosition)
            return roomGrid.TryGetBlockInfo(currentWorldPosition, out blockInfo);

        blockInfo = default(TilePlacementGrid.TileBlockInfo);
        return false;
    }

    private Vector3 GetDesiredWorldPosition(float deltaTime)
    {
        if (movementMode == CursorMovementMode.DirectScreenPosition || !hasWorldPosition)
            return ScreenToWorld(Input.mousePosition);

        Vector2 velocityInput = GetPointerOffsetVelocity(Input.mousePosition);
        Vector3 worldVelocity = new Vector3(velocityInput.x, velocityInput.y, 0f) *
            maxCursorMoveSpeed *
            GetMovementSpeedMultiplier();
        return currentWorldPosition + worldVelocity * Mathf.Max(0f, deltaTime);
    }

    private bool IsStartupInputLocked()
    {
        return startupInputUnlockTime > 0f && Time.unscaledTime < startupInputUnlockTime;
    }

    private float GetMovementSpeedMultiplier()
    {
        return IsOutsideRuntimeRoomAt(currentWorldPosition)
            ? Mathf.Clamp(outsideMoveSpeedMultiplier, 0.05f, 1f)
            : 1f;
    }

    private bool ShouldAllowOutdoorRuntimeMovement()
    {
        return allowOutdoorMovementFromRuntimeRooms &&
               ShouldUseRuntimeTileWalkableAreaFirst();
    }

    private bool IsOutsideRuntimeRoomAt(Vector3 worldPosition)
    {
        return allowOutdoorMovementFromRuntimeRooms &&
               runtimeTileWalkableArea != null &&
               runtimeTileWalkableArea.HasWalkableCells &&
               !runtimeTileWalkableArea.ContainsWorldPoint(worldPosition, PlayerCollisionRadius);
    }

    private Vector2 GetPointerOffsetVelocity(Vector2 screenPosition)
    {
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 delta = screenPosition - screenCenter;
        float distance = delta.magnitude;

        if (distance <= cursorInputDeadZonePixels)
            return Vector2.zero;

        float maxDistance;
        if (cursorInputFullSpeedPixels > cursorInputDeadZonePixels)
        {
            maxDistance = cursorInputFullSpeedPixels;
        }
        else if (normalizePointerOffsetByScreenSize)
        {
            Vector2 halfScreen = new Vector2(
                Mathf.Max(1f, Screen.width * 0.5f),
                Mathf.Max(1f, Screen.height * 0.5f)
            );
            maxDistance = Mathf.Max(1f, Mathf.Min(halfScreen.x, halfScreen.y));
        }
        else
        {
            maxDistance = Mathf.Max(cursorInputDeadZonePixels + 1f, distance);
        }

        float strength = Mathf.Clamp01((distance - cursorInputDeadZonePixels) / Mathf.Max(1f, maxDistance - cursorInputDeadZonePixels));
        strength = Mathf.Pow(strength, Mathf.Max(0.01f, cursorInputResponsePower));
        return delta.normalized * strength;
    }

    private void ApplyCameraPan(bool pointerOverUI, float deltaTime)
    {
        if (!driveCameraFromCursorOffset || targetCamera == null || !hasWorldPosition)
            return;

        if (pointerOverUI && freezeWorldCursorWhenPointerOverUI)
            return;

        Vector2 cursorScreenPoint = targetCamera.WorldToScreenPoint(currentWorldPosition);
        Vector2 panInput = GetCenterPanInput(cursorScreenPoint);
        if (panInput == Vector2.zero)
            return;

        Vector3 cameraPosition = targetCamera.transform.position;
        cameraPosition += new Vector3(panInput.x, panInput.y, 0f) * maxCameraPanSpeed * deltaTime;
        targetCamera.transform.position = cameraPosition;
    }

    private Vector2 GetCenterPanInput(Vector2 cursorScreenPoint)
    {
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 delta = cursorScreenPoint - screenCenter;

        float distance = delta.magnitude;
        if (distance <= panDeadZonePixels)
            return Vector2.zero;

        Vector2 halfScreen = new Vector2(
            Mathf.Max(1f, Screen.width * 0.5f),
            Mathf.Max(1f, Screen.height * 0.5f)
        );

        Vector2 normalized = new Vector2(
            Mathf.Clamp(delta.x / halfScreen.x, -1f, 1f),
            Mathf.Clamp(delta.y / halfScreen.y, -1f, 1f)
        );

        float maxDistance = Mathf.Max(1f, Mathf.Min(halfScreen.x, halfScreen.y));
        float strength = Mathf.Clamp01((distance - panDeadZonePixels) / Mathf.Max(1f, maxDistance - panDeadZonePixels));

        if (normalized.sqrMagnitude > 1f)
            normalized = normalized.normalized;

        return normalized * strength;
    }

    private void UpdatePlayerVisual(bool pointerOverUI)
    {
        if (playerImage == null || playerRectTransform == null)
            return;

        Vector2 screenPoint;
        if (pointerOverUI && showPlayerAtHeadingPointOverUI && hasHeadingWorldPosition)
            screenPoint = headingScreenPosition;
        else if (targetCamera != null && hasWorldPosition)
            screenPoint = targetCamera.WorldToScreenPoint(currentWorldPosition);
        else if (hasHeadingWorldPosition)
            screenPoint = headingScreenPosition;
        else
            screenPoint = Input.mousePosition;

        MoveUIImageToScreenPoint(playerRectTransform, playerCanvas, screenPoint);
    }

    private void UpdateHeadingPointVisual()
    {
        if (headingPointImage == null || headingPointRectTransform == null)
            return;

        headingPointImage.enabled = showHeadingPoint && hasHeadingWorldPosition;
        if (!headingPointImage.enabled)
            return;

        ApplyHeadingPointVisualSettings();
        MoveUIImageToScreenPoint(headingPointRectTransform, headingPointCanvas, headingScreenPosition);
    }

    private void MoveUIImageToScreenPoint(RectTransform rectTransform, Canvas canvas, Vector2 screenPoint)
    {
        if (rectTransform == null)
            return;

        RectTransform parentRect = rectTransform.parent as RectTransform;
        if (parentRect == null)
            return;

        Camera eventCamera = null;
        if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            eventCamera = canvas.worldCamera != null ? canvas.worldCamera : targetCamera;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, eventCamera, out Vector2 localPoint))
            rectTransform.anchoredPosition = localPoint;
    }

    private void ApplyCursorVisualSettings()
    {
        if (playerImage == null)
            return;

        if (cursorSprite != null)
            playerImage.sprite = cursorSprite;
        else if (playerImage.sprite == null)
            playerImage.sprite = GetRuntimeCursorSprite();

        playerAnimator = playerImage.GetComponent<Animator>();
        if (cursorVisualMode == CursorVisualMode.Animator || cursorAnimatorController != null)
        {
            if (playerAnimator == null)
                playerAnimator = playerImage.gameObject.AddComponent<Animator>();

            if (cursorAnimatorController != null)
                playerAnimator.runtimeAnimatorController = cursorAnimatorController;

            playerAnimator.enabled = true;
        }
        else
        {
            playerAnimator = null;
        }
    }

    private void ResolveHeadingPointVisual()
    {
        if (!showHeadingPoint)
        {
            if (headingPointImage != null)
                headingPointImage.enabled = false;

            return;
        }

        if (headingPointImage == null)
            headingPointImage = FindUIImageByName(headingPointObjectName);

        if (headingPointImage == null && createHeadingPointIfMissing)
            headingPointImage = CreateHeadingPointImage();

        if (headingPointImage == null)
            return;

        headingPointImage.raycastTarget = false;
        headingPointImage.enabled = true;
        headingPointRectTransform = headingPointImage.rectTransform;
        headingPointCanvas = headingPointImage.GetComponentInParent<Canvas>();
        ConfigureHeadingPointVisualLayer();
        ConfigureHeadingPointAdaptiveVisual();
        ApplyHeadingPointVisualSettings();
    }

    private void ConfigureHeadingPointAdaptiveVisual()
    {
        if (headingPointImage == null)
            return;

        if (!headingPointUseAdaptiveVisualRenderer)
        {
            if (headingPointAdaptiveVisual == null)
                headingPointAdaptiveVisual = headingPointImage.GetComponent<GameplayVisualRenderer>();

            if (headingPointAdaptiveVisual != null)
                headingPointAdaptiveVisual.enabled = false;
            return;
        }

        if (headingPointAdaptiveVisual == null)
        {
            headingPointAdaptiveVisual = GameplayVisualRenderer.Ensure(
                headingPointImage,
                GameplayVisualPriority.HeadingPoint);
        }

        if (headingPointAdaptiveVisual == null)
            return;

        headingPointAdaptiveVisual.enabled = true;
        headingPointAdaptiveVisual.collectTargetsAutomatically = false;
        headingPointAdaptiveVisual.graphics = new Graphic[] { headingPointImage };
        headingPointAdaptiveVisual.contrastStrength = 1.25f;
        headingPointAdaptiveVisual.edgeContrast = 0.7f;
        headingPointAdaptiveVisual.enableOutline = true;
        headingPointAdaptiveVisual.outlineWidth = 1.2f;
        headingPointAdaptiveVisual.outlineStrength = 0.65f;
        headingPointAdaptiveVisual.Refresh();
    }

    private Image CreateHeadingPointImage()
    {
        RectTransform parentRect = null;
        if (playerImage != null)
            parentRect = playerImage.rectTransform.parent as RectTransform;

        if (parentRect == null)
        {
            Canvas existingCanvas = FindFirstObjectByType<Canvas>();
            if (existingCanvas == null)
                existingCanvas = CreatePlayerControlCanvas();

            parentRect = existingCanvas.transform as RectTransform;
        }

        if (parentRect == null)
            return null;

        GameObject headingObject = new GameObject(
            string.IsNullOrWhiteSpace(headingPointObjectName) ? "Heading Point" : headingPointObjectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        headingObject.transform.SetParent(parentRect, false);
        headingObject.transform.SetAsLastSibling();

        RectTransform rectTransform = headingObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);

        return headingObject.GetComponent<Image>();
    }

    private Canvas CreatePlayerControlCanvas()
    {
        GameObject canvasObject = new GameObject(
            "Player Control Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler)
        );

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return canvas;
    }

    private void ConfigureHeadingPointVisualLayer()
    {
        if (headingPointImage == null)
            return;

        headingPointImage.raycastTarget = false;

        if (headingPointUseDedicatedOverlayCanvas)
        {
            Canvas overlayCanvas = EnsureHeadingPointOverlayCanvas();
            if (overlayCanvas != null && headingPointImage.transform.parent != overlayCanvas.transform)
                headingPointImage.transform.SetParent(overlayCanvas.transform, false);

            headingPointCanvas = overlayCanvas;
        }
        else
        {
            headingPointCanvas = headingPointImage.GetComponentInParent<Canvas>();
            if (headingPointCanvas != null)
                headingPointCanvas.sortingOrder = Mathf.Max(headingPointCanvas.sortingOrder, headingPointCanvasSortingOrder);
        }

        if (headingPointCanvas != null)
        {
            headingPointCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            headingPointCanvas.overrideSorting = true;
            headingPointCanvas.sortingOrder = headingPointCanvasSortingOrder;
        }

        headingPointImage.transform.SetAsLastSibling();
    }

    private Canvas EnsureHeadingPointOverlayCanvas()
    {
        if (headingPointOverlayCanvas != null)
            return headingPointOverlayCanvas;

        Transform existing = transform.Find("Heading Point Overlay Canvas");
        if (existing != null)
            headingPointOverlayCanvas = existing.GetComponent<Canvas>();

        if (headingPointOverlayCanvas == null)
        {
            GameObject canvasObject = new GameObject(
                "Heading Point Overlay Canvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            headingPointOverlayCanvas = canvasObject.GetComponent<Canvas>();
            headingPointOverlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        headingPointOverlayCanvas.overrideSorting = true;
        headingPointOverlayCanvas.sortingOrder = headingPointCanvasSortingOrder;
        return headingPointOverlayCanvas;
    }

    private void UpdateOutdoorWarningVisual()
    {
        if (!showOutdoorWarning)
        {
            if (outdoorWarningText != null)
                outdoorWarningText.gameObject.SetActive(false);
            return;
        }

        bool shouldShow = playerInputEnabled && playerIsOutsideRuntimeRoom;
        if (!shouldShow)
        {
            if (outdoorWarningText != null)
                outdoorWarningText.gameObject.SetActive(false);
            return;
        }

        EnsureOutdoorWarningText();
        if (outdoorWarningText == null)
            return;

        outdoorWarningText.gameObject.SetActive(true);
        ApplyOutdoorWarningTypography();
    }

    private void ApplyOutdoorWarningTypography()
    {
        if (outdoorWarningText == null)
            return;

        string message = DuoCurtainLocalization.Text(
            "player.outsideWarning",
            outdoorWarningMessage,
            outdoorWarningMessageEnglish);
        TMP_FontAsset warningFont = CjkUiFontUtility.Resolve(
            outdoorWarningFont,
            outdoorWarningFontResourcesPath,
            message);
        if (warningFont != null)
            outdoorWarningText.font = warningFont;

        outdoorWarningText.text = message;
        outdoorWarningText.fontSize = outdoorWarningFontSize;
        outdoorWarningText.color = outdoorWarningColor;
        outdoorWarningText.rectTransform.anchoredPosition = outdoorWarningAnchoredPosition;
    }

    private void EnsureOutdoorWarningText()
    {
        if (outdoorWarningText != null)
            return;

        Canvas canvas = playerCanvas != null ? playerCanvas : FindFirstObjectByType<Canvas>();
        if (canvas == null)
            canvas = CreatePlayerControlCanvas();

        outdoorWarningCanvas = canvas;
        RectTransform parentRect = canvas.transform as RectTransform;
        if (parentRect == null)
            return;

        GameObject warningObject = new GameObject(
            "Outdoor Vulnerability Warning",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI));
        warningObject.transform.SetParent(parentRect, false);
        warningObject.transform.SetAsLastSibling();

        RectTransform rect = warningObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = outdoorWarningAnchoredPosition;
        rect.sizeDelta = new Vector2(1200f, 92f);

        outdoorWarningText = warningObject.GetComponent<TextMeshProUGUI>();
        outdoorWarningText.raycastTarget = false;
        outdoorWarningText.alignment = TextAlignmentOptions.Center;
        outdoorWarningText.textWrappingMode = TextWrappingModes.NoWrap;
        ApplyOutdoorWarningTypography();
        outdoorWarningText.gameObject.SetActive(false);
    }

    private void ApplyHeadingPointVisualSettings()
    {
        if (headingPointImage == null)
            return;

        ConfigureHeadingPointVisualLayer();
        Sprite sprite = headingPointSprite != null ? headingPointSprite : GetRuntimeHeadingPointSprite();
        if (sprite != null)
            headingPointImage.sprite = sprite;

        ApplyHeadingPointMaterial();

        Color color;
        if (headingPointUseScreenInvertShader && headingPointInvertMaterial != null)
            color = Color.white;
        else
            color = ShouldInvertHeadingPointColor() ? InvertColor(headingPointColor) : headingPointColor;

        color.a = Mathf.Clamp01(color.a * headingPointAlpha);
        headingPointImage.color = color;

        if (headingPointRectTransform == null)
            headingPointRectTransform = headingPointImage.rectTransform;

        if (headingPointRectTransform != null)
        {
            float size = Mathf.Max(1f, headingPointSize);
            headingPointRectTransform.sizeDelta = new Vector2(size, size);
        }
    }

    private void ApplyHeadingPointMaterial()
    {
        if (headingPointImage == null)
            return;

        if (!headingPointUseScreenInvertShader)
        {
            headingPointImage.material = null;
            return;
        }

        if (headingPointInvertMaterial == null)
        {
            Shader shader = Shader.Find("DuoCurtain/UI/HeadingPointInvert");
            if (shader != null)
            {
                headingPointInvertMaterial = new Material(shader)
                {
                    name = "Runtime Heading Point Invert Material",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        headingPointImage.material = headingPointInvertMaterial;
    }

    private bool ShouldInvertHeadingPointColor()
    {
        return invertHeadingPointColorWhenPlayerInputDisabled && !playerInputEnabled;
    }

    private static Color InvertColor(Color color)
    {
        return new Color(1f - color.r, 1f - color.g, 1f - color.b, color.a);
    }

    private Sprite GetRuntimeHeadingPointSprite()
    {
        if (runtimeHeadingPointSprite != null)
            return runtimeHeadingPointSprite;

        const int size = 64;
        runtimeHeadingPointTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        runtimeHeadingPointTexture.name = "Runtime Heading Point";
        runtimeHeadingPointTexture.hideFlags = HideFlags.HideAndDontSave;

        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.46f;
        float feather = 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01((radius - distance) / feather);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        runtimeHeadingPointTexture.SetPixels(pixels);
        runtimeHeadingPointTexture.Apply(false, true);

        runtimeHeadingPointSprite = Sprite.Create(
            runtimeHeadingPointTexture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size
        );
        runtimeHeadingPointSprite.name = "Runtime Heading Point";
        runtimeHeadingPointSprite.hideFlags = HideFlags.HideAndDontSave;
        return runtimeHeadingPointSprite;
    }

    private Sprite GetRuntimeCursorSprite()
    {
        if (runtimeCursorSprite != null)
            return runtimeCursorSprite;

        const int size = 64;
        runtimeCursorTexture = CreateSoftCircleTexture(size, "Runtime Player Cursor");
        runtimeCursorSprite = Sprite.Create(
            runtimeCursorTexture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size
        );
        runtimeCursorSprite.name = "Runtime Player Cursor";
        runtimeCursorSprite.hideFlags = HideFlags.HideAndDontSave;
        return runtimeCursorSprite;
    }

    private static Texture2D CreateSoftCircleTexture(int size, string textureName)
    {
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.name = textureName;
        texture.hideFlags = HideFlags.HideAndDontSave;

        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.46f;
        float feather = 2f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float alpha = Mathf.Clamp01((radius - distance) / feather);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return texture;
    }

    private void DestroyRuntimeHeadingPointAssets()
    {
        if (runtimeCursorSprite != null)
        {
            Destroy(runtimeCursorSprite);
            runtimeCursorSprite = null;
        }

        if (runtimeCursorTexture != null)
        {
            Destroy(runtimeCursorTexture);
            runtimeCursorTexture = null;
        }

        if (runtimeHeadingPointSprite != null)
        {
            Destroy(runtimeHeadingPointSprite);
            runtimeHeadingPointSprite = null;
        }

        if (runtimeHeadingPointTexture != null)
        {
            Destroy(runtimeHeadingPointTexture);
            runtimeHeadingPointTexture = null;
        }

        if (headingPointInvertMaterial != null)
        {
            Destroy(headingPointInvertMaterial);
            headingPointInvertMaterial = null;
        }
    }

    private void UpdateCursorMotionEffects(float movedDistance)
    {
        UpdateCursorAnimatorParameters();
        UpdateFootstepAudio(movedDistance);
    }

    private void UpdateCursorAnimatorParameters()
    {
        if (!driveAnimatorParameters || playerAnimator == null || playerAnimator.runtimeAnimatorController == null)
            return;

        bool isMoving = currentCursorSpeed > 0.01f;
        if (driveAnimatorPlaybackSpeedFromStepClock && useUnifiedStepClock && stepClock != null)
        {
            float min = Mathf.Min(animatorPlaybackSpeedRange.x, animatorPlaybackSpeedRange.y);
            float max = Mathf.Max(animatorPlaybackSpeedRange.x, animatorPlaybackSpeedRange.y);
            playerAnimator.speed = isMoving
                ? stepClock.GetAnimatorSpeedMultiplier(animatorReferenceStepsPerSecond, min, max)
                : 1f;
        }

        if (!string.IsNullOrWhiteSpace(movingAnimatorBool) &&
            HasAnimatorParameter(movingAnimatorBool, AnimatorControllerParameterType.Bool))
        {
            playerAnimator.SetBool(movingAnimatorBool, isMoving);
        }

        if (!string.IsNullOrWhiteSpace(speedAnimatorFloat) &&
            HasAnimatorParameter(speedAnimatorFloat, AnimatorControllerParameterType.Float))
        {
            playerAnimator.SetFloat(speedAnimatorFloat, currentCursorSpeed);
        }

        if (useUnifiedStepClock && stepClock != null)
        {
            if (!string.IsNullOrWhiteSpace(stepPhaseAnimatorFloat) &&
                HasAnimatorParameter(stepPhaseAnimatorFloat, AnimatorControllerParameterType.Float))
            {
                playerAnimator.SetFloat(stepPhaseAnimatorFloat, stepClock.StepPhase);
            }

            if (!string.IsNullOrWhiteSpace(stepRateAnimatorFloat) &&
                HasAnimatorParameter(stepRateAnimatorFloat, AnimatorControllerParameterType.Float))
            {
                playerAnimator.SetFloat(stepRateAnimatorFloat, stepClock.CurrentStepsPerSecond);
            }

            if (fireStepAnimatorTrigger && stepTriggeredThisFrame &&
                !string.IsNullOrWhiteSpace(stepAnimatorTrigger) &&
                HasAnimatorParameter(stepAnimatorTrigger, AnimatorControllerParameterType.Trigger))
            {
                playerAnimator.SetTrigger(stepAnimatorTrigger);
            }
        }
    }

    private bool HasAnimatorParameter(string parameterName, AnimatorControllerParameterType parameterType)
    {
        AnimatorControllerParameter[] parameters = playerAnimator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            if (parameter.type == parameterType && parameter.name == parameterName)
                return true;
        }

        return false;
    }

    private void ResolveFootstepAudioSource()
    {
        if (!playFootstepSounds || footstepAudioSource != null)
            return;

        if (footstepClips == null || footstepClips.Length == 0)
            return;

        footstepAudioSource = GetComponent<AudioSource>();
        if (footstepAudioSource == null)
            footstepAudioSource = gameObject.AddComponent<AudioSource>();

        footstepAudioSource.playOnAwake = false;
    }

    private void ResolveStepClock()
    {
        if (!useUnifiedStepClock)
            return;

        if (stepClock == null)
            stepClock = GetComponent<FoleyStepClock>();

        if (stepClock == null)
            stepClock = gameObject.AddComponent<FoleyStepClock>();

        if (!syncFootstepSettingsToStepClock)
            return;

        stepClock.distancePerStep = Mathf.Max(0.01f, worldDistancePerFootstep);
        stepClock.minSecondsBetweenSteps = Mathf.Max(0f, minSecondsBetweenFootsteps);
        stepClock.speedForFullCadence = Mathf.Max(0.01f, speedForFullFootstepCadence);
        stepClock.runSpeedThreshold = Mathf.Max(0f, runSpeedThreshold);
        stepClock.slowHeelToeDelay = Mathf.Max(0f, slowHeelToeDelay);
        stepClock.fastHeelToeDelay = Mathf.Max(0f, fastHeelToeDelay);
    }

    private void UpdateStepClock(float movedDistance, float deltaTime)
    {
        stepTriggeredThisFrame = false;

        if (!useUnifiedStepClock)
            return;

        ResolveStepClock();
        if (stepClock == null)
            return;

        stepTriggeredThisFrame = stepClock.Tick(
            movedDistance,
            currentCursorSpeed,
            deltaTime,
            currentWorldPosition,
            out currentStepData
        );
    }

    private void ResolveFootstepFoleyPlayer()
    {
        if (!useFoleyProfileForFootsteps || footstepFoleyPlayer != null || footstepFoleyProfile == null)
            return;

        footstepFoleyPlayer = GetComponent<FoleyPlayer>();
        if (footstepFoleyPlayer == null)
            footstepFoleyPlayer = gameObject.AddComponent<FoleyPlayer>();
    }

    private void UpdateFootstepAudio(float movedDistance)
    {
        if (useUnifiedStepClock)
        {
            if (!stepTriggeredThisFrame)
                return;

            PlayFootstepForStep(currentStepData);
            return;
        }

        if (!playFootstepSounds || movedDistance <= 0.0001f)
            return;

        bool hasFoleyProfile = useFoleyProfileForFootsteps && footstepFoleyProfile != null;
        bool hasLegacyClips = footstepClips != null && footstepClips.Length > 0;
        if (!hasFoleyProfile && !hasLegacyClips)
            return;

        footstepDistanceAccumulator += movedDistance;
        if (footstepDistanceAccumulator < worldDistancePerFootstep)
            return;

        float now = useUnscaledTime ? Time.unscaledTime : Time.time;
        if (now < nextFootstepTime)
            return;

        if (TryPlayFootstepFoley())
        {
            footstepDistanceAccumulator = 0f;
            nextFootstepTime = now + minSecondsBetweenFootsteps;
            return;
        }

        if (!hasLegacyClips)
            return;

        ResolveFootstepAudioSource();
        if (footstepAudioSource == null)
            return;

        AudioClip clip = PickFootstepClip();
        if (clip == null)
            return;

        footstepDistanceAccumulator = 0f;
        nextFootstepTime = now + minSecondsBetweenFootsteps;
        footstepAudioSource.pitch = Random.Range(
            Mathf.Min(footstepPitchRange.x, footstepPitchRange.y),
            Mathf.Max(footstepPitchRange.x, footstepPitchRange.y)
        );
        footstepAudioSource.PlayOneShot(clip, footstepVolume);
    }

    private void PlayFootstepForStep(FoleyStepClock.StepData stepData)
    {
        if (!playFootstepSounds)
            return;

        bool hasFoleyProfile = useFoleyProfileForFootsteps && footstepFoleyProfile != null;
        bool hasLegacyClips = footstepClips != null && footstepClips.Length > 0;
        if (!hasFoleyProfile && !hasLegacyClips)
            return;

        if (TryPlayFootstepFoley(stepData))
            return;

        if (!hasLegacyClips)
            return;

        ResolveFootstepAudioSource();
        if (footstepAudioSource == null)
            return;

        AudioClip clip = PickFootstepClip();
        if (clip == null)
            return;

        footstepAudioSource.pitch = Random.Range(
            Mathf.Min(footstepPitchRange.x, footstepPitchRange.y),
            Mathf.Max(footstepPitchRange.x, footstepPitchRange.y)
        ) * Mathf.Max(0.01f, stepData.pitchMultiplier);

        footstepAudioSource.PlayOneShot(clip, Mathf.Clamp01(footstepVolume * stepData.volumeMultiplier));
    }

    private bool TryPlayFootstepFoley()
    {
        if (!useFoleyProfileForFootsteps || footstepFoleyProfile == null)
            return false;

        ResolveFootstepFoleyPlayer();
        if (footstepFoleyPlayer == null)
            return false;

        string surfaceId = string.IsNullOrWhiteSpace(footstepSurfaceIdOverride) ? null : footstepSurfaceIdOverride;
        return footstepFoleyPlayer.Play(footstepFoleyProfile, currentWorldPosition, footstepVolume, surfaceId);
    }

    private bool TryPlayFootstepFoley(FoleyStepClock.StepData stepData)
    {
        if (!useFoleyProfileForFootsteps || footstepFoleyProfile == null)
            return false;

        ResolveFootstepFoleyPlayer();
        if (footstepFoleyPlayer == null)
            return false;

        string surfaceId = string.IsNullOrWhiteSpace(footstepSurfaceIdOverride) ? null : footstepSurfaceIdOverride;
        return footstepFoleyPlayer.Play(
            footstepFoleyProfile,
            stepData.worldPosition,
            footstepVolume * stepData.volumeMultiplier,
            surfaceId,
            stepData.pitchMultiplier,
            stepData.delayMultiplier
        );
    }

    private AudioClip PickFootstepClip()
    {
        for (int attempts = 0; attempts < footstepClips.Length; attempts++)
        {
            int index = Random.Range(0, footstepClips.Length);
            AudioClip clip = footstepClips[index];
            if (clip == null)
                continue;

            if (footstepClips.Length > 1 && index == lastFootstepClipIndex)
                continue;

            lastFootstepClipIndex = index;
            return clip;
        }

        for (int i = 0; i < footstepClips.Length; i++)
        {
            if (footstepClips[i] != null)
            {
                lastFootstepClipIndex = i;
                return footstepClips[i];
            }
        }

        return null;
    }

    void OnDrawGizmos()
    {
        if (!drawDebugPlayerIndicator)
            return;

        Vector3 playerPosition = hasWorldPosition ? currentWorldPosition : transform.position;
        float radius = Mathf.Max(0.05f, PlayerCollisionRadius);
        bool playerIsInRoom = !hasWorldPosition || IsPlayerInsideCurrentWalkableArea(playerPosition);

        Gizmos.color = playerIsInRoom ? debugPlayerColor : debugBlockedPlayerColor;
        Gizmos.DrawWireSphere(playerPosition, radius);
        Gizmos.DrawLine(playerPosition + Vector3.left * radius, playerPosition + Vector3.right * radius);
        Gizmos.DrawLine(playerPosition + Vector3.down * radius, playerPosition + Vector3.up * radius);

        if (drawDebugHeadingReach && limitHeadingPointReach)
        {
            Gizmos.color = debugHeadingReachColor;
            Gizmos.DrawWireSphere(playerPosition, Mathf.Max(0f, headingPointReachRadius));
        }

        if (hasHeadingWorldPosition)
        {
            Gizmos.color = debugHeadingColor;
            float headingRadius = Mathf.Max(0.05f, PlayerCollisionRadius * 0.35f);
            Gizmos.DrawWireSphere(headingWorldPosition, headingRadius);
        }

#if UNITY_EDITOR
        DrawDebugLabels(playerPosition, radius, playerIsInRoom);
#endif
    }

#if UNITY_EDITOR
    private void DrawDebugLabels(Vector3 playerPosition, float radius, bool playerIsInRoom)
    {
        GUIStyle playerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            normal = { textColor = playerIsInRoom ? debugPlayerColor : debugBlockedPlayerColor }
        };

        string playerLabel = "Player";
        if (TryGetCurrentBlock(out TilePlacementGrid.TileBlockInfo blockInfo))
            playerLabel += "\nBlock: " + blockInfo.displayName + " (" + blockInfo.cell.x + "," + blockInfo.cell.y + ")";
        else
            playerLabel += "\nBlock: none";

        Handles.Label(playerPosition + Vector3.up * (radius + 0.2f), playerLabel, playerStyle);

        if (!hasHeadingWorldPosition)
            return;

        GUIStyle headingStyle = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            normal = { textColor = debugHeadingColor }
        };

        Handles.Label(headingWorldPosition + Vector3.up * Mathf.Max(0.15f, radius * 0.45f), "Heading Point", headingStyle);
    }
#endif

    private bool IsPlayerInsideCurrentWalkableArea(Vector3 playerPosition)
    {
        if (ShouldUseRuntimeTileWalkableAreaFirst())
            return runtimeTileWalkableArea.ContainsWorldPoint(playerPosition, PlayerCollisionRadius);

        if (roomGrid != null && roomGrid.HasRoomCells)
            return roomGrid.ContainsWorldPoint(playerPosition, PlayerCollisionRadius);

        if (runtimeTileWalkableArea != null && runtimeTileWalkableArea.HasWalkableCells)
            return runtimeTileWalkableArea.ContainsWorldPoint(playerPosition, PlayerCollisionRadius);

        return true;
    }

    private bool ShouldUseRuntimeTileWalkableAreaFirst()
    {
        return preferRuntimeTileWalkableArea &&
               runtimeTileWalkableArea != null &&
               runtimeTileWalkableArea.HasWalkableCells;
    }

    private Vector3 ScreenToWorld(Vector3 screenPosition)
    {
        screenPosition.x = Mathf.Clamp(screenPosition.x, 0f, Mathf.Max(1f, Screen.width));
        screenPosition.y = Mathf.Clamp(screenPosition.y, 0f, Mathf.Max(1f, Screen.height));
        screenPosition.z = targetCamera != null ? Mathf.Abs(targetCamera.transform.position.z) : 0f;

        Vector3 world = targetCamera.ScreenToWorldPoint(screenPosition);
        world.z = 0f;
        return world;
    }

    private bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    private Image FindUIImageByName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
            return null;

        Image[] images = FindObjectsByType<Image>(FindObjectsSortMode.None);
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

    private void ApplySystemCursorState()
    {
        if (cursorStateApplied)
            return;

        previousCursorVisible = Cursor.visible;
        previousCursorLockMode = Cursor.lockState;
        cursorStateApplied = true;

        if (!hideSystemCursor)
            return;

        Cursor.visible = false;
        Cursor.lockState = confineSystemCursorToWindow ? CursorLockMode.Confined : CursorLockMode.None;
    }

    private void RestoreSystemCursorState()
    {
        if (!cursorStateApplied)
            return;

        Cursor.visible = previousCursorVisible;
        Cursor.lockState = previousCursorLockMode;
        cursorStateApplied = false;
    }
}
