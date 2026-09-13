using UnityEngine;

/// <summary>
/// Contract for ground contact detection across entities (Player, Enemies, NPCs).
/// Adheres to Interface Segregation and Dependency Inversion principles.
/// </summary>
public interface IGroundChecker
{
    /// <summary>
    /// Returns true if the entity is currently touching a walkable surface.
    /// </summary>
    bool IsGrounded { get; }

    /// <summary>
    /// Performs the ground detection query (typically raycast or boxcast).
    /// </summary>
    bool CheckGrounded();
}
