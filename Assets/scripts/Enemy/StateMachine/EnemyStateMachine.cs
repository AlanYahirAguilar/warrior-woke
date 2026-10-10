/// <summary>Holds and switches the current state of an enemy (Exit → Enter), like PlayerStateMachine.</summary>
public class EnemyStateMachine
{
    public EnemyState CurrentState { get; private set; }

    /// <summary>The state before the current one.</summary>
    public EnemyState PreviousState { get; private set; }

    /// <summary>Fires after every change (the animation, the tests).</summary>
    public event System.Action<EnemyState> OnStateChanged;

    public void Initialize(EnemyState start)
    {
        CurrentState = start;
        CurrentState.Enter();
        OnStateChanged?.Invoke(CurrentState);
    }

    public void ChangeState(EnemyState next)
    {
        if (next == null) return;
        CurrentState?.Exit();
        PreviousState = CurrentState;
        CurrentState = next;
        CurrentState.Enter();
        OnStateChanged?.Invoke(CurrentState);
    }
}
