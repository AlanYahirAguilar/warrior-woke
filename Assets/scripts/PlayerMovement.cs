using UnityEngine;

/// <summary>
/// Handles physics-based movement and velocity management on the Rigidbody.
/// Follows Single Responsibility Principle (SRP) by focusing strictly on movement dynamics.
/// Optimized according to project best practices: cached references, zero GC allocations, and FixedUpdate execution.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Dynamics")]
    [Tooltip("Force applied in world X and Z coordinates.")]
    [SerializeField] private float moveForce = 25f;

    [Tooltip("Maximum allowed horizontal velocity on the plane.")]
    [SerializeField] private float maxSpeed = 12f;

    [Tooltip("Rate at which the sphere decelerates horizontally when no movement input is provided.")]
    [SerializeField] private float decelerationRate = 6f;

    [Tooltip("Type of force applied to the Rigidbody.")]
    [SerializeField] private ForceMode forceMode = ForceMode.Force;

    [Header("Rolling Animation")]
    [Tooltip("Enables never-slip procedural rolling rotation synchronized with displacement.")]
    [SerializeField] private bool enableRollingRotation = true;

    private Rigidbody _rigidbody;
    private float _sphereRadius;

    public float MoveForce
    {
        get => moveForce;
        set => moveForce = Mathf.Max(0f, value);
    }

    public float MaxSpeed
    {
        get => maxSpeed;
        set => maxSpeed = Mathf.Max(0f, value);
    }

    public float DecelerationRate
    {
        get => decelerationRate;
        set => decelerationRate = Mathf.Max(0f, value);
    }

    public bool EnableRollingRotation
    {
        get => enableRollingRotation;
        set => enableRollingRotation = value;
    }

    private void Awake()
    {
        // Cache Rigidbody component once during initialization to avoid lookups in the loop
        _rigidbody = GetComponent<Rigidbody>();

        // Calculate effective world radius of the sphere for accurate rolling speed (v = omega * r)
        SphereCollider sphereCollider = GetComponent<SphereCollider>();
        if (sphereCollider != null)
        {
            Vector3 scale = transform.lossyScale;
            float maxScale = Mathf.Max(scale.x, Mathf.Max(scale.y, scale.z));
            _sphereRadius = sphereCollider.radius * maxScale;
        }
        else
        {
            _sphereRadius = 0.75f;
        }
    }

    /// <summary>
    /// Processes physical movement based on a given 2D input vector.
    /// Should be called during FixedUpdate to maintain timestep synchronization.
    /// </summary>
    /// <param name="inputDirection">2D direction vector (X = left/right, Y = forward/back).</param>
    public void ProcessMovement(Vector2 inputDirection)
    {
        if (_rigidbody == null) return;

        Vector3 currentVelocity = _rigidbody.linearVelocity;
        Vector3 horizontalVelocity = new Vector3(currentVelocity.x, 0f, currentVelocity.z);

        if (inputDirection.sqrMagnitude > 0.001f)
        {
            // Normalize direction if magnitude exceeds 1 to prevent diagonal speed boosting
            Vector2 direction = inputDirection.sqrMagnitude > 1f ? inputDirection.normalized : inputDirection;
            Vector3 force = new Vector3(direction.x, 0f, direction.y) * moveForce;

            _rigidbody.AddForce(force, forceMode);

            // Clamp maximum horizontal speed while preserving vertical (gravity/bounce) velocity
            if (horizontalVelocity.sqrMagnitude > maxSpeed * maxSpeed)
            {
                horizontalVelocity = horizontalVelocity.normalized * maxSpeed;
                _rigidbody.linearVelocity = new Vector3(horizontalVelocity.x, currentVelocity.y, horizontalVelocity.z);
            }
        }
        else
        {
            // Apply controlled horizontal deceleration when no movement keys are pressed
            if (horizontalVelocity.sqrMagnitude > 0.0001f)
            {
                Vector3 dampedHorizontalVelocity = Vector3.MoveTowards(
                    horizontalVelocity,
                    Vector3.zero,
                    decelerationRate * Time.fixedDeltaTime
                );

                _rigidbody.linearVelocity = new Vector3(
                    dampedHorizontalVelocity.x,
                    currentVelocity.y,
                    dampedHorizontalVelocity.z
                );
            }
        }

        // Apply synchronized angular velocity for realistic, slip-free ball rolling
        if (enableRollingRotation)
        {
            ApplyRollingRotation();
        }
    }

    /// <summary>
    /// Synchronizes angular velocity with horizontal linear displacement (v = omega * r).
    /// Ensures ball texture rotates precisely without sliding or slipping across the surface.
    /// </summary>
    private void ApplyRollingRotation()
    {
        Vector3 currentVelocity = _rigidbody.linearVelocity;
        Vector3 horizontalVelocity = new Vector3(currentVelocity.x, 0f, currentVelocity.z);
        float speed = horizontalVelocity.magnitude;

        if (speed > 0.05f && _sphereRadius > 0.001f)
        {
            // Rolling axis is perpendicular to movement on the horizontal plane
            Vector3 rollAxis = Vector3.Cross(Vector3.up, horizontalVelocity / speed);
            float angularSpeed = speed / _sphereRadius;

            _rigidbody.angularVelocity = rollAxis * angularSpeed;
        }
        else
        {
            // Stop rotational drift when the ball comes to a halt
            _rigidbody.angularVelocity = Vector3.zero;
        }
    }
}
