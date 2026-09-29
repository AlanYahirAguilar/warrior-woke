using UnityEngine;

// ────────────────────────────────────────────────────────────────────────────────
// PlayerLightAttackState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Fast attack state (Mouse1). Duration ~0.25s per GDD.
/// Chains into HeavyAttack if Mouse2 is pressed within the combo window.
/// Can chain up to 3 light attacks consecutively before forcing return to Idle.
///
/// Semana 4: Hitbox.Activate() now called with damage from WeaponHolder.
/// </summary>
public class PlayerLightAttackState : PlayerState
{
    private const float AttackDuration  = 0.25f;
    private const float ComboWindow     = 0.5f;
    private const int   MaxLightChain   = 3;

    private int           _chainCount;
    private Hitbox        _hitbox;
    private WeaponHolder  _weaponHolder;

    public PlayerLightAttackState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine)
    {
        // Cache components once — never inside the loop (best practice #2)
        _hitbox       = player.GetComponent<Hitbox>();
        _weaponHolder = player.GetComponent<WeaponHolder>();
    }

    public override void Enter()
    {
        base.Enter();
        _chainCount++;

        // Apply correct damage from currently equipped weapon before activating the hitbox
        if (_hitbox != null && _weaponHolder != null)
            _hitbox.SetDamage(_weaponHolder.GetLightDamage());

        // Stop horizontal movement during strike (planted while attacking)
        player.SetVelocity(0f, player.Rb.linearVelocity.y);

        // Activate hitbox on the frame the attack starts
        _hitbox?.Activate();

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
/// Applies knockback via the receiver's HealthSystem.
///
/// Semana 4: Hitbox.Activate() now called with WeaponHolder heavy damage.
/// </summary>
public class PlayerHeavyAttackState : PlayerState
{
    private const float AttackDuration  = 0.8f;
    private const float HitWindowStart  = 0.1f; // Slight delay before hitbox fires

    private Hitbox        _hitbox;
    private WeaponHolder  _weaponHolder;
    private bool          _hasActivatedHitbox;

    public PlayerHeavyAttackState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine)
    {
        _hitbox       = player.GetComponent<Hitbox>();
        _weaponHolder = player.GetComponent<WeaponHolder>();
    }

    public override void Enter()
    {
        base.Enter();
        _hasActivatedHitbox = false;
        player.SetVelocity(0f, player.Rb.linearVelocity.y);

        // TODO: trigger animation — animator.SetTrigger("heavyAttack");
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        float elapsed = Time.time - startTime;

        // Fire hitbox slightly after the state begins to match the animation swing peak
        if (!_hasActivatedHitbox && elapsed >= HitWindowStart)
        {
            if (_hitbox != null && _weaponHolder != null)
                _hitbox.SetDamage(_weaponHolder.GetHeavyDamage());

            _hitbox?.Activate();
            _hasActivatedHitbox = true;
        }

        if (elapsed >= AttackDuration)
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
    private const float DamageReductionFactor = 0.05f; // absorbs 95%, player takes 5%

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
/// Direction: always in the current FacingDirection (forward dodge).
/// Cannot be used while airborne (only grounded).
/// Cooldown enforced via Time.time (zero GC — no Coroutine).
///
/// Semana 4: IFrames now activated directly on HealthSystem via ActivateIFrames().
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

    private HealthSystem _healthSystem;
    private bool         _iFramesActivated;

    public PlayerDodgeState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine)
    {
        _healthSystem = player.GetComponent<HealthSystem>();
    }

    public override void Enter()
    {
        base.Enter();
        _lastDodgeTime    = Time.time;
        _iFramesActivated = false;

        // Activate invincibility frames on the HealthSystem for IFramesDuration seconds
        _healthSystem?.ActivateIFrames(IFramesDuration);
        _iFramesActivated = true;

        // TODO: trigger animation — animator.SetTrigger("dodge");
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (Time.time - startTime >= DodgeDuration)
        {
            // Return to run if still holding a direction, otherwise idle
            stateMachine.ChangeState(
                player.HasMoveInput ? (PlayerState)player.RunState : player.IdleState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        // Dash in the current facing direction regardless of input
        player.SetVelocity(player.transform.forward * DodgeSpeed, player.Rb.linearVelocity.y);
    }
}
