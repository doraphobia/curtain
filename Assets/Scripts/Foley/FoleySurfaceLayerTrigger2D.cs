using UnityEngine;

[RequireComponent(typeof(Collider2D))]
[DisallowMultipleComponent]
public class FoleySurfaceLayerTrigger2D : MonoBehaviour
{
    [Header("References")]
    public FoleyPlayer foleyPlayer;
    public FoleyStepClock stepClock;
    public FoleyProfile layerProfile;
    public string surfaceIdOverride;

    [Header("Triggering")]
    public bool triggerFromPhysics = true;
    public bool triggerFromLogicalCursor = true;
    public bool playOnEnter = true;
    public bool playWhileInsideOnStep = false;
    [Range(0f, 1f)]
    public float volume = 1f;

    private Collider2D targetCollider;
    private bool logicalCursorInside;
    private int lastPlayedStepIndex = -1;

    void Awake()
    {
        targetCollider = GetComponent<Collider2D>();
        ResolvePlayer();
    }

    void Update()
    {
        if (!triggerFromLogicalCursor || targetCollider == null)
            return;

        ResolvePlayer();
        if (!LogicalCursorController.TryGetWorldPosition(out Vector3 cursorWorld))
            return;

        cursorWorld.z = transform.position.z;
        bool isInside = targetCollider.OverlapPoint(cursorWorld);
        bool entered = isInside && !logicalCursorInside;
        logicalCursorInside = isInside;

        if (entered && playOnEnter)
            Play(cursorWorld);

        if (isInside && playWhileInsideOnStep && stepClock != null)
        {
            int stepIndex = stepClock.LastStepData.index;
            if (stepIndex != lastPlayedStepIndex &&
                stepClock.LastStepData.stepsPerSecond > 0f &&
                stepClock.CurrentSpeed >= stepClock.minMovingSpeed)
            {
                lastPlayedStepIndex = stepIndex;
                Play(cursorWorld);
            }
        }
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!triggerFromPhysics || !playOnEnter)
            return;

        Play(other.transform.position);
    }

    public void Play(Vector3 worldPosition)
    {
        ResolvePlayer();
        if (foleyPlayer == null || layerProfile == null)
            return;

        string surfaceId = string.IsNullOrWhiteSpace(surfaceIdOverride) ? null : surfaceIdOverride;
        foleyPlayer.Play(layerProfile, worldPosition, volume, surfaceId);
    }

    private void ResolvePlayer()
    {
        if (foleyPlayer == null)
            foleyPlayer = GetComponent<FoleyPlayer>();

        if (stepClock == null)
            stepClock = GetComponent<FoleyStepClock>();
    }
}
