using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public class LogicalCursorController : MonoBehaviour
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

    public static LogicalCursorController Active { get; private set; }

    [Header("References")]
    public Camera targetCamera;
    public TilePlacementGrid roomGrid;
    public Image uiCursorImage;
    public string uiCursorObjectName = "drag";

    [Header("System Cursor")]
    public bool hideSystemCursor = true;
    public bool confineSystemCursorToWindow = true;
    public bool restoreSystemCursorOnDisable = true;

    [Header("Logical Cursor")]
    public bool clampCursorToRoom = true;
    public bool freezeWorldCursorWhenPointerOverUI = true;
    public bool showUICursorAtSystemPointerOverUI = true;

    [Header("Cursor Movement")]
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

    [Header("Cursor Visual")]
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

    private RectTransform uiCursorRectTransform;
    private Canvas uiCursorCanvas;
    private Animator uiCursorAnimator;
    private Vector3 currentWorldPosition;
    private bool hasWorldPosition;
    private float currentCursorSpeed;
    private bool previousCursorVisible;
    private CursorLockMode previousCursorLockMode;
    private bool cursorStateApplied;
    private bool warnedAboutMissingRoomArea;
    private float footstepDistanceAccumulator;
    private float nextFootstepTime;
    private int lastFootstepClipIndex = -1;
    private bool stepTriggeredThisFrame;
    private FoleyStepClock.StepData currentStepData;

    public Vector3 CurrentWorldPosition => currentWorldPosition;
    public bool HasWorldPosition => hasWorldPosition;

    public static bool IsRunning => Active != null && Active.isActiveAndEnabled;
    public static bool HasActive => IsRunning && Active.hasWorldPosition;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateDefaultControllerIfNeeded()
    {
        if (FindFirstObjectByType<LogicalCursorController>() != null)
            return;

        if (FindFirstObjectByType<TilePlacementGrid>() == null)
            return;

        GameObject controllerObject = new GameObject("Interaction Manager");
        controllerObject.AddComponent<LogicalCursorController>();
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
    }

    void LateUpdate()
    {
        ResolveReferences();

        bool pointerOverUI = IsPointerOverUI();
        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        float movedDistance = UpdateLogicalWorldPosition(pointerOverUI, deltaTime);
        UpdateStepClock(movedDistance, deltaTime);
        ApplyCameraPan(pointerOverUI, deltaTime);
        UpdateUICursorVisual(pointerOverUI);
        UpdateCursorMotionEffects(movedDistance);
    }

    void OnDisable()
    {
        if (Active == this)
            Active = null;

        if (restoreSystemCursorOnDisable)
            RestoreSystemCursorState();
    }

    void OnDestroy()
    {
        if (Active == this)
            Active = null;

        if (restoreSystemCursorOnDisable)
            RestoreSystemCursorState();
    }

    public static bool TryGetWorldPosition(out Vector3 worldPosition)
    {
        if (HasActive)
        {
            worldPosition = Active.currentWorldPosition;
            return true;
        }

        worldPosition = default(Vector3);
        return false;
    }

    public static bool TryGetScreenPosition(out Vector2 screenPosition)
    {
        if (HasActive && Active.targetCamera != null)
        {
            screenPosition = Active.targetCamera.WorldToScreenPoint(Active.currentWorldPosition);
            return true;
        }

        screenPosition = default(Vector2);
        return false;
    }

    private void ResolveReferences()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (roomGrid == null)
            roomGrid = FindFirstObjectByType<TilePlacementGrid>();

        if (uiCursorImage == null)
            uiCursorImage = FindUICursorImageByName(uiCursorObjectName);

        if (uiCursorImage != null)
        {
            uiCursorImage.raycastTarget = false;
            uiCursorRectTransform = uiCursorImage.rectTransform;
            uiCursorCanvas = uiCursorImage.GetComponentInParent<Canvas>();
            ApplyCursorVisualSettings();
        }

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
            startWorld = roomGrid.ClampWorldPoint(startWorld, startWorld);

        startWorld.z = 0f;
        currentWorldPosition = startWorld;
        hasWorldPosition = true;
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
                nextWorld = roomGrid.ClampWorldPoint(desiredWorld, currentWorldPosition);
                warnedAboutMissingRoomArea = false;
            }
            else if (!warnedAboutMissingRoomArea)
            {
                Debug.LogWarning("[LogicalCursorController] No occupied room cells found. Cursor clamp is inactive until room cells are registered.");
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

    private void UpdateUICursorVisual(bool pointerOverUI)
    {
        if (uiCursorImage == null || uiCursorRectTransform == null)
            return;

        Vector2 screenPoint;
        if (pointerOverUI && showUICursorAtSystemPointerOverUI)
            screenPoint = Input.mousePosition;
        else if (targetCamera != null && hasWorldPosition)
            screenPoint = targetCamera.WorldToScreenPoint(currentWorldPosition);
        else
            screenPoint = Input.mousePosition;

        RectTransform parentRect = uiCursorRectTransform.parent as RectTransform;
        if (parentRect == null)
            return;

        Camera eventCamera = null;
        if (uiCursorCanvas != null && uiCursorCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            eventCamera = uiCursorCanvas.worldCamera != null ? uiCursorCanvas.worldCamera : targetCamera;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, eventCamera, out Vector2 localPoint))
            uiCursorRectTransform.anchoredPosition = localPoint;
    }

    private void ApplyCursorVisualSettings()
    {
        if (uiCursorImage == null)
            return;

        if (cursorSprite != null)
            uiCursorImage.sprite = cursorSprite;

        uiCursorAnimator = uiCursorImage.GetComponent<Animator>();
        if (cursorVisualMode == CursorVisualMode.Animator || cursorAnimatorController != null)
        {
            if (uiCursorAnimator == null)
                uiCursorAnimator = uiCursorImage.gameObject.AddComponent<Animator>();

            if (cursorAnimatorController != null)
                uiCursorAnimator.runtimeAnimatorController = cursorAnimatorController;

            uiCursorAnimator.enabled = true;
        }
        else
        {
            uiCursorAnimator = null;
        }
    }

    private void UpdateCursorMotionEffects(float movedDistance)
    {
        UpdateCursorAnimatorParameters();
        UpdateFootstepAudio(movedDistance);
    }

    private void UpdateCursorAnimatorParameters()
    {
        if (!driveAnimatorParameters || uiCursorAnimator == null || uiCursorAnimator.runtimeAnimatorController == null)
            return;

        bool isMoving = currentCursorSpeed > 0.01f;
        if (driveAnimatorPlaybackSpeedFromStepClock && useUnifiedStepClock && stepClock != null)
        {
            float min = Mathf.Min(animatorPlaybackSpeedRange.x, animatorPlaybackSpeedRange.y);
            float max = Mathf.Max(animatorPlaybackSpeedRange.x, animatorPlaybackSpeedRange.y);
            uiCursorAnimator.speed = isMoving
                ? stepClock.GetAnimatorSpeedMultiplier(animatorReferenceStepsPerSecond, min, max)
                : 1f;
        }

        if (!string.IsNullOrWhiteSpace(movingAnimatorBool) &&
            HasAnimatorParameter(movingAnimatorBool, AnimatorControllerParameterType.Bool))
        {
            uiCursorAnimator.SetBool(movingAnimatorBool, isMoving);
        }

        if (!string.IsNullOrWhiteSpace(speedAnimatorFloat) &&
            HasAnimatorParameter(speedAnimatorFloat, AnimatorControllerParameterType.Float))
        {
            uiCursorAnimator.SetFloat(speedAnimatorFloat, currentCursorSpeed);
        }

        if (useUnifiedStepClock && stepClock != null)
        {
            if (!string.IsNullOrWhiteSpace(stepPhaseAnimatorFloat) &&
                HasAnimatorParameter(stepPhaseAnimatorFloat, AnimatorControllerParameterType.Float))
            {
                uiCursorAnimator.SetFloat(stepPhaseAnimatorFloat, stepClock.StepPhase);
            }

            if (!string.IsNullOrWhiteSpace(stepRateAnimatorFloat) &&
                HasAnimatorParameter(stepRateAnimatorFloat, AnimatorControllerParameterType.Float))
            {
                uiCursorAnimator.SetFloat(stepRateAnimatorFloat, stepClock.CurrentStepsPerSecond);
            }

            if (fireStepAnimatorTrigger && stepTriggeredThisFrame &&
                !string.IsNullOrWhiteSpace(stepAnimatorTrigger) &&
                HasAnimatorParameter(stepAnimatorTrigger, AnimatorControllerParameterType.Trigger))
            {
                uiCursorAnimator.SetTrigger(stepAnimatorTrigger);
            }
        }
    }

    private bool HasAnimatorParameter(string parameterName, AnimatorControllerParameterType parameterType)
    {
        AnimatorControllerParameter[] parameters = uiCursorAnimator.parameters;
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

    private Image FindUICursorImageByName(string objectName)
    {
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
