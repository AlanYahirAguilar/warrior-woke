using UnityEngine;

// ────────────────────────────────────────────────────────────────────────────────
// PlayerJumpState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Airborne state entered after a grounded jump.
/// Detects ledges on both the ascending and descending arc.
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
