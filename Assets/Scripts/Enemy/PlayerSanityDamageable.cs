using UnityEngine;

/// <summary>
/// Bridges <see cref="IDamageable"/> to the project's existing <see cref="SanitySystem"/>.
/// Attach to the Player object (or a child) for enemy attacks.
/// </summary>
[DisallowMultipleComponent]
public class PlayerSanityDamageable : MonoBehaviour, IDamageable
{
    public SanitySystem sanitySystem;

    public bool IsAlive => sanitySystem == null || sanitySystem.CurrentSanity > 0f;

    void Awake()
    {
        if (sanitySystem == null)
            sanitySystem = FindFirstObjectByType<SanitySystem>();
    }

    public void TakeDamage(float amount)
    {
        if (sanitySystem == null || amount <= 0f)
            return;

        sanitySystem.DrainSanity(amount);
    }
}
