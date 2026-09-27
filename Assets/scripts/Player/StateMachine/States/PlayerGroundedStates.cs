using UnityEngine;

// ────────────────────────────────────────────────────────────────────────────────
// PlayerIdleState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Default grounded state. Transitions to Run, Jump, Block, or Dodge.
/// </summary>
public class PlayerIdleState : PlayerState
{
    public PlayerIdleState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        // Combat takes priority over movement when grounded
        if (player.IsBlockHeld)
        {
            stateMachine.ChangeState(player.BlockState);
            return;
        }

        if (player.DodgeTriggered && player.IsGrounded)
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
            stateMachine.ChangeState(player.JumpState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        // Preserve vertical velocity (gravity) while zeroing horizontal drift
        player.SetVelocity(0f, player.Rb.linearVelocity.y);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerRunState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Grounded running state. Auto-sprint activates after SprintActivationTime seconds.
/// </summary>
public class PlayerRunState : PlayerState
{
    public PlayerRunState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        player.IsSprint = false;
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        // Auto-sprint: activates after the configured threshold (default 3s)
        if (!player.IsSprint && Time.time - startTime >= player.SprintActivationTime)
            player.IsSprint = true;

        // Combat interrupts run (can attack while moving)
        if (player.LightAttackTriggered)
        {
            player.IsSprint = false;
            stateMachine.ChangeState(player.LightAttackState);
            return;
        }

        if (player.HeavyAttackTriggered)
        {
            player.IsSprint = false;
            stateMachine.ChangeState(player.HeavyAttackState);
            return;
        }

        if (player.IsBlockHeld)
        {
            player.IsSprint = false;
            stateMachine.ChangeState(player.BlockState);
            return;
        }

        if (player.DodgeTriggered && player.IsGrounded)
        {
            player.IsSprint = false;
            stateMachine.ChangeState(player.DodgeState);
            return;
        }

        // Parkour — disabled: vault/ledge/wall-jump assume a fixed 2.5D plane (left/right
        // raycasts) and don't have a defined behavior yet in free-roam 3D. See CONTEXTO.md.
        // if (player.EnvChecker.IsObstacleVaultable(player.FacingDirection))
        // {
        //     stateMachine.ChangeState(player.VaultState);
        //     return;
        // }

        if (player.SlideTriggered && player.IsGrounded)
        {
            stateMachine.ChangeState(player.SlideState);
            return;
        }

        if (player.JumpTriggered && player.IsGrounded)
        {
            stateMachine.ChangeState(player.JumpState);
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
        float speed = player.IsSprint ? player.SprintSpeed : player.BaseSpeed;
        player.SetVelocity(player.MoveDirection * speed, player.Rb.linearVelocity.y);
    }

    public override void Exit()
    {
        base.Exit();
        player.IsSprint = false;
    }
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
