using UnityEngine;

/// <summary>
/// Component solely responsible for detecting ground contact with a short downward sphere cast
/// (the footprint of the feet, not a single ray under the center: the body stays grounded on the
/// edge of a step instead of flickering into the air).
/// Implements IGroundChecker and conforms to the Single Responsibility Principle (SRP).
/// Reusable across player, enemies, and other physics-driven actors.
/// </summary>
public class GroundChecker : MonoBehaviour, IGroundChecker
{
    [Header("Detection Settings")]
    [Tooltip("Layers recognized as solid ground.")]
    [SerializeField] private LayerMask groundLayer = ~0;

    [Tooltip("Extra raycast distance below collider bottom.")]
    [SerializeField] private float extraDistance = 0.15f;

    [Header("Gizmos Debugging")]
    [SerializeField] private bool showGizmos = true;

    private CapsuleCollider _capsuleCollider;
    private bool _isGrounded;

    public bool IsGrounded => _isGrounded;

    private void Awake()
    {
        _capsuleCollider = GetComponent<CapsuleCollider>();
    }

    /// <summary>
    /// Evaluates if the actor is touching the ground.
    /// </summary>
    public bool CheckGrounded()
    {
        if (_capsuleCollider != null)
        {
            // Sphere slightly narrower than the capsule, starting just above the feet
            float radius = _capsuleCollider.radius * 0.9f;
            Bounds b = _capsuleCollider.bounds;
            Vector3 origin = new Vector3(b.center.x, b.min.y + radius + 0.05f, b.center.z);
            _isGrounded = Physics.SphereCast(origin, radius, Vector3.down, out _, 0.05f + extraDistance, groundLayer, QueryTriggerInteraction.Ignore);
            return _isGrounded;
        }

        _isGrounded = Physics.Raycast(GetRayOrigin(), Vector3.down, GetRayLength(), groundLayer, QueryTriggerInteraction.Ignore);
        return _isGrounded;
    }

    private Vector3 GetRayOrigin()
    {
        if (_capsuleCollider != null)
        {
            // Start the ray slightly above the bottom-most point of the collider (bounds.min.y)
            return new Vector3(_capsuleCollider.bounds.center.x, _capsuleCollider.bounds.min.y + 0.1f, _capsuleCollider.bounds.center.z);
        }
        return transform.position + Vector3.up * 0.1f;
    }

    private float GetRayLength()
    {
        // Ray length covers the 0.1f offset + the desired extra distance
        return 0.1f + extraDistance;
    }

    private void OnDrawGizmosSelected()
    {
        if (!showGizmos) return;

        Gizmos.color = _isGrounded ? Color.green : Color.red;
        Vector3 origin = GetRayOrigin();
        float length = GetRayLength();
        Gizmos.DrawLine(origin, origin + Vector3.down * length);
    }
}
