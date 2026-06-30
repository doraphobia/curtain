using UnityEngine;
using DuoCurtain.RuntimeTileMesh;

/// <summary>
/// Bridges <see cref="IDamageable"/> to the project's active sanity system.
/// Attach to the Player object (or a child) for enemy attacks.
/// </summary>
[DisallowMultipleComponent]
public class PlayerSanityDamageable : MonoBehaviour, IDamageable
{
    public SanitySystem sanitySystem;
    public FusionSanityController fusionSanitySystem;

    public bool IsAlive
    {
        get
        {
            ResolveReferences();
            if (fusionSanitySystem != null)
                return !fusionSanitySystem.IsDead;
            return sanitySystem == null || sanitySystem.CurrentSanity > 0f;
        }
    }

    void Awake()
    {
        ResolveReferences();
    }

    private void ResolveReferences()
    {
        if (sanitySystem == null)
            sanitySystem = FindFirstObjectByType<SanitySystem>();
        if (fusionSanitySystem == null)
            fusionSanitySystem = FusionSanityController.Active != null
                ? FusionSanityController.Active
                : FindFirstObjectByType<FusionSanityController>();
    }

    public void TakeDamage(float amount)
    {
        if (amount <= 0f)
            return;

        ResolveReferences();
        if (fusionSanitySystem != null)
        {
            fusionSanitySystem.DrainSanity(amount);
            return;
        }

        if (sanitySystem == null)
            return;

        sanitySystem.DrainSanity(amount);
    }
}
