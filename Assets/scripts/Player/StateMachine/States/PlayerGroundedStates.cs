using UnityEngine;

// ────────────────────────────────────────────────────────────────────────────────
// PlayerIdleState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Default grounded state. Transitions to Run, Jump, Fall, Block, Dodge or the attacks.
/// </summary>
public class PlayerIdleState : PlayerState
{
    public PlayerIdleState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        // Sprint never carries over once the player stops (e.g. landing from a sprint jump).
        player.IsSprint = false;
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        // Lost the ground without jumping (pushed off an edge, platform removed)
        if (player.AirTime > player.FallGraceTime)
        {
            stateMachine.ChangeState(player.FallState);
            return;
        }

        // Combat takes priority over movement when grounded
        if (player.IsBlockHeld)
        {
            stateMachine.ChangeState(player.BlockState);
            return;
        }

        if (player.DodgeTriggered && player.IsGrounded && player.DodgeState.CanDodge)
        {
            stateMachine.ChangeState(player.DodgeState);
            return;
        }

        if (player.LightAttackTriggered)
        {
            stateMachine.ChangeState(player.LightAttackState);
            return;
        }

        if (player.HeavyAttackTriggered)
        {
            stateMachine.ChangeState(player.HeavyAttackState);
            return;
        }

        // C standing still: lower onto a hang at an edge ahead, or crouch
        if (player.SlideTriggered && player.IsGrounded)
        {
            stateMachine.ChangeState(PlayerLedgeDropState.TryStart(player) ? (PlayerState)player.LedgeDropState : player.CrouchState);
            return;
        }

        // Movement
        if (player.HasMoveInput)
        {
            stateMachine.ChangeState(player.RunState);
            return;
        }

        if (player.JumpTriggered && player.IsGrounded)
        {
            PlayerState next = ResolveSpace(player);
            if (next != null) stateMachine.ChangeState(next);
        }
    }

    /// <summary>
    /// Space on the ground, by context — the geometry ahead, the speed and the distance pick the
    /// action, not the obstacle's type:
    ///  - slow (walking, standing) in front of a block with room on top: climb onto it (mantle);
    ///  - an obstacle that can be crossed: vault it (GDD §5.4), waiting for the take-off point when
    ///    running (returns null meanwhile: the caller keeps the intention);
    ///  - a block too tall to vault but within the mantle range: mantle, at any speed;
    ///  - a ledge within reach: grab it (P2);
    ///  - otherwise: jump.
    /// </summary>
    public static PlayerState ResolveSpace(PlayerMovement player)
    {
        bool slow = player.HorizontalSpeed < ParkourTimings.VaultClipSpeed * 0.8f;
        if (slow && PlayerMantleState.TryStart(player)) return player.MantleState;
        PlayerVaultState.Approach vault = PlayerVaultState.Evaluate(player);
        if (vault == PlayerVaultState.Approach.Ready) return player.VaultState;
        if (vault == PlayerVaultState.Approach.Approaching) return null;
        PlayerVaultState.Approach mantle = PlayerMantleState.Evaluate(player);
        if (mantle == PlayerVaultState.Approach.Ready) return player.MantleState;
        if (mantle == PlayerVaultState.Approach.Approaching) return null;
        if (PlayerLedgeGrabState.TryStartFromGround(player)) return player.LedgeGrabState;
        return player.JumpState;
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        // Brake smoothly (the locomotion blend shows Run → Jog → Walk → Idle); gravity is preserved
        player.AccelerateHorizontal(Vector3.zero);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerRunState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Grounded locomotion (WASD) in three gaits: walk (Left Ctrl), run, sprint (Shift, GDD §5.2).
/// Accelerates toward the target speed; the directional blend follows the real velocity under the
/// body. Walking is oriented (strafe and backward, facing the camera); running turns toward the input
/// except backward, where it runs backward facing the camera (PlayerMovement.IsOriented).
/// Low edges are climbed and stepped down automatically (auto step). After a landing the speed recovers gradually.
/// </summary>
public class PlayerRunState : PlayerState
{
    private float _vaultIntentAt = -10f;

    /// <summary>Seconds a vault intention waits for the run to reach the obstacle's take-off point.</summary>
    private const float VaultIntentTime = 0.6f;

    public PlayerRunState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        _vaultIntentAt = -10f;
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        // Sprint follows Shift every tick; a cancelled sprint waits for Shift to be released.
        player.IsSprint = player.CanSprint;

        // Ran off an edge: keep the current speed (and sprint) into the fall
        if (player.AirTime > player.FallGraceTime)
        {
            stateMachine.ChangeState(player.FallState);
            return;
        }

        // Combat interrupts run (can attack while moving). Attacking and blocking cancel the sprint (GDD §5.2).
        if (player.LightAttackTriggered)
        {
            player.CancelSprint();
            stateMachine.ChangeState(player.LightAttackState);
            return;
        }

        if (player.HeavyAttackTriggered)
        {
            player.CancelSprint();
            stateMachine.ChangeState(player.HeavyAttackState);
            return;
        }

        if (player.IsBlockHeld)
        {
            player.CancelSprint();
            stateMachine.ChangeState(player.BlockState);
            return;
        }

        if (player.DodgeTriggered && player.IsGrounded && player.DodgeState.CanDodge)
        {
            player.IsSprint = false;
            stateMachine.ChangeState(player.DodgeState);
            return;
        }

        // C, by context: a slide when the run has momentum, the surface and the free space for it
        // (P2, P28); otherwise the body crouches and keeps moving crouched
        if (player.SlideTriggered && player.IsGrounded)
        {
            player.IsSprint = false;
            stateMachine.ChangeState(PlayerLedgeDropState.TryStart(player) ? player.LedgeDropState
                                   : PlayerSlideState.CanStart(player) ? (PlayerState)player.SlideState
                                   : player.CrouchState);
            return;
        }

        // Space: vault, mantle, ledge grab or jump, by context. An action ahead keeps the intention
        // until the run reaches its starting point; the intention never turns into a late jump.
        bool intent = Time.time - _vaultIntentAt < VaultIntentTime;
        if ((player.JumpTriggered || intent) && player.IsGrounded)
        {
            PlayerState next = PlayerIdleState.ResolveSpace(player);
            if (intent && !player.JumpTriggered && next == player.JumpState) next = null;
            if (next != null)
            {
                _vaultIntentAt = -10f;
                stateMachine.ChangeState(next);
                return;
            }
            if (player.JumpTriggered) _vaultIntentAt = Time.time;
        }

        // Stop running
        if (!player.HasMoveInput)
        {
            player.IsSprint = false;
            stateMachine.ChangeState(player.IdleState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        float speed = player.IsWalking      ? player.WalkSpeed
                    : player.IsBackpedaling ? player.BackpedalSpeed
                    : player.IsSprint       ? player.SprintSpeed
                    :                         player.BaseSpeed;
        // After a landing the legs absorb the impact: full speed comes back over the recovery.
        // Free running carries its momentum along the body (turns curve, reversals brake first);
        // oriented movement (walking, running backward) faces the camera and moves along the input.
        if (player.IsOriented)
            player.AccelerateHorizontal(player.MoveDirection * (speed * player.RecoverySpeedScale));
        else
            player.AccelerateAlongFacing(player.MoveDirection, speed * player.RecoverySpeedScale);
        player.TryAutoStep();
        player.TryStepDown();
    }

    // IsSprint is intentionally NOT cleared on Exit so a jump or fall started while sprinting
    // keeps sprint speed (GDD §5.2). Every other exit clears it, and Idle.Enter resets it.
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerCrouchState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Crouched (P28, outside the GDD): C toggles it when there is no momentum for a slide. The
/// collider lowers keeping its base on the ground; the body moves slowly and turns toward the input.
/// C again, Shift or Space stand up — never under a ceiling.
/// </summary>
public class PlayerCrouchState : PlayerState
{
    public PlayerCrouchState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        player.IsSprint = false;
        player.ShrinkCollider(player.CrouchHeightFactor);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (player.AirTime > player.FallGraceTime)
        {
            stateMachine.ChangeState(player.FallState);
            return;
        }

        bool standUp = player.SlideTriggered || player.JumpTriggered || player.IsSprintHeld;
        if (standUp && !player.HasCeilingOverhead())
            stateMachine.ChangeState(player.HasMoveInput ? (PlayerState)player.RunState : player.IdleState);
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        if (player.HasMoveInput)
            player.AccelerateAlongFacing(player.MoveDirection, player.CrouchSpeed);
        else
            player.AccelerateHorizontal(Vector3.zero);
    }

    public override void Exit()
    {
        base.Exit();
        player.ResetCollider();
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerSlideState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Contextual slide (outside the GDD, kept by decision P2): C while running. It is a solution for a
/// situation, not a triggered clip:
///   start  — only with momentum to spend (≥ SlideMinEntrySpeed), on a flat surface and with free
///            space ahead at slide height for at least the slide's minimum length;
///   during — the body keeps the entry speed and loses it with friction (more if the move input is
///            released), and brakes harder to stop short of an obstacle ahead; under a ceiling it keeps
///            moving at SlideMinSpeed until there is room to stand;
///   end    — momentum spent, space ahead used up, input released or reversed, or the valid window
///            over; Space chains into a vault, a ledge grab or a jump with the slide's speed.
/// The collider shrinks to half height keeping its bottom on the ground. The animation follows the
/// movement: PlayerAnimator plays the drop at a rate set by the entry speed and holds the slide pose
/// for the predicted duration (it never loops).
/// </summary>
public class PlayerSlideState : PlayerState
{
    private float _speed;
    private bool  _blocked;      // stopped against an obstacle
    private bool  _spaceEndsSoon; // braking because the free space ahead ends
    private float _spaceQueuedAt = -10f;

    /// <summary>Seconds a Space press during the slide waits for an obstacle ahead to come within vault reach.</summary>
    private const float SpaceBuffer = 0.5f;

    public PlayerSlideState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    /// <summary>Horizontal speed when the slide started (m/s).</summary>
    public float EntrySpeed { get; private set; }

    /// <summary>Expected slide time (s) from the entry speed, friction and free space, used to pace the animation.</summary>
    public float PredictedDuration { get; private set; }

    /// <summary>Current slide speed (m/s).</summary>
    public float Speed => _speed;

    /// <summary>Why the last slide ended (for tests and tuning).</summary>
    public string EndReason { get; private set; } = "";

    /// <summary>Speed, surface and free space allow a slide in the facing direction.</summary>
    public static bool CanStart(PlayerMovement player)
    {
        float speed = player.HorizontalSpeed;
        if (speed < player.SlideMinEntrySpeed || !player.HasSlideSurface()) return false;
        // Enough room to get down and slide at least until the body reaches the ground
        float minLength = speed * player.SlideMinTime + 0.5f;
        return player.SlideClearance(player.transform.forward, minLength) >= minLength;
    }

    public override void Enter()
    {
        base.Enter();
        player.ShrinkCollider(0.5f);
        EntrySpeed = Mathf.Min(player.HorizontalSpeed, player.SlideSpeed);
        _speed = EntrySpeed;
        _blocked = false;
        _spaceQueuedAt = -10f;
        EndReason = "";

        // Slide length the momentum allows, shortened by the free space ahead
        float friction = player.HasMoveInput ? player.SlideFriction : player.SlideBrakeFriction;
        float momentumTime = Mathf.Max(0f, EntrySpeed - player.SlideMinSpeed) / friction;
        float momentumDistance = (EntrySpeed + player.SlideMinSpeed) * 0.5f * momentumTime;
        float free = player.SlideClearance(player.transform.forward, momentumDistance + 1f);
        PredictedDuration = Mathf.Clamp(momentumTime * Mathf.Clamp01(free / Mathf.Max(0.01f, momentumDistance)),
                                        player.SlideMinTime, player.SlideMaxTime);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();
        float elapsed = Time.time - startTime;
        if (player.JumpTriggered) _spaceQueuedAt = Time.time; // remembered even while dropping
        if (elapsed < player.SlideMinTime) return; // still dropping to the ground

        bool ceiling = player.HasCeilingOverhead();
        if (ceiling) return; // cannot stand: keep sliding until there is room

        // Chain: Space turns the slide's momentum into the action the geometry allows. An obstacle
        // ahead that is not yet within reach keeps the intention for a moment: the slide carries the
        // body up to it and the vault starts as soon as it is valid. In the open, Space jumps at once.
        if (Time.time - _spaceQueuedAt < SpaceBuffer)
        {
            float horizon = _speed * SpaceBuffer + ParkourStandard.VaultReach + 0.5f;
            bool obstacleComing = !_blocked && player.SlideClearance(player.transform.forward, horizon) < horizon;
            PlayerState next = PlayerVaultState.TryStart(player) ? player.VaultState
                             : obstacleComing ? null
                             : PlayerIdleState.ResolveSpace(player);
            // (ResolveSpace is null while still approaching a vault's take-off point: keep sliding)
            if (next != null)
            {
                EndReason = "encadena";
                stateMachine.ChangeState(next);
                return;
            }
        }

        bool reversed = player.HasMoveInput && Vector3.Dot(player.MoveDirection, player.transform.forward) < -0.5f;
        bool spent = _speed <= player.SlideMinSpeed + 0.01f;
        string reason = _blocked || (spent && _spaceEndsSoon) ? "obstáculo"
                      : spent ? "momentum"
                      : reversed ? "entrada"
                      : elapsed >= player.SlideMaxTime ? "ventana"
                      : null;
        if (reason == null) return;

        EndReason = reason;
        stateMachine.ChangeState(player.HasMoveInput && !reversed ? (PlayerState)player.RunState : player.IdleState);
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        bool ceiling = player.HasCeilingOverhead();
        float friction = player.HasMoveInput ? player.SlideFriction : player.SlideBrakeFriction;
        float decel = friction;

        // Space ahead: brake to stop short of an obstacle at slide height (never run into it)
        float stopping = _speed * _speed / (2f * friction) + 0.5f;
        float free = player.SlideClearance(player.transform.forward, stopping + 0.5f);
        const float margin = 0.3f;
        _spaceEndsSoon = free < stopping + 0.5f;
        if (_spaceEndsSoon)
        {
            float room = free - margin;
            if (room <= 0.05f) { _speed = 0f; _blocked = true; }
            else decel = Mathf.Max(decel, _speed * _speed / (2f * room));
        }

        float floor = ceiling && !_blocked ? player.SlideMinSpeed : 0f; // a ceiling forces the slide on
        _speed = Mathf.Max(floor, _speed - decel * Time.fixedDeltaTime);

        // The facing is locked during the slide: momentum, not steering
        player.SetVelocity(player.transform.forward * _speed, player.Rb.linearVelocity.y);
    }

    public override void Exit()
    {
        base.Exit();
        player.ResetCollider();
    }
}
