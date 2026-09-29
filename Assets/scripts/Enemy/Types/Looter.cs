using UnityEngine;

/// <summary>
/// Looter (Saqueador) — Basic enemy type from Region 1: Las Ruinas de Ashfall.
///
/// GDD Profile:
///   - HP: 50  |  Reward: 20 XP + scrap
///   - Behavior: attacks directly, almost no defense.
///   - Weakness: dodge and counter-attack.
///
/// This subclass adds no new states — it inherits Enemy base behavior.
/// Subclassing exists to allow prefab-specific configuration and future
/// Looter-exclusive logic (e.g., flee at low HP) without modifying the base class.
/// Open/Closed Principle in action.
/// </summary>
public class Looter : Enemy
{
    // ─── Looter-Specific Inspector Fields ────────────────────────────────────────
    [Header("Looter Behaviour")]
    [Tooltip("HP percentage below which the Looter attempts to flee. 0 = never flees.")]
    [SerializeField] private float fleeHealthThreshold = 0f; // Disabled by default for MVP

    // Future expansion: override OnSpawn() to play a taunt sound, etc.
}
