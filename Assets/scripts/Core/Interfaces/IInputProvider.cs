/// <summary>
/// Defines the contract for input delivery in a 3D action-platformer.
/// Follows Interface Segregation and Dependency Inversion principles.
/// Sprint is handled internally by the state machine (auto-sprint), not by this contract.
/// </summary>
public interface IInputProvider
{
    // ─── Movement ───────────────────────────────────────────────────────────────

    /// <summary>Horizontal axis input (-1f to 1f: left/right).</summary>
    float HorizontalMove { get; }

    // ─── Parkour ─────────────────────────────────────────────────────────────────

    /// <summary>Indicates whether jump input was pressed this cycle.</summary>
    bool IsJumpTriggered { get; }

    /// <summary>Indicates whether slide input was pressed this cycle.</summary>
    bool IsSlideTriggered { get; }

    /// <summary>Consumes the pending jump trigger to prevent repeated executions across physics steps.</summary>
    bool ConsumeJumpTrigger();

    /// <summary>Consumes the pending slide trigger to prevent repeated executions.</summary>
    bool ConsumeSlideTrigger();

    // ─── Combat ─────────────────────────────────────────────────────────────────

    /// <summary>Light attack — Mouse1 (left click). True only on the frame it was pressed.</summary>
    bool IsLightAttackTriggered { get; }

    /// <summary>Heavy attack — Mouse2 (right click). True only on the frame it was pressed.</summary>
    bool IsHeavyAttackTriggered { get; }

    /// <summary>Block — F key held down. True while the key is held.</summary>
    bool IsBlockHeld { get; }

    /// <summary>Dodge — E key. True only on the frame it was pressed.</summary>
    bool IsDodgeTriggered { get; }

    /// <summary>Consumes the pending light attack trigger.</summary>
    bool ConsumeLightAttackTrigger();

    /// <summary>Consumes the pending heavy attack trigger.</summary>
    bool ConsumeHeavyAttackTrigger();

    /// <summary>Consumes the pending dodge trigger.</summary>
    bool ConsumeDodgeTrigger();
}
