using UnityEngine;

/// <summary>
/// Component solely responsible for detecting ground contact via downward raycasts.
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
        Vector3 origin = GetRayOrigin();
        float length = GetRayLength();

        _isGrounded = Physics.Raycast(origin, Vector3.down, length, groundLayer);
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
