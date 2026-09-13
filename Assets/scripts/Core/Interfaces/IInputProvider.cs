using UnityEngine;

/// <summary>
/// Defines the contract for input delivery in a 2.5D action-platformer.
/// Follows Interface Segregation and Dependency Inversion principles.
/// </summary>
public interface IInputProvider
{
    /// <summary>
    /// Horizontal axis input (-1f to 1f: left/right).
    /// </summary>
    float HorizontalMove { get; }

    /// <summary>
    /// Indicates whether sprint button/key is held down.
    /// </summary>
    bool IsSprintPressed { get; }

    /// <summary>
    /// Indicates whether jump input was pressed this cycle.
    /// </summary>
    bool IsJumpTriggered { get; }

    /// <summary>
    /// Indicates whether slide input was pressed this cycle.
    /// </summary>
    bool IsSlideTriggered { get; }

    /// <summary>
    /// Consumes the pending jump trigger to prevent repeated executions across physics steps.
    /// </summary>
    bool ConsumeJumpTrigger();

    /// <summary>
    /// Consumes the pending slide trigger to prevent repeated executions.
    /// </summary>
    bool ConsumeSlideTrigger();
}
