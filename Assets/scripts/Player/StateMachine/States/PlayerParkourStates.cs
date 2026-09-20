using UnityEngine;

public class PlayerVaultState : PlayerState
{
    private Vector3 startPos;
    private Vector3 targetPos;
    private float vaultDuration = 0.4f;

    public PlayerVaultState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        player.SetKinematic(true);
        startPos = player.transform.position;
        // Asume un obstáculo pequeño, se mueve hacia adelante y un poco arriba
        targetPos = startPos + new Vector3(player.FacingDirection * 2f, 0f, 0f);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();
        float t = (Time.time - startTime) / vaultDuration;
        
        if (t >= 1f)
        {
            player.transform.position = targetPos;
            stateMachine.ChangeState(player.RunState);
        }
        else
        {
            // Movimiento Lerp en arco
            Vector3 currentPos = Vector3.Lerp(startPos, targetPos, t);
            currentPos.y += Mathf.Sin(t * Mathf.PI) * 1f; // Arco de altura 1
            player.transform.position = currentPos;
        }
    }

    public override void Exit()
    {
        base.Exit();
        player.SetKinematic(false);
    }
}

public class PlayerLedgeGrabState : PlayerState
{
    public PlayerLedgeGrabState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        player.SetVelocity(0f, 0f);
        player.SetKinematic(true);

        // Snap to ledge offset
        float grabOffsetX = 0.4f;
        float grabOffsetY = 1f;
        Vector3 snapPos = player.CurrentLedgeCorner - new Vector3(player.FacingDirection * grabOffsetX, grabOffsetY, 0f);
        player.transform.position = snapPos;
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (player.JumpTriggered)
        {
            // Espacio presionado -> Subir el borde
            stateMachine.ChangeState(player.LedgeClimbState);
        }
        else if (player.InputX == -player.FacingDirection)
        {
            // Soltarse si presiona la dirección opuesta
            stateMachine.ChangeState(player.IdleState);
        }
    }

    public override void Exit()
    {
        base.Exit();
        // Solo restaurar kinematic si NO vamos al ClimbState
        if (stateMachine.CurrentState != player.LedgeClimbState)
        {
            player.SetKinematic(false);
        }
    }
}

public class PlayerLedgeClimbState : PlayerState
{
    private Vector3 startPos;
    private Vector3 climbTargetPos;
    private float climbDuration = 0.5f;

    public PlayerLedgeClimbState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        startPos = player.transform.position;
        // Subir y adelantar sobre la esquina
        climbTargetPos = player.CurrentLedgeCorner + new Vector3(player.FacingDirection * 0.4f, 0.1f, 0f);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();
        float t = (Time.time - startTime) / climbDuration;
        
        if (t >= 1f)
        {
            player.transform.position = climbTargetPos;
            stateMachine.ChangeState(player.IdleState);
        }
        else
        {
            // Movimiento por código: primero arriba, luego hacia adelante
            Vector3 currentPos = startPos;
            if (t < 0.5f)
            {
                currentPos.y = Mathf.Lerp(startPos.y, climbTargetPos.y, t * 2f);
            }
            else
            {
                currentPos.y = climbTargetPos.y;
                currentPos.x = Mathf.Lerp(startPos.x, climbTargetPos.x, (t - 0.5f) * 2f);
            }
            player.transform.position = currentPos;
        }
    }

    public override void Exit()
    {
        base.Exit();
        player.SetKinematic(false);
    }
}
