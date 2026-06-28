using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using UnityEngine.UI;

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
    [FormerlySerializedAs("uiCursorImage")]
    public Image playerImage;
    [FormerlySerializedAs("uiCursorObjectName")]
    public string playerObjectName = "Player";

    [Header("System Cursor")]
    public bool hideSystemCursor = true;
    public bool confineSystemCursorToWindow = true;
    public bool restoreSystemCursorOnDisable = true;

    [Header("Player Point")]
    public bool clampCursorToRoom = true;
    public bool freezeWorldCursorWhenPointerOverUI = true;
    public bool showPlayerAtHeadingPointOverUI = false;

    [Header("Player Collision")]
    public bool usePlayerCollisionRadius = true;
    [Min(0f)]
    public float playerCollisionRadius = 0.35f;
    public bool drawDebugPlayerIndicator = true;
    public Color debugPlayerColor = new Color(0.1f, 0.75f, 1f, 1f);
    public Color debugHeadingColor = new Color(1f, 0.82f, 0.2f, 1f);
    public Color debugBlockedPlayerColor = new Color(1f, 0.25f, 0.25f, 1f);

    [Header("Heading Point")]
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
    private RectTransform headingPointRectTransform;
    private Canvas headingPointCanvas;
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

    public Vector3 PlayerWorldPosition => currentWorldPosition;
    public Vector3 CurrentWorldPosition => PlayerWorldPosition;
    public Vector3 HeadingWorldPosition => headingWorldPosition;
    public Vector2 HeadingScreenPosition => headingScreenPosition;
    public bool HasWorldPosition => hasWorldPosition;
    public bool HasPlayerWorldPosition => hasWorldPosition;
    public bool HasHeadingWorldPosition => hasHeadingWorldPosition;
    public float PlayerCollisionRadius => usePlayerCollisionRadius ? Mathf.Max(0f, playerCollisionRadius) : 0f;

    public static bool IsRunning => Active != null && Active.isActiveAndEnabled;
    public static bool HasActive => IsRunning && Active.hasWorldPosition;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateDefaultControllerIfNeeded()
    {
        if (FindFirstObjectByType<PlayerControl>() != null)
            return;

        if (FindFirstObjectByType<TilePlacementGrid>() == null)
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
        ResolveReferences();

        lastPointerOverUI = IsPointerOverUI();
        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        UpdateHeadingPoint();
        float movedDistance = UpdateLogicalWorldPosition(lastPointerOverUI, deltaTime);
        UpdateStepClock(movedDistance, deltaTime);
        ApplyCameraPan(lastPointerOverUI, deltaTime);
        UpdateCursorMotionEffects(movedDistance);
    }

    void LateUpdate()
    {
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

    private void ResolveReferences()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (roomGrid == null)
            roomGrid = FindFirstObjectByType<TilePlacementGrid>();

        if (playerImage == null)
            playerImage = FindUIImageByName(playerObjectName);

        if (playerImage != null)
        {
            playerImage.raycastTarget = false;
            playerRectTransform = playerImage.rectTransform;
            playerCanvas = playerImage.GetComponentInParent<Canvas>();
            ApplyCursorVisualSettings();
        }

        ResolveHeadingPointVisual();
        ResolveStepClock();
        ResolveFootstepFoleyPlayer();
        ResolveFootstepAudioSource();
    }

    private void InitializeWorldPosition()
    {
        if (hasWorldPosition || targetCamera == null)
            return;

        Vector3 startWorld = ScreenToWorld(Input.mousePosition);
        if (clampCursorToRoom && roomGrid != null && roomGrid.HasRoomCells)
            startWorld = roomGrid.ClampPlayerWorldPoint(startWorld, startWorld, PlayerCollisionRadius);

        startWorld.z = 0f;
        currentWorldPosition = startWorld;
        hasWorldPosition = true;
    }

    private void UpdateHeadingPoint()
    {
        if (targetCamera == null)
        {
            hasHeadingWorldPosition = false;
            return;
        }

        headingScreenPosition = Input.mousePosition;
        headingWorldPosition = ScreenToWorld(headingScreenPosition);
        hasHeadingWorldPosition = true;
    }

    private float UpdateLogicalWorldPosition(bool pointerOverUI, float deltaTime)
    {
        if (targetCamera == null)
            return 0f;

        InitializeWorldPosition();

        if (pointerOverUI && freezeWorldCursorWhenPointerOverUI)
        {
            currentCursorSpeed = 0f;
            return 0f;
        }

        bool hadWorldPosition = hasWorldPosition;
        Vector3 previousWorldPosition = currentWorldPosition;
        Vector3 desiredWorld = GetDesiredWorldPosition(deltaTime);
        Vector3 nextWorld = desiredWorld;

        if (clampCursorToRoom && roomGrid != null)
        {
            if (roomGrid.HasRoomCells)
            {
                nextWorld = roomGrid.ClampPlayerWorldPoint(desiredWorld, currentWorldPosition, PlayerCollisionRadius);
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
        Vector3 worldVelocity = new Vector3(velocityInput.x, velocityInput.y, 0f) * maxCursorMoveSpeed;
        return currentWorldPosition + worldVelocity * Mathf.Max(0f, deltaTime);
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
        ApplyHeadingPointVisualSettings();
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

    private void ApplyHeadingPointVisualSettings()
    {
        if (headingPointImage == null)
            return;

        Sprite sprite = headingPointSprite != null ? headingPointSprite : GetRuntimeHeadingPointSprite();
        if (sprite != null)
            headingPointImage.sprite = sprite;

        Color color = headingPointColor;
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

    private void DestroyRuntimeHeadingPointAssets()
    {
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
        bool playerIsInRoom = roomGrid == null || !roomGrid.HasRoomCells || !hasWorldPosition ||
                              roomGrid.ContainsWorldPoint(playerPosition, PlayerCollisionRadius);

        Gizmos.color = playerIsInRoom ? debugPlayerColor : debugBlockedPlayerColor;
        Gizmos.DrawWireSphere(playerPosition, radius);
        Gizmos.DrawLine(playerPosition + Vector3.left * radius, playerPosition + Vector3.right * radius);
        Gizmos.DrawLine(playerPosition + Vector3.down * radius, playerPosition + Vector3.up * radius);

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
