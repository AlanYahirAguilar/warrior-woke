using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Third-person orbit camera over the shoulder (GDD §15, decision P5). The player turns it freely
/// with the mouse: its yaw and pitch are its own, not the character's. Player movement is relative
/// to this camera (PlayerMovement), so the camera no longer follows the body's facing — turning the
/// character never swings the camera, and walking backward or pivoting does not drag it around.
/// The camera moves only with the mouse: an automatic recenter behind the body brought back the loop
/// where turning the body turns the camera, which turns "forward" again.
/// Includes a collision pull-in so the camera never clips through geometry.
/// Zero GC allocations per frame in LateUpdate.
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

    [Tooltip("Sideways offset from the target, along the camera's right (positive = right shoulder).")]
    [SerializeField] private float shoulderSide = 0.5f;

    [Tooltip("Distance behind the shoulder point the camera sits.")]
    [SerializeField] private float distance = 3.5f;

    [Header("Orbit")]
    [Tooltip("Degrees of rotation per pixel of mouse movement.")]
    [SerializeField] private float mouseSensitivity = 0.12f;

    [Tooltip("Lowest and highest pitch (degrees; positive looks down).")]
    [SerializeField] private float minPitch = -30f, maxPitch = 60f;

    [Tooltip("Pitch when the camera snaps behind the target.")]
    [SerializeField] private float defaultPitch = 10f;

    [Tooltip("Lock and hide the cursor while playing (Escape releases it until the next click).")]
    [SerializeField] private bool lockCursor = true;

    [Header("Smoothing")]
    [Tooltip("Position smoothing time (lower is snappier).")]
    [SerializeField] private float positionSmoothTime = 0.08f;

    [Header("Collision")]
    [Tooltip("Layers considered solid for camera collision avoidance.")]
    [SerializeField] private LayerMask collisionMask = ~0;

    [Tooltip("Radius of the sphere cast used to keep the camera out of geometry.")]
    [SerializeField] private float collisionRadius = 0.25f;

    [Tooltip("Extra pull-in distance from a collision hit point, to avoid clipping into the surface.")]
    [SerializeField] private float collisionBuffer = 0.15f;

    private Vector3   _positionVelocity;
    private float     _yaw, _pitch;

    /// <summary>Camera yaw (degrees, world). Movement input is relative to it.</summary>
    public float Yaw => _yaw;

    private void OnEnable()
    {
        Player.OnPlayerSpawned += HandlePlayerSpawned;
    }

    private void OnDisable()
    {
        Player.OnPlayerSpawned -= HandlePlayerSpawned;
        if (lockCursor) Cursor.lockState = CursorLockMode.None;
    }

    private void Awake()
    {
        // The camera must never collide with the character it orbits
        int playerLayer = LayerMask.NameToLayer("Player");
        if (playerLayer >= 0) collisionMask &= ~(1 << playerLayer);
        InitializeTarget();
    }

    private void Start()
    {
        // Backup in case the Player spawned between Awake and Start
        if (target == null && autoDetectTarget)
            InitializeTarget();

        if (target != null)
            SnapToTarget();
    }

    private void Update()
    {
        UpdateCursor();
        ReadLook();
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Quaternion orbit = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 shoulderPoint = GetShoulderPoint(orbit);
        Vector3 back = orbit * Vector3.back;
        Vector3 desiredPosition = shoulderPoint + back * distance;

        if (Physics.SphereCast(shoulderPoint, collisionRadius, back, out RaycastHit hit, distance, collisionMask, QueryTriggerInteraction.Ignore))
            desiredPosition = shoulderPoint + back * Mathf.Max(hit.distance - collisionBuffer, 0.1f);

        transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _positionVelocity, positionSmoothTime);
        transform.rotation = orbit;
    }

    // ─── Orbit ───────────────────────────────────────────────────────────────────

    private void ReadLook()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || (lockCursor && Cursor.lockState != CursorLockMode.Locked)) return;
        Vector2 delta = mouse.delta.ReadValue();
        if (delta.sqrMagnitude < 0.0001f) return;
        _yaw   += delta.x * mouseSensitivity;
        _pitch  = Mathf.Clamp(_pitch - delta.y * mouseSensitivity, minPitch, maxPitch);
    }

    private void UpdateCursor()
    {
        if (!lockCursor) return;
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            Cursor.lockState = CursorLockMode.None;
        else if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
    }

    private Vector3 GetShoulderPoint(Quaternion orbit)
    {
        Vector3 right = Quaternion.Euler(0f, orbit.eulerAngles.y, 0f) * Vector3.right;
        return target.position + Vector3.up * shoulderHeight + right * shoulderSide;
    }

    // ─── Target ──────────────────────────────────────────────────────────────────

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
            SnapToTarget();
    }

    /// <summary>
    /// Teletransporta instantáneamente la cámara detrás del hombro del target, mirando hacia donde
    /// mira el target (útil tras spawn/respawn).
    /// </summary>
    public void SnapToTarget()
    {
        if (target == null) return;

        _yaw   = target.eulerAngles.y;
        _pitch = defaultPitch;
        Quaternion orbit = Quaternion.Euler(_pitch, _yaw, 0f);
        transform.rotation = orbit;
        transform.position = GetShoulderPoint(orbit) + orbit * Vector3.back * distance;
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
                SetTarget(playerObj.transform, snapImmediately: true);
        }
    }
}
