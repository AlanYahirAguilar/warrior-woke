using UnityEngine;

/// <summary>
/// One state of an enemy's AI (D2: one class per state, its transitions inside it). Ticks every frame
/// from Enemy.Update; the enemy's perception and the coordinator are read through the context.
/// </summary>
public abstract class EnemyState
{
    protected readonly Enemy enemy;
    protected readonly EnemyStateMachine stateMachine;
    protected float startTime;

    protected EnemyState(Enemy enemy, EnemyStateMachine stateMachine)
    {
        this.enemy = enemy;
        this.stateMachine = stateMachine;
    }

    /// <summary>Seconds since the state was entered.</summary>
    public float Elapsed => Time.time - startTime;

    /// <summary>Short name for logs and tests.</summary>
    public virtual string Name => GetType().Name.Replace("Enemy", "").Replace("State", "");

    public virtual void Enter() => startTime = Time.time;

    public virtual void Tick(float dt) { }

    public virtual void Exit() { }
}
