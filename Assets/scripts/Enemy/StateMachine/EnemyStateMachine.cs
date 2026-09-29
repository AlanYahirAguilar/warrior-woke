/// <summary>
/// Generic state machine for enemies.
/// Identical in structure to PlayerStateMachine — promotes code reuse and reduces cognitive load.
/// </summary>
public class EnemyStateMachine
{
    public EnemyState CurrentState { get; private set; }

    public void Initialize(EnemyState startingState)
    {
        CurrentState = startingState;
        CurrentState.Enter();
    }

    public void ChangeState(EnemyState newState)
    {
        CurrentState?.Exit();
        CurrentState = newState;
        CurrentState.Enter();
    }
}
