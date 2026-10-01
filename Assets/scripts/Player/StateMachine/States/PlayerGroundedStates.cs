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
        {
            // Space next to a low obstacle vaults it (GDD §5.4); otherwise it is a jump.
            stateMachine.ChangeState(PlayerVaultState.TryStart(player) ? (PlayerState)player.VaultState : player.JumpState);
        }
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
/// Low edges are climbed automatically (auto step).
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

        // Space: vault a low obstacle in front (GDD §5.4) or jump
        if (player.JumpTriggered && player.IsGrounded)
        {
            stateMachine.ChangeState(PlayerVaultState.TryStart(player) ? (PlayerState)player.VaultState : player.JumpState);
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
        player.AccelerateHorizontal(player.MoveDirection * speed);
        player.TryAutoStep();
    }

    // IsSprint is intentionally NOT cleared on Exit so a jump or fall started while sprinting
    // keeps sprint speed (GDD §5.2). Every other exit clears it, and Idle.Enter resets it.
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerSlideState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Slide mechanic: shrinks collider and maintains speed for SlideDuration seconds.
/// The player cannot exit the slide if there is a ceiling overhead (maintains ducked posture).
/// </summary>
public class PlayerSlideState : PlayerState
{
    public PlayerSlideState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        player.ShrinkCollider(0.5f);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (Time.time >= startTime + player.SlideDuration)
        {
            // Keep crouching if there is something above the player
            if (!player.HasCeilingOverhead())
                stateMachine.ChangeState(player.RunState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        // Use the character's current facing instead of raw input to maintain momentum even if keys are released
        player.SetVelocity(player.transform.forward * player.SlideSpeed, player.Rb.linearVelocity.y);
    }

    public override void Exit()
    {
        base.Exit();
        player.ResetCollider();
    }
}
