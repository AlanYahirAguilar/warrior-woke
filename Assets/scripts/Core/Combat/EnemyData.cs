using UnityEngine;

/// <summary>
/// Immutable data container for an enemy archetype definition.
/// ScriptableObject — create via Assets > Create > WarriorWoke > Enemy Data.
///
/// Implements the Data-Driven design pattern: swap the asset to create a completely
/// different enemy without touching code. Follows SRP: pure data, zero behaviour.
/// </summary>
[CreateAssetMenu(fileName = "NewEnemyData", menuName = "WarriorWoke/Enemy Data")]
public class EnemyData : ScriptableObject
{
    // ─── Identity ─────────────────────────────────────────────────────────────────
    [Header("Identity")]
    [SerializeField] private string enemyName = "New Enemy";

    // ─── Vitals ───────────────────────────────────────────────────────────────────
    [Header("Vitals")]
    [Tooltip("Max hit points for this enemy type.")]
    [SerializeField] private int maxHealth = 50;

    // ─── Movement ─────────────────────────────────────────────────────────────────
    [Header("Movement")]
    [Tooltip("Walk / chase speed (units/s).")]
    [SerializeField] private float moveSpeed = 3f;

    [Tooltip("How far the enemy patrols left/right from its spawn point.")]
    [SerializeField] private float patrolRange = 4f;

    // ─── Detection ────────────────────────────────────────────────────────────────
    [Header("Detection")]
    [Tooltip("Sphere radius for initial player detection.")]
    [SerializeField] private float detectionRange = 6f;

    [Tooltip("Sphere radius for losing track of the player.")]
    [SerializeField] private float loseTrackRange = 10f;

    // ─── Combat ───────────────────────────────────────────────────────────────────
    [Header("Combat")]
    [Tooltip("Melee attack range (units).")]
    [SerializeField] private float attackRange = 1.2f;

    [Tooltip("Time in seconds between consecutive attacks.")]
    [SerializeField] private float attackCooldown = 1.5f;

    [Tooltip("Damage dealt per attack.")]
    [SerializeField] private int attackDamage = 10;

    [Tooltip("Duration of the attack animation window (seconds).")]
    [SerializeField] private float attackDuration = 0.4f;

    // ─── Read-Only Properties ─────────────────────────────────────────────────────
    public string EnemyName      => enemyName;
    public int    MaxHealth      => maxHealth;
    public float  MoveSpeed      => moveSpeed;
    public float  PatrolRange    => patrolRange;
    public float  DetectionRange => detectionRange;
    public float  LoseTrackRange => loseTrackRange;
    public float  AttackRange    => attackRange;
    public float  AttackCooldown => attackCooldown;
    public int    AttackDamage   => attackDamage;
    public float  AttackDuration => attackDuration;
}
