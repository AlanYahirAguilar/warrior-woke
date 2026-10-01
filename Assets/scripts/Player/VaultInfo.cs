using UnityEngine;

/// <summary>
/// Result of EnvironmentChecker.TryFindVault: everything PlayerVaultState needs to cross a
/// low obstacle (GDD §5.4). Plain data, no allocations.
/// </summary>
public struct VaultInfo
{
    /// <summary>Horizontal direction of the vault (toward the obstacle), normalized.</summary>
    public Vector3 Direction;

    /// <summary>Ground point behind the obstacle where the feet land.</summary>
    public Vector3 LandingPoint;

    /// <summary>Point on top of the obstacle, near its front edge, where the left hand rests (IK).</summary>
    public Vector3 HandPoint;

    /// <summary>World Y of the obstacle's top surface.</summary>
    public float TopY;
}
