using UnityEngine;

public abstract class PlayerState
{
    protected PlayerMovement player;
    protected PlayerStateMachine stateMachine;
    protected float startTime;

    public PlayerState(PlayerMovement player, PlayerStateMachine stateMachine)
    {
        this.player = player;
        this.stateMachine = stateMachine;
    }

    public virtual void Enter()
    {
        startTime = Time.time;
    }

    public virtual void LogicUpdate() { }

    public virtual void PhysicsUpdate() { }

    public virtual void Exit() { }
}
