using UnityEngine;

/// <summary>
/// Result of EnvironmentChecker.TryFindLedge: a real grab point measured on the geometry.
/// Plain data, no allocations.
/// </summary>
public struct LedgeInfo
{
    /// <summary>Point on the wall's face plane at the height of the top surface, in front of the player.</summary>
    public Vector3 Edge;

    /// <summary>Horizontal wall normal, pointing out of the wall toward the player.</summary>
    public Vector3 Normal;

    /// <summary>Point on the top surface, inset from the edge, where the feet end after climbing.</summary>
    public Vector3 StandPoint;

    /// <summary>World Y of the top surface.</summary>
    public float TopY;

    /// <summary>Rotation that faces the wall.</summary>
    public Quaternion FacingRotation => Quaternion.LookRotation(-Normal, Vector3.up);

    /// <summary>Horizontal axis along the edge (to the player's right when facing the wall).</summary>
    public Vector3 Tangent => Vector3.Cross(Vector3.up, -Normal).normalized;

    /// <summary>Point on the edge at the lateral position of <paramref name="worldPoint"/>.</summary>
    public Vector3 EdgeAt(Vector3 worldPoint)
    {
        Vector3 t = Tangent;
        return Edge + t * Vector3.Dot(worldPoint - Edge, t);
    }
}
