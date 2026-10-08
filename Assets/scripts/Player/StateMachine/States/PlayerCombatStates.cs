using UnityEngine;

// ────────────────────────────────────────────────────────────────────────────────
// PlayerLightAttackState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Fast attack state (J). Duration ~0.25s per GDD.
/// Chains into HeavyAttack if K is pressed within the combo window.
/// Can chain up to 3 light attacks consecutively before forcing return to Idle.
///
/// Feel: on Enter the character turns to the input direction and steps forward briefly (lunge);
/// the hit lands at HitTime, when the punch connects in the animation. Presses made during the
/// strike are buffered and consumed when the combo window opens, so fast inputs are not lost.
/// </summary>
public class PlayerLightAttackState : PlayerState
{
    private const float AttackDuration  = 0.25f;
    private const float ComboWindow     = 0.5f;
    private const int   MaxLightChain   = 3;
    private const float HitTime         = 0.1f;  // punch extension in the 0.5 s animation
    private const float LungeTime       = 0.12f;
    private const float LungeSpeed      = 3f;

    private int           _chainCount;
    private Hitbox        _hitbox;
    private WeaponHolder  _weaponHolder;
    private bool          _hasHit;
    private bool          _bufferedLight;
    private bool          _bufferedHeavy;

    /// <summary>Position of the current hit in the chain (1..3). Lets the animation alternate fists.</summary>
    public int ChainIndex => _chainCount;

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
        _hasHit        = false;
        _bufferedLight = false;
        _bufferedHeavy = false;

        // Aim the strike where the player is pushing
        if (player.HasMoveInput)
            player.FaceDirection(player.MoveDirection);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        float elapsed = Time.time - startTime;

        if (!_hasHit && elapsed >= HitTime)
        {
            // Apply correct damage from currently equipped weapon before activating the hitbox
            if (_hitbox != null && _weaponHolder != null)
                _hitbox.SetDamage(_weaponHolder.GetLightDamage());
            _hitbox?.Activate();
            _hasHit = true;
        }

        // Buffer presses made while the strike is still playing
        if (player.LightAttackTriggered) _bufferedLight = true;
        if (player.HeavyAttackTriggered) _bufferedHeavy = true;

        if (elapsed < AttackDuration) return;

        // ── Combo chaining window ──
        if (elapsed < ComboWindow)
        {
            // Chain into Heavy Attack (finisher)
            if (_bufferedHeavy)
            {
                _chainCount = 0;
                stateMachine.ChangeState(player.HeavyAttackState);
                return;
            }

            // Chain into another Light Attack (max 3)
            if (_bufferedLight && _chainCount < MaxLightChain)
            {
                // Re-enter same state to reset the timer and increment chain
                stateMachine.ChangeState(player.LightAttackState);
                return;
            }

            if (!_bufferedLight) return; // keep the window open
        }

        // ── Combo window expired or chain maxed ──
        _chainCount = 0;
        stateMachine.ChangeState(player.IdleState);
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        // Short lunge at the start of the strike, then planted (vertical velocity preserved)
        bool lunging = Time.time - startTime < LungeTime;
        if (lunging) player.SetVelocity(player.transform.forward * LungeSpeed, player.Velocity.y);
        else         player.StopHorizontal(player.Velocity.y);
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
/// Slow, powerful attack state (K). Duration 0.8s per GDD.
/// Leaves the player vulnerable if it misses (no cancel window).
/// Turns to the input direction, lunges forward and lands the hit at the swing's peak.
/// Knockback (WeaponHolder.GetKnockback) is not applied yet.
/// </summary>
public class PlayerHeavyAttackState : PlayerState
{
    private const float AttackDuration  = 0.8f;
    private const float HitWindowStart  = 0.3f;  // swing peak of the 0.8 s animation
    private const float LungeTime       = 0.15f;
    private const float LungeSpeed      = 4f;

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
        if (player.HasMoveInput)
            player.FaceDirection(player.MoveDirection);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        float elapsed = Time.time - startTime;

        // Fire the hitbox at the swing peak of the animation
        if (!_hasActivatedHitbox && elapsed >= HitWindowStart)
        {
            if (_hitbox != null && _weaponHolder != null)
                _hitbox.SetDamage(_weaponHolder.GetHeavyDamage());

            _hitbox?.Activate();
            _hasActivatedHitbox = true;
        }

        if (elapsed >= AttackDuration)
            stateMachine.ChangeState(player.HasMoveInput ? (PlayerState)player.RunState : player.IdleState);
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        bool lunging = Time.time - startTime < LungeTime;
        if (lunging) player.SetVelocity(player.transform.forward * LungeSpeed, player.Velocity.y);
        else         player.StopHorizontal(player.Velocity.y);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerBlockState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Defensive state (L held). Restricts mobility — player cannot run or jump while blocking.
/// Light attack while blocking counterattacks.
/// Reduces damage by 70 % only for hits coming from the front (GDD §5.8). HealthSystem asks
/// through IDamageModifier (implemented by PlayerMovement), which forwards here while blocking.
/// </summary>
public class PlayerBlockState : PlayerState
{
    private const float DamageReductionFactor = 0.3f; // absorbs 70%, player takes 30% (GDD §5.8)

    // cos(60°): hits within ±60° of the facing direction count as frontal.
    private const float FrontalDotThreshold = 0.5f;

    public float DamageReductionMultiplier => DamageReductionFactor;

    /// <summary>Applies the block reduction if the source is in front of the player.</summary>
    public int ModifyIncomingDamage(int amount, Vector3 source)
    {
        Vector3 toSource = source - player.transform.position;
        toSource.y = 0f;

        // A source at the player's own position has no direction: treat it as unblocked.
        if (toSource.sqrMagnitude < 0.0001f)
            return amount;

        bool isFrontal = Vector3.Dot(player.transform.forward, toSource.normalized) >= FrontalDotThreshold;
        return isFrontal ? Mathf.RoundToInt(amount * DamageReductionFactor) : amount;
    }

    public PlayerBlockState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        player.StopHorizontal(player.Velocity.y);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        // Exit when L is released
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
        player.StopHorizontal(player.Velocity.y);
    }

}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerDodgeState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Quick evasion state (Q + direction, GDD §5.5). Duration 0.5s with 0.2s of iframes.
/// Direction: the camera-relative movement input at the moment of the dodge; without input,
/// the current facing direction. The body does not turn during the dodge, so the animation
/// picks a forward/back/left/right roll from LocalDirection.
/// Cannot be used while airborne (only grounded).
/// Cooldown (1 s, GDD §5.5) enforced via Time.time (zero GC — no Coroutine); the transitions
/// into this state check CanDodge.
///
/// Semana 4: IFrames now activated directly on HealthSystem via ActivateIFrames().
/// </summary>
public class PlayerDodgeState : PlayerState
{
    private const float DodgeDuration   = 0.5f;
    private const float IFramesDuration = 0.2f;
    private const float DodgeSpeed      = 12f;
    private const float DodgeCooldown   = 1f;

    private float _lastDodgeTime = -999f;

    /// <summary>Returns true if the dodge cooldown has elapsed.</summary>
    public bool CanDodge => Time.time - _lastDodgeTime >= DodgeCooldown;

    private HealthSystem _healthSystem;
    private Vector3      _dodgeDirection;

    /// <summary>Dodge direction relative to the character (x = right, y = forward), normalized.</summary>
    public Vector2 LocalDirection { get; private set; }

    public PlayerDodgeState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine)
    {
        _healthSystem = player.GetComponent<HealthSystem>();
    }

    public override void Enter()
    {
        base.Enter();
        _lastDodgeTime    = Time.time;

        _dodgeDirection = player.HasMoveInput ? player.MoveDirection : player.transform.forward;
        Vector3 local   = player.transform.InverseTransformDirection(_dodgeDirection);
        LocalDirection  = new Vector2(local.x, local.z).normalized;

        // Activate invincibility frames on the HealthSystem for IFramesDuration seconds
        _healthSystem?.ActivateIFrames(IFramesDuration);
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
        // Dash in the direction chosen on Enter, even if the input changes mid-dodge
        player.SetVelocity(_dodgeDirection * DodgeSpeed, player.Velocity.y);
    }
}
