using UnityEngine;

/// <summary>
/// Trigger-based hitbox that detects IDamageable entities within a sphere on activation.
///
/// Best practices applied:
///  - Uses Physics.OverlapSphereNonAlloc with a pre-allocated buffer — zero GC per activation.
///  - The hitbox is disabled by default; only active during attack animation windows.
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

    [Header("Debug")]
    [SerializeField] private bool showGizmos = true;

    // ─── Pre-Allocated Buffer (zero GC on activation) ────────────────────────────
    private readonly Collider[] _hitBuffer = new Collider[10];

    // ─── Events ───────────────────────────────────────────────────────────────────
    /// <summary>Fires for every unique IDamageable hit. Args: damageable, hit position.</summary>
    public event System.Action<IDamageable, Vector3> OnHit;

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
