using System.Collections.Generic;
using DuoCurtain.RuntimeTileMesh;
using UnityEngine;

/// <summary>
/// Invisible-enemy footprint trace: alternating L/R prefabs, latest vs residual decay, state-driven spawning.
/// </summary>
[DisallowMultipleComponent]
public class EnemyFootprintTrace : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private EnemyController enemyController;
    [SerializeField] private PrefabFootprintRenderer footprintRenderer;
    [SerializeField] private EnemyFootstepAudio footstepAudio;
    [SerializeField] private MonoBehaviour surfaceModifierComponent;

    [Header("Enemy Visibility")]
    [SerializeField] private bool hideEnemyBody = true;
    [SerializeField] private Renderer[] enemyRenderersToHide;

    [Header("Footprint Prefabs")]
    [SerializeField] private GameObject leftFootprintPrefab;
    [SerializeField] private GameObject rightFootprintPrefab;
    [SerializeField] private Transform footprintParent;

    [Header("Step Timing")]
    [SerializeField] private float baseStepInterval = 0.35f;
    [SerializeField] private float fastStepInterval = 0.18f;
    [SerializeField] private bool scaleStepIntervalByMoveSpeed = true;
    [SerializeField] private float referenceMoveSpeed = 2.5f;
    [SerializeField] private float minMoveDistanceForStep = 0.25f;
    [SerializeField] private float movementThreshold = 0.02f;

    [Header("Footprint Placement")]
    [SerializeField] private float sideSpacing = 0.18f;
    [SerializeField] private float forwardOffset = 0.1f;
    [SerializeField] private float verticalOffset = 0f;
    [SerializeField] private bool alignToMoveDirection = true;
    [SerializeField] private bool useEnemyForwardWhenStationary = true;
    [SerializeField] private float footprintRotationOffset = 0f;

    [Header("Footprint Lifetime")]
    [SerializeField] private int maxFootprintPairs = 6;
    [SerializeField] private bool removeOldestAsSingleFootprint = true;
    [SerializeField] private bool fadeOldestBeforeDestroy = true;
    [SerializeField] private float forcedOldestFadeOutDuration = 0.4f;
    [SerializeField] private bool useTimeBasedLifetime = false;
    [SerializeField] private float footprintLifetime = 8f;

    [Header("Opacity Animation")]
    [SerializeField] private FootprintVisualProfile visualProfile = new FootprintVisualProfile();
    [SerializeField] private float fadeInDuration = 0.12f;
    [SerializeField] private AnimationCurve fadeInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private float residualDecayDuration = 0.25f;
    [SerializeField] private AnimationCurve residualDecayCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.75f);
    [SerializeField] private float fadeOutDuration = 0.6f;
    [SerializeField] private AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("Latest vs Residual Visuals")]
    [SerializeField] private float latestAlpha = 1f;
    [SerializeField] private float residualAlphaMultiplier = 0.78f;
    [SerializeField] private float minimumResidualAlpha = 0.05f;
    [SerializeField] private Color normalFootprintColor = Color.white;
    [SerializeField] private Color breakingDoorFootprintColor = Color.red;
    [SerializeField] private bool autoContrastFootprintColor = true;
    [SerializeField] private Color lightSurfaceFootprintColor = new Color(0f, 0f, 0f, 0.78f);
    [SerializeField] private Color darkSurfaceFootprintColor = new Color(1f, 1f, 1f, 0.9f);
    [SerializeField] private bool tintLastPairRedWhenBreakingDoor = true;

    [Header("Enemy State Integration")]
    [SerializeField] private bool spawnOnlyWhenMoving = true;
    [SerializeField] private bool pauseWhenWatching = true;
    [SerializeField] private bool pauseWhenBreakingDoor = true;
    [SerializeField] private bool useFastStepsWhenTargetingDoor = true;
    [SerializeField] private bool preserveLastPairWhenStopped = true;
    [SerializeField] private bool denyIndoorFootprintsBeforeEntry = true;

    [Header("Debug")]
    [SerializeField] private bool debugDrawStepPositions = true;
    [SerializeField] private bool debugLogFootprintStateChanges = false;
    [SerializeField] private bool debugShowCurrentTraceState = true;
    [SerializeField] private bool debugLogFootprintDenials = false;
    [SerializeField] private EnemyTraceState currentTraceState = EnemyTraceState.NormalMoving;

    private IFootprintSurfaceModifier SurfaceModifier =>
        surfaceModifierComponent as IFootprintSurfaceModifier;

    private readonly List<FootprintInstance> footprints = new List<FootprintInstance>();
    private Transform runtimeFootprintParent;
    private Vector3 lastPosition;
    private Vector3 lastMoveDirection = Vector3.up;
    private float distanceSinceLastStep;
    private float stepTimer;
    private bool nextStepIsLeft = true;
    private bool breakingDoorTintApplied;
    private float spawnTimeAccumulator;
    private FusionNightFootprintEnemy fusionTraceEnemy;

    public EnemyTraceState CurrentTraceState => currentTraceState;
    public int ActiveFootprintCount => footprints.Count;
    public int MaxIndividualFootprints => Mathf.Max(1, maxFootprintPairs) * 2;
    public bool PreserveLastPairWhenStopped => preserveLastPairWhenStopped;

    public void ClearFootprints(bool immediate)
    {
        for (int i = footprints.Count - 1; i >= 0; i--)
        {
            FootprintInstance footprint = footprints[i];
            if (footprint == null)
                continue;

            if (!immediate)
            {
                footprint.FadeOutAndDestroy(fadeOutDuration, fadeOutCurve);
                continue;
            }

            if (Application.isPlaying)
                Destroy(footprint.gameObject);
            else
                DestroyImmediate(footprint.gameObject);
        }

        footprints.Clear();
    }

    public void ConfigureFootprintPrefabs(GameObject leftPrefab, GameObject rightPrefab, Transform parent = null)
    {
        leftFootprintPrefab = leftPrefab;
        rightFootprintPrefab = rightPrefab;
        if (parent != null)
        {
            footprintParent = parent;
            runtimeFootprintParent = parent;
        }

        if (footprintRenderer == null)
            footprintRenderer = GetComponent<PrefabFootprintRenderer>();
        if (footprintRenderer == null)
            footprintRenderer = gameObject.AddComponent<PrefabFootprintRenderer>();

        SyncVisualProfileFromInspector();
        footprintRenderer.Configure(leftFootprintPrefab, rightFootprintPrefab, visualProfile);
    }

    public void ConfigureFootprintColors(Color normalColor, Color breakingColor)
    {
        normalFootprintColor = normalColor;
        breakingDoorFootprintColor = breakingColor;
        SyncVisualProfileFromInspector();
        if (footprintRenderer != null)
            footprintRenderer.Configure(leftFootprintPrefab, rightFootprintPrefab, visualProfile);
    }

    void Awake()
    {
        if (enemyController == null)
            enemyController = GetComponent<EnemyController>();
        if (fusionTraceEnemy == null)
            fusionTraceEnemy = GetComponent<FusionNightFootprintEnemy>();

        if (footprintRenderer == null)
            footprintRenderer = GetComponent<PrefabFootprintRenderer>();
        if (footprintRenderer == null)
            footprintRenderer = gameObject.AddComponent<PrefabFootprintRenderer>();

        if (footstepAudio == null)
            footstepAudio = GetComponent<EnemyFootstepAudio>();
        if (footstepAudio == null)
            footstepAudio = gameObject.AddComponent<EnemyFootstepAudio>();

        SyncVisualProfileFromInspector();
        footprintRenderer.Configure(leftFootprintPrefab, rightFootprintPrefab, visualProfile);

        if (footprintParent == null)
        {
            GameObject parentObject = new GameObject("Enemy Footprints");
            runtimeFootprintParent = parentObject.transform;
        }
        else
        {
            runtimeFootprintParent = footprintParent;
        }

        lastPosition = transform.position;
        ApplyEnemyVisibility();
    }

    void Update()
    {
        if (PauseManager.IsGamePaused || currentTraceState == EnemyTraceState.Disabled)
            return;

        Vector3 currentPosition = transform.position;
        Vector3 frameDelta = currentPosition - lastPosition;
        float frameDistance = new Vector2(frameDelta.x, frameDelta.y).magnitude;
        if (frameDistance > movementThreshold)
        {
            lastMoveDirection = frameDelta.normalized;
            distanceSinceLastStep += frameDistance;
        }

        if (useTimeBasedLifetime)
            UpdateTimeBasedLifetime();

        if (!ShouldSpawnFootprints())
        {
            lastPosition = currentPosition;
            return;
        }

        stepTimer += Time.deltaTime;
        float interval = GetCurrentStepInterval();
        if (stepTimer < interval || distanceSinceLastStep < minMoveDistanceForStep)
        {
            lastPosition = currentPosition;
            return;
        }

        SpawnFootprint();
        stepTimer = 0f;
        distanceSinceLastStep = 0f;
        lastPosition = currentPosition;
    }

    public void SetTraceState(EnemyTraceState newState)
    {
        if (currentTraceState == newState)
            return;

        if (debugLogFootprintStateChanges)
            Debug.Log("[EnemyFootprintTrace] " + name + " trace state: " + currentTraceState + " -> " + newState, this);

        EnemyTraceState previous = currentTraceState;
        currentTraceState = newState;

        if (newState == EnemyTraceState.BreakingDoor)
            OnStartedBreakingDoor();
        else if (previous == EnemyTraceState.BreakingDoor)
            OnStoppedBreakingDoor();
        else if (newState == EnemyTraceState.TargetingDoor)
            OnTargetingDoor();
    }

    public void SyncFromEnemyState(EnemyController.EnemyState enemyState)
    {
        switch (enemyState)
        {
            case EnemyController.EnemyState.SpawnOutside:
                SetTraceState(EnemyTraceState.NormalMoving);
                break;
            case EnemyController.EnemyState.SearchOutside:
            case EnemyController.EnemyState.DetectPlayer:
                SetTraceState(EnemyTraceState.Watching);
                break;
            case EnemyController.EnemyState.MoveToExteriorDoor:
                SetTraceState(EnemyTraceState.TargetingDoor);
                break;
            case EnemyController.EnemyState.BreakingDoor:
                SetTraceState(EnemyTraceState.BreakingDoor);
                break;
            case EnemyController.EnemyState.EnterRoom:
            case EnemyController.EnemyState.ChasePlayer:
                SetTraceState(EnemyTraceState.ChasingPlayer);
                break;
            case EnemyController.EnemyState.AttackPlayer:
                SetTraceState(EnemyTraceState.Attacking);
                break;
            case EnemyController.EnemyState.LostPlayer:
                SetTraceState(EnemyTraceState.Watching);
                break;
            case EnemyController.EnemyState.SearchLastKnownRoom:
                SetTraceState(enemyController != null && enemyController.HasTargetDoor
                    ? EnemyTraceState.TargetingDoor
                    : EnemyTraceState.Watching);
                break;
            default:
                SetTraceState(EnemyTraceState.NormalMoving);
                break;
        }
    }

    public void OnTargetingDoor() { }

    public void OnStartedBreakingDoor()
    {
        if (pauseWhenBreakingDoor)
            currentTraceState = EnemyTraceState.BreakingDoor;

        if (tintLastPairRedWhenBreakingDoor && !breakingDoorTintApplied)
        {
            TintLastFootprintPair(breakingDoorFootprintColor);
            breakingDoorTintApplied = true;
        }
    }

    public void OnStoppedBreakingDoor()
    {
        breakingDoorTintApplied = false;
    }

    private void SpawnFootprint()
    {
        DecayExistingFootprints();

        FootprintSide side = nextStepIsLeft ? FootprintSide.Left : FootprintSide.Right;
        nextStepIsLeft = !nextStepIsLeft;

        Vector2 moveDirection = GetFootprintFacingDirection();
        Vector2 perpendicular = new Vector2(-moveDirection.y, moveDirection.x);
        float sideSign = side == FootprintSide.Left ? -1f : 1f;
        Vector3 basePosition = transform.position;
        Vector3 spawnPosition = basePosition
            + (Vector3)(moveDirection * forwardOffset)
            + (Vector3)(perpendicular * sideSpacing * sideSign)
            + new Vector3(0f, verticalOffset, 0f);

        float angle = Mathf.Atan2(moveDirection.y, moveDirection.x) * Mathf.Rad2Deg;
        Quaternion rotation = Quaternion.Euler(0f, 0f, angle + footprintRotationOffset - 90f);

        FootprintSpawnData data = new FootprintSpawnData
        {
            position = spawnPosition,
            rotation = rotation,
            side = side,
            isLatest = true,
            color = ResolveFootprintColor(spawnPosition),
            alpha = latestAlpha,
            parent = runtimeFootprintParent,
            surfaceContext = null
        };
        SurfaceModifier?.ModifyFootprint(ref data);
        Color displayColor = new Color(data.color.r, data.color.g, data.color.b, 1f);
        float displayAlpha = Mathf.Clamp01(data.alpha * data.color.a);
        FootprintInstance instance = footprintRenderer.SpawnFootprint(data);
        if (instance == null)
            return;

        instance.SetAsLatest(displayAlpha, displayColor);
        footprints.Add(instance);
        EnforceMaxFootprintCount();
        footstepAudio?.PlayFootstep(spawnPosition, side);
    }

    private void DecayExistingFootprints()
    {
        for (int i = footprints.Count - 1; i >= 0; i--)
        {
            FootprintInstance footprint = footprints[i];
            if (footprint == null)
            {
                footprints.RemoveAt(i);
                continue;
            }

            int decayIndex = footprint.IsLatest ? 1 : footprint.DecayIndex + 1;
            float alpha = CalculateResidualAlpha(decayIndex);
            footprint.SetResidual(decayIndex, alpha, footprint.BaseColor);
        }
    }

    private Color ResolveFootprintColor(Vector3 spawnPosition)
    {
        if (!autoContrastFootprintColor)
            return normalFootprintColor;

        return IsLightSurface(spawnPosition)
            ? lightSurfaceFootprintColor
            : darkSurfaceFootprintColor;
    }

    private bool IsLightSurface(Vector3 worldPosition)
    {
        if (RoomManager.IsInsideAnyRoom(worldPosition))
            return true;

        return fusionTraceEnemy != null && fusionTraceEnemy.IsInsideAnyFusionRoom(worldPosition);
    }

    private float CalculateResidualAlpha(int decayIndex)
    {
        float alpha = latestAlpha;
        for (int i = 0; i < decayIndex; i++)
            alpha *= residualAlphaMultiplier;

        return Mathf.Max(minimumResidualAlpha, alpha);
    }

    private void EnforceMaxFootprintCount()
    {
        if (!removeOldestAsSingleFootprint)
            return;

        int maxCount = MaxIndividualFootprints;
        while (footprints.Count > maxCount)
        {
            FootprintInstance oldest = footprints[0];
            footprints.RemoveAt(0);
            if (oldest == null)
                continue;

            if (fadeOldestBeforeDestroy)
                oldest.FadeOutAndDestroy(forcedOldestFadeOutDuration, fadeOutCurve);
            else
                Destroy(oldest.gameObject);
        }
    }

    private void TintLastFootprintPair(Color color)
    {
        int tinted = 0;
        for (int i = footprints.Count - 1; i >= 0 && tinted < 2; i--)
        {
            FootprintInstance footprint = footprints[i];
            if (footprint == null)
                continue;

            footprint.Tint(color);
            tinted++;
        }
    }

    private bool ShouldSpawnFootprints()
    {
        if (currentTraceState == EnemyTraceState.Disabled)
            return false;

        if (pauseWhenBreakingDoor && currentTraceState == EnemyTraceState.BreakingDoor)
            return false;

        if (pauseWhenWatching && currentTraceState == EnemyTraceState.Watching)
            return false;

        if (spawnOnlyWhenMoving && distanceSinceLastStep < movementThreshold)
            return false;

        if (denyIndoorFootprintsBeforeEntry && IsIndoorFootprintDenied(transform.position))
            return false;

        return true;
    }

    private bool IsIndoorFootprintDenied(Vector3 worldPosition)
    {
        if (CanSpawnIndoorFootprints())
            return false;

        bool insideRoom = RoomManager.IsInsideAnyRoom(worldPosition);
        if (!insideRoom && fusionTraceEnemy != null)
            insideRoom = fusionTraceEnemy.IsInsideAnyFusionRoom(worldPosition);

        if (!insideRoom)
            return false;

        if (debugLogFootprintDenials)
        {
            Debug.Log(
                "[Footprint] Spawn denied: indoor footprint while enemy has not entered room. Position=" +
                worldPosition,
                this);
        }

        return true;
    }

    private bool CanSpawnIndoorFootprints()
    {
        if (fusionTraceEnemy != null)
            return fusionTraceEnemy.HasEnemyEnteredRoom();

        if (enemyController == null)
            return true;

        EnemyController.EnemyState state = enemyController.CurrentState;
        return state == EnemyController.EnemyState.EnterRoom ||
               state == EnemyController.EnemyState.ChasePlayer ||
               state == EnemyController.EnemyState.AttackPlayer;
    }

    private float GetCurrentStepInterval()
    {
        float interval = baseStepInterval;
        if (useFastStepsWhenTargetingDoor &&
            (currentTraceState == EnemyTraceState.TargetingDoor || currentTraceState == EnemyTraceState.ChasingPlayer))
        {
            interval = fastStepInterval;
        }

        if (!scaleStepIntervalByMoveSpeed || enemyController == null || referenceMoveSpeed <= 0.0001f)
            return interval;

        float speed = enemyController.CurrentMoveSpeed;
        float speedScale = Mathf.Clamp(speed / referenceMoveSpeed, 0.5f, 2.5f);
        return interval / speedScale;
    }

    private Vector2 GetFootprintFacingDirection()
    {
        if (alignToMoveDirection && lastMoveDirection.sqrMagnitude > movementThreshold * movementThreshold)
            return lastMoveDirection.normalized;

        if (useEnemyForwardWhenStationary && enemyController != null)
            return enemyController.FacingDirection;

        return Vector2.up;
    }

    private void UpdateTimeBasedLifetime()
    {
        spawnTimeAccumulator += Time.deltaTime;
        if (spawnTimeAccumulator < footprintLifetime)
            return;

        spawnTimeAccumulator = 0f;
        if (footprints.Count == 0)
            return;

        FootprintInstance oldest = footprints[0];
        footprints.RemoveAt(0);
        if (oldest != null)
            oldest.FadeOutAndDestroy(fadeOutDuration, fadeOutCurve);
    }

    private void SyncVisualProfileFromInspector()
    {
        visualProfile.fadeInDuration = fadeInDuration;
        visualProfile.fadeInCurve = fadeInCurve;
        visualProfile.residualDecayDuration = residualDecayDuration;
        visualProfile.residualDecayCurve = residualDecayCurve;
        visualProfile.fadeOutDuration = fadeOutDuration;
        visualProfile.fadeOutCurve = fadeOutCurve;
        visualProfile.latestAlpha = latestAlpha;
        visualProfile.residualAlphaMultiplier = residualAlphaMultiplier;
        visualProfile.minimumResidualAlpha = minimumResidualAlpha;
        visualProfile.normalFootprintColor = normalFootprintColor;
        visualProfile.breakingDoorFootprintColor = breakingDoorFootprintColor;
    }

    private void ApplyEnemyVisibility()
    {
        if (!hideEnemyBody)
            return;

        if (enemyRenderersToHide == null || enemyRenderersToHide.Length == 0)
            enemyRenderersToHide = GetComponentsInChildren<Renderer>(true);

        for (int i = 0; i < enemyRenderersToHide.Length; i++)
        {
            if (enemyRenderersToHide[i] != null)
                enemyRenderersToHide[i].enabled = false;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!debugDrawStepPositions)
            return;

        Vector2 moveDirection = Application.isPlaying
            ? GetFootprintFacingDirection()
            : (Vector2)transform.up;
        Vector2 perpendicular = new Vector2(-moveDirection.y, moveDirection.x);
        Vector3 basePosition = transform.position;

        Gizmos.color = Color.cyan;
        Gizmos.DrawSphere(basePosition, 0.05f);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(basePosition, basePosition + (Vector3)(moveDirection * 0.35f));
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(basePosition + (Vector3)(perpendicular * sideSpacing), 0.04f);
        Gizmos.DrawSphere(basePosition - (Vector3)(perpendicular * sideSpacing), 0.04f);

#if UNITY_EDITOR
        if (debugShowCurrentTraceState && Application.isPlaying)
            UnityEditor.Handles.Label(basePosition + Vector3.up * 0.4f, currentTraceState.ToString());
#endif
    }

    void OnValidate()
    {
        maxFootprintPairs = Mathf.Max(1, maxFootprintPairs);
        SyncVisualProfileFromInspector();
    }
}
