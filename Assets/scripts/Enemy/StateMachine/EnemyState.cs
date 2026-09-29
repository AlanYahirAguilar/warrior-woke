using UnityEngine;

/// <summary>
/// Base class for all enemy states.
/// Mirrors the PlayerState pattern for architectural consistency.
/// Receives a reference to the Enemy MonoBehaviour (context) and its StateMachine.
/// </summary>
public abstract class EnemyState
{
    protected Enemy         enemy;
    protected EnemyStateMachine stateMachine;
    protected float         startTime;

    public EnemyState(Enemy enemy, EnemyStateMachine stateMachine)
    {
        this.enemy        = enemy;
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
