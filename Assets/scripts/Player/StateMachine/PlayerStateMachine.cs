using UnityEngine;

public class PlayerStateMachine
{
    public PlayerState CurrentState { get; private set; }

    /// <summary>Fires after a state has been entered (including re-entering the same state).</summary>
    public event System.Action<PlayerState> OnStateChanged;

    public void Initialize(PlayerState startingState)
    {
        CurrentState = startingState;
        CurrentState.Enter();
        OnStateChanged?.Invoke(CurrentState);
    }

    public void ChangeState(PlayerState newState)
    {
        if (CurrentState != null)
        {
            CurrentState.Exit();
        }

        CurrentState = newState;
        CurrentState.Enter();
        OnStateChanged?.Invoke(CurrentState);
    }
}
