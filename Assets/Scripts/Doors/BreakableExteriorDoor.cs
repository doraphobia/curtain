using UnityEngine;

/// <summary>
/// Exterior entry door for enemies. Separate from fusion interior <see cref="DuoCurtain.RuntimeTileMesh.RuntimeTileMeshFusionDoor"/>.
/// Enemy must break this door before entering; break speed is supplied by the attacker.
/// </summary>
[DisallowMultipleComponent]
public class BreakableExteriorDoor : MonoBehaviour
{
    [Header("Room")]
    public Room ownerRoom;

    [Header("Door Type")]
    public bool isExteriorDoor = true;
    public bool canBeBroken = true;
    [Min(0.01f)]
    public float breakRequiredAmount = 100f;

    [Header("State")]
    [SerializeField] private float currentBreakAmount;
    [SerializeField] private bool isBroken;
    [SerializeField] private bool isOpen;

    [Header("Anchors")]
    public Transform outsideAnchor;
    public Transform insideAnchor;
    public Transform progressBarAnchor;

    [Header("Blocking")]
    public Collider2D doorBlocker;
    public SpriteRenderer doorSprite;

    [Header("Break UI")]
    public bool showDoorBreakProgress = true;
    public Vector3 doorProgressBarOffset = new Vector3(0f, 1.2f, 0f);
    public bool hideProgressBarWhenNotBreaking = true;
    public GameObject progressBarPrefab;
    public DoorBreakProgressBar progressBarInstance;

    public float CurrentBreakAmount => currentBreakAmount;
    public float NormalizedProgress => breakRequiredAmount > 0f
        ? Mathf.Clamp01(currentBreakAmount / breakRequiredAmount)
        : 0f;
    public bool IsBroken => isBroken;
    public bool IsOpen => isOpen;
    public bool IsBeingBroken { get; private set; }

    public Vector3 OutsideApproachPosition =>
        outsideAnchor != null ? outsideAnchor.position : transform.position;

    public Vector3 InsideEntryPosition =>
        insideAnchor != null ? insideAnchor.position : transform.position;

    void Awake()
    {
        if (doorBlocker == null)
            doorBlocker = GetComponent<Collider2D>();

        if (doorSprite == null)
            doorSprite = GetComponent<SpriteRenderer>();

        EnsureProgressBar();
        ApplyBrokenState();
    }

    void OnValidate()
    {
        breakRequiredAmount = Mathf.Max(0.01f, breakRequiredAmount);
        currentBreakAmount = Mathf.Clamp(currentBreakAmount, 0f, breakRequiredAmount);
    }

    public void ApplyBreakProgress(float amount)
    {
        if (!canBeBroken || isBroken || amount <= 0f)
            return;

        IsBeingBroken = true;
        currentBreakAmount = Mathf.Min(currentBreakAmount + amount, breakRequiredAmount);
        EnsureProgressBar();
        progressBarInstance?.SetProgress(NormalizedProgress, true);

        if (currentBreakAmount >= breakRequiredAmount - 0.0001f)
            CompleteBreak();
    }

    public void StopBreaking()
    {
        IsBeingBroken = false;
        if (hideProgressBarWhenNotBreaking && progressBarInstance != null && !isBroken)
            progressBarInstance.SetVisible(false);
    }

    public void ResetDoor()
    {
        currentBreakAmount = 0f;
        isBroken = false;
        isOpen = false;
        IsBeingBroken = false;
        ApplyBrokenState();
        progressBarInstance?.SetProgress(0f, false);
    }

    private void CompleteBreak()
    {
        isBroken = true;
        isOpen = true;
        IsBeingBroken = false;
        ApplyBrokenState();
        progressBarInstance?.SetProgress(1f, true);
    }

    private void ApplyBrokenState()
    {
        if (doorBlocker != null)
            doorBlocker.enabled = !isOpen;

        if (doorSprite != null)
            doorSprite.enabled = !isBroken;
    }

    private void EnsureProgressBar()
    {
        if (!showDoorBreakProgress)
            return;

        if (progressBarInstance != null)
            return;

        Transform anchor = progressBarAnchor != null ? progressBarAnchor : transform;
        if (progressBarPrefab != null)
        {
            GameObject instance = Instantiate(progressBarPrefab, anchor);
            instance.transform.localPosition = doorProgressBarOffset;
            progressBarInstance = instance.GetComponent<DoorBreakProgressBar>();
        }

        if (progressBarInstance == null)
            progressBarInstance = DoorBreakProgressBar.CreateDefault(anchor, doorProgressBarOffset);

        progressBarInstance.SetVisible(false);
    }
}
