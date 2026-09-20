using UnityEngine;

public class PlayerIdleState : PlayerState
{
    public PlayerIdleState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    public override void LogicUpdate()
    {
        base.LogicUpdate();
        
        if (Mathf.Abs(player.InputX) > 0.1f)
        {
            stateMachine.ChangeState(player.RunState);
        }
        else if (player.JumpTriggered && player.IsGrounded)
        {
            stateMachine.ChangeState(player.JumpState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        player.SetVelocity(0f, player.Rb.linearVelocity.y);
    }
}

public class PlayerRunState : PlayerState
{
    public PlayerRunState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        // Sprint automático después de 1 segundo de correr continuamente
        if (Time.time - startTime >= 2f)
        {
            player.IsSprint = true;
        }

        if (Mathf.Abs(player.InputX) < 0.1f)
        {
            player.IsSprint = false;
            stateMachine.ChangeState(player.IdleState);
        }
        else if (player.JumpTriggered && player.IsGrounded)
        {
            stateMachine.ChangeState(player.JumpState);
        }
        else if (player.SlideTriggered && player.IsGrounded)
        {
            stateMachine.ChangeState(player.SlideState);
        }
        else if (player.EnvChecker.IsObstacleVaultable(player.FacingDirection))
        {
            stateMachine.ChangeState(player.VaultState);
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        float currentSpeed = player.IsSprint ? player.SprintSpeed : player.BaseSpeed;
        player.SetVelocity(player.InputX * currentSpeed, player.Rb.linearVelocity.y);
    }
}

public class PlayerSlideState : PlayerState
{
    public PlayerSlideState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

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
            if (!player.HasCeilingOverhead())
            {
                stateMachine.ChangeState(player.RunState);
            }
        }
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        player.SetVelocity(player.FacingDirection * player.SlideSpeed, player.Rb.linearVelocity.y);
    }

    public override void Exit()
    {
        base.Exit();
        player.ResetCollider();
    }
}
