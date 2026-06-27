using UnityEngine;

[DisallowMultipleComponent]
public class FoleyMovementEmitter : MonoBehaviour
{
    [Header("References")]
    public FoleyPlayer foleyPlayer;
    public FoleyProfile movementProfile;
    public FoleyStepClock stepClock;
    public Transform targetTransform;
    public string surfaceIdOverride;

    [Header("Triggering")]
    public bool trackTransformMotion = true;
    public bool useStepClock = true;
    [Min(0.01f)]
    public float distancePerTrigger = 1.2f;
    [Min(0f)]
    public float minSecondsBetweenTriggers = 0.08f;
    [Range(0f, 1f)]
    public float volumeMultiplier = 1f;
    public bool useUnscaledTime = false;

    [Header("Idle Reset")]
    public bool resetNuisanceWhenIdle = true;
    [Min(0f)]
    public float idleSecondsBeforeReset = 0.4f;

    private Vector3 previousPosition;
    private float distanceAccumulator;
    private float nextTriggerTime;
    private float lastMovementTime;
    private bool hasPreviousPosition;
    private bool hasResetAfterIdle;

    void Awake()
    {
        ResolveReferences();
        InitializePosition();
    }

    void Update()
    {
        ResolveReferences();

        if (trackTransformMotion && targetTransform != null)
            TrackTransformMotion();

        TryResetNuisanceAfterIdle();
    }

    public void AddMovement(float movedDistance, Vector3 worldPosition)
    {
        if (movedDistance <= 0.0001f)
            return;

        ResolveReferences();
        if (foleyPlayer == null || movementProfile == null)
            return;

        float now = GetTime();
        lastMovementTime = now;
        hasResetAfterIdle = false;

        if (useStepClock && stepClock != null)
        {
            float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (!stepClock.Tick(movedDistance, GetSpeed(movedDistance), deltaTime, worldPosition, out FoleyStepClock.StepData stepData))
                return;

            if (foleyPlayer.Play(
                    movementProfile,
                    worldPosition,
                    volumeMultiplier * stepData.volumeMultiplier,
                    surfaceIdOverride,
                    stepData.pitchMultiplier,
                    stepData.delayMultiplier))
            {
                return;
            }
        }

        distanceAccumulator += movedDistance;

        if (distanceAccumulator < distancePerTrigger || now < nextTriggerTime)
            return;

        if (!foleyPlayer.Play(movementProfile, worldPosition, volumeMultiplier, surfaceIdOverride))
            return;

        distanceAccumulator = 0f;
        nextTriggerTime = now + minSecondsBetweenTriggers;
    }

    public void ResetEmitter()
    {
        distanceAccumulator = 0f;
        nextTriggerTime = 0f;
        hasPreviousPosition = false;
        hasResetAfterIdle = false;
        if (foleyPlayer != null && movementProfile != null)
            foleyPlayer.ResetNuisance(movementProfile);
    }

    private void TrackTransformMotion()
    {
        if (!hasPreviousPosition)
        {
            InitializePosition();
            return;
        }

        Vector3 currentPosition = targetTransform.position;
        float movedDistance = Vector2.Distance(previousPosition, currentPosition);
        previousPosition = currentPosition;
        AddMovement(movedDistance, currentPosition);
    }

    private void TryResetNuisanceAfterIdle()
    {
        if (!resetNuisanceWhenIdle || hasResetAfterIdle || foleyPlayer == null || movementProfile == null)
            return;

        float now = GetTime();
        if (now < lastMovementTime + idleSecondsBeforeReset)
            return;

        foleyPlayer.ResetNuisance(movementProfile);
        hasResetAfterIdle = true;
    }

    private void ResolveReferences()
    {
        if (foleyPlayer == null)
            foleyPlayer = GetComponent<FoleyPlayer>();

        if (stepClock == null)
            stepClock = GetComponent<FoleyStepClock>();

        if (targetTransform == null)
            targetTransform = transform;
    }

    private void InitializePosition()
    {
        if (targetTransform == null)
            return;

        previousPosition = targetTransform.position;
        hasPreviousPosition = true;
        lastMovementTime = GetTime();
    }

    private float GetSpeed(float movedDistance)
    {
        float deltaTime = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        return deltaTime > 0f ? movedDistance / deltaTime : 0f;
    }

    private float GetTime()
    {
        return useUnscaledTime ? Time.unscaledTime : Time.time;
    }
}
