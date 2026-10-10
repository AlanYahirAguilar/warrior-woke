using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Detects IDamageable entities on its target layers and applies damage to them. Two queries:
///  - <see cref="Activate"/>: a pulse — every target inside a sphere in front of the owner (the enemy's attack).
///  - <see cref="Sweep"/>: the path of a striking limb this frame (the player's attacks, decision P37):
///    a capsule from where the fist or foot was to where it is, so a hit is real contact with the limb
///    the animation shows, once per target and attack.
/// <see cref="FindTarget"/> picks what an attack turns toward. A target's collider may be a child of
/// the object that takes the damage (the training dummy's swaying body).
///
/// Best practices applied:
///  - Physics queries with pre-allocated buffers (NonAlloc) — zero GC per query.
///  - Only active during attack windows; it is not a persistent trigger.
///  - Does NOT apply damage to the owner (self-hit prevention via OwnerCollider exclusion).
/// </summary>
public class Hitbox : MonoBehaviour
{
    // ─── Inspector Configuration ─────────────────────────────────────────────────
    [Header("Detection")]
    [SerializeField] private float radius          = 0.6f;
    [Tooltip("Local offset of the sphere center from this transform (e.g. forward reach in front of the torso).")]
    [SerializeField] private Vector3 localOffset   = Vector3.zero;
    [SerializeField] private LayerMask targetLayers;
    [SerializeField] private int    damage         = 10;
    [SerializeField] private Collider ownerCollider;
    [Tooltip("Surfaces a blow never goes through: nothing on them may stand between the owner and the point struck.")]
    [SerializeField] private LayerMask blockingLayers;

    [Header("Debug")]
    [SerializeField] private bool showGizmos = true;

    // ─── Pre-Allocated Buffer (zero GC on activation) ────────────────────────────
    private readonly Collider[] _hitBuffer = new Collider[10];

    // ─── Events ───────────────────────────────────────────────────────────────────
    /// <summary>Fires for every unique IDamageable hit. Args: damageable, hit position.</summary>
    public event System.Action<IDamageable, Vector3> OnHit;

    private void Awake()
    {
        if (blockingLayers.value == 0) blockingLayers = LayerMask.GetMask("Ground", "Obstacle");
    }

    /// <summary>
    /// Nothing solid between the owner's centre and <paramref name="point"/> (the last 15 cm are not
    /// checked: a fist sunk a little into a target must not be blocked by something inside it, like the
    /// training dummy's post).
    /// </summary>
    public bool ClearPath(Vector3 point)
    {
        Vector3 from = transform.position;
        Vector3 d = point - from;
        float length = d.magnitude - 0.15f;
        return length <= 0.01f || !Physics.Raycast(from, d.normalized, length, blockingLayers, QueryTriggerInteraction.Ignore);
    }

    // ─── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Performs the overlap query and applies damage to all valid targets found.
    /// Call this from an Animation Event or directly from a state's Enter()/PhysicsUpdate().
    /// </summary>
    public void Activate()
    {
        Vector3 center = Center;
        int count = Physics.OverlapSphereNonAlloc(center, radius, _hitBuffer, targetLayers);

        for (int i = 0; i < count; i++)
        {
            Collider col = _hitBuffer[i];

            // Skip the owner's own collider
            if (ownerCollider != null && col == ownerCollider) continue;

            // Only damage entities that implement IDamageable
            if (col.TryGetComponent(out IDamageable target))
            {
                target.TakeDamage(damage, transform.position);
                OnHit?.Invoke(target, col.ClosestPoint(center));
            }
        }
    }

    /// <summary>
    /// The path of a striking limb this frame: a capsule of <paramref name="limbRadius"/> from
    /// <paramref name="from"/> to <paramref name="to"/>. Every living IDamageable it touches that is not
    /// yet in <paramref name="struck"/> takes <paramref name="amount"/> and is added to it. Returns how
    /// many it struck now.
    /// </summary>
    public int Sweep(Vector3 from, Vector3 to, float limbRadius, int amount, List<IDamageable> struck)
    {
        int count = Physics.OverlapCapsuleNonAlloc(from, to, limbRadius, _hitBuffer, targetLayers, QueryTriggerInteraction.Collide);
        int hits = 0;
        for (int i = 0; i < count; i++)
        {
            Collider col = _hitBuffer[i];
            if (ownerCollider != null && col == ownerCollider) continue;
            IDamageable target = col.GetComponentInParent<IDamageable>();
            if (target == null || target.IsDead || struck.Contains(target)) continue;
            Vector3 point = col.ClosestPoint(to);
            if (!ClearPath(point)) continue; // never through a wall
            struck.Add(target);
            target.TakeDamage(amount, transform.position);
            OnHit?.Invoke(target, point);
            hits++;
        }
        return hits;
    }

    /// <summary>
    /// The target an attack should turn toward: the living IDamageable within <paramref name="range"/>
    /// (m, horizontally from <paramref name="origin"/>) and <paramref name="maxAngle"/> (°) of
    /// <paramref name="direction"/> that is the best mix of near and in front. <paramref name="target"/>
    /// is the object that takes the damage (its position is the target's axis on the floor) and
    /// <paramref name="targetRadius"/> its horizontal radius.
    /// </summary>
    public bool FindTarget(Vector3 origin, Vector3 direction, float range, float maxAngle, out Transform target, out float targetRadius)
    {
        target = null;
        targetRadius = 0f;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return false;
        direction.Normalize();

        int count = Physics.OverlapSphereNonAlloc(origin, range + 1f, _hitBuffer, targetLayers, QueryTriggerInteraction.Collide);
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Collider col = _hitBuffer[i];
            if (ownerCollider != null && col == ownerCollider) continue;
            var damageable = col.GetComponentInParent<IDamageable>();
            if (damageable == null || damageable.IsDead || !(damageable is Component owner)) continue;

            Vector3 to = owner.transform.position - origin;
            to.y = 0f;
            float distance = to.magnitude;
            float r = Mathf.Min(col.bounds.extents.x, col.bounds.extents.z);
            if (distance - r > range) continue;
            float angle = distance > 0.01f ? Vector3.Angle(direction, to) : 0f;
            if (angle > maxAngle) continue;
            // Near and in front: an angle of maxAngle weighs like 1 m more distance
            float score = distance + angle / maxAngle;
            if (score >= best) continue;
            best = score;
            target = owner.transform;
            targetRadius = r;
        }
        return target != null;
    }

    /// <summary>Adjusts damage value at runtime (e.g., weapon swaps, buffs).</summary>
    public void SetDamage(int newDamage) => damage = newDamage;

    /// <summary>Overrides the detection radius at runtime (e.g., weapon-specific reach).</summary>
    public void SetRadius(float newRadius) => radius = Mathf.Max(0.01f, newRadius);

    /// <summary>Current detection radius.</summary>
    public float Radius => radius;

    /// <summary>World-space center of the detection sphere.</summary>
    public Vector3 Center => transform.TransformPoint(localOffset);

    // ─── Gizmos ──────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.35f);
        Gizmos.DrawSphere(Center, radius);
    }
}
