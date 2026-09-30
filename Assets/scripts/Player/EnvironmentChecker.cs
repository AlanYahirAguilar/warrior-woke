using UnityEngine;

/// <summary>
/// Handles environment detection (ledges, low obstacles) using Raycasts.
/// Complies with SRP by isolating collision detection from movement logic.
/// Uses an OverlapSphere pre-filter to skip Raycasts when no geometry is nearby,
/// reducing per-frame physics work in open areas.
/// </summary>
public class EnvironmentChecker : MonoBehaviour
{
    [Header("Layer Masks")]
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Wall Check")]
    [SerializeField] private float wallCheckDistance = 0.6f;
    [SerializeField] private Transform centerPoint;

    [Header("Vault Check")]
    [SerializeField] private float vaultHeightCheck = 1.2f;
    [SerializeField] private Transform headPoint;

    [Header("Ledge Check")]
    [SerializeField] private float ledgeTopCheckDistance = 0.5f;

    [Header("Pre-filter")]
    [Tooltip("Sphere radius used as a cheap pre-filter before casting any rays. Set slightly larger than wallCheckDistance.")]
    [SerializeField] private float proximityCheckRadius = 1.2f;

    // Reusable buffer for OverlapSphere — avoids GC allocations every check
    private readonly Collider[] _proximityBuffer = new Collider[4];

    /// <summary>Returns true when at least one obstacle collider sits within the proximity sphere.</summary>
    private bool HasNearbyGeometry()
    {
        if (centerPoint == null) return false;
        int count = Physics.OverlapSphereNonAlloc(centerPoint.position, proximityCheckRadius, _proximityBuffer, obstacleLayer);
        return count > 0;
    }

    public bool IsObstacleVaultable(Vector3 direction)
    {
        // Si tocamos pared en el centro, pero NO tocamos pared a la altura de la cabeza, es un vault
        if (headPoint == null) return false;
        if (!HasNearbyGeometry()) return false;

        bool touchingMid = Physics.Raycast(centerPoint.position, direction, wallCheckDistance, obstacleLayer);
        bool touchingHigh = Physics.Raycast(headPoint.position, direction, wallCheckDistance, obstacleLayer);

        return touchingMid && !touchingHigh;
    }

    public bool IsLedgeDetected(Vector3 direction, out Vector3 ledgeCorner)
    {
        ledgeCorner = Vector3.zero;
        if (headPoint == null || centerPoint == null) return false;
        if (!HasNearbyGeometry()) return false;

        // Si choca la cabeza y el centro, es una pared alta
        bool touchingHigh = Physics.Raycast(headPoint.position, direction, wallCheckDistance, obstacleLayer);
        bool touchingMid = Physics.Raycast(centerPoint.position, direction, wallCheckDistance, obstacleLayer);

        if (touchingHigh && touchingMid)
        {
            // Tirar un rayo desde un poco más arriba y hacia adelante, luego hacia abajo para encontrar la esquina
            Vector3 topCheckOrigin = headPoint.position + Vector3.up * 0.5f + direction * wallCheckDistance;
            if (Physics.Raycast(topCheckOrigin, Vector3.down, out RaycastHit hit, ledgeTopCheckDistance, obstacleLayer))
            {
                ledgeCorner = hit.point;
                return true;
            }
        }
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (centerPoint != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(centerPoint.position, centerPoint.position + transform.forward * wallCheckDistance);
            Gizmos.color = new Color(0f, 0.5f, 1f, 0.15f);
            Gizmos.DrawWireSphere(centerPoint.position, proximityCheckRadius);
        }
        if (headPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(headPoint.position, headPoint.position + transform.forward * wallCheckDistance);
        }
    }
}

