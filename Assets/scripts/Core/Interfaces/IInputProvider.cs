/// <summary>
/// Defines the contract for input delivery in a 3D action-platformer.
/// Follows Interface Segregation and Dependency Inversion principles.
/// Bindings follow GDD §14 (decision P1); see PlayerInputHandler for the keyboard layout.
/// </summary>
public interface IInputProvider
{
    // ─── Movement ───────────────────────────────────────────────────────────────

    /// <summary>Horizontal axis input (-1f to 1f: left/right strafe, camera-relative).</summary>
    float HorizontalMove { get; }

    /// <summary>Vertical axis input (-1f to 1f: back/forward, camera-relative).</summary>
    float VerticalMove { get; }

    /// <summary>Sprint — Shift held (GDD §5.2). True while the key is held.</summary>
    bool IsSprintHeld { get; }

    /// <summary>Walk — Left Ctrl held (P28): slow, oriented movement (strafe and walking backward).</summary>
    bool IsWalkHeld { get; }

    // ─── Parkour ─────────────────────────────────────────────────────────────────

    /// <summary>Indicates whether jump input was pressed this cycle.</summary>
    bool IsJumpTriggered { get; }

    /// <summary>C pressed this cycle: slide when running with momentum, crouch toggle otherwise (P28).</summary>
    bool IsSlideTriggered { get; }

    /// <summary>Consumes the pending jump trigger to prevent repeated executions across physics steps.</summary>
    bool ConsumeJumpTrigger();

    /// <summary>Consumes the pending slide trigger to prevent repeated executions.</summary>
    bool ConsumeSlideTrigger();

    // ─── Combat ─────────────────────────────────────────────────────────────────

    /// <summary>Light attack — J. True only on the frame it was pressed.</summary>
    bool IsLightAttackTriggered { get; }

    /// <summary>Heavy attack — K. True only on the frame it was pressed.</summary>
    bool IsHeavyAttackTriggered { get; }

    /// <summary>Block — L held down. True while the key is held.</summary>
    bool IsBlockHeld { get; }

    /// <summary>Dodge — Q (+ movement direction). True only on the frame it was pressed.</summary>
    bool IsDodgeTriggered { get; }

    /// <summary>Consumes the pending light attack trigger.</summary>
    bool ConsumeLightAttackTrigger();

    /// <summary>Consumes the pending heavy attack trigger.</summary>
    bool ConsumeHeavyAttackTrigger();

    /// <summary>Consumes the pending dodge trigger.</summary>
    bool ConsumeDodgeTrigger();
}
