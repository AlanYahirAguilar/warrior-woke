using UnityEngine;

// ────────────────────────────────────────────────────────────────────────────────
// PlayerJumpState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Airborne state entered after a grounded jump.
/// Detects ledges on both the ascending AND descending arc (GDD requirement).
/// Wall jumps are allowed while ascending if a wall is touched and the consecutive limit is not reached.
/// </summary>
public class PlayerJumpState : PlayerState
{
    public PlayerJumpState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        Vector3 horizontal = new Vector3(player.Rb.linearVelocity.x, 0f, player.Rb.linearVelocity.z);
        player.SetVelocity(horizontal, player.JumpSpeed);
        player.ResetConsecutiveWallJumps();
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        // ── Ledge detection and wall jump — habilitado en 3D
        if (player.EnvChecker.IsLedgeDetected(player.transform.forward, out Vector3 ledgeCorner))
        {
            player.CurrentLedgeCorner = ledgeCorner;
            stateMachine.ChangeState(player.LedgeGrabState);
            return;
        }

        if (player.Rb.linearVelocity.y > 0f &&
            player.EnvChecker.IsTouchingWall(player.transform.forward) &&
            player.JumpTriggered &&
            player.ConsecutiveWallJumps < 2)
        {
            stateMachine.ChangeState(player.WallJumpState);
            return;
        }

        // ── Land detection — transition to idle when grounded ──
        if (player.Rb.linearVelocity.y <= 0f && player.IsGrounded)
        {
            stateMachine.ChangeState(player.IdleState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        float speed = player.IsSprint ? player.SprintSpeed : player.BaseSpeed;
        player.SetVelocity(player.MoveDirection * speed, player.Rb.linearVelocity.y);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerWallJumpState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Short airborne state that applies wall-rebound impulse.
/// Locks directional input for wallJumpLockoutDuration seconds to prevent
/// the player from immediately sticking back to the same wall.
/// GDD: max 2 consecutive wall jumps before touching the ground again.
/// </summary>
public class PlayerWallJumpState : PlayerState
{
    private const float WallJumpLockoutDuration = 0.15f;

    public PlayerWallJumpState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        player.IncrementWallJump();

        // Impulse in the opposite direction of the wall (3D)
        Vector3 jumpDir = -player.transform.forward;
        player.SetVelocity(jumpDir * player.BaseSpeed, player.JumpSpeed);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (Time.time >= startTime + WallJumpLockoutDuration)
            stateMachine.ChangeState(player.JumpState);
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        // Intentionally empty: horizontal impulse from Enter() is preserved during lockout.
        // This prevents the player from immediately re-grabbing the same wall.
    }
}
