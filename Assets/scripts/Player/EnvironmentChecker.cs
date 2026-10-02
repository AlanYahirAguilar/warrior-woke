using UnityEngine;

/// <summary>
/// Handles environment detection (low obstacles and ledges) using Raycasts.
/// The vault check follows the approach of Dynamic Parkour System's VaultObstacle (MIT, Èric Canela):
/// knee-height ray → obstacle top → landing point behind it — but measures the obstacle's real
/// height and depth with rays instead of relying on its transform scale, and uses layers, not tags.
/// Every result is measured on the geometry (face normal, edge, top surface, free space) so the
/// parkour states can place the body and the IK goals on real contact points.
/// Complies with SRP by isolating collision detection from movement logic.
/// Uses an OverlapSphere pre-filter to skip Raycasts when no geometry is nearby,
/// reducing per-frame physics work in open areas.
/// </summary>
public class EnvironmentChecker : MonoBehaviour
{
    [Header("Layer Masks")]
    [SerializeField] private LayerMask obstacleLayer;

    [Header("Body Points")]
    [SerializeField] private Transform centerPoint;
    [SerializeField] private Transform headPoint;

    [Header("Vault Check")]
    [Tooltip("Highest obstacle (m above the feet) that can be vaulted (GDD §5.4: low obstacles only).")]
    [SerializeField] private float vaultHeightCheck = 1.2f;
    [Tooltip("Lowest obstacle (m above the feet) that counts as a vault; lower ones are auto-stepped or jumped.")]
    [SerializeField] private float minVaultHeight = 0.45f;
    [Tooltip("Deepest obstacle (m along the movement) that can be vaulted in one move.")]
    [SerializeField] private float maxVaultDepth = 1.5f;
    [Tooltip("How far ahead (m from the feet) an obstacle is detected when Space is pressed.")]
    [SerializeField] private float vaultReach = 1.1f;
    [Tooltip("Closest landing (m behind the obstacle's back face) accepted when the clip's natural landing is blocked.")]
    [SerializeField] private float vaultLandOffset = 0.6f;
    [Tooltip("Layers that count as ground for the landing point.")]
    [SerializeField] private LayerMask landingLayer;

    [Header("Ledge Check")]
    [Tooltip("How far ahead (m from the body center) a wall is detected for a grab from the ground.")]
    [SerializeField] private float ledgeReachGround = 1.0f;
    [Tooltip("How far ahead (m from the body center) a wall is detected for a grab in the air.")]
    [SerializeField] private float ledgeReachAir = 0.75f;
    [Tooltip("Inset (m) from the edge where the feet stand after climbing.")]
    [SerializeField] private float standInset = 0.45f;

    [Header("Pre-filter")]
    [Tooltip("Sphere radius used as a cheap pre-filter before casting any rays.")]
    [SerializeField] private float proximityCheckRadius = 1.4f;

    // Standing body used for free-space checks (radius slightly below the real collider).
    private const float BodyRadius = 0.3f;
    private const float BodyHeight = 1.8f;

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
        Vector3 origin = centerPoint != null ? centerPoint.position : transform.position;
        int count = Physics.OverlapSphereNonAlloc(origin, proximityCheckRadius, _proximityBuffer, obstacleLayer, QueryTriggerInteraction.Ignore);
        return count > 0;
    }

    /// <summary>True when a standing body fits with its feet at <paramref name="feet"/>.</summary>
    private bool HasStandingRoom(Vector3 feet)
    {
        Vector3 bottom = feet + Vector3.up * (BodyRadius + 0.08f);
        Vector3 top    = feet + Vector3.up * (BodyHeight - BodyRadius);
        return !Physics.CheckCapsule(bottom, top, BodyRadius, landingLayer, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// Looks for a vaultable obstacle in <paramref name="direction"/> (GDD §5.4): a front face within
    /// reach at knee height, a top between minVaultHeight and vaultHeightCheck above the feet, a depth
    /// up to maxVaultDepth and free ground behind it. The vault direction is perpendicular to the face
    /// (the body lines up with the obstacle), the hand point lies on the top surface on the left-hand
    /// side, and the landing is as far as the clip naturally lands, or closer if that spot is blocked.
    /// Allocation-free.
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
        Vector3 faceNormal = new Vector3(front.normal.x, 0f, front.normal.z);
        if (faceNormal.sqrMagnitude < 0.25f || Vector3.Dot(-faceNormal.normalized, direction) < 0.6f) return false;
        Vector3 dir = -faceNormal.normalized; // vault perpendicular to the face

        // 2. Top: cast down just past the front face, from above the highest vaultable height
        Vector3 topOrigin = front.point + dir * 0.05f;
        topOrigin.y = feetY + vaultHeightCheck + 0.3f;
        if (!Physics.Raycast(topOrigin, Vector3.down, out RaycastHit top, vaultHeightCheck + 0.3f, obstacleLayer, QueryTriggerInteraction.Ignore))
            return false;
        float height = top.point.y - feetY;
        if (top.distance < 0.01f || top.normal.y < 0.7f || height < minVaultHeight || height > vaultHeightCheck) return false;

        // 3. Depth: cast back toward the player from beyond the deepest vaultable obstacle.
        //    If the origin is still inside the obstacle the ray hits nothing → too deep.
        Vector3 backOrigin = front.point + dir * (maxVaultDepth + 0.05f);
        backOrigin.y = top.point.y - 0.1f;
        if (!Physics.Raycast(backOrigin, -dir, out RaycastHit back, maxVaultDepth + 0.05f, obstacleLayer, QueryTriggerInteraction.Ignore))
            return false;

        // 4. Landing: where the clip lands at run speed, or closer if that spot is not free ground
        bool landed = false;
        RaycastHit land = default;
        for (float d = ParkourTimings.VaultClipLandDistance; d >= vaultLandOffset - 0.001f; d -= 0.5f)
        {
            Vector3 landOrigin = back.point + dir * Mathf.Max(d, vaultLandOffset);
            landOrigin.y = top.point.y + 0.5f;
            if (!Physics.Raycast(landOrigin, Vector3.down, out land, top.point.y - feetY + 2.5f, landingLayer, QueryTriggerInteraction.Ignore))
                continue;
            if (land.point.y > top.point.y - 0.2f || land.normal.y < 0.7f) continue; // on top of something tall: not a vault
            if (!HasStandingRoom(land.point)) continue;
            landed = true;
            break;
        }
        if (!landed) return false;

        // Left hand on the top surface, just past the front edge (the clip plants it left of the body line)
        Vector3 left = Vector3.Cross(dir, Vector3.up);
        Vector3 edgePoint = new Vector3(front.point.x, top.point.y, front.point.z);
        Vector3 hand = edgePoint + dir * 0.12f + left * 0.3f;
        if (!Physics.Raycast(hand + Vector3.up * 0.3f, Vector3.down, out RaycastHit handTop, 0.45f, obstacleLayer, QueryTriggerInteraction.Ignore))
            hand = edgePoint + dir * 0.12f; // narrow obstacle: plant it on the body line
        else
            hand.y = handTop.point.y;

        info.Direction    = dir;
        info.LandingPoint = land.point;
        info.TopY         = top.point.y;
        info.FrontPoint   = edgePoint;
        info.Depth        = Vector3.Dot(back.point - front.point, dir);
        info.HandPoint    = hand;
        Vector3 toHand = hand - transform.position;
        toHand.y = 0f;
        info.HandDistance = toHand.magnitude;
        return true;
    }

    /// <summary>
    /// Looks for a grabbable ledge in <paramref name="direction"/>: a wall face roughly facing the
    /// player, a flat top surface between <paramref name="minRise"/> and <paramref name="maxRise"/>
    /// above the feet and room to stand on top (so the climb can finish). The edge is measured on the
    /// face plane at the top's height, so the hands can be placed exactly on it.
    /// </summary>
    public bool TryFindLedge(Vector3 direction, float feetY, float minRise, float maxRise, bool fromGround, out LedgeInfo ledge)
    {
        ledge = default;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || !HasNearbyGeometry()) return false;
        direction.Normalize();

        float reach = fromGround ? ledgeReachGround : ledgeReachAir;
        Vector3 basePos = new Vector3(transform.position.x, feetY, transform.position.z);

        // 1. Wall face: chest ray first, head ray for walls that start above the chest
        RaycastHit face;
        if (!Physics.Raycast(basePos + Vector3.up * 1.2f, direction, out face, reach, obstacleLayer, QueryTriggerInteraction.Ignore) &&
            !Physics.Raycast(basePos + Vector3.up * 1.75f, direction, out face, reach, obstacleLayer, QueryTriggerInteraction.Ignore))
            return false;
        Vector3 normal = new Vector3(face.normal.x, 0f, face.normal.z);
        if (normal.sqrMagnitude < 0.25f) return false;
        normal.Normalize();
        if (Vector3.Dot(-normal, direction) < 0.5f) return false;

        // 2. Top surface just inside the face
        Vector3 topOrigin = face.point - normal * 0.1f;
        topOrigin.y = feetY + maxRise + 0.3f;
        if (!Physics.Raycast(topOrigin, Vector3.down, out RaycastHit top, maxRise - minRise + 0.6f, obstacleLayer, QueryTriggerInteraction.Ignore))
            return false;
        float rise = top.point.y - feetY;
        if (top.distance < 0.01f || top.normal.y < 0.7f || rise < minRise || rise > maxRise) return false;

        // 3. The face right below the edge (exact plane where the fingers wrap)
        Vector3 edgeProbe = new Vector3(face.point.x, top.point.y - 0.1f, face.point.z) + normal * 0.6f;
        if (!Physics.Raycast(edgeProbe, -normal, out RaycastHit edgeFace, 1.0f, obstacleLayer, QueryTriggerInteraction.Ignore))
            return false;
        Vector3 edge = new Vector3(edgeFace.point.x, top.point.y, edgeFace.point.z);

        // 4. Room to stand on top and nothing over the edge where the hands go
        Vector3 stand = edge - normal * standInset;
        if (!Physics.Raycast(stand + Vector3.up * 0.3f, Vector3.down, out RaycastHit standTop, 0.5f, obstacleLayer, QueryTriggerInteraction.Ignore))
            return false;
        stand.y = standTop.point.y;
        if (!HasStandingRoom(stand)) return false;

        ledge.Edge       = edge;
        ledge.Normal     = normal;
        ledge.TopY       = top.point.y;
        ledge.StandPoint = stand;
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 origin = centerPoint != null ? centerPoint.position : transform.position;
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(origin, origin + transform.forward * ledgeReachGround);
        Gizmos.color = new Color(0f, 0.5f, 1f, 0.15f);
        Gizmos.DrawWireSphere(origin, proximityCheckRadius);
        if (headPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(headPoint.position, headPoint.position + transform.forward * ledgeReachAir);
        }
    }
}
