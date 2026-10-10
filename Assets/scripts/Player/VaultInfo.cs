using UnityEngine;

/// <summary>
/// Result of EnvironmentChecker.TryFindVault: the obstacle measured on the geometry (GDD §5.4). Which
/// clip crosses it, where the hands go and where the feet land are decided per clip by VaultPlanner.
/// Plain data, no allocations.
/// </summary>
public struct VaultInfo
{
    /// <summary>Horizontal direction of the vault: into the obstacle's front face (−face normal), normalized.</summary>
    public Vector3 Direction;

    /// <summary>World Y of the obstacle's top surface.</summary>
    public float TopY;

    /// <summary>Front edge of the obstacle on the body's line (front face at the top's height).</summary>
    public Vector3 FrontPoint;

    /// <summary>Depth of the obstacle along Direction (m).</summary>
    public float Depth;

    /// <summary>The colliders measured as the front face and the top: the body passes through them during the vault (PlayerMovement.BeginRootMotion).</summary>
    public Collider Front, Top;
}
