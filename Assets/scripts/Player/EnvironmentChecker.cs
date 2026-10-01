using UnityEngine;

/// <summary>
/// Handles environment detection (ledges, low obstacles) using Raycasts.
/// The vault check follows the approach of Dynamic Parkour System's VaultObstacle (MIT, Èric Canela):
/// knee-height ray → obstacle top → landing point behind it — but measures the obstacle's real
/// height and depth with rays instead of relying on its transform scale, and uses layers, not tags.
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
    [Tooltip("Highest obstacle (m above the feet) that can be vaulted (GDD §5.4: low obstacles only).")]
    [SerializeField] private float vaultHeightCheck = 1.2f;
    [Tooltip("Lowest obstacle (m above the feet) that counts as a vault; lower ones are auto-stepped or jumped.")]
    [SerializeField] private float minVaultHeight = 0.45f;
    [Tooltip("Deepest obstacle (m along the movement) that can be vaulted in one move.")]
    [SerializeField] private float maxVaultDepth = 1.5f;
    [Tooltip("How far ahead (m from the feet) an obstacle is detected when Space is pressed.")]
    [SerializeField] private float vaultReach = 1.1f;
    [Tooltip("Distance (m) behind the obstacle's back face where the feet land.")]
    [SerializeField] private float vaultLandOffset = 0.6f;
    [Tooltip("Layers that count as ground for the landing point.")]
    [SerializeField] private LayerMask landingLayer;
    [SerializeField] private Transform headPoint;

    [Header("Ledge Check")]
    [SerializeField] private float ledgeTopCheckDistance = 0.5f;

    [Header("Pre-filter")]
    [Tooltip("Sphere radius used as a cheap pre-filter before casting any rays. Set slightly larger than wallCheckDistance.")]
    [SerializeField] private float proximityCheckRadius = 1.2f;

    // Reusable buffer for OverlapSphere — avoids GC allocations every check
    private readonly Collider[] _proximityBuffer = new Collider[4];

    private void Awake()
    {
        if (landingLayer.value == 0)
            landingLayer = LayerMask.GetMask("Ground", "Obstacle");
    }

    /// <summary>Returns true when at least one obstacle collider sits within the proximity sphere.</summary>
    private bool HasNearbyGeometry()
    {
        if (centerPoint == null) return false;
        int count = Physics.OverlapSphereNonAlloc(centerPoint.position, proximityCheckRadius, _proximityBuffer, obstacleLayer);
        return count > 0;
    }

    /// <summary>
    /// Looks for a vaultable obstacle in <paramref name="direction"/> (GDD §5.4): a front face within
    /// reach at knee height, a top between minVaultHeight and vaultHeightCheck above the feet, a depth
    /// up to maxVaultDepth and ground behind it. Allocation-free.
    /// </summary>
    public bool TryFindVault(Vector3 direction, float feetY, out VaultInfo info)
    {
        info = default;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || !HasNearbyGeometry()) return false;
        direction.Normalize();

        Vector3 feet = new Vector3(transform.position.x, feetY, transform.position.z);

        // 1. Front face at knee height, roughly facing the player
        Vector3 knee = feet + Vector3.up * (minVaultHeight * 0.6f);
        if (!Physics.Raycast(knee, direction, out RaycastHit front, vaultReach, obstacleLayer, QueryTriggerInteraction.Ignore))
            return false;
        if (Vector3.Dot(-front.normal, direction) < 0.6f) return false;

        // 2. Top: cast down just past the front face, from above the highest vaultable height
        Vector3 topOrigin = front.point + direction * 0.05f;
        topOrigin.y = feetY + vaultHeightCheck + 0.3f;
        if (!Physics.Raycast(topOrigin, Vector3.down, out RaycastHit top, vaultHeightCheck + 0.3f, obstacleLayer, QueryTriggerInteraction.Ignore))
            return false;
        float height = top.point.y - feetY;
        if (top.distance < 0.01f || height < minVaultHeight || height > vaultHeightCheck) return false;

        // 3. Depth: cast back toward the player from beyond the deepest vaultable obstacle.
        //    If the origin is still inside the obstacle the ray hits nothing → too deep.
        Vector3 backOrigin = front.point + direction * (maxVaultDepth + 0.05f);
        backOrigin.y = top.point.y - 0.1f;
        if (!Physics.Raycast(backOrigin, -direction, out RaycastHit back, maxVaultDepth + 0.05f, obstacleLayer, QueryTriggerInteraction.Ignore))
            return false;

        // 4. Landing: ground behind the back face
        Vector3 landOrigin = back.point + direction * vaultLandOffset;
        landOrigin.y = top.point.y + 0.5f;
        if (!Physics.Raycast(landOrigin, Vector3.down, out RaycastHit land, top.point.y - feetY + 2.5f, landingLayer, QueryTriggerInteraction.Ignore))
            return false;
        if (land.point.y > top.point.y - 0.2f) return false; // lands on top of something tall: not a vault

        info.Direction    = direction;
        info.LandingPoint = land.point;
        info.TopY         = top.point.y;
        info.HandPoint    = new Vector3(front.point.x, top.point.y, front.point.z) + direction * 0.15f;
        return true;
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

