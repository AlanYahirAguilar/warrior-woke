using System.Collections;
using UnityEngine;

/// <summary>
/// Controls 2.5D platformer physical movement for Kael (Fase 1).
/// Enforces Z-axis and full rotation constraints, handles sprinting, jumping, and sliding.
/// Complies with SOLID principles: delegates ground checking to IGroundChecker (DIP/SRP)
/// and structures logic into cohesive, single-responsibility methods.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
[Header("Movement Velocities")]
    [Tooltip("Base horizontal movement speed.")]
    [SerializeField] private float speed = 8f; 

    [Tooltip("Sprint horizontal movement speed.")]
    [SerializeField] private float sprintSpeed = 11.5f;

    [Tooltip("Slide horizontal boost speed.")]
    [SerializeField] private float slideSpeed = 14f;

    [Header("Jumping")]
    [Tooltip("Vertical speed applied when jumping.")]
    [SerializeField] private float jumpSpeed = 7f;

    [Header("Sliding Configuration")]
    [Tooltip("Duration of the slide maneuver in seconds.")]
    [SerializeField] private float slideDuration = 0.7f;

    [Tooltip("Layer mask checked to prevent standing up under low ceilings.")]
    [SerializeField] private LayerMask ceilingLayer = ~0;

    [Header("Facing Direction")]
    [Tooltip("Rotates the transform to face the horizontal direction of movement.")]
    [SerializeField] private bool faceMovementDirection = true;

    private Rigidbody _rigidbody;
    private CapsuleCollider _capsuleCollider;
    private IGroundChecker _groundChecker;
    private WaitForSeconds _slideWait;

    private bool _isSliding;
    private float _originalColliderHeight;
    private Vector3 _originalColliderCenter;
    private float _facingDirection = 1f;

    public bool IsGrounded => _groundChecker != null && _groundChecker.IsGrounded;
    public bool IsSliding => _isSliding;

    private void Awake()
    {
        InitializeComponents();
        ConfigurePhysicsConstraints();
        CacheSlideConfiguration();
    }

    /// <summary>
    /// Master physics processing loop called each FixedUpdate.
    /// </summary>
    public void ProcessMovement(float horizontalInput, bool isSprint, bool jumpTriggered, bool slideTriggered)
    {
        if (_rigidbody == null) return;

        UpdateGroundStatus();
        HandleSlide(slideTriggered, horizontalInput);

        // Calculate target velocity once to avoid overriding physics engine state mid-frame
        Vector3 targetVelocity = _rigidbody.linearVelocity;
        targetVelocity.z = 0f; // Enforce 2.5D constraint

        ApplyHorizontalVelocity(ref targetVelocity, horizontalInput, isSprint);
        HandleJump(ref targetVelocity, jumpTriggered);

        // Apply final velocity
        _rigidbody.linearVelocity = targetVelocity;

        UpdateFacingDirection(horizontalInput);
    }

    private void InitializeComponents()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _capsuleCollider = GetComponent<CapsuleCollider>();

        // Dependency Inversion: resolve IGroundChecker or fallback safely
        _groundChecker = GetComponent<IGroundChecker>();
        if (_groundChecker == null)
        {
            _groundChecker = gameObject.AddComponent<GroundChecker>();
        }
    }

    private void ConfigurePhysicsConstraints()
    {
        // Enforce strict 2.5D physics constraints: freeze depth (Z) and all rotations
        _rigidbody.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
    }

    private void CacheSlideConfiguration()
    {
        // Cache coroutine wait object to eliminate GC pressure per best practices
        _slideWait = new WaitForSeconds(slideDuration);

        if (_capsuleCollider != null)
        {
            _originalColliderHeight = _capsuleCollider.height;
            _originalColliderCenter = _capsuleCollider.center;
        }
    }

    private void UpdateGroundStatus()
    {
        _groundChecker?.CheckGrounded();
    }

    private void HandleJump(ref Vector3 currentVelocity, bool jumpTriggered)
    {
        if (jumpTriggered)
        {
            if (IsGrounded && !_isSliding)
            {
                currentVelocity.y = jumpSpeed;
                // Debug.Log($"[PlayerMovement] Salto ejecutado! Velocidad Y: {jumpSpeed}");
            }
            // else
            // {
            //     Debug.Log($"[PlayerMovement] Salto denegado -> IsGrounded: {IsGrounded} | IsSliding: {_isSliding}");
            // }
        }
    }

    private void HandleSlide(bool slideTriggered, float horizontalInput)
    {
        if (slideTriggered && IsGrounded && !_isSliding && Mathf.Abs(horizontalInput) > 0.1f)
        {
            StartCoroutine(SlideRoutine());
        }
    }

    private void ApplyHorizontalVelocity(ref Vector3 currentVelocity, float horizontalInput, bool isSprint)
    {
        float currentSpeed = speed;

        if (_isSliding)
        {
            currentSpeed = slideSpeed;
            horizontalInput = _facingDirection; // Preserve slide momentum forward
        }
        else if (isSprint)
        {
            currentSpeed = sprintSpeed;
        }

        currentVelocity.x = horizontalInput * currentSpeed;
    }

    private void UpdateFacingDirection(float horizontalInput)
    {
        if (!faceMovementDirection || _isSliding || Mathf.Abs(horizontalInput) <= 0.05f) return;

        _facingDirection = Mathf.Sign(horizontalInput);
        transform.rotation = Quaternion.Euler(0f, _facingDirection > 0 ? 90f : -90f, 0f);
    }

    private IEnumerator SlideRoutine()
    {
        _isSliding = true;

        if (_capsuleCollider != null)
        {
            // Halve the collider height and drop center towards the floor
            _capsuleCollider.height = _originalColliderHeight * 0.5f;
            _capsuleCollider.center = new Vector3(_originalColliderCenter.x, _originalColliderCenter.y * 0.5f, _originalColliderCenter.z);
        }

        yield return _slideWait;

        // Ceiling raycast: stay crouched if an overhead obstacle prevents standing
        if (_capsuleCollider != null)
        {
            float ceilingRayLength = _originalColliderHeight * 0.8f;
            while (Physics.Raycast(transform.position, Vector3.up, ceilingRayLength, ceilingLayer))
            {
                yield return null;
            }

            // Restore normal collider boundaries
            _capsuleCollider.height = _originalColliderHeight;
            _capsuleCollider.center = _originalColliderCenter;
        }

        _isSliding = false;
    }
}
