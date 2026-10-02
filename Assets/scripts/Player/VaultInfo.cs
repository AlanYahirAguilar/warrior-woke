using UnityEngine;

/// <summary>
/// Result of EnvironmentChecker.TryFindVault: everything PlayerVaultState needs to cross a
/// low obstacle (GDD §5.4). Plain data, no allocations.
/// </summary>
public struct VaultInfo
{
    /// <summary>Horizontal direction of the vault: into the obstacle's front face (−face normal), normalized.</summary>
    public Vector3 Direction;

    /// <summary>Ground point behind the obstacle where the feet land.</summary>
    public Vector3 LandingPoint;

    /// <summary>Point on the obstacle's top surface, just past its front edge, where the left hand is planted.</summary>
    public Vector3 HandPoint;

    /// <summary>World Y of the obstacle's top surface.</summary>
    public float TopY;

    /// <summary>Front edge of the obstacle on the body's line (front face at the top's height).</summary>
    public Vector3 FrontPoint;

    /// <summary>Depth of the obstacle along Direction (m).</summary>
    public float Depth;

    /// <summary>Horizontal distance (m) from the body to the hand point when the vault was detected.</summary>
    public float HandDistance;
}
