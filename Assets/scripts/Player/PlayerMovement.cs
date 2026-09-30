using UnityEngine;

/// <summary>
/// Context for the PlayerStateMachine in a free-roam 3D character controller.
/// Owns all state instances and exposes physics helpers consumed by states.
/// Movement is camera-relative on the XZ plane (third-person, over-the-shoulder style):
/// input is projected onto the main camera's flattened forward/right vectors, and the
/// character smoothly rotates to face its current movement direction.
/// Adheres to SRP: orchestrates state routing, never implements game logic directly.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour
{
    // ─── Inspector Configuration ─────────────────────────────────────────────────
    [Header("Movement Speeds")]
    public float BaseSpeed   = 8f;
    public float SprintSpeed = 11.5f;
    public float SlideSpeed  = 14f;
    public float JumpSpeed   = 7f;

    [Header("Sliding")]
    public float SlideDuration = 0.7f;
    [SerializeField] private LayerMask ceilingLayer = ~0;

    [Header("Auto-Sprint")]
    [Tooltip("Seconds of continuous running before auto-sprint activates.")]
    public float SprintActivationTime = 3f;

    [Header("Facing")]
    [SerializeField] private bool faceMovementDirection = true;
    [Tooltip("Smoothing time (seconds) for turning to face the movement direction. Lower = snappier/more sensitive, higher = smoother/heavier.")]
    [SerializeField] private float turnSmoothTime = 0.12f;

    private float _turnSmoothVelocity;

    [Header("Input Deadzone")]
    [SerializeField] private float moveInputDeadzone = 0.1f;

    // ─── Cached Components (set once in Awake, never in the loop) ────────────────
    public Rigidbody        Rb         { get; private set; }
    private CapsuleCollider _capsuleCollider;
    private IGroundChecker  _groundChecker;
    private PlayerStamina   _stamina;
    private Transform       _cameraTransform;
    public EnvironmentChecker EnvChecker { get; private set; }

    // ─── Movement Input State ─────────────────────────────────────────────────────
    public float InputX         { get; private set; }
    public float InputZ         { get; private set; }
    public bool  JumpTriggered  { get; private set; }
    public bool  SlideTriggered { get; private set; }

    /// <summary>True when the combined move input exceeds the deadzone.</summary>
    public bool HasMoveInput => (InputX * InputX + InputZ * InputZ) > (moveInputDeadzone * moveInputDeadzone);

    // ─── Combat Input State ───────────────────────────────────────────────────────
    public bool LightAttackTriggered { get; private set; }
    public bool HeavyAttackTriggered { get; private set; }
    public bool IsBlockHeld          { get; private set; }
    public bool DodgeTriggered       { get; private set; }

    // ─── Runtime State ────────────────────────────────────────────────────────────
    public bool  IsSprint           { get; set; }
    public bool CanSprint
    {
        get
        {
            if (_stamina == null)
                _stamina = GetComponent<PlayerStamina>();
            return _stamina != null && _stamina.CanSprint;
        }
    }

    /// <summary>Camera-relative, normalized movement direction on the XZ plane for this tick (zero when no input).</summary>
    public Vector3 MoveDirection    { get; private set; } = Vector3.zero;

    public bool  IsGrounded         => _groundChecker != null && _groundChecker.IsGrounded;
    public int   ConsecutiveWallJumps { get; private set; }
    public Vector3 CurrentLedgeCorner { get; set; }

    // ─── State Machine ────────────────────────────────────────────────────────────
    public PlayerStateMachine    StateMachine       { get; private set; }
    public PlayerIdleState       IdleState          { get; private set; }
    public PlayerRunState        RunState           { get; private set; }
    public PlayerJumpState       JumpState          { get; private set; }
    public PlayerSlideState      SlideState         { get; private set; }
    public PlayerVaultState      VaultState         { get; private set; }
    public PlayerLedgeGrabState  LedgeGrabState     { get; private set; }
    public PlayerLedgeClimbState LedgeClimbState    { get; private set; }
    public PlayerWallJumpState   WallJumpState      { get; private set; }
    // Combat states — populated after combat system is added
    public PlayerLightAttackState LightAttackState  { get; private set; }
    public PlayerHeavyAttackState HeavyAttackState  { get; private set; }
    public PlayerBlockState       BlockState        { get; private set; }
    public PlayerDodgeState       DodgeState        { get; private set; }

    // ─── Collider Snapshots ───────────────────────────────────────────────────────
    private float   _originalColliderHeight;
    private Vector3 _originalColliderCenter;

    // ─── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        CacheComponents();
        SnapshotColliderDimensions();
        ConfigureRigidbody();
        BuildStateMachine();
    }

    private void Start()
    {
        StateMachine.Initialize(IdleState);
    }

    private void FixedUpdate()
    {
        // Rotation applied in physics step so it stays in sync with Rigidbody movement
        ApplyFacingRotation();
        StateMachine.CurrentState?.PhysicsUpdate();
    }

    // ─── Input Entry Point (called by Player.cs from FixedUpdate) ────────────────

    /// <summary>
    /// Receives all input values from Player.cs and ticks the current state's logic.
    /// </summary>
    public void ProcessMovement(
        float horizontal,
        float vertical,
        bool  jumpTriggered,
        bool  slideTriggered,
        bool  lightAttack,
        bool  heavyAttack,
        bool  blockHeld,
        bool  dodgeTriggered)
    {
        InputX                = horizontal;
        InputZ                = vertical;
        JumpTriggered         = jumpTriggered;
        SlideTriggered        = slideTriggered;
        LightAttackTriggered  = lightAttack;
        HeavyAttackTriggered  = heavyAttack;
        IsBlockHeld           = blockHeld;
        DodgeTriggered        = dodgeTriggered;

        _groundChecker?.CheckGrounded();
        UpdateMoveDirection();   // pure logic — no transform writes here

        StateMachine.CurrentState?.LogicUpdate();
    }

    // ─── Physics Helpers ─────────────────────────────────────────────────────────

    /// <summary>Sets horizontal (X/Z) and vertical (Y) velocity independently.</summary>
    public void SetVelocity(Vector3 horizontalVelocity, float verticalVelocity)
    {
        Rb.linearVelocity = new Vector3(horizontalVelocity.x, verticalVelocity, horizontalVelocity.z);
    }

    /// <summary>Convenience overload: sets a single horizontal axis (Z is zeroed) and vertical velocity.</summary>
    public void SetVelocity(float x, float verticalVelocity)
    {
        SetVelocity(new Vector3(x, 0f, 0f), verticalVelocity);
    }

    public void SetKinematic(bool isKinematic)
    {
        Rb.isKinematic = isKinematic;
    }

    public void ShrinkCollider(float factor)
    {
        if (_capsuleCollider == null) return;
        _capsuleCollider.height = _originalColliderHeight * factor;
        _capsuleCollider.center = new Vector3(
            _originalColliderCenter.x,
            _originalColliderCenter.y * factor,
            _originalColliderCenter.z);
    }

    public void ResetCollider()
    {
        if (_capsuleCollider == null) return;
        _capsuleCollider.height = _originalColliderHeight;
        _capsuleCollider.center = _originalColliderCenter;
    }

    public bool HasCeilingOverhead()
    {
        if (_capsuleCollider == null) return false;
        return Physics.Raycast(transform.position, Vector3.up, _originalColliderHeight * 0.8f, ceilingLayer);
    }

    public void IncrementWallJump()    => ConsecutiveWallJumps++;
    public void ResetConsecutiveWallJumps() => ConsecutiveWallJumps = 0;

    // ─── Private Helpers ─────────────────────────────────────────────────────────

    private void CacheComponents()
    {
        Rb               = GetComponent<Rigidbody>();
        _capsuleCollider = GetComponent<CapsuleCollider>();
        _stamina         = GetComponent<PlayerStamina>();
        _groundChecker   = GetComponent<IGroundChecker>() ?? gameObject.AddComponent<GroundChecker>();
        EnvChecker       = GetComponent<EnvironmentChecker>() ?? gameObject.AddComponent<EnvironmentChecker>();

        if (Camera.main != null)
            _cameraTransform = Camera.main.transform;
    }

    private void SnapshotColliderDimensions()
    {
        if (_capsuleCollider == null) return;
        _originalColliderHeight = _capsuleCollider.height;
        _originalColliderCenter = _capsuleCollider.center;
    }

    private void ConfigureRigidbody()
    {
        // Free movement on X and Z (camera-relative third person); rotation is driven
        // manually via transform.rotation, so it stays frozen on the Rigidbody itself.
        Rb.constraints = RigidbodyConstraints.FreezeRotation;
    }

    private void BuildStateMachine()
    {
        StateMachine      = new PlayerStateMachine();
        IdleState         = new PlayerIdleState(this, StateMachine);
        RunState          = new PlayerRunState(this, StateMachine);
        JumpState         = new PlayerJumpState(this, StateMachine);
        SlideState        = new PlayerSlideState(this, StateMachine);
        VaultState        = new PlayerVaultState(this, StateMachine);
        LedgeGrabState    = new PlayerLedgeGrabState(this, StateMachine);
        LedgeClimbState   = new PlayerLedgeClimbState(this, StateMachine);
        WallJumpState     = new PlayerWallJumpState(this, StateMachine);
        LightAttackState  = new PlayerLightAttackState(this, StateMachine);
        HeavyAttackState  = new PlayerHeavyAttackState(this, StateMachine);
        BlockState        = new PlayerBlockState(this, StateMachine);
        DodgeState        = new PlayerDodgeState(this, StateMachine);
    }

    /// <summary>
    /// Projects raw input onto the camera's flattened forward/right axes to compute
    /// the world-space movement direction for this tick. Pure data — no transform writes.
    /// Falls back to world-space axes if no camera is available.
    /// </summary>
    private void UpdateMoveDirection()
    {
        if (!HasMoveInput)
        {
            MoveDirection = Vector3.zero;
            return;
        }

        if (_cameraTransform == null && Camera.main != null)
            _cameraTransform = Camera.main.transform;

        Vector3 forward;
        Vector3 right;

        if (_cameraTransform != null)
        {
            forward = _cameraTransform.forward;
            right   = _cameraTransform.right;
        }
        else
        {
            forward = Vector3.forward;
            right   = Vector3.right;
        }

        forward.y = 0f;
        right.y   = 0f;
        forward.Normalize();
        right.Normalize();

        Vector3 direction = forward * InputZ + right * InputX;
        MoveDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    /// <summary>
    /// Smoothly rotates the character to face MoveDirection.
    /// Lives in FixedUpdate so rotation is applied in the same physics step as velocity,
    /// keeping the Rigidbody and transform in sync and avoiding visual jitter.
    /// </summary>
    private void ApplyFacingRotation()
    {
        if (!faceMovementDirection || MoveDirection == Vector3.zero || !CanRotateInCurrentState())
            return;

        // SmoothDampAngle eases in/out of the turn instead of snapping at a flat angular
        // speed — this is what actually fixes "the camera spins too fast": the camera
        // trails transform.forward, so a snappy instant turn reads as a fast camera swing.
        float targetYaw  = Quaternion.LookRotation(MoveDirection, Vector3.up).eulerAngles.y;
        float currentYaw = transform.eulerAngles.y;
        float newYaw     = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref _turnSmoothVelocity, turnSmoothTime);
        transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
    }

    private bool CanRotateInCurrentState()
    {
        // Lock visual direction during kinematic/parkour/locked combat states
        var current = StateMachine.CurrentState;
        return current != WallJumpState   &&
               current != LedgeGrabState  &&
               current != LedgeClimbState &&
               current != VaultState      &&
               current != BlockState      &&
               current != LightAttackState&&
               current != HeavyAttackState;
    }
}
