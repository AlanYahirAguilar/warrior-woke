using UnityEngine;

/// <summary>
/// Core MonoBehaviour for all enemy types in Warrior Woke.
/// Acts as the Context in the State Machine pattern — owns all state instances
/// and exposes subsystem references consumed by states.
///
/// Architecture mirrors Player.cs / PlayerMovement.cs for consistency.
/// Adheres to SRP: orchestrates state routing and component access only.
///
/// Usage:
///   - Attach this (or a subclass like Looter / Brute) to an enemy prefab.
///   - Assign an EnemyData ScriptableObject in the Inspector.
///   - The enemy automatically starts in PatrolState.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(HealthSystem))]
[RequireComponent(typeof(Hitbox))]
public class Enemy : MonoBehaviour, IPoolable
{
    // ─── Inspector Configuration ─────────────────────────────────────────────────
    [Header("Data")]
    [Tooltip("ScriptableObject defining this enemy's stats. Required.")]
    [SerializeField] private EnemyData data;

    [Header("Detection")]
    [Tooltip("Layers considered as valid ground for edge detection.")]
    [SerializeField] protected LayerMask groundLayer;

    // ─── IPoolable ────────────────────────────────────────────────────────────────
    public string PoolId { get; set; }

    // ─── Subsystem References ─────────────────────────────────────────────────────
    public Rigidbody   Rb            { get; private set; }
    public HealthSystem HealthSystem { get; private set; }
    public Hitbox       Hitbox       { get; private set; }
    public EnemyData    Data         => data;

    // ─── Runtime State ────────────────────────────────────────────────────────────
    public Transform     PlayerTarget { get; private set; }
    public Vector3       SpawnPoint   { get; private set; }
    public float         FacingDir    { get; private set; } = 1f;
    public bool          IsGrounded   { get; private set; }

    // ─── Detection Buffer (pre-allocated — zero GC per frame) ────────────────────
    private readonly Collider[] _detectionBuffer = new Collider[5];

    // ─── State Machine ────────────────────────────────────────────────────────────
    public EnemyStateMachine StateMachine { get; private set; }
    public EnemyPatrolState  PatrolState  { get; private set; }
    public EnemyChaseState   ChaseState   { get; private set; }
    public EnemyAttackState  AttackState  { get; private set; }
    public EnemyDeadState    DeadState    { get; private set; }

    // ─── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        CacheComponents();
        BuildStateMachine();
        SpawnPoint = transform.position;
    }

    private void OnEnable()
    {
        HealthSystem.OnDeath += OnDeath;
    }

    private void OnDisable()
    {
        HealthSystem.OnDeath -= OnDeath;
    }

    private void Start()
    {
        StateMachine.Initialize(PatrolState);
    }

    private void FixedUpdate()
    {
        CheckGrounded();
        StateMachine.CurrentState?.PhysicsUpdate();
    }

    private void Update()
    {
        DetectPlayer();
        StateMachine.CurrentState?.LogicUpdate();
    }

    // ─── IPoolable ───────────────────────────────────────────────────────────────

    public virtual void OnSpawn()
    {
        // Reset health and restart in patrol when re-spawned from pool
        HealthSystem.Heal(data.MaxHealth);
        StateMachine.ChangeState(PatrolState);
        SpawnPoint = transform.position;
    }

    public virtual void OnDespawn()
    {
        // No extra cleanup needed — HealthSystem resets on Heal in OnSpawn
    }

    // ─── Public API (consumed by states) ─────────────────────────────────────────

    /// <summary>
    /// Moves the enemy horizontally at the given speed on the X axis.
    /// Z is always zero (2.5D constraint). Y preserves gravity.
    /// </summary>
    public void Move(float direction, float speed)
    {
        FacingDir = direction;
        Rb.linearVelocity = new Vector3(direction * speed, Rb.linearVelocity.y, 0f);
        // Rotate to face movement direction
        if (Mathf.Abs(direction) > 0.05f)
            transform.rotation = Quaternion.Euler(0f, direction > 0 ? 90f : -90f, 0f);
    }

    public void StopMovement()
    {
        Rb.linearVelocity = new Vector3(0f, Rb.linearVelocity.y, 0f);
    }

    /// <summary>Returns true if the player is within attack range of this enemy.</summary>
    public bool IsPlayerInAttackRange()
    {
        if (PlayerTarget == null) return false;
        return Vector3.Distance(transform.position, PlayerTarget.position) <= data.AttackRange;
    }

    /// <summary>Returns true if the player is within detection range.</summary>
    public bool IsPlayerInDetectionRange() => PlayerTarget != null;

    /// <summary>Returns true if the player has moved beyond lose-track range.</summary>
    public bool HasLostPlayer()
    {
        if (PlayerTarget == null) return true;
        return Vector3.Distance(transform.position, PlayerTarget.position) > data.LoseTrackRange;
    }

    // ─── Private Helpers ─────────────────────────────────────────────────────────

    private void CacheComponents()
    {
        Rb           = GetComponent<Rigidbody>();
        HealthSystem = GetComponent<HealthSystem>();
        Hitbox       = GetComponent<Hitbox>();

        // Initialize HealthSystem from EnemyData so the Inspector field is not the source of truth
        if (data != null)
            HealthSystem.InitializeHealth(data.MaxHealth);

        // 2.5D constraint: freeze Z and all rotations (handled by physics)
        Rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
    }

    private void BuildStateMachine()
    {
        StateMachine = new EnemyStateMachine();
        PatrolState  = new EnemyPatrolState(this, StateMachine);
        ChaseState   = new EnemyChaseState(this, StateMachine);
        AttackState  = new EnemyAttackState(this, StateMachine);
        DeadState    = new EnemyDeadState(this, StateMachine);
    }

    /// <summary>
    /// Uses OverlapSphereNonAlloc (pre-allocated buffer — zero GC) to detect the player.
    /// Checks tags to avoid allocating strings per frame.
    /// </summary>
    private void DetectPlayer()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, data.DetectionRange, _detectionBuffer);

        PlayerTarget = null;
        for (int i = 0; i < count; i++)
        {
            if (_detectionBuffer[i].CompareTag("Player"))
            {
                PlayerTarget = _detectionBuffer[i].transform;

                // Line-of-sight check along X axis
                Vector3 dir    = (PlayerTarget.position - transform.position).normalized;
                float   dist   = Vector3.Distance(transform.position, PlayerTarget.position);
                if (Physics.Raycast(transform.position, dir, dist, groundLayer))
                    PlayerTarget = null; // wall in the way — line of sight broken

                break;
            }
        }
    }

    private void CheckGrounded()
    {
        var col    = GetComponent<CapsuleCollider>();
        float len  = col != null ? col.bounds.extents.y + 0.15f : 0.6f;
        IsGrounded = Physics.Raycast(transform.position, Vector3.down, len, groundLayer);
    }

    private void OnDeath()
    {
        // Grant XP to the player before entering dead state
        if (PlayerTarget != null)
        {
            var xpReceiver = PlayerTarget.GetComponent<IXpReceiver>();
            xpReceiver?.AddXp(data.XpReward);
        }

        StateMachine.ChangeState(DeadState);
    }

    private void OnDrawGizmosSelected()
    {
        if (data == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, data.DetectionRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, data.AttackRange);
        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(transform.position, data.LoseTrackRange);
    }
}
