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

    private Collider _capsuleCollider; // a CapsuleCollider or a CharacterController
    private float _radius;
    private bool _isGrounded;

    public bool IsGrounded => _isGrounded;

    private void Awake()
    {
        if (TryGetComponent(out CharacterController controller)) { _capsuleCollider = controller; _radius = controller.radius; }
        else if (TryGetComponent(out CapsuleCollider capsule))   { _capsuleCollider = capsule;    _radius = capsule.radius; }
    }

    /// <summary>
    /// Evaluates if the actor is touching the ground.
    /// </summary>
    public bool CheckGrounded()
    {
        if (_capsuleCollider != null)
        {
            // Sphere slightly narrower than the capsule, starting just above the feet (computed from
            // the shape, not the bounds: a CharacterController is off while parkour moves the body)
            float radius = _radius * 0.9f;
            Vector3 feet = Feet();
            Vector3 origin = new Vector3(feet.x, feet.y + radius + 0.05f, feet.z);
            _isGrounded = Physics.SphereCast(origin, radius, Vector3.down, out _, 0.05f + extraDistance, groundLayer, QueryTriggerInteraction.Ignore);
            return _isGrounded;
        }

        _isGrounded = Physics.Raycast(GetRayOrigin(), Vector3.down, GetRayLength(), groundLayer, QueryTriggerInteraction.Ignore);
        return _isGrounded;
    }

    /// <summary>Bottom of the capsule in world space.</summary>
    private Vector3 Feet()
    {
        Vector3 center = Vector3.zero;
        float height = 0f;
        if (_capsuleCollider is CharacterController c) { center = c.center; height = c.height; }
        else if (_capsuleCollider is CapsuleCollider k) { center = k.center; height = k.height; }
        Vector3 world = transform.TransformPoint(center);
        return new Vector3(world.x, world.y - height * 0.5f * transform.lossyScale.y, world.z);
    }

    private Vector3 GetRayOrigin()
    {
        // Start the ray slightly above the bottom-most point of the collider
        if (_capsuleCollider != null) return Feet() + Vector3.up * 0.1f;
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
