using UnityEngine;

/// <summary>
/// Controls 2.5D platformer physical movement for Kael (Fase 1).
/// Enforces Z-axis and full rotation constraints.
/// Refactored to act as the Context for the PlayerStateMachine (SOLID / SRP).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Velocities")]
    public float BaseSpeed = 8f; 
    public float SprintSpeed = 11.5f;
    public float SlideSpeed = 14f;
    public float JumpSpeed = 7f;
    public float WallSlideSpeed = -2f;

    [Header("Sliding Configuration")]
    public float SlideDuration = 0.7f;
    [SerializeField] private LayerMask ceilingLayer = ~0;

    [Header("Facing Direction")]
    [SerializeField] private bool faceMovementDirection = true;

    // Components
    public Rigidbody Rb { get; private set; }
    private CapsuleCollider _capsuleCollider;
    private IGroundChecker _groundChecker;
    public EnvironmentChecker EnvChecker { get; private set; }

    // Input state
    public float InputX { get; private set; }
    public bool IsSprint { get; set; }
    public bool JumpTriggered { get; private set; }
    public bool SlideTriggered { get; private set; }

    // State machine properties
    public PlayerStateMachine StateMachine { get; private set; }
    public PlayerIdleState IdleState { get; private set; }
    public PlayerRunState RunState { get; private set; }
    public PlayerJumpState JumpState { get; private set; }
    public PlayerFallState FallState { get; private set; }
    public PlayerSlideState SlideState { get; private set; }
    public PlayerVaultState VaultState { get; private set; }
    public PlayerLedgeGrabState LedgeGrabState { get; private set; }
    public PlayerLedgeClimbState LedgeClimbState { get; private set; }
    public PlayerWallJumpState WallJumpState { get; private set; }
    public PlayerWallSlideState WallSlideState { get; private set; }

    public bool IsGrounded => _groundChecker != null && _groundChecker.IsGrounded;
    public float FacingDirection { get; private set; } = 1f;
    public Vector3 CurrentLedgeCorner { get; set; }
    public int ConsecutiveWallJumps { get; private set; } = 0;

    private float _originalColliderHeight;
    private Vector3 _originalColliderCenter;

    private void Awake()
    {
        Rb = GetComponent<Rigidbody>();
        _capsuleCollider = GetComponent<CapsuleCollider>();
        _groundChecker = GetComponent<IGroundChecker>() ?? gameObject.AddComponent<GroundChecker>();
        EnvChecker = GetComponent<EnvironmentChecker>() ?? gameObject.AddComponent<EnvironmentChecker>();

        if (_capsuleCollider != null)
        {
            _originalColliderHeight = _capsuleCollider.height;
            _originalColliderCenter = _capsuleCollider.center;
        }

        Rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;

        StateMachine = new PlayerStateMachine();
        IdleState = new PlayerIdleState(this, StateMachine);
        RunState = new PlayerRunState(this, StateMachine);
        JumpState = new PlayerJumpState(this, StateMachine);
        FallState = new PlayerFallState(this, StateMachine);
        SlideState = new PlayerSlideState(this, StateMachine);
        VaultState = new PlayerVaultState(this, StateMachine);
        LedgeGrabState = new PlayerLedgeGrabState(this, StateMachine);
        LedgeClimbState = new PlayerLedgeClimbState(this, StateMachine);
        WallJumpState = new PlayerWallJumpState(this, StateMachine);
        WallSlideState = new PlayerWallSlideState(this, StateMachine);
    }

    private void Start()
    {
        StateMachine.Initialize(IdleState);
    }

    /// <summary>
    /// Receives input from Player.cs and ticks the logic of the current state.
    /// </summary>
    public void ProcessMovement(float horizontalInput, bool isSprint, bool jumpTriggered, bool slideTriggered)
    {
        InputX = horizontalInput;
        JumpTriggered = jumpTriggered;
        SlideTriggered = slideTriggered;

        _groundChecker?.CheckGrounded();
        UpdateFacingDirection();

        StateMachine.CurrentState?.LogicUpdate();
    }

    private void FixedUpdate()
    {
        StateMachine.CurrentState?.PhysicsUpdate();
    }

    private void UpdateFacingDirection()
    {
        if (!faceMovementDirection || Mathf.Abs(InputX) <= 0.05f) return;
        
        // Bloquear cambio de dirección visual durante ciertas animaciones
        if (StateMachine.CurrentState == WallJumpState || 
            StateMachine.CurrentState == LedgeGrabState || 
            StateMachine.CurrentState == LedgeClimbState || 
            StateMachine.CurrentState == VaultState) return;

        FacingDirection = Mathf.Sign(InputX);
        transform.rotation = Quaternion.Euler(0f, FacingDirection > 0 ? 90f : -90f, 0f);
    }

    public void SetVelocity(float x, float y)
    {
        Vector3 newVelocity = new Vector3(x, y, 0f);
        Rb.linearVelocity = newVelocity;
    }

    public void SetKinematic(bool isKinematic)
    {
        Rb.isKinematic = isKinematic;
    }

    public void ShrinkCollider(float factor)
    {
        if (_capsuleCollider != null)
        {
            _capsuleCollider.height = _originalColliderHeight * factor;
            _capsuleCollider.center = new Vector3(_originalColliderCenter.x, _originalColliderCenter.y * factor, _originalColliderCenter.z);
        }
    }

    public void ResetCollider()
    {
        if (_capsuleCollider != null)
        {
            _capsuleCollider.height = _originalColliderHeight;
            _capsuleCollider.center = _originalColliderCenter;
        }
    }

    public bool HasCeilingOverhead()
    {
        if (_capsuleCollider == null) return false;
        float ceilingRayLength = _originalColliderHeight * 0.8f;
        return Physics.Raycast(transform.position, Vector3.up, ceilingRayLength, ceilingLayer);
    }

    public void IncrementWallJump()
    {
        ConsecutiveWallJumps++;
    }

    public void ResetConsecutiveWallJumps()
    {
        ConsecutiveWallJumps = 0;
    }
}
