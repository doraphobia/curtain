using UnityEngine;

/// <summary>
/// Minimal damage contract for combat entities.
/// Player uses <see cref="PlayerSanityDamageable"/> to bridge into <see cref="SanitySystem"/>.
/// </summary>
public interface IDamageable
{
    void TakeDamage(float amount);
    bool IsAlive { get; }
}
