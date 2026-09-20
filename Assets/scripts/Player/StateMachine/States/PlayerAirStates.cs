using UnityEngine;

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

        if (player.Rb.linearVelocity.y <= 0f)
        {
            // Cayendo
            if (player.IsGrounded)
            {
                stateMachine.ChangeState(player.IdleState);
            }
            else if (player.EnvChecker.IsLedgeDetected(player.FacingDirection, out Vector3 ledgeCorner))
            {
                player.CurrentLedgeCorner = ledgeCorner;
                stateMachine.ChangeState(player.LedgeGrabState);
            }
        }
        else if (player.EnvChecker.IsTouchingWall(player.FacingDirection) && player.JumpTriggered && player.ConsecutiveWallJumps < 2)
        {
            stateMachine.ChangeState(player.WallJumpState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        float currentSpeed = player.IsSprint ? player.SprintSpeed : player.BaseSpeed;
        player.SetVelocity(player.InputX * currentSpeed, player.Rb.linearVelocity.y);
    }
}

public class PlayerWallJumpState : PlayerState
{
    private float wallJumpLockoutDuration = 0.15f;

    public PlayerWallJumpState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        player.IncrementWallJump();
        
        // Impulso en dirección contraria a la pared
        float jumpDir = -player.FacingDirection;
        player.SetVelocity(jumpDir * player.BaseSpeed, player.JumpSpeed);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (Time.time >= startTime + wallJumpLockoutDuration)
        {
            stateMachine.ChangeState(player.JumpState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        // Durante este estado (los 0.15s), ignoramos el InputX para no pegarnos a la pared inmediatamente
    }
}
