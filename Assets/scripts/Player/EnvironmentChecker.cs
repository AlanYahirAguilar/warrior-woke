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

    [Header("Landing")]
    [Tooltip("Layers that count as ground for the landing point.")]
    [SerializeField] private LayerMask landingLayer;

    // Detection limits come from the Parkour Obstacle Standard (ParkourStandard), not from
    // per-instance fields, so the obstacles, their validation and the detection never disagree.

    // Reusable buffer for OverlapSphere — avoids GC allocations every check
    private readonly Collider[] _proximityBuffer = new Collider[4];

    private void Awake()
    {
        if (landingLayer.value == 0)
            landingLayer = LayerMask.GetMask("Ground", "Obstacle");
    }

    /// <summary>Returns true when at least one obstacle collider sits within the proximity sphere.</summary>
    private bool HasNearbyGeometry(float radius = ParkourStandard.ProximityRadius)
    {
        Vector3 origin = centerPoint != null ? centerPoint.position : transform.position;
        int count = Physics.OverlapSphereNonAlloc(origin, radius, _proximityBuffer, obstacleLayer, QueryTriggerInteraction.Ignore);
        return count > 0;
    }

    /// <summary>True when a standing body fits with its feet at <paramref name="feet"/>.</summary>
    private bool HasStandingRoom(Vector3 feet)
    {
        Vector3 bottom = feet + Vector3.up * (ParkourStandard.StandCheckRadius + 0.08f);
        Vector3 top    = feet + Vector3.up * (ParkourStandard.StandCheckHeight - ParkourStandard.StandCheckRadius);
        return !Physics.CheckCapsule(bottom, top, ParkourStandard.StandCheckRadius, landingLayer, QueryTriggerInteraction.Ignore);
    }

    /// <summary>
    /// Looks for a vaultable obstacle in <paramref name="direction"/> (GDD §5.4): a front face within
    /// reach at knee height, a top between ParkourStandard.VaultMinHeight and VaultMaxHeight above the
    /// feet, a depth up to VaultMaxDepth and free ground behind it. The vault direction is perpendicular to the face
    /// (the body lines up with the obstacle), the hand point lies on the top surface on the left-hand
    /// side, and the landing is as far as the clip naturally lands, or closer if that spot is blocked.
    /// Allocation-free.
    /// </summary>
    /// <summary>Measurement tolerance (m) of the obstacle heights against the standard's limits.</summary>
    private const float HeightTolerance = 0.01f;

    public bool TryFindVault(Vector3 direction, float feetY, out VaultInfo info) =>
        TryFindVault(direction, feetY, ParkourStandard.VaultReach, out info);

    /// <summary>Same as TryFindVault, looking <paramref name="reach"/> m ahead (a fast run spots the obstacle earlier).</summary>
    public bool TryFindVault(Vector3 direction, float feetY, float reach, out VaultInfo info)
    {
        info = default;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || !HasNearbyGeometry(Mathf.Max(ParkourStandard.ProximityRadius, reach + 0.3f))) return false;
        direction.Normalize();

        Vector3 feet = new Vector3(transform.position.x, feetY, transform.position.z);

        // 1. Front face at knee height, roughly facing the player
        Vector3 knee = feet + Vector3.up * ParkourStandard.VaultKneeRay;
        if (!Physics.Raycast(knee, direction, out RaycastHit front, reach, obstacleLayer, QueryTriggerInteraction.Ignore))
            return false;
        Vector3 faceNormal = new Vector3(front.normal.x, 0f, front.normal.z);
        if (faceNormal.sqrMagnitude < 0.25f || Vector3.Dot(-faceNormal.normalized, direction) < 0.6f) return false;
        Vector3 dir = -faceNormal.normalized; // vault perpendicular to the face

        // 2. Top: cast down just past the front face, from above the highest vaultable height
        Vector3 topOrigin = front.point + dir * 0.05f;
        topOrigin.y = feetY + ParkourStandard.VaultMaxHeight + 0.3f;
        if (!Physics.Raycast(topOrigin, Vector3.down, out RaycastHit top, ParkourStandard.VaultMaxHeight + 0.3f, obstacleLayer, QueryTriggerInteraction.Ignore))
            return false;
        float height = top.point.y - feetY;
        // (with a tolerance: the standard high vault is exactly VaultMaxHeight, and the feet of a
        // CharacterController resting on the ground read a fraction of a millimetre below it)
        if (top.distance < 0.01f || top.normal.y < 0.7f ||
            height < ParkourStandard.VaultMinHeight - HeightTolerance || height > ParkourStandard.VaultMaxHeight + HeightTolerance) return false;

        // 3. Depth: cast back toward the player from beyond the deepest vaultable obstacle.
        //    If the origin is still inside the obstacle the ray hits nothing → too deep.
        Vector3 backOrigin = front.point + dir * (ParkourStandard.VaultMaxDepth + 0.05f);
        backOrigin.y = top.point.y - 0.1f;
        if (!Physics.Raycast(backOrigin, -dir, out RaycastHit back, ParkourStandard.VaultMaxDepth + 0.05f, obstacleLayer, QueryTriggerInteraction.Ignore))
            return false;

        // 4. Landing: where the clip lands at run speed, or closer if that spot is not free ground
        bool landed = false;
        RaycastHit land = default;
        for (float d = ParkourTimings.VaultClipLandDistance; d >= ParkourStandard.VaultMinLanding - 0.001f; d -= 0.5f)
        {
            Vector3 landOrigin = back.point + dir * Mathf.Max(d, ParkourStandard.VaultMinLanding);
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
        Vector3 hand = edgePoint + dir * ParkourTimings.VaultHandInset + left * ParkourTimings.VaultHandLateral;
        if (!Physics.Raycast(hand + Vector3.up * 0.3f, Vector3.down, out RaycastHit handTop, 0.45f, obstacleLayer, QueryTriggerInteraction.Ignore))
            hand = edgePoint + dir * ParkourTimings.VaultHandInset; // narrow obstacle: plant it on the body line
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
        float reach = fromGround ? ParkourStandard.LedgeReachGround : ParkourStandard.LedgeReachAir;
        return TryFindTop(direction, feetY, ParkourStandard.LedgeChestRay, ParkourStandard.LedgeHeadRay, reach,
                          minRise, maxRise, ParkourStandard.LedgeStandInset, out ledge);
    }

    /// <summary>
    /// Looks for a block to climb onto with a mantle (P28): a face within reach at knee/waist height,
    /// a flat top 0.8–1.5 m above the feet and room to stand where the mantle ends (~1 m past the edge).
    /// </summary>
    public bool TryFindMantle(Vector3 direction, float feetY, float reach, out LedgeInfo top)
    {
        return TryFindTop(direction, feetY, ParkourStandard.MantleLowRay, ParkourStandard.MantleHighRay, reach,
                          ParkourStandard.MantleMinRise, ParkourStandard.MantleMaxRise, ParkourStandard.MantleStandInset, out top);
    }

    /// <summary>
    /// Looks for an edge to lower onto and hang from (P28): standing on a top, walking toward its edge
    /// in <paramref name="direction"/>, the ground ends within reach, below it the drop is at least
    /// ParkourStandard.DropMinHeight, the block has a face to brace the feet on, and a hanging body
    /// fits in front of it. The edge is measured like a ledge (the hands go on it), with the normal
    /// pointing away from the block.
    /// </summary>
    public bool TryFindDrop(Vector3 direction, float feetY, out LedgeInfo ledge)
    {
        ledge = default;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return false;
        direction.Normalize();
        Vector3 basePos = new Vector3(transform.position.x, feetY, transform.position.z);

        // 1. Where the ground ends ahead (within reach)
        float d = 0.2f;
        for (; d <= ParkourStandard.DropReach; d += 0.1f)
        {
            Vector3 probe = basePos + direction * d + Vector3.up * 0.3f;
            if (!Physics.Raycast(probe, Vector3.down, ParkourStandard.DropMinHeight + 0.3f, landingLayer, QueryTriggerInteraction.Ignore))
                break;
        }
        if (d > ParkourStandard.DropReach) return false;

        // 2. The block's face below the edge, seen from the open side
        Vector3 faceProbe = basePos + direction * (d + 0.4f) + Vector3.down * 0.15f;
        if (!Physics.Raycast(faceProbe, -direction, out RaycastHit face, 0.8f, obstacleLayer, QueryTriggerInteraction.Ignore))
            return false;
        Vector3 normal = new Vector3(face.normal.x, 0f, face.normal.z);
        if (normal.sqrMagnitude < 0.25f || Vector3.Dot(normal.normalized, direction) < 0.5f) return false;
        normal.Normalize();

        // 3. Room for the hanging body in front of the face
        Vector3 edge = new Vector3(face.point.x, feetY, face.point.z);
        Vector3 hangTop = edge + normal * (StandCheckRadiusHang + 0.05f) + Vector3.down * 0.4f;
        Vector3 hangBottom = hangTop + Vector3.down * 1.4f;
        if (Physics.CheckCapsule(hangBottom, hangTop, StandCheckRadiusHang, landingLayer, QueryTriggerInteraction.Ignore)) return false;

        ledge.Edge       = edge;
        ledge.Normal     = normal;
        ledge.TopY       = feetY;
        ledge.StandPoint = edge - normal * ParkourStandard.LedgeStandInset;
        return true;
    }

    /// <summary>Radius of the hanging body checked in front of a face (m).</summary>
    private const float StandCheckRadiusHang = 0.25f;

    /// <summary>
    /// Shared measurement of a top to reach: the face (two horizontal rays, the lower first), its
    /// normal, a flat top within the rise range, the exact edge on the face plane and room to stand
    /// at <paramref name="standInset"/> past the edge. Allocation-free.
    /// </summary>
    private bool TryFindTop(Vector3 direction, float feetY, float lowRay, float highRay, float reach,
                            float minRise, float maxRise, float standInset, out LedgeInfo ledge)
    {
        ledge = default;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || !HasNearbyGeometry(Mathf.Max(ParkourStandard.ProximityRadius, reach + 0.3f))) return false;
        direction.Normalize();

        Vector3 basePos = new Vector3(transform.position.x, feetY, transform.position.z);

        // 1. Face: the lower ray first, the higher one for faces that start above it
        RaycastHit face;
        if (!Physics.Raycast(basePos + Vector3.up * lowRay, direction, out face, reach, obstacleLayer, QueryTriggerInteraction.Ignore) &&
            !Physics.Raycast(basePos + Vector3.up * highRay, direction, out face, reach, obstacleLayer, QueryTriggerInteraction.Ignore))
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
        if (top.distance < 0.01f || top.normal.y < 0.7f || rise < minRise - HeightTolerance || rise > maxRise + HeightTolerance) return false;

        // 3. The face right below the edge (exact plane where the hands go)
        Vector3 edgeProbe = new Vector3(face.point.x, top.point.y - 0.1f, face.point.z) + normal * 0.6f;
        if (!Physics.Raycast(edgeProbe, -normal, out RaycastHit edgeFace, 1.0f, obstacleLayer, QueryTriggerInteraction.Ignore))
            return false;
        Vector3 edge = new Vector3(edgeFace.point.x, top.point.y, edgeFace.point.z);

        // 4. Room to stand on top where the action ends
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
        Gizmos.DrawLine(origin, origin + transform.forward * ParkourStandard.LedgeReachGround);
        Gizmos.color = new Color(0f, 0.5f, 1f, 0.15f);
        Gizmos.DrawWireSphere(origin, ParkourStandard.ProximityRadius);
        if (headPoint != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(headPoint.position, headPoint.position + transform.forward * ParkourStandard.LedgeReachAir);
        }
    }
}
