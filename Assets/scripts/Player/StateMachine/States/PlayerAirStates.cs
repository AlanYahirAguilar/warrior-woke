using UnityEngine;

// ────────────────────────────────────────────────────────────────────────────────
// PlayerJumpState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Rising part of a grounded jump. Detects ledges on the way up and hands over to
/// PlayerFallState at the apex (or lands directly if it touches ground while not rising).
/// The take-off keeps the ground speed (sprint extends jumps, GDD §5.2); in the air the input only
/// steers the momentum (PlayerMovement.AccelerateAir), it does not replace it, and the body never
/// flies into a wall it does not clear (PlayerMovement.LimitAirIntoWalls).
/// </summary>
public class PlayerJumpState : PlayerState
{
    public PlayerJumpState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        Vector3 horizontal = new Vector3(player.Velocity.x, 0f, player.Velocity.z);
        player.SetVelocity(horizontal, player.JumpSpeed);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (PlayerLedgeGrabState.TryStartInAir(player))
        {
            stateMachine.ChangeState(player.LedgeGrabState);
            return;
        }

        // ── Apex reached: land if already on the ground, otherwise start falling ──
        if (player.Velocity.y <= 0f)
        {
            stateMachine.ChangeState(player.IsGrounded ? (PlayerState)player.IdleState : player.FallState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        float speed = player.IsSprint ? player.SprintSpeed : player.BaseSpeed;
        player.AccelerateAir(player.MoveDirection * speed);
        player.LimitAirIntoWalls();
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerFallState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Airborne without rising: after a jump's apex, after leaving an edge without jumping or after
/// letting go of a ledge. Keeps the momentum it arrived with, can grab ledges, and on touching the
/// ground registers the landing (its weight depends on the drop height) before going to Run or Idle.
/// It never flies into a wall it does not clear (PlayerMovement.LimitAirIntoWalls).
/// The future fatal-fall check (GDD §5.12, F15) belongs here.
/// </summary>
public class PlayerFallState : PlayerState
{
    public PlayerFallState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (PlayerLedgeGrabState.TryStartInAir(player))
        {
            stateMachine.ChangeState(player.LedgeGrabState);
            return;
        }

        if (player.IsGrounded && player.Velocity.y <= 0f)
        {
            player.RegisterLanding();
            stateMachine.ChangeState(player.HasMoveInput ? (PlayerState)player.RunState : player.IdleState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        float speed = player.IsSprint ? player.SprintSpeed : player.BaseSpeed;
        player.AccelerateAir(player.MoveDirection * speed);
        player.LimitAirIntoWalls();
    }
}
