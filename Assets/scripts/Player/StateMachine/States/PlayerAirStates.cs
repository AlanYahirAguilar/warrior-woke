using UnityEngine;

public class PlayerFallState : PlayerState
{
    public PlayerFallState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (player.IsGrounded)
        {
            stateMachine.ChangeState(player.IdleState);
        }
        else if (player.EnvChecker.IsLedgeDetected(player.FacingDirection, out Vector3 ledgeCorner))
        {
            player.CurrentLedgeCorner = ledgeCorner;
            stateMachine.ChangeState(player.LedgeGrabState);
        }
        else if (player.EnvChecker.IsTouchingWall(player.FacingDirection) && player.Rb.linearVelocity.y < 0f)
        {
            stateMachine.ChangeState(player.WallSlideState);
        }
        // Jump buffer or coyote time could be added here later if desired
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        float currentSpeed = player.IsSprint ? player.SprintSpeed : player.BaseSpeed;
        player.SetVelocity(player.InputX * currentSpeed, player.Rb.linearVelocity.y);
    }
}

public class PlayerJumpState : PlayerState
{
    public PlayerJumpState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        player.SetVelocity(player.Rb.linearVelocity.x, player.JumpSpeed);
        player.ResetConsecutiveWallJumps();
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        // Wall jump is intentionally NOT available from JumpState (ascending).
        // The player must first contact the wall (WallSlideState) before being
        // able to wall jump. This gives the mechanic weight and prevents exploiting
        // it as a free double-jump mid-air.
        if (player.Rb.linearVelocity.y <= 0f)
        {
            stateMachine.ChangeState(player.FallState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        float currentSpeed = player.IsSprint ? player.SprintSpeed : player.BaseSpeed;
        player.SetVelocity(player.InputX * currentSpeed, player.Rb.linearVelocity.y);
    }
}

public class PlayerWallSlideState : PlayerState
{
    public PlayerWallSlideState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (player.IsGrounded)
        {
            stateMachine.ChangeState(player.IdleState);
        }
        else if (player.JumpTriggered && player.ConsecutiveWallJumps < 2 && !PlayerWallJumpState.IsCoolingDown)
        {
            stateMachine.ChangeState(player.WallJumpState);
        }
        else if (!player.EnvChecker.IsTouchingWall(player.FacingDirection))
        {
            stateMachine.ChangeState(player.FallState);
        }
        else if (player.EnvChecker.IsLedgeDetected(player.FacingDirection, out Vector3 ledgeCorner))
        {
            player.CurrentLedgeCorner = ledgeCorner;
            stateMachine.ChangeState(player.LedgeGrabState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        // Fall slowly to give the player time to react
        player.SetVelocity(0f, player.WallSlideSpeed);
    }
}

public class PlayerWallJumpState : PlayerState
{
    /// <summary>
    /// Direction lockout: prevents immediately re-sticking to the same wall
    /// right after launching. Purely a feel/input concern.
    /// </summary>
    private const float WallJumpDirectionLockout = 0.15f;

    /// <summary>
    /// GDD-mandated cooldown between consecutive wall jumps (max 2 before touching ground).
    /// Separate from the direction lockout — this is the inter-jump pacing constraint.
    /// </summary>
    private const float WallJumpCooldown = 0.2f;

    /// <summary>
    /// Tracks when the last wall jump was executed to enforce the GDD cooldown.
    /// Static so it persists across state re-entries within the same session.
    /// </summary>
    private static float _lastWallJumpTime = -999f;

    public PlayerWallJumpState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    public static bool IsCoolingDown => Time.time < _lastWallJumpTime + WallJumpCooldown;

    public override void Enter()
    {
        base.Enter();
        _lastWallJumpTime = Time.time;
        player.IncrementWallJump();

        // Directional: pressing toward the wall = vertical wall climb; otherwise = diagonal rebound
        float inputDir = Mathf.Sign(player.InputX);
        bool isPressingTowardsWall = Mathf.Abs(player.InputX) > 0.1f && inputDir == player.FacingDirection;

        if (isPressingTowardsWall)
        {
            // Vertical wall climb
            player.SetVelocity(0f, player.JumpSpeed * 1.1f);
        }
        else
        {
            // Classic diagonal wall jump rebound
            float jumpDir = -player.FacingDirection;
            player.SetVelocity(jumpDir * player.BaseSpeed, player.JumpSpeed);
        }
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        // Wait for direction lockout before handing control back to other states
        if (Time.time >= startTime + WallJumpDirectionLockout)
        {
            if (player.Rb.linearVelocity.y <= 0f)
            {
                stateMachine.ChangeState(player.FallState);
            }
            else
            {
                stateMachine.ChangeState(player.JumpState);
            }
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        // During the direction lockout window, ignore InputX so the player cannot
        // immediately counter-steer back into the same wall or distort the launch arc.
    }
}
