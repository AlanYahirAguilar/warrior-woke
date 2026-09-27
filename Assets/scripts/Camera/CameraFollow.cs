using UnityEngine;

/// <summary>
/// Third-person "over-the-shoulder" camera (Sleeping Dogs / GTA style).
/// Sits behind and above one shoulder of the target, looking slightly past it, and
/// smoothly trails position and rotation as the target moves and turns.
/// Player movement (see PlayerMovement) is computed relative to this camera's flattened
/// forward/right axes, so turning the character re-orients where "forward" means next tick.
/// Includes a simple collision pull-in so the camera never clips through geometry.
/// Generates zero GC allocations per frame in LateUpdate.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Target Tracking")]
    [Tooltip("Target transform to follow. If autoDetectTarget is disabled, assign manually here.")]
    [SerializeField] private Transform target;

    [Tooltip("Automatically detects the Player via events or scene search when true. If false, uses the manual Target assigned above.")]
    [SerializeField] private bool autoDetectTarget = true;

    [Header("Shoulder Framing")]
    [Tooltip("Height above the target's pivot used as the shoulder/look point.")]
    [SerializeField] private float shoulderHeight = 1.6f;

    [Tooltip("Sideways offset from the target (positive = right shoulder, negative = left shoulder).")]
    [SerializeField] private float shoulderSide = 0.5f;

    [Tooltip("Distance behind the shoulder point the camera sits.")]
    [SerializeField] private float distance = 3.5f;

    [Tooltip("How far past the shoulder point the camera looks, to frame the direction of travel.")]
    [SerializeField] private float lookAheadDistance = 2f;

    [Header("Smoothing")]
    [Tooltip("Position smoothing time (lower is snappier).")]
    [SerializeField] private float positionSmoothTime = 0.12f;

    [Tooltip("Rotation smoothing speed (higher snaps faster to the target look direction).")]
    [SerializeField] private float rotationSmoothSpeed = 10f;

    [Header("Collision")]
    [Tooltip("Layers considered solid for camera collision avoidance.")]
    [SerializeField] private LayerMask collisionMask = ~0;

    [Tooltip("Radius of the sphere cast used to keep the camera out of geometry.")]
    [SerializeField] private float collisionRadius = 0.25f;

    [Tooltip("Extra pull-in distance from a collision hit point, to avoid clipping into the surface.")]
    [SerializeField] private float collisionBuffer = 0.15f;

    private Vector3 _positionVelocity;

    private void OnEnable()
    {
        Player.OnPlayerSpawned += HandlePlayerSpawned;
    }

    private void OnDisable()
    {
        Player.OnPlayerSpawned -= HandlePlayerSpawned;
    }

    private void Awake()
    {
        InitializeTarget();
    }

    private void Start()
    {
        // Backup in case the Player spawned between Awake and Start
        if (target == null && autoDetectTarget)
        {
            InitializeTarget();
        }

        if (target != null)
        {
            SnapToTarget();
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 shoulderPoint = GetShoulderPoint();
        Vector3 desiredPosition = shoulderPoint - target.forward * distance;

        float allowedDistance = distance;
        if (Physics.SphereCast(shoulderPoint, collisionRadius, (desiredPosition - shoulderPoint).normalized,
                out RaycastHit hit, distance, collisionMask, QueryTriggerInteraction.Ignore))
        {
            allowedDistance = Mathf.Max(hit.distance - collisionBuffer, 0.1f);
            desiredPosition = shoulderPoint - target.forward * allowedDistance;
        }

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _positionVelocity, positionSmoothTime);

        Vector3 lookTarget = shoulderPoint + target.forward * lookAheadDistance;
        Quaternion desiredRotation = Quaternion.LookRotation((lookTarget - transform.position).normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationSmoothSpeed * Time.deltaTime);
    }

    private Vector3 GetShoulderPoint()
    {
        return target.position + Vector3.up * shoulderHeight + target.right * shoulderSide;
    }

    private void HandlePlayerSpawned(Player player)
    {
        if (!autoDetectTarget || player == null) return;
        SetTarget(player.transform, snapImmediately: true);
    }

    /// <summary>
    /// Asigna un nuevo objetivo a seguir. Si snapImmediately es true, alinea la cámara de inmediato sin retraso de suavizado.
    /// </summary>
    public void SetTarget(Transform newTarget, bool snapImmediately = false)
    {
        target = newTarget;
        if (target == null) return;

        if (snapImmediately)
        {
            SnapToTarget();
        }
    }

    /// <summary>
    /// Teletransporta instantáneamente la cámara detrás del hombro del target (útil tras spawn/respawn).
    /// </summary>
    public void SnapToTarget()
    {
        if (target == null) return;

        Vector3 shoulderPoint = GetShoulderPoint();
        transform.position = shoulderPoint - target.forward * distance;
        transform.rotation = Quaternion.LookRotation((shoulderPoint + target.forward * lookAheadDistance - transform.position).normalized, Vector3.up);
        _positionVelocity = Vector3.zero;
    }

    private void InitializeTarget()
    {
        if (target != null || !autoDetectTarget) return;

        if (Player.Instance != null)
        {
            SetTarget(Player.Instance.transform, snapImmediately: true);
            return;
        }

        Player player = FindAnyObjectByType<Player>();
        if (player != null)
        {
            SetTarget(player.transform, snapImmediately: true);
        }
        else
        {
            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                SetTarget(playerObj.transform, snapImmediately: true);
            }
        }
    }
}
