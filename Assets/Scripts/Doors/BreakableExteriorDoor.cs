using DuoCurtain.Combat;
using UnityEngine;

/// <summary>
/// Exterior entry door for enemies. Separate from fusion interior <see cref="DuoCurtain.RuntimeTileMesh.RuntimeTileMeshFusionDoor"/>.
/// Enemy must break this door before entering; break speed is supplied by the attacker.
/// </summary>
[DisallowMultipleComponent]
public class BreakableExteriorDoor : MonoBehaviour, IDamageReceiver
{
    [Header("Room")]
    public Room ownerRoom;

    [Header("Door Type")]
    public bool isExteriorDoor = true;
    public bool canBeBroken = true;
    [Min(0.01f)]
    public float breakRequiredAmount = 100f;
    public bool invulnerable;
    [Min(0f)] public float destroyDelay;
    public ImpactFeedbackPreset impactPreset;

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

    private CombatHealth combatHealth;
    private DamageReceiverProgressPresenter progressPresenter;
    private ImpactObjectFeedback impactFeedback;

    public float CurrentBreakAmount => combatHealth != null
        ? Mathf.Max(0f, combatHealth.MaxHealth - combatHealth.CurrentHealth)
        : currentBreakAmount;
    public float NormalizedProgress => combatHealth != null ? 1f - combatHealth.NormalizedHealth : 0f;
    public bool IsBroken => isBroken;
    public bool IsOpen => isOpen;
    public bool IsBeingBroken { get; private set; }
    public GameObject ReceiverObject => gameObject;
    public float CurrentHealth => combatHealth != null ? combatHealth.CurrentHealth : breakRequiredAmount;
    public float MaxHealth => combatHealth != null ? combatHealth.MaxHealth : breakRequiredAmount;
    public float NormalizedHealth => combatHealth != null ? combatHealth.NormalizedHealth : 1f;
    bool IDamageReceiver.IsDestroyed => isBroken;

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
        EnsureCombatHealth();
        ApplyBrokenState();
    }

    void OnValidate()
    {
        breakRequiredAmount = Mathf.Max(0.01f, breakRequiredAmount);
        currentBreakAmount = Mathf.Clamp(currentBreakAmount, 0f, breakRequiredAmount);
        destroyDelay = Mathf.Max(0f, destroyDelay);
        if (Application.isPlaying)
            EnsureCombatHealth();
    }

    public void ApplyBreakProgress(float amount)
    {
        ReceiveDamage(new DamageRequest(null, amount, transform.position, Vector2.right, impactPreset));
    }

    public DamageResult ReceiveDamage(DamageRequest request)
    {
        EnsureCombatHealth();
        if (!canBeBroken || isBroken || combatHealth == null)
            return new DamageResult(false, CurrentHealth, CurrentHealth, isBroken);

        IsBeingBroken = true;
        ImpactFeedbackPreset selectedPreset = request.impactPreset != null ? request.impactPreset : impactPreset;
        DamageResult result = combatHealth.ReceiveDamage(new DamageRequest(
            request.source,
            request.amount,
            request.worldPosition,
            request.direction,
            selectedPreset,
            request.randomSeed));
        currentBreakAmount = CurrentBreakAmount;
        return result;
    }

    public void Repair(float amount)
    {
        EnsureCombatHealth();
        combatHealth?.Repair(amount);
        currentBreakAmount = CurrentBreakAmount;
        if (combatHealth != null && !combatHealth.IsDestroyed)
        {
            isBroken = false;
            isOpen = false;
            ApplyBrokenState();
        }
    }

    public void StopBreaking()
    {
        IsBeingBroken = false;
        if (hideProgressBarWhenNotBreaking && progressBarInstance != null && !isBroken)
            progressBarInstance.SetVisible(false);
    }

    public void ResetDoor()
    {
        ResetHealth();
    }

    public void ResetHealth()
    {
        EnsureCombatHealth();
        combatHealth?.ResetHealth();
        currentBreakAmount = 0f;
        isBroken = false;
        isOpen = false;
        IsBeingBroken = false;
        ApplyBrokenState();
        progressBarInstance?.SetProgress(1f, false);
    }

    public void DestroyDoor()
    {
        isBroken = true;
        isOpen = true;
        IsBeingBroken = false;
        ApplyBrokenState();
        progressBarInstance?.SetProgress(1f, true);
    }

    private void EnsureCombatHealth()
    {
        if (combatHealth == null)
            combatHealth = GetComponent<CombatHealth>();
        if (combatHealth == null)
            combatHealth = gameObject.AddComponent<CombatHealth>();
        combatHealth.Configure(breakRequiredAmount, invulnerable || !canBeBroken, destroyDelay, impactPreset);
        combatHealth.Destroyed -= HandleDestroyed;
        combatHealth.Destroyed += HandleDestroyed;

        EnsureProgressBar();
        if (progressPresenter == null)
            progressPresenter = GetComponent<DamageReceiverProgressPresenter>();
        if (progressPresenter == null)
            progressPresenter = gameObject.AddComponent<DamageReceiverProgressPresenter>();
        progressPresenter.progressBar = progressBarInstance;
        progressPresenter.hideDelay = hideProgressBarWhenNotBreaking ? 0.9f : 999999f;
        progressPresenter.Bind(
            combatHealth,
            progressBarAnchor != null ? progressBarAnchor : transform,
            doorProgressBarOffset,
            progressBarInstance != null ? progressBarInstance.smoothSpeed : 8f);

        if (impactFeedback == null)
            impactFeedback = GetComponent<ImpactObjectFeedback>();
        if (impactFeedback == null)
            impactFeedback = gameObject.AddComponent<ImpactObjectFeedback>();
        impactFeedback.receiverObject = gameObject;
        impactFeedback.visualTarget = doorSprite != null ? doorSprite.transform : transform;
    }

    private void HandleDestroyed(CombatHealth source, DamageResult result)
    {
        DestroyDoor();
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
