using UnityEngine;

/// <summary>
/// Context for the PlayerStateMachine in a free-roam 3D character controller.
/// Owns all state instances and exposes physics helpers consumed by states.
/// Movement is camera-relative on the XZ plane (third-person, over-the-shoulder style):
/// input is projected onto the main camera's flattened forward/right vectors, and the
/// character smoothly rotates to face its current movement direction — except when
/// backpedaling (S / back diagonals without sprint), where it keeps facing the camera's forward.
/// Adheres to SRP: orchestrates state routing, never implements game logic directly.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerMovement : MonoBehaviour, IDamageModifier
{
    // ─── Inspector Configuration ─────────────────────────────────────────────────
    [Header("Movement Speeds")]
    [Tooltip("Run speed (WASD). Matched to the stride of the Run animation clip.")]
    public float BaseSpeed   = 5f;
    [Tooltip("Sprint = BaseSpeed × this value. GDD §5.2: ~+40 %.")]
    public float SprintMultiplier = 1.4f;
    public float SlideSpeed  = 9f;
    public float JumpSpeed   = 7f;
    [Tooltip("Walking backward speed (S without sprint). Matches the Ch45 'Walk Backward' clip (~1.5 m/s).")]
    public float BackpedalSpeed = 1.5f;

    public float SprintSpeed => BaseSpeed * SprintMultiplier;

    [Header("Acceleration")]
    [Tooltip("m/s² used to speed up on the ground. The locomotion blend passes Idle → Walk → Jog → Run while accelerating.")]
    public float Acceleration = 12f;
    [Tooltip("m/s² used to slow down on the ground (releasing input, or a lower target speed).")]
    public float Deceleration = 16f;

    [Header("Sliding")]
    public float SlideDuration = 0.8f;
    [SerializeField] private LayerMask ceilingLayer = ~0;

    [Header("Auto Step (adapted from Dynamic Parkour System, MIT)")]
    [Tooltip("Highest ledge (m above the feet) climbed automatically while moving on the ground.")]
    public float StepHeight = 0.4f;
    [SerializeField] private LayerMask stepLayer = ~0;

    [Header("Falling")]
    [Tooltip("Seconds without ground under Idle/Run before switching to the Fall state (absorbs small steps and slopes).")]
    public float FallGraceTime = 0.15f;

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
    private HealthSystem    _healthSystem;
    private Transform       _cameraTransform;
    public EnvironmentChecker EnvChecker { get; private set; }

    // ─── Movement Input State ─────────────────────────────────────────────────────
    public float InputX         { get; private set; }
    public float InputZ         { get; private set; }
    public bool  JumpTriggered  { get; private set; }
    public bool  IsSprintHeld   { get; private set; }
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

    /// <summary>
    /// True while Shift is held and sprint has not been cancelled. GDD §5.2: damage, blocking
    /// and attacking cancel the sprint; it stays cancelled until Shift is released and pressed again.
    /// </summary>
    public bool  CanSprint          => IsSprintHeld && !_sprintCancelled;
    private bool _sprintCancelled;

    /// <summary>Seconds since the ground checker last reported ground (0 while grounded).</summary>
    public float AirTime            { get; private set; }

    /// <summary>
    /// Walking backward while facing forward: back input (S or back diagonals) without sprint.
    /// Sprinting backward turns around and runs normally.
    /// </summary>
    public bool  IsBackpedaling     => HasMoveInput && InputZ < -0.1f && !IsSprint;

    /// <summary>Half of the standing collider height: distance from the origin (torso center) to the feet.</summary>
    public float StandingHalfHeight => _originalColliderHeight * 0.5f;

    /// <summary>World Y of the bottom of the collider (the feet).</summary>
    public float FeetY => _capsuleCollider != null ? _capsuleCollider.bounds.min.y : transform.position.y;

    /// <summary>Obstacle found by the last vault check (set right before entering VaultState).</summary>
    public VaultInfo PendingVault   { get; set; }

    /// <summary>Camera-relative, normalized movement direction on the XZ plane for this tick (zero when no input).</summary>
    public Vector3 MoveDirection    { get; private set; } = Vector3.zero;

    public bool  IsGrounded         => _groundChecker != null && _groundChecker.IsGrounded;
    public Vector3 CurrentLedgeCorner { get; set; }

    // ─── State Machine ────────────────────────────────────────────────────────────
    public PlayerStateMachine    StateMachine       { get; private set; }

    /// <summary>
    /// Fires after every state change. Declared here (not only on the state machine) so listeners
    /// such as PlayerAnimator can subscribe in OnEnable even before this component's Awake ran.
    /// </summary>
    public event System.Action<PlayerState> StateChanged;
    public PlayerIdleState       IdleState          { get; private set; }
    public PlayerRunState        RunState           { get; private set; }
    public PlayerJumpState       JumpState          { get; private set; }
    public PlayerFallState       FallState          { get; private set; }
    public PlayerSlideState      SlideState         { get; private set; }
    public PlayerVaultState      VaultState         { get; private set; }
    public PlayerLedgeGrabState  LedgeGrabState     { get; private set; }
    public PlayerLedgeClimbState LedgeClimbState    { get; private set; }
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

    private void OnEnable()
    {
        if (_healthSystem != null)
            _healthSystem.OnDamageReceived += HandleDamageReceived;
    }

    private void OnDisable()
    {
        if (_healthSystem != null)
            _healthSystem.OnDamageReceived -= HandleDamageReceived;
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
        bool  sprintHeld,
        bool  jumpTriggered,
        bool  slideTriggered,
        bool  lightAttack,
        bool  heavyAttack,
        bool  blockHeld,
        bool  dodgeTriggered)
    {
        InputX                = horizontal;
        InputZ                = vertical;
        IsSprintHeld          = sprintHeld;
        JumpTriggered         = jumpTriggered;
        SlideTriggered        = slideTriggered;
        LightAttackTriggered  = lightAttack;
        HeavyAttackTriggered  = heavyAttack;
        IsBlockHeld           = blockHeld;
        DodgeTriggered        = dodgeTriggered;

        // A cancelled sprint is re-armed only by releasing Shift (GDD §5.2).
        if (!sprintHeld)
            _sprintCancelled = false;

        _groundChecker?.CheckGrounded();
        AirTime = IsGrounded ? 0f : AirTime + Time.fixedDeltaTime;
        UpdateMoveDirection();   // pure logic — no transform writes here

        StateMachine.CurrentState?.LogicUpdate();
    }

    // ─── Sprint ──────────────────────────────────────────────────────────────────

    /// <summary>Stops the sprint until Shift is released (GDD §5.2: damage, block, attack).</summary>
    public void CancelSprint()
    {
        IsSprint         = false;
        _sprintCancelled = true;
    }

    private void HandleDamageReceived(int amount, Vector3 source)
    {
        CancelSprint();
    }

    // ─── IDamageModifier ─────────────────────────────────────────────────────────

    /// <summary>Called by HealthSystem before applying damage. Only the Block state reduces it.</summary>
    public int ModifyIncomingDamage(int amount, Vector3 source)
    {
        return StateMachine != null && StateMachine.CurrentState == BlockState
            ? BlockState.ModifyIncomingDamage(amount, source)
            : amount;
    }

    // ─── Physics Helpers ─────────────────────────────────────────────────────────

    /// <summary>Sets horizontal (X/Z) and vertical (Y) velocity independently.</summary>
    public void SetVelocity(Vector3 horizontalVelocity, float verticalVelocity)
    {
        Rb.linearVelocity = new Vector3(horizontalVelocity.x, verticalVelocity, horizontalVelocity.z);
    }

    /// <summary>Zeroes horizontal (X/Z) velocity and sets the vertical (Y) velocity.</summary>
    public void StopHorizontal(float verticalVelocity)
    {
        Rb.linearVelocity = new Vector3(0f, verticalVelocity, 0f);
    }

    public void SetKinematic(bool isKinematic)
    {
        Rb.isKinematic = isKinematic;
    }

    /// <summary>
    /// Moves the horizontal velocity toward <paramref name="targetHorizontal"/> using Acceleration
    /// (speeding up) or Deceleration (slowing down). Keeps the vertical velocity.
    /// </summary>
    public void AccelerateHorizontal(Vector3 targetHorizontal)
    {
        Vector3 v = Rb.linearVelocity;
        Vector3 current = new Vector3(v.x, 0f, v.z);
        float rate = targetHorizontal.sqrMagnitude >= current.sqrMagnitude ? Acceleration : Deceleration;
        Vector3 next = Vector3.MoveTowards(current, targetHorizontal, rate * Time.fixedDeltaTime);
        Rb.linearVelocity = new Vector3(next.x, v.y, next.z);
    }

    /// <summary>Turns the character instantly to face <paramref name="direction"/> (XZ). Used at the start of attacks.</summary>
    public void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        _turnSmoothVelocity = 0f;
    }

    /// <summary>
    /// Auto step (logic adapted from Dynamic Parkour System's MovementCharacterController.AutoStep):
    /// if a low edge blocks the feet in the movement direction and the space above it is free,
    /// lift the body onto it so small steps and curbs do not stop the run.
    /// </summary>
    public void TryAutoStep()
    {
        if (_capsuleCollider == null || MoveDirection == Vector3.zero || !IsGrounded) return;

        float feet = FeetY;
        float reach = _capsuleCollider.radius + 0.2f;
        Vector3 basePos = new Vector3(transform.position.x, feet, transform.position.z);

        if (!Physics.Raycast(basePos + Vector3.up * 0.05f, MoveDirection, out RaycastHit low, reach, stepLayer, QueryTriggerInteraction.Ignore))
            return;
        if (Mathf.Abs(low.normal.y) > 0.3f) return; // a slope, not a step

        if (Physics.Raycast(basePos + Vector3.up * (StepHeight + 0.02f), MoveDirection, reach + 0.1f, stepLayer, QueryTriggerInteraction.Ignore))
            return; // too tall: it is a wall or a vault obstacle

        Vector3 topOrigin = low.point + MoveDirection * 0.05f + Vector3.up * (StepHeight + 0.05f);
        if (!Physics.Raycast(topOrigin, Vector3.down, out RaycastHit top, StepHeight + 0.1f, stepLayer, QueryTriggerInteraction.Ignore))
            return;

        float rise = top.point.y - feet;
        if (rise <= 0.02f || rise > StepHeight) return;

        Rb.position += Vector3.up * (rise + 0.01f);
        Vector3 v = Rb.linearVelocity;
        if (v.y < 0f) Rb.linearVelocity = new Vector3(v.x, 0f, v.z);
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

    // ─── Private Helpers ─────────────────────────────────────────────────────────

    private void CacheComponents()
    {
        Rb               = GetComponent<Rigidbody>();
        _capsuleCollider = GetComponent<CapsuleCollider>();
        _groundChecker   = GetComponent<IGroundChecker>() ?? gameObject.AddComponent<GroundChecker>();
        _healthSystem    = GetComponent<HealthSystem>();
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
        StateMachine.OnStateChanged += state => StateChanged?.Invoke(state);
        IdleState         = new PlayerIdleState(this, StateMachine);
        RunState          = new PlayerRunState(this, StateMachine);
        JumpState         = new PlayerJumpState(this, StateMachine);
        FallState         = new PlayerFallState(this, StateMachine);
        SlideState        = new PlayerSlideState(this, StateMachine);
        VaultState        = new PlayerVaultState(this, StateMachine);
        LedgeGrabState    = new PlayerLedgeGrabState(this, StateMachine);
        LedgeClimbState   = new PlayerLedgeClimbState(this, StateMachine);
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

        // Backpedaling keeps the body facing the camera's forward instead of turning 180°.
        Vector3 facing = MoveDirection;
        if (IsBackpedaling && StateMachine.CurrentState == RunState)
        {
            facing = CameraForwardFlat();
            if (facing == Vector3.zero) return;
        }

        // SmoothDampAngle eases in/out of the turn instead of snapping at a flat angular
        // speed — this is what actually fixes "the camera spins too fast": the camera
        // trails transform.forward, so a snappy instant turn reads as a fast camera swing.
        float targetYaw  = Quaternion.LookRotation(facing, Vector3.up).eulerAngles.y;
        float currentYaw = transform.eulerAngles.y;
        float newYaw     = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref _turnSmoothVelocity, turnSmoothTime);
        transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
    }

    private Vector3 CameraForwardFlat()
    {
        if (_cameraTransform == null) return Vector3.zero;
        Vector3 f = _cameraTransform.forward;
        f.y = 0f;
        return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.zero;
    }

    private bool CanRotateInCurrentState()
    {
        // Lock visual direction during kinematic/parkour/locked combat states
        var current = StateMachine.CurrentState;
        return current != LedgeGrabState  &&
               current != LedgeClimbState &&
               current != VaultState      &&
               current != BlockState      &&
               current != DodgeState      &&
               current != LightAttackState&&
               current != HeavyAttackState;
    }
}
