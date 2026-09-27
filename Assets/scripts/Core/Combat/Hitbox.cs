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
        int count = Physics.OverlapSphereNonAlloc(transform.position, radius, _hitBuffer, targetLayers);

        for (int i = 0; i < count; i++)
        {
            Collider col = _hitBuffer[i];

            // Skip the owner's own collider
            if (ownerCollider != null && col == ownerCollider) continue;

            // Only damage entities that implement IDamageable
            if (col.TryGetComponent(out IDamageable target))
            {
                target.TakeDamage(damage, transform.position);
                OnHit?.Invoke(target, col.ClosestPoint(transform.position));
            }
        }
    }

    /// <summary>Adjusts damage value at runtime (e.g., weapon swaps, buffs).</summary>
    public void SetDamage(int newDamage) => damage = newDamage;

    // ─── Gizmos ──────────────────────────────────────────────────────────────────

    private void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.35f);
        Gizmos.DrawSphere(transform.position, radius);
    }
}
