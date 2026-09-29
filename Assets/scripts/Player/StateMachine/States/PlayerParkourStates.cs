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
        startPos = player.Rb.position;
        // Obstacle of moderate height: move forward and slightly up in an arc
        targetPos = startPos + player.transform.forward * 2f;
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        float t = (Time.time - startTime) / vaultDuration;

        if (t >= 1f)
        {
            // Snap to final position through the Rigidbody, never bypass physics
            player.Rb.MovePosition(targetPos);
            stateMachine.ChangeState(player.RunState);
        }
        else
        {
            // Arc movement via Lerp — routed through MovePosition so physics stays aware
            Vector3 currentPos = Vector3.Lerp(startPos, targetPos, t);
            currentPos.y += Mathf.Sin(t * Mathf.PI) * 1f; // Arc height 1 unit
            player.Rb.MovePosition(currentPos);
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

        // Snap to ledge offset — always route through Rigidbody so the physics engine stays aware
        float grabOffsetX = 0.4f;
        float grabOffsetY = 1f;
        Vector3 snapPos = player.CurrentLedgeCorner - player.transform.forward * grabOffsetX - Vector3.up * grabOffsetY;
        player.Rb.MovePosition(snapPos);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (player.JumpTriggered)
        {
            // Espacio presionado -> Subir el borde
            stateMachine.ChangeState(player.LedgeClimbState);
        }
        else if (player.HasMoveInput && Vector3.Dot(player.MoveDirection, player.transform.forward) < -0.5f)
        {
            // Soltarse si presiona la dirección opuesta al forward
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
        startPos = player.Rb.position;
        // Move up and forward past the ledge corner
        climbTargetPos = player.CurrentLedgeCorner + player.transform.forward * 0.4f + Vector3.up * 0.1f;
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        float t = (Time.time - startTime) / climbDuration;

        if (t >= 1f)
        {
            // Final snap through the Rigidbody — never bypass physics
            player.Rb.MovePosition(climbTargetPos);
            stateMachine.ChangeState(player.IdleState);
        }
        else
        {
            // Two-phase movement: first go up, then slide forward onto the ledge
            Vector3 currentPos = startPos;
            if (t < 0.5f)
            {
                currentPos.y = Mathf.Lerp(startPos.y, climbTargetPos.y, t * 2f);
            }
            else
            {
                currentPos.y = climbTargetPos.y;
                Vector3 startXZ = new Vector3(startPos.x, 0, startPos.z);
                Vector3 targetXZ = new Vector3(climbTargetPos.x, 0, climbTargetPos.z);
                Vector3 currentXZ = Vector3.Lerp(startXZ, targetXZ, (t - 0.5f) * 2f);
                currentPos.x = currentXZ.x;
                currentPos.z = currentXZ.z;
            }
            player.Rb.MovePosition(currentPos);
        }
    }

    public override void Exit()
    {
        base.Exit();
        player.SetKinematic(false);
    }
}
