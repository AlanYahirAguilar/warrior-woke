using UnityEngine;

/// <summary>
/// Controls smooth camera tracking of the player ball across the horizontal plane (X and Z axes),
/// while maintaining a fixed vertical height (Y axis) and orthographic projection framing.
/// Adheres to Single Responsibility Principle (SRP) and avoids GC allocations in the update loop.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Target Tracking")]
    [Tooltip("Target transform to track. If unassigned, automatically detects the Player.")]
    [SerializeField] private Transform target;

    [Tooltip("Automatically finds the Player GameObject on Awake if target is null.")]
    [SerializeField] private bool autoDetectTarget = true;

    [Header("Offset Configuration")]
    [Tooltip("Offset distance relative to the target.")]
    [SerializeField] private Vector3 offset;

    [Tooltip("Automatically calculates offset from the initial scene position.")]
    [SerializeField] private bool autoCalculateOffset = true;

    [Header("Smoothing Settings")]
    [Tooltip("Approximate time it takes to reach the target position (lower is faster/snappier).")]
    [SerializeField] private float smoothTime = 0.2f;

    [Header("Axis Constraints")]
    [Tooltip("Locks the vertical Y position to preserve top-down camera height.")]
    [SerializeField] private bool lockY = true;

    private Vector3 _currentVelocity;
    private float _fixedY;

    private void Awake()
    {
        InitializeTarget();
        InitializeOffset();
    }

    private void LateUpdate()
    {
        if (target == null) return;

        // Calculate target destination combining player position and horizontal offset
        Vector3 targetPosition = target.position + offset;

        if (lockY)
        {
            targetPosition.y = _fixedY;
        }

        // Smoothly damp towards destination without memory allocations
        transform.position = Vector3.SmoothDamp(
            transform.position,
            targetPosition,
            ref _currentVelocity,
            smoothTime
        );
    }

    /// <summary>
    /// Discovers the player target if not manually assigned in the Inspector.
    /// </summary>
    private void InitializeTarget()
    {
        if (target != null || !autoDetectTarget) return;

        Player player = FindAnyObjectByType<Player>();
        if (player != null)
        {
            target = player.transform;
        }
        else
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                target = playerObject.transform;
            }
        }
    }

    /// <summary>
    /// Configures the initial offset between camera and player.
    /// </summary>
    private void InitializeOffset()
    {
        _fixedY = transform.position.y;

        if (autoCalculateOffset && target != null)
        {
            offset = transform.position - target.position;
        }
        else if (offset == Vector3.zero)
        {
            // Default pinball camera offset based on scene setup (Y: 13.6, Z: -4.1)
            offset = new Vector3(0f, 13.6f, -4.1f);
        }
    }
}
