using UnityEngine;

// ────────────────────────────────────────────────────────────────────────────────
// PlayerLightAttackState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Fast attack state (Mouse1). Duration ~0.25s per GDD.
/// Chains into HeavyAttack if Mouse2 is pressed within the combo window.
/// Can chain up to 3 light attacks consecutively before forcing return to Idle.
/// </summary>
public class PlayerLightAttackState : PlayerState
{
    private const float AttackDuration  = 0.25f;
    private const float ComboWindow     = 0.5f;
    private const int   MaxLightChain   = 3;

    private int _chainCount;

    public PlayerLightAttackState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        _chainCount++;
        // Stop horizontal movement during attack (player planted while striking)
        player.SetVelocity(0f, player.Rb.linearVelocity.y);

        // TODO: trigger animation — animator.SetTrigger("lightAttack");
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        float elapsed = Time.time - startTime;

        if (elapsed >= AttackDuration)
        {
            // ── Combo chaining window ──
            if (elapsed < ComboWindow)
            {
                // Chain into Heavy Attack (finisher)
                if (player.HeavyAttackTriggered)
                {
                    _chainCount = 0;
                    stateMachine.ChangeState(player.HeavyAttackState);
                    return;
                }

                // Chain into another Light Attack (max 3)
                if (player.LightAttackTriggered && _chainCount < MaxLightChain)
                {
                    // Re-enter same state to reset the timer and increment chain
                    stateMachine.ChangeState(player.LightAttackState);
                    return;
                }
            }

            // ── Combo window expired or chain maxed ──
            _chainCount = 0;
            stateMachine.ChangeState(player.IdleState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        // Preserve vertical velocity; zero horizontal during strike
        player.SetVelocity(0f, player.Rb.linearVelocity.y);
    }

    public override void Exit()
    {
        base.Exit();
        // Reset chain counter if we exit for any reason other than chaining
        if (stateMachine.CurrentState != player.LightAttackState &&
            stateMachine.CurrentState != player.HeavyAttackState)
        {
            _chainCount = 0;
        }
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerHeavyAttackState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Slow, powerful attack state (Mouse2). Duration 0.8s per GDD.
/// Leaves the player vulnerable if it misses (no cancel window).
/// Can generate knockback on enemies — handled by the receiver's HealthSystem.
/// </summary>
public class PlayerHeavyAttackState : PlayerState
{
    private const float AttackDuration = 0.8f;

    public PlayerHeavyAttackState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        player.SetVelocity(0f, player.Rb.linearVelocity.y);

        // TODO: trigger animation — animator.SetTrigger("heavyAttack");
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (Time.time - startTime >= AttackDuration)
            stateMachine.ChangeState(player.IdleState);
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        player.SetVelocity(0f, player.Rb.linearVelocity.y);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerBlockState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Defensive state (F held). Reduces incoming damage by ~70% per GDD.
/// Restricts mobility — player cannot run or jump while blocking.
/// A successful block followed by a release enables a counterattack window.
/// </summary>
public class PlayerBlockState : PlayerState
{
    private const float DamageReductionFactor = 0.3f; // absorbs 70%, player takes 30%

    /// <summary>Exposes the reduction factor so HealthSystem can query it if needed.</summary>
    public float DamageReductionMultiplier => DamageReductionFactor;

    public PlayerBlockState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        player.SetVelocity(0f, player.Rb.linearVelocity.y);

        // TODO: trigger animation — animator.SetBool("isBlocking", true);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        // Exit when F is released
        if (!player.IsBlockHeld)
        {
            stateMachine.ChangeState(player.IdleState);
            return;
        }

        // Allow counterattack: light attack while blocking exits to LightAttack
        if (player.LightAttackTriggered)
        {
            stateMachine.ChangeState(player.LightAttackState);
            return;
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        // Block anchors the player in place
        player.SetVelocity(0f, player.Rb.linearVelocity.y);
    }

    public override void Exit()
    {
        base.Exit();
        // TODO: clear animation — animator.SetBool("isBlocking", false);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerDodgeState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Quick evasion state (E key). Duration 0.5s with 0.2s of iframes per GDD.
/// Direction: always in the current FacingDirection (forward dodge, GDD §8 confirmed).
/// Cannot be used while airborne (only grounded).
/// Cooldown is enforced via Time.time comparison instead of a Coroutine (zero GC).
/// </summary>
public class PlayerDodgeState : PlayerState
{
    private const float DodgeDuration   = 0.5f;
    private const float IFramesDuration = 0.2f;
    private const float DodgeSpeed      = 12f;
    private const float DodgeCooldown   = 1f;

    private static float _lastDodgeTime = -999f;

    /// <summary>Returns true if the dodge cooldown has elapsed.</summary>
    public static bool CanDodge => Time.time - _lastDodgeTime >= DodgeCooldown;

    public PlayerDodgeState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        _lastDodgeTime = Time.time;

        // TODO: enable iframes on HealthSystem for IFramesDuration seconds
        // e.g., player.GetComponent<HealthSystem>().ActivateIFrames(IFramesDuration);
        // TODO: trigger animation — animator.SetTrigger("dodge");
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (Time.time - startTime >= DodgeDuration)
        {
            // Return to run if still holding a direction, otherwise idle
            stateMachine.ChangeState(
                Mathf.Abs(player.InputX) > 0.1f ? (PlayerState)player.RunState : player.IdleState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        // Dash in the current facing direction regardless of input
        player.SetVelocity(player.FacingDirection * DodgeSpeed, player.Rb.linearVelocity.y);
    }
}
