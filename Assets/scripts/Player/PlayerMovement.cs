using UnityEngine;

/// <summary>
/// Context for the PlayerStateMachine in a free-roam 3D character controller.
/// Owns all state instances and exposes physics helpers consumed by states.
/// Movement is camera-relative on the XZ plane (third-person, over-the-shoulder style):
/// input is projected onto the main camera's flattened forward/right vectors, and the
/// character smoothly rotates to face its current movement direction — except when
/// backpedaling (S / back diagonals without sprint), where it keeps facing the camera's forward.
/// Parkour actions (vault, ledge grab, climb) are driven by the animation's root motion: while
/// IsRootMotionDriven the body is kinematic and PlayerAnimator moves it with the clip, warped onto
/// the real contact points (decision P22). Everything else is moved by the states through the Rigidbody.
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
    [Tooltip("Slide entry speed cap (m/s). The slide keeps the current speed (up to this) and loses it with friction.")]
    public float SlideSpeed  = 7.5f;
    [Tooltip("Vertical take-off speed. 4.5 m/s ≈ 1 m of rise: a human jump, not a superhero one.")]
    public float JumpSpeed   = 4.5f;
    [Tooltip("Running backward speed (S without sprint, facing the camera): the measured pace of the RunBackward clip.")]
    public float BackpedalSpeed = 3.5f;
    [Tooltip("Walking speed (Left Ctrl held), in every direction: the measured pace of the Walk clip.")]
    public float WalkSpeed = 1.7f;
    [Tooltip("Crouched walking speed.")]
    public float CrouchSpeed = 1.0f;
    [Tooltip("Collider height while crouched, as a fraction of the standing height.")]
    public float CrouchHeightFactor = 0.62f;

    public float SprintSpeed => BaseSpeed * SprintMultiplier;

    [Header("Acceleration")]
    [Tooltip("m/s² used to speed up on the ground. The locomotion blend passes Idle → Walk → Jog → Run while accelerating.")]
    public float Acceleration = 10f;
    [Tooltip("m/s² used to slow down on the ground (releasing input, or a lower target speed).")]
    public float Deceleration = 13f;
    [Tooltip("m/s² of steering in the air. Momentum is kept: without input the horizontal speed barely changes.")]
    public float AirAcceleration = 4f;
    [Tooltip("m/s² of horizontal drag in the air when there is no input.")]
    public float AirDrag = 0.5f;

    [Header("Sliding")]
    [Tooltip("m/s² of friction while sliding with the move input held.")]
    public float SlideFriction = 4f;
    [Tooltip("m/s² of friction when the move input is released: the legs dig in and the slide ends sooner.")]
    public float SlideBrakeFriction = 9f;
    [Tooltip("Momentum is spent below this speed (m/s); it is also the speed kept while a ceiling forces the slide on.")]
    public float SlideMinSpeed = 2.5f;
    [Tooltip("Seconds the body needs to drop to the ground ('Slide Down'); the slide cannot end or chain before.")]
    public float SlideMinTime = 0.35f;
    [Tooltip("Longest slide without a ceiling (s): the end of the valid window.")]
    public float SlideMaxTime = 2f;
    [SerializeField] private LayerMask ceilingLayer = ~0;

    /// <summary>
    /// Slowest entry into a slide: it must still have momentum after the body reaches the ground
    /// (SlideMinSpeed + SlideFriction × SlideMinTime ≈ 3.9 m/s). Walking or jogging does not slide.
    /// </summary>
    public float SlideMinEntrySpeed => SlideMinSpeed + SlideFriction * SlideMinTime;

    /// <summary>Height (m above the feet) and radius of the probe that looks for obstacles along a slide.</summary>
    public const float SlideProbeHeight = 0.45f, SlideProbeRadius = 0.3f;

    [Header("Auto Step (adapted from Dynamic Parkour System, MIT)")]
    /// <summary>Highest rise (m) climbed or descended automatically while moving on the ground (ParkourStandard).</summary>
    public float StepHeight => ParkourStandard.StepMaxHeight;
    [SerializeField] private LayerMask stepLayer = ~0;

    [Header("Falling and Landing")]
    [Tooltip("Seconds without ground under Idle/Run before switching to the Fall state (absorbs small steps and slopes).")]
    public float FallGraceTime = 0.15f;
    [Tooltip("Drops lower than this (m) land without a landing animation or speed loss.")]
    public float SoftLandingHeight = 0.6f;
    [Tooltip("Drop (m) that produces the heaviest landing.")]
    public float HardLandingHeight = 3.2f;
    [Tooltip("Horizontal speed kept right after the heaviest landing (fraction).")]
    public float HardLandingSpeedKept = 0.3f;
    [Tooltip("Seconds the heaviest landing takes to recover full speed. Control is never blocked.")]
    public float HardLandingRecovery = 0.7f;
    [Tooltip("A landing at least this heavy, at RollMinSpeed or more with input, is absorbed with a roll.")]
    public float RollMinSeverity = 0.6f;
    [Tooltip("Horizontal speed (m/s) needed to roll out of a heavy landing.")]
    public float RollMinSpeed = 3f;
    [Tooltip("Horizontal speed kept by the roll (fraction) and seconds it takes to recover the rest.")]
    public float RollSpeedKept = 0.7f, RollRecovery = 0.45f;

    [Header("Ledge")]
    [Tooltip("Seconds after letting go of a ledge before another can be grabbed.")]
    public float LedgeRegrabDelay = 0.4f;

    [Header("Facing")]
    [SerializeField] private bool faceMovementDirection = true;
    [Tooltip("Turn smoothing time (s) at walking/running speed. Lower = snappier, higher = heavier.")]
    [SerializeField] private float turnSmoothTime = 0.12f;
    [Tooltip("Turn smoothing time (s) at sprint speed: a fast body turns wider.")]
    [SerializeField] private float sprintTurnSmoothTime = 0.2f;
    [Tooltip("Fastest turn (°/s) at low speed.")]
    [SerializeField] private float maxTurnRateStill = 720f;
    [Tooltip("Largest sideways acceleration (m/s²) a running body can take in a turn (~0.9 g). It limits the turn rate at speed (rate = this / speed), so a sharp turn at a sprint has to brake first.")]
    [SerializeField] private float maxLateralAcceleration = 9f;
    [Tooltip("Input this far (°) behind the facing is a reversal: the runner brakes and turns before accelerating again.")]
    [SerializeField] private float pivotAngle = 135f;

    private float _turnSmoothVelocity;

    [Header("Input Deadzone")]
    [SerializeField] private float moveInputDeadzone = 0.1f;

    // ─── Cached Components (set once in Awake, never in the loop) ────────────────
    public Rigidbody        Rb         { get; private set; }
    private CapsuleCollider _capsuleCollider;
    private IGroundChecker  _groundChecker;
    private HealthSystem    _healthSystem;
    private Transform       _cameraTransform;
    private RigidbodyInterpolation _interpolation;
    public EnvironmentChecker EnvChecker { get; private set; }

    // ─── Movement Input State ─────────────────────────────────────────────────────
    public float InputX         { get; private set; }
    public float InputZ         { get; private set; }
    public bool  JumpTriggered  { get; private set; }
    public bool  IsSprintHeld   { get; private set; }
    public bool  IsWalkHeld     { get; private set; }
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

    /// <summary>Highest feet height reached since the body last left the ground.</summary>
    public float AirPeakFeetY       { get; private set; }

    /// <summary>
    /// Moving backward while facing the camera: back input (S or back diagonals) without sprint.
    /// Sprinting backward pivots and runs the other way.
    /// </summary>
    public bool  IsBackpedaling     => HasMoveInput && InputZ < -0.1f && !IsSprint;

    /// <summary>Walking gait (Left Ctrl held, not sprinting).</summary>
    public bool  IsWalking          => IsWalkHeld && !IsSprint;

    /// <summary>
    /// Oriented locomotion: the body keeps facing the camera's direction and moves in any direction
    /// under it (strafe, diagonals, backward). Walking is always oriented; running is oriented only
    /// when the input goes backward. Otherwise the body turns toward where it moves.
    /// </summary>
    public bool  IsOriented         => IsWalking || IsBackpedaling;

    /// <summary>The last landing was absorbed with a roll (fast, heavy landing with input).</summary>
    public bool  LandedWithRoll     { get; private set; }

    /// <summary>Half of the standing collider height: distance from the origin (torso center) to the feet.</summary>
    public float StandingHalfHeight => _originalColliderHeight * 0.5f;

    /// <summary>World Y of the bottom of the collider (the feet).</summary>
    public float FeetY => _capsuleCollider != null ? _capsuleCollider.bounds.min.y : transform.position.y;

    /// <summary>Horizontal speed of the body (m/s).</summary>
    public float HorizontalSpeed
    {
        get
        {
            Vector3 v = Rb.linearVelocity;
            return new Vector2(v.x, v.z).magnitude;
        }
    }

    /// <summary>Obstacle found by the last vault check (set right before entering VaultState).</summary>
    public VaultInfo PendingVault   { get; set; }

    /// <summary>Ledge found by the last ledge check (set right before entering LedgeGrabState).</summary>
    public LedgeInfo CurrentLedge   { get; set; }

    /// <summary>Time the player last let go of a ledge (for LedgeRegrabDelay).</summary>
    public float LedgeReleaseTime   { get; set; } = -10f;

    /// <summary>Camera-relative, normalized movement direction on the XZ plane for this tick (zero when no input).</summary>
    public Vector3 MoveDirection    { get; private set; } = Vector3.zero;

    public bool  IsGrounded         => _groundChecker != null && _groundChecker.IsGrounded;

    /// <summary>Set by PlayerAnimator. Parkour states read their clip's progress from it.</summary>
    public IParkourAnimationProgress ParkourAnimation { get; set; }

    /// <summary>Normalized progress of the current parkour clip, or −1 if unknown.</summary>
    public float ParkourProgress => ParkourAnimation != null ? ParkourAnimation.Progress : -1f;

    /// <summary>Fired after an auto step moves the body up (positive) or down (negative), in m. Used to smooth the model visually.</summary>
    public event System.Action<float> Stepped;

    // ─── Landing ──────────────────────────────────────────────────────────────────
    /// <summary>0 (no landing) … 1 (heaviest landing) of the last landing.</summary>
    public float LandingSeverity    { get; private set; }
    /// <summary>Drop height (m) of the last landing.</summary>
    public float LastDropHeight     { get; private set; }
    private float _landingTime = -10f;
    private float _recoveryDuration;

    /// <summary>
    /// Fraction of the target ground speed allowed while recovering from a landing (1 = recovered).
    /// The legs absorb the impact: speed comes back gradually, control is never blocked.
    /// </summary>
    public float RecoverySpeedScale
    {
        get
        {
            if (_recoveryDuration <= 0f) return 1f;
            float t = (Time.time - _landingTime) / _recoveryDuration;
            if (t >= 1f) return 1f;
            float min = Mathf.Lerp(1f, HardLandingSpeedKept, LandingSeverity);
            return Mathf.Lerp(min, 1f, t * t);
        }
    }

    // ─── Root motion (parkour) ────────────────────────────────────────────────────
    /// <summary>True while the body is kinematic and moved by the animation (vault, ledge grab, climb).</summary>
    public bool IsRootMotionDriven  { get; private set; }

    /// <summary>
    /// Horizontal velocity the animation is moving the body at (smoothed). Parkour actions hand it
    /// to the Rigidbody when they end, so the run continues at the speed the clip was really moving.
    /// </summary>
    public Vector3 RootMotionVelocity { get; private set; }

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
    public PlayerCrouchState     CrouchState        { get; private set; }
    public PlayerVaultState      VaultState         { get; private set; }
    public PlayerMantleState     MantleState        { get; private set; }
    public PlayerLedgeGrabState  LedgeGrabState     { get; private set; }
    public PlayerLedgeClimbState LedgeClimbState    { get; private set; }
    public PlayerLedgeDropState  LedgeDropState     { get; private set; }
    public PlayerLightAttackState LightAttackState  { get; private set; }
    public PlayerHeavyAttackState HeavyAttackState  { get; private set; }
    public PlayerBlockState       BlockState        { get; private set; }
    public PlayerDodgeState       DodgeState        { get; private set; }

    // ─── Collider Snapshots ───────────────────────────────────────────────────────
    private float   _originalColliderHeight;
    private Vector3 _originalColliderCenter;
    private bool    _wasGrounded;
    private float   _lastStepUpTime = -10f;

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
        bool  walkHeld,
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
        IsWalkHeld            = walkHeld;
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
        TrackAirPeak();
        UpdateMoveDirection();   // pure logic — no transform writes here

        StateMachine.CurrentState?.LogicUpdate();
    }

    private void TrackAirPeak()
    {
        bool grounded = IsGrounded || IsRootMotionDriven;
        if (!grounded)
            AirPeakFeetY = _wasGrounded ? FeetY : Mathf.Max(AirPeakFeetY, FeetY);
        _wasGrounded = grounded;
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

    // ─── Landing ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by the Fall state when it touches the ground. The drop since the highest point decides
    /// how heavy the landing is: part of the horizontal speed is absorbed at once and the rest comes
    /// back over the recovery time (Fall → Landing → Recovery → Locomotion).
    /// </summary>
    public void RegisterLanding()
    {
        LastDropHeight  = Mathf.Max(0f, AirPeakFeetY - FeetY);
        LandingSeverity = LastDropHeight < SoftLandingHeight
            ? 0f
            : Mathf.Clamp01(Mathf.InverseLerp(SoftLandingHeight, HardLandingHeight, LastDropHeight) * 0.85f + 0.15f);

        _landingTime      = Time.time;
        _recoveryDuration = LandingSeverity > 0f ? Mathf.Lerp(0.15f, HardLandingRecovery, LandingSeverity) : 0f;

        // A heavy landing at speed with the run held turns into a roll: the impact goes into the
        // forward motion instead of the legs, so less speed is lost and it comes back sooner
        LandedWithRoll = LandingSeverity >= RollMinSeverity && HorizontalSpeed >= RollMinSpeed && HasMoveInput;

        if (LandingSeverity > 0f)
        {
            Vector3 v = Rb.linearVelocity;
            float kept = LandedWithRoll ? RollSpeedKept : Mathf.Lerp(0.9f, HardLandingSpeedKept, LandingSeverity);
            if (LandedWithRoll) _recoveryDuration = RollRecovery;
            Rb.linearVelocity = new Vector3(v.x * kept, v.y, v.z * kept);
        }
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

    /// <summary>
    /// Ground locomotion with momentum: the velocity follows the body's facing instead of jumping
    /// to the input direction, so a turn curves the run (no sideways skating) and its speed drops
    /// with the sharpness of the turn. Input far behind the facing (a reversal) brakes first, the way a
    /// runner plants a foot, and the body turns while it slows down. Keeps the vertical velocity.
    /// </summary>
    public void AccelerateAlongFacing(Vector3 desiredDirection, float targetSpeed)
    {
        Vector3 v = Rb.linearVelocity;
        Vector3 current = new Vector3(v.x, 0f, v.z);
        Vector3 facing = Rb.rotation * Vector3.forward;
        facing.y = 0f;
        facing.Normalize();

        float angle = Vector3.Angle(facing, desiredDirection);
        float target = angle >= pivotAngle ? 0f : targetSpeed * Mathf.Lerp(1f, 0.6f, angle / pivotAngle);

        float speed = current.magnitude;
        Vector3 dir = speed > 0.05f ? current / speed : facing;
        dir = Vector3.RotateTowards(dir, facing, MaxTurnRate(speed) * 1.2f * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);
        speed = Mathf.MoveTowards(speed, target, (target >= speed ? Acceleration : Deceleration) * Time.fixedDeltaTime);
        Rb.linearVelocity = new Vector3(dir.x * speed, v.y, dir.z * speed);
    }

    /// <summary>
    /// Fastest turn (°/s) at <paramref name="speed"/>: quick at low speed, and at speed limited by the
    /// sideways acceleration a body can take (a = speed × turn rate), so a fast run curves wide.
    /// </summary>
    public float MaxTurnRate(float speed)
    {
        return Mathf.Min(maxTurnRateStill, maxLateralAcceleration / Mathf.Max(speed, 0.1f) * Mathf.Rad2Deg);
    }

    /// <summary>
    /// Free distance (m) along <paramref name="direction"/> for a sliding body, up to
    /// <paramref name="maxDistance"/>: a sphere at slide height (it passes under bars and tunnels,
    /// and stops at walls, curbs and obstacles).
    /// </summary>
    public float SlideClearance(Vector3 direction, float maxDistance)
    {
        Vector3 origin = new Vector3(transform.position.x, FeetY + SlideProbeHeight, transform.position.z);
        return Physics.SphereCast(origin, SlideProbeRadius, direction, out RaycastHit hit, maxDistance, ceilingLayer, QueryTriggerInteraction.Ignore)
            ? hit.distance : maxDistance;
    }

    /// <summary>True if the ground under the feet is flat enough to slide on.</summary>
    public bool HasSlideSurface()
    {
        Vector3 origin = new Vector3(transform.position.x, FeetY + 0.2f, transform.position.z);
        return Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 0.5f, ceilingLayer, QueryTriggerInteraction.Ignore) && hit.normal.y > 0.9f;
    }

    /// <summary>
    /// Air control with momentum: input steers the horizontal velocity toward
    /// <paramref name="targetHorizontal"/> at AirAcceleration; without input only a light drag applies,
    /// so a jump keeps the speed it took off with. Keeps the vertical velocity.
    /// </summary>
    public void AccelerateAir(Vector3 targetHorizontal)
    {
        Vector3 v = Rb.linearVelocity;
        Vector3 current = new Vector3(v.x, 0f, v.z);
        Vector3 next = HasMoveInput
            ? Vector3.MoveTowards(current, targetHorizontal, AirAcceleration * Time.fixedDeltaTime)
            : Vector3.MoveTowards(current, Vector3.zero, AirDrag * Time.fixedDeltaTime);
        Rb.linearVelocity = new Vector3(next.x, v.y, next.z);
    }

    /// <summary>Turns the character instantly to face <paramref name="direction"/> (XZ). Used at the start of attacks.</summary>
    public void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        Quaternion rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        Rb.rotation = rotation;
        transform.rotation = rotation;
        _turnSmoothVelocity = 0f;
    }

    // ─── Root Motion (parkour) ───────────────────────────────────────────────────

    /// <summary>
    /// Hands the body to the animation: kinematic, no interpolation, moved by ApplyRootMotion.
    /// Collisions are off for the duration; the action was validated against the geometry before.
    /// </summary>
    public void BeginRootMotion()
    {
        if (IsRootMotionDriven) return;
        Vector3 v = Rb.linearVelocity;
        RootMotionVelocity = new Vector3(v.x, 0f, v.z); // until the clip reports its own
        StopHorizontal(0f);
        Rb.isKinematic   = true;
        Rb.interpolation = RigidbodyInterpolation.None;
        IsRootMotionDriven = true;
    }

    /// <summary>
    /// Moves the body by an animation delta (called by PlayerAnimator from OnAnimatorMove, once per
    /// frame). Writes the transform directly: the body is kinematic, so physics follows it, and
    /// MatchTarget reads the real position every frame.
    /// </summary>
    public void ApplyRootMotion(Vector3 deltaPosition, Quaternion deltaRotation)
    {
        if (!IsRootMotionDriven) return;
        transform.position += deltaPosition;
        if (Time.deltaTime > 0f)
        {
            Vector3 frame = new Vector3(deltaPosition.x, 0f, deltaPosition.z) / Time.deltaTime;
            RootMotionVelocity = Vector3.Lerp(RootMotionVelocity, frame, 0.3f);
        }
        float yaw = (deltaRotation * transform.rotation).eulerAngles.y;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    /// <summary>Gives the body back to physics at its current pose, with the interpolation restored.</summary>
    public void EndRootMotion()
    {
        if (!IsRootMotionDriven) return;
        IsRootMotionDriven = false;
        Rb.position = transform.position;
        Rb.rotation = transform.rotation;
        Rb.isKinematic   = false;
        Rb.interpolation = _interpolation;
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
        _lastStepUpTime = Time.time;
        Stepped?.Invoke(rise + 0.01f);
    }

    /// <summary>
    /// Step down: walking off a step or curb (up to StepHeight) keeps the feet on the ground instead
    /// of dropping into the Fall state on every step.
    /// </summary>
    public void TryStepDown()
    {
        if (_capsuleCollider == null || IsGrounded || Rb.linearVelocity.y > 0.1f) return;
        if (Time.time - _lastStepUpTime < 0.3f) return; // just stepped up: the body is moving onto the step

        float feet = FeetY;
        Vector3 origin = new Vector3(transform.position.x, feet + 0.05f, transform.position.z);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, StepHeight + 0.1f, stepLayer, QueryTriggerInteraction.Ignore))
            return;
        if (hit.normal.y < 0.7f) return;

        float drop = feet - hit.point.y;
        if (drop <= 0.02f || drop > StepHeight) return;

        Rb.position += Vector3.down * drop;
        Vector3 v = Rb.linearVelocity;
        if (v.y < 0f) Rb.linearVelocity = new Vector3(v.x, 0f, v.z);
        Stepped?.Invoke(-drop);
    }

    /// <summary>Shrinks the collider keeping its bottom on the ground (the body crouches, it does not float or sink).</summary>
    public void ShrinkCollider(float factor)
    {
        if (_capsuleCollider == null) return;
        float height = _originalColliderHeight * factor;
        _capsuleCollider.height = height;
        _capsuleCollider.center = new Vector3(
            _originalColliderCenter.x,
            _originalColliderCenter.y - (_originalColliderHeight - height) * 0.5f,
            _originalColliderCenter.z);
    }

    public void ResetCollider()
    {
        if (_capsuleCollider == null) return;
        _capsuleCollider.height = _originalColliderHeight;
        _capsuleCollider.center = _originalColliderCenter;
    }

    /// <summary>True if something is over the head of a standing body (measured from the feet).</summary>
    public bool HasCeilingOverhead()
    {
        if (_capsuleCollider == null) return false;
        Vector3 feet = new Vector3(transform.position.x, FeetY + 0.1f, transform.position.z);
        return Physics.Raycast(feet, Vector3.up, _originalColliderHeight, ceilingLayer, QueryTriggerInteraction.Ignore);
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
        // Free movement on X and Z (camera-relative third person); rotation is driven through
        // MoveRotation, so it stays frozen against physics torques.
        Rb.constraints = RigidbodyConstraints.FreezeRotation;
        _interpolation = Rb.interpolation;
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
        CrouchState       = new PlayerCrouchState(this, StateMachine);
        VaultState        = new PlayerVaultState(this, StateMachine);
        MantleState       = new PlayerMantleState(this, StateMachine);
        LedgeGrabState    = new PlayerLedgeGrabState(this, StateMachine);
        LedgeClimbState   = new PlayerLedgeClimbState(this, StateMachine);
        LedgeDropState    = new PlayerLedgeDropState(this, StateMachine);
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
    /// Smoothly rotates the character to face MoveDirection through Rigidbody.MoveRotation, so the
    /// turn is interpolated between physics steps like the movement. Faster bodies turn wider.
    /// </summary>
    private void ApplyFacingRotation()
    {
        if (!faceMovementDirection || MoveDirection == Vector3.zero || IsRootMotionDriven || !CanRotateInCurrentState())
            return;

        // Oriented locomotion (walking, or running backward) keeps the body facing the camera's
        // forward and moves under it, instead of turning toward the input.
        Vector3 facing = MoveDirection;
        if (IsOriented && StateMachine.CurrentState == RunState)
        {
            facing = CameraForwardFlat();
            if (facing == Vector3.zero) return;
        }

        // SmoothDampAngle eases in/out of the turn instead of snapping at a flat angular speed;
        // the camera trails transform.forward, so a snappy turn reads as a fast camera swing.
        float speed01    = SprintSpeed > BaseSpeed ? Mathf.InverseLerp(BaseSpeed, SprintSpeed, HorizontalSpeed) : 0f;
        float smoothTime = Mathf.Lerp(turnSmoothTime, sprintTurnSmoothTime, speed01);
        float targetYaw  = Quaternion.LookRotation(facing, Vector3.up).eulerAngles.y;
        float currentYaw = Rb.rotation.eulerAngles.y;
        float newYaw     = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref _turnSmoothVelocity, smoothTime, MaxTurnRate(HorizontalSpeed), Time.fixedDeltaTime);
        Rb.MoveRotation(Quaternion.Euler(0f, newYaw, 0f));
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
               current != LedgeDropState  &&
               current != VaultState      &&
               current != MantleState     &&
               current != SlideState      &&
               current != BlockState      &&
               current != DodgeState      &&
               current != LightAttackState&&
               current != HeavyAttackState;
    }
}
