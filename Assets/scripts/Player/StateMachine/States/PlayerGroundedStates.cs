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

        // Movement
        if (player.HasMoveInput)
        {
            stateMachine.ChangeState(player.RunState);
            return;
        }

        if (player.JumpTriggered && player.IsGrounded)
            stateMachine.ChangeState(ResolveSpace(player));
    }

    /// <summary>
    /// Space on the ground, by context: vault a low obstacle in front (GDD §5.4), grab a ledge within
    /// reach (P2) or jump.
    /// </summary>
    public static PlayerState ResolveSpace(PlayerMovement player)
    {
        if (PlayerVaultState.TryStart(player)) return player.VaultState;
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
/// Grounded running state (WASD). Sprint while Shift is held (GDD §5.2).
/// Accelerates toward the target speed, so the animation blends Idle → Walk → Jog → Run.
/// Back input without sprint walks backward facing forward (PlayerMovement.IsBackpedaling).
/// Low edges are climbed and stepped down automatically (auto step). After a landing the speed recovers gradually.
/// </summary>
public class PlayerRunState : PlayerState
{
    public PlayerRunState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

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

        // Slide: C while sprinting (outside the GDD, kept by decision P2)
        if (player.SlideTriggered && player.IsGrounded && player.IsSprint)
        {
            player.IsSprint = false;
            stateMachine.ChangeState(player.SlideState);
            return;
        }

        // Space: vault, ledge grab or jump, by context
        if (player.JumpTriggered && player.IsGrounded)
        {
            stateMachine.ChangeState(PlayerIdleState.ResolveSpace(player));
            return;
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
        float speed = player.IsBackpedaling ? player.BackpedalSpeed
                    : player.IsSprint       ? player.SprintSpeed
                    :                         player.BaseSpeed;
        // After a landing the legs absorb the impact: full speed comes back over the recovery
        player.AccelerateHorizontal(player.MoveDirection * (speed * player.RecoverySpeedScale));
        player.TryAutoStep();
        player.TryStepDown();
    }

    // IsSprint is intentionally NOT cleared on Exit so a jump or fall started while sprinting
    // keeps sprint speed (GDD §5.2). Every other exit clears it, and Idle.Enter resets it.
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerSlideState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Slide (outside the GDD, kept by decision P2): C while sprinting. The collider shrinks to half
/// height keeping its bottom on the ground, the body keeps the speed it had (no boost beyond
/// SlideSpeed) and loses it with friction. It cannot stand up under a ceiling, so it keeps sliding
/// (at least at SlideMinSpeed) until there is room.
/// </summary>
public class PlayerSlideState : PlayerState
{
    private float _speed;

    public PlayerSlideState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        player.ShrinkCollider(0.5f);
        _speed = Mathf.Min(Mathf.Max(player.HorizontalSpeed, player.BaseSpeed), player.SlideSpeed);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (Time.time >= startTime + player.SlideDuration)
        {
            // Keep crouching if there is something above the player
            if (!player.HasCeilingOverhead())
                stateMachine.ChangeState(player.HasMoveInput ? (PlayerState)player.RunState : player.IdleState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        _speed = Mathf.Max(player.SlideMinSpeed, _speed - player.SlideFriction * Time.fixedDeltaTime);
        // The facing is locked during the slide: momentum, not steering
        player.SetVelocity(player.transform.forward * _speed, player.Rb.linearVelocity.y);
    }

    public override void Exit()
    {
        base.Exit();
        player.ResetCollider();
    }
}
