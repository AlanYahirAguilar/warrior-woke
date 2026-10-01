using UnityEngine;

// ────────────────────────────────────────────────────────────────────────────────
// PlayerJumpState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Rising part of a grounded jump. Detects ledges on the way up and hands over to
/// PlayerFallState at the apex (or lands directly if it touches ground while not rising).
/// Keeps sprint speed if the jump started while sprinting (GDD §5.2: sprint extends jumps).
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
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        // ── Ledge detection — habilitado en 3D
        if (player.EnvChecker.IsLedgeDetected(player.transform.forward, out Vector3 ledgeCorner))
        {
            player.CurrentLedgeCorner = ledgeCorner;
            stateMachine.ChangeState(player.LedgeGrabState);
            return;
        }

        // ── Apex reached: land if already on the ground, otherwise start falling ──
        if (player.Rb.linearVelocity.y <= 0f)
        {
            stateMachine.ChangeState(player.IsGrounded ? (PlayerState)player.IdleState : player.FallState);
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
// PlayerFallState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Airborne without rising: after a jump's apex or after leaving an edge without jumping
/// (docs/arquitectura.md T13). Keeps air control and the sprint speed it arrived with,
/// can grab ledges, and lands into Run or Idle.
/// The future fatal-fall check (GDD §5.12, F15) belongs here.
/// </summary>
public class PlayerFallState : PlayerState
{
    public PlayerFallState(PlayerMovement player, PlayerStateMachine stateMachine)
        : base(player, stateMachine) { }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (player.EnvChecker.IsLedgeDetected(player.transform.forward, out Vector3 ledgeCorner))
        {
            player.CurrentLedgeCorner = ledgeCorner;
            stateMachine.ChangeState(player.LedgeGrabState);
            return;
        }

        if (player.IsGrounded && player.Rb.linearVelocity.y <= 0f)
        {
            stateMachine.ChangeState(player.HasMoveInput ? (PlayerState)player.RunState : player.IdleState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        float speed = player.IsSprint ? player.SprintSpeed : player.BaseSpeed;
        player.SetVelocity(player.MoveDirection * speed, player.Rb.linearVelocity.y);
    }
}
