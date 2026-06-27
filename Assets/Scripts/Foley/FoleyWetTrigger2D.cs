using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class FoleyWetTrigger2D : MonoBehaviour
{
    [Header("References")]
    public FoleyPlayer foleyPlayer;
    public FoleyStepClock stepClock;
    public FoleyProfile wetProfile;
    public string surfaceIdOverride = "Wet";

    [Header("Triggering")]
    public bool triggerFromLogicalCursor = true;
    public bool playOnEnter = true;
    public bool playOnEachStepInside = true;
    [Range(0f, 1f)]
    public float volume = 1f;

    private Collider2D targetCollider;
    private bool logicalCursorInside;
    private int lastPlayedStepIndex = -1;

    void Awake()
    {
        targetCollider = GetComponent<Collider2D>();
        ResolveReferences();
    }

    void Update()
    {
        if (!triggerFromLogicalCursor || targetCollider == null)
            return;

        ResolveReferences();
        if (!LogicalCursorController.TryGetWorldPosition(out Vector3 cursorWorld))
            return;

        cursorWorld.z = transform.position.z;
        bool isInside = targetCollider.OverlapPoint(cursorWorld);
        bool entered = isInside && !logicalCursorInside;
        logicalCursorInside = isInside;

        if (entered && playOnEnter)
            Play(cursorWorld);

        if (!isInside || !playOnEachStepInside || stepClock == null)
            return;

        int stepIndex = stepClock.LastStepData.index;
        if (stepIndex == lastPlayedStepIndex ||
            stepClock.LastStepData.stepsPerSecond <= 0f ||
            stepClock.CurrentSpeed < stepClock.minMovingSpeed)
            return;

        lastPlayedStepIndex = stepIndex;
        Play(cursorWorld, stepClock.LastStepData);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!playOnEnter)
            return;

        Play(other.transform.position);
    }

    public void Play(Vector3 worldPosition)
    {
        Play(worldPosition, default(FoleyStepClock.StepData));
    }

    public void Play(Vector3 worldPosition, FoleyStepClock.StepData stepData)
    {
        ResolveReferences();
        if (foleyPlayer == null || wetProfile == null)
            return;

        string surfaceId = string.IsNullOrWhiteSpace(surfaceIdOverride) ? null : surfaceIdOverride;
        float pitchMultiplier = stepData.pitchMultiplier > 0f ? stepData.pitchMultiplier : 1f;
        float delayMultiplier = stepData.delayMultiplier > 0f ? stepData.delayMultiplier : 1f;
        float volumeMultiplier = stepData.volumeMultiplier > 0f ? stepData.volumeMultiplier : 1f;
        foleyPlayer.Play(wetProfile, worldPosition, volume * volumeMultiplier, surfaceId, pitchMultiplier, delayMultiplier);
    }

    private void ResolveReferences()
    {
        if (foleyPlayer == null)
            foleyPlayer = GetComponent<FoleyPlayer>();

        if (stepClock == null)
            stepClock = GetComponent<FoleyStepClock>();
    }
}
