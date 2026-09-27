/// <summary>
/// Contract for any entity that can receive damage.
/// Implemented by Player, Enemy, and any damageable object.
/// Decouples the damage source (Hitbox) from the damage receiver — Dependency Inversion.
/// </summary>
public interface IDamageable
{
    /// <summary>Current hit points.</summary>
    int CurrentHealth { get; }

    /// <summary>Whether the entity has no remaining hit points.</summary>
    bool IsDead { get; }

    /// <summary>
    /// Applies damage to the entity. Implementations must enforce the
    /// min/max health clamp and iframes logic internally.
    /// </summary>
    /// <param name="amount">Damage points to subtract (always positive).</param>
    /// <param name="source">World position of the damage source (used for knockback direction).</param>
    void TakeDamage(int amount, UnityEngine.Vector3 source);
}
