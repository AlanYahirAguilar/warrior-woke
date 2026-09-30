using UnityEngine;

// ────────────────────────────────────────────────────────────────────────────────
// EnemyPatrolState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Default idle/patrol state. Enemy walks left and right within patrolRange.
/// Transitions to ChaseState when the player enters detection range.
/// </summary>
public class EnemyPatrolState : EnemyState
{
    private float _patrolDirection = 1f;

    public EnemyPatrolState(Enemy enemy, EnemyStateMachine stateMachine)
        : base(enemy, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        _patrolDirection = 1f; // Start patrolling right
    }

    public override void LogicUpdate()
    {
        // Detect player — switch to chase immediately
        if (enemy.IsPlayerInDetectionRange())
        {
            stateMachine.ChangeState(enemy.ChaseState);
            return;
        }

        // Reverse direction when patrol boundary is reached
        float distFromSpawn = enemy.transform.position.x - enemy.SpawnPoint.x;
        if (distFromSpawn > enemy.Data.PatrolRange)
            _patrolDirection = -1f;
        else if (distFromSpawn < -enemy.Data.PatrolRange)
            _patrolDirection = 1f;
    }

    public override void PhysicsUpdate()
    {
        enemy.Move(_patrolDirection, enemy.Data.MoveSpeed * 0.5f); // Patrol at half speed
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// EnemyChaseState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Pursues the player until within attack range or until line of sight is lost.
/// Returns to patrol if the player escapes LoseTrackRange. The GDD §5.14 assigned zone is
/// not implemented yet (planned in the 3D rewrite, docs/arquitectura.md §7).
/// </summary>
public class EnemyChaseState : EnemyState
{
    public EnemyChaseState(Enemy enemy, EnemyStateMachine stateMachine)
        : base(enemy, stateMachine) { }

    public override void LogicUpdate()
    {
        // Player escaped → return to patrol
        if (enemy.HasLostPlayer())
        {
            stateMachine.ChangeState(enemy.PatrolState);
            return;
        }

        // Player is close enough → start attacking
        if (enemy.IsPlayerInAttackRange())
        {
            stateMachine.ChangeState(enemy.AttackState);
            return;
        }
    }

    public override void PhysicsUpdate()
    {
        if (enemy.PlayerTarget == null) return;

        float dirX = Mathf.Sign(enemy.PlayerTarget.position.x - enemy.transform.position.x);
        enemy.Move(dirX, enemy.Data.MoveSpeed);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// EnemyAttackState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Melee attack state. Activates the Hitbox during the attack window.
/// Respects the GDD cooldown per attack type.
/// Returns to Chase after each attack cycle.
/// </summary>
public class EnemyAttackState : EnemyState
{
    private bool  _hasActivatedHitbox;
    private float _lastAttackTime = -999f;

    // Hitbox fires at 30% into the animation (feels snappy but not instant)
    private const float HitWindowFraction = 0.3f;

    public EnemyAttackState(Enemy enemy, EnemyStateMachine stateMachine)
        : base(enemy, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        _hasActivatedHitbox = false;
        enemy.StopMovement();
        enemy.Hitbox?.SetDamage(enemy.Data.AttackDamage);

        // TODO: trigger attack animation — animator.SetTrigger("attack");
    }

    public override void LogicUpdate()
    {
        float elapsed = Time.time - startTime;

        // Fire the hitbox at the hit window frame
        if (!_hasActivatedHitbox && elapsed >= enemy.Data.AttackDuration * HitWindowFraction)
        {
            enemy.Hitbox?.Activate();
            _hasActivatedHitbox = true;
            _lastAttackTime     = Time.time;
        }

        // Attack animation finished
        if (elapsed >= enemy.Data.AttackDuration)
        {
            // Player still in range and cooldown elapsed → attack again
            if (enemy.IsPlayerInAttackRange() &&
                Time.time - _lastAttackTime >= enemy.Data.AttackCooldown)
            {
                stateMachine.ChangeState(enemy.AttackState);
            }
            // Player moved away → chase
            else if (!enemy.IsPlayerInAttackRange())
            {
                stateMachine.ChangeState(enemy.ChaseState);
            }
            // Waiting for cooldown — stay in attack state (idle, facing player)
        }
    }

    public override void PhysicsUpdate()
    {
        // Stay planted while attacking
        enemy.StopMovement();
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// EnemyDeadState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Terminal state entered when HealthSystem reaches zero.
/// Disables physics, plays death feedback, then returns the enemy to the object pool.
/// </summary>
public class EnemyDeadState : EnemyState
{
    private const float DeathDuration = 1.5f; // Time before pooling (for death animation/VFX)

    public EnemyDeadState(Enemy enemy, EnemyStateMachine stateMachine)
        : base(enemy, stateMachine) { }

    public override void Enter()
    {
        base.Enter();

        // Stop all movement immediately
        enemy.StopMovement();
        if (enemy.TryGetComponent(out Rigidbody rb))
            rb.isKinematic = true;

        // No need to disable the Hitbox: it only fires from EnemyAttackState, which is no longer
        // reachable. (It lives on the enemy root, so deactivating its GameObject would hide the
        // whole enemy and stop this state before it can return to the pool.)

        // TODO: trigger death animation — animator.SetTrigger("die");
        // TODO: spawn death VFX via ObjectPoolManager
    }

    public override void LogicUpdate()
    {
        // Return to pool after death animation window expires
        if (Time.time - startTime >= DeathDuration)
        {
            ReturnToPool();
        }
    }

    private void ReturnToPool()
    {
        if (enemy.TryGetComponent(out Rigidbody rb))
            rb.isKinematic = false;

        if (ObjectPoolManager.Instance != null && !string.IsNullOrEmpty(enemy.PoolId))
            ObjectPoolManager.Instance.ReturnToPool(enemy.gameObject, enemy.PoolId);
        else
            enemy.gameObject.SetActive(false);
    }
}
