using UnityEngine;

/// <summary>
/// Controls 2.5D side-scroller camera tracking.
/// Follows the player along the horizontal X axis, provides damped tracking along the vertical Y axis
/// to accommodate high platforms without jitter during small jumps, and locks depth distance along Z.
/// Generates zero GC allocations per frame in LateUpdate.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Target Tracking")]
    [Tooltip("Target transform to follow. If autoDetectTarget is disabled, assign manually here.")]
    [SerializeField] private Transform target;

    [Tooltip("Automatically detects the Player via events or scene search when true. If false, uses the manual Target assigned above.")]
    [SerializeField] private bool autoDetectTarget = true;

    [Header("Offset & Depth")]
    [Tooltip("Camera offset relative to target. Z controls the lateral viewing distance.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 2f, -10f);

    [Tooltip("Automatically computes offset from initial scene placement.")]
    [SerializeField] private bool autoCalculateOffset = true;

    [Header("Smoothing")]
    [Tooltip("Horizontal tracking responsiveness (lower is snappier).")]
    [SerializeField] private float smoothTimeX = 0.15f;

    [Tooltip("Vertical tracking responsiveness. Higher value cushions small jumps while following tall platforms.")]
    [SerializeField] private float smoothTimeY = 0.35f;

    [Tooltip("Vertical distance the player can move before camera begins vertical tracking.")]
    [SerializeField] private float verticalDeadZone = 1.2f;

    [Header("Framing & Zoom")]
    [Tooltip("If true and camera is orthographic, applies an expanded framing size for vertical visibility.")]
    [SerializeField] private bool adjustOrthographicSize = true;

    [Tooltip("Target orthographic size (6.5 gives optimal visibility for elevated platforms).")]
    [SerializeField] private float targetOrthographicSize = 6.5f;

    private Camera _camera;
    private float _velocityX;
    private float _velocityY;
    private float _currentCameraY;

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
        _camera = GetComponent<Camera>();
        if (_camera != null && _camera.orthographic && adjustOrthographicSize)
        {
            _camera.orthographicSize = targetOrthographicSize;
        }

        InitializeTarget();
        InitializeOffset();

        _currentCameraY = transform.position.y;
    }

    private void Start()
    {
        // Respaldo por si el Player apareció entre Awake y Start
        if (target == null && autoDetectTarget)
        {
            InitializeTarget();
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Vector3 targetPos = target.position;

        // 1. Horizontal Tracking (X)
        float targetX = targetPos.x + offset.x;
        float newX = Mathf.SmoothDamp(transform.position.x, targetX, ref _velocityX, smoothTimeX);

        // 2. Vertical Tracking (Y) with deadzone to cushion jumps
        float targetY = targetPos.y + offset.y;
        float deltaY = targetY - _currentCameraY;

        if (Mathf.Abs(deltaY) > verticalDeadZone)
        {
            float desiredY = targetY - Mathf.Sign(deltaY) * verticalDeadZone;
            _currentCameraY = Mathf.SmoothDamp(_currentCameraY, desiredY, ref _velocityY, smoothTimeY);
        }
        else
        {
            _currentCameraY = Mathf.SmoothDamp(_currentCameraY, targetY, ref _velocityY, smoothTimeY * 1.5f);
        }

        // 3. Fixed Depth (Z)
        float fixedZ = offset.z;

        transform.position = new Vector3(newX, _currentCameraY, fixedZ);
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
    /// Teletransporta instantáneamente la cámara a la posición del target + offset (útil tras spawn/respawn).
    /// </summary>
    public void SnapToTarget()
    {
        if (target == null) return;

        Vector3 targetPos = target.position;
        float targetX = targetPos.x + offset.x;
        float targetY = targetPos.y + offset.y;
        float fixedZ = offset.z;

        transform.position = new Vector3(targetX, targetY, fixedZ);
        _currentCameraY = targetY;
        _velocityX = 0f;
        _velocityY = 0f;
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

    private void InitializeOffset()
    {
        if (autoCalculateOffset && target != null)
        {
            offset = transform.position - target.position;
        }
        else if (offset.z >= 0f)
        {
            offset.z = -10f; // Ensure standard side-view depth
        }
    }
}
