using UnityEngine;

/// <summary>
/// Interface defining the contract for input providers, decoupling input sources from movement logic.
/// Follows the Interface Segregation and Dependency Inversion principles.
/// </summary>
public interface IInputProvider
{
    /// <summary>
    /// Normalized or raw 2D input direction (X = horizontal, Y = vertical).
    /// </summary>
    Vector2 MoveInput { get; }

    /// <summary>
    /// Indicates whether active movement input is being received.
    /// </summary>
    bool HasMoveInput { get; }
}
