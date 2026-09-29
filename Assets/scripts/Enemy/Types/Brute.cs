using UnityEngine;

/// <summary>
/// Brute (Matón) — Basic enemy type from Region 1: Las Ruinas de Ashfall.
///
/// GDD Profile:
///   - HP: 80  |  Reward: 30 XP + resources
///   - Behavior: slow, resistant, blocks occasionally.
///   - Weakness: fast attacks after its heavy blows.
///
/// This subclass introduces a BlockState override on the EnemyAttackState:
/// occasionally the Brute will block incoming damage, requiring the player
/// to time attacks correctly.
///
/// Architecture: inherits Enemy base, overrides BuildStateMachine to inject
/// the BruteBlockState into the chain. Closed to modification, open to extension.
/// </summary>
public class Brute : Enemy
{
    // ─── Brute-Specific Inspector Fields ─────────────────────────────────────────
    [Header("Brute Behaviour")]
    [Tooltip("Probability (0-1) that the Brute will block after an attack (for future Block state).")]
    [SerializeField] private float blockProbability = 0.35f;

    [Tooltip("How much the Brute slows down to show heaviness. Multiplier applied to MoveSpeed.")]
    [SerializeField] private float moveSpeedMultiplier = 0.8f;

    // ─── Exposed for States ───────────────────────────────────────────────────────
    public float BlockProbability    => blockProbability;
    public float MoveSpeedMultiplier => moveSpeedMultiplier;
}
