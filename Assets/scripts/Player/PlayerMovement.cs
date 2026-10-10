using UnityEngine;

/// <summary>
/// Context for the PlayerStateMachine in a free-roam 3D character controller.
/// Owns all state instances and exposes physics helpers consumed by states.
/// Movement is camera-relative on the XZ plane (third-person, over-the-shoulder style):
/// input is projected onto the main camera's flattened forward/right vectors, and the
/// character smoothly rotates to face its current movement direction — except when
/// backpedaling (S / back diagonals without sprint), where it keeps facing the camera's forward.
///
/// The body is a CharacterController, the single owner of the motion (decision P30). The states
/// write <see cref="Velocity"/> at the physics rate; the motor moves the body once per frame in
/// LateUpdate, after the animation: in Idle and Run the motion matching locomotion
/// (PlayerMxMLocomotion, P29) carries it with its root motion, blended with the states' velocity by
/// the motion matching weight; everywhere else the velocity moves it, with gravity. An attack's own
/// step (its clip's root motion) also goes through the motor, so a wall or a target's post stops it
/// (P37). Parkour actions (vault, ledge grab, climb, mantle, drop) are driven by the animation's root
/// motion, warped onto the real contact points (P22, P36): while IsRootMotionDriven that motion goes
/// through CharacterController.Move too (phase 4 of the motion matching plan), with the colliders of the
/// obstacle the action crosses let through (Physics.IgnoreCollision) and everything else solid.
/// Adheres to SRP: orchestrates state routing, never implements game logic directly.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(-100)] // the motor moves the body before the animation's LateUpdate work and the camera
public class PlayerMovement : MonoBehaviour, IDamageModifier
{
    // ─── Inspector Configuration ─────────────────────────────────────────────────
    [Header("Movement Speeds")]
    [Tooltip("Run speed (WASD): the jog of the motion matching mocap (P33).")]
    public float BaseSpeed   = 3.4f;
    [Tooltip("Sprint = BaseSpeed × this value. GDD §5.2: ~+40 % (4.8 m/s, the mocap's sprint, P33).")]
    public float SprintMultiplier = 1.41f;
    [Tooltip("Slide entry speed cap (m/s). The slide keeps the current speed (up to this) and loses it with friction.")]
    public float SlideSpeed  = 7.5f;
    [Tooltip("Vertical take-off speed. 4.5 m/s ≈ 1 m of rise: a human jump, not a superhero one.")]
    public float JumpSpeed   = 4.5f;
    [Tooltip("Running backward speed (S without sprint, facing the camera): the top of the 100STYLE backward takes (P34).")]
    public float BackpedalSpeed = 2.0f;
    [Tooltip("Walking speed (Left Ctrl held), in every direction: the mocap's walk (P33).")]
    public float WalkSpeed = 1.3f;
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
    public float SlideFriction = 2.5f;
    [Tooltip("m/s² of friction when the move input is released: the legs dig in and the slide ends sooner.")]
    public float SlideBrakeFriction = 9f;
    [Tooltip("Momentum is spent below this speed (m/s); it is also the speed kept while a ceiling forces the slide on.")]
    public float SlideMinSpeed = 1.8f;
    [Tooltip("Seconds the body needs to drop to the ground ('Slide Down'); the slide cannot end or chain before.")]
    public float SlideMinTime = 0.35f;
    [Tooltip("Longest slide without a ceiling (s): the end of the valid window.")]
    public float SlideMaxTime = 2f;
    [SerializeField] private LayerMask ceilingLayer = ~0;

    /// <summary>
    /// Slowest entry into a slide: it must still have momentum after the body reaches the ground
    /// (SlideMinSpeed + SlideFriction × SlideMinTime ≈ 2.7 m/s). Running and sprinting slide, walking does not.
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
    public float RollMinSpeed = 2f;
    [Tooltip("Horizontal speed kept by the roll (fraction) and seconds it takes to recover the rest.")]
    public float RollSpeedKept = 0.7f, RollRecovery = 0.45f;

    [Header("Ledge")]
    [Tooltip("Seconds after letting go of a ledge before another can be grabbed.")]
    public float LedgeRegrabDelay = 0.4f;

    [Header("Vault")]
    [Tooltip("Measured vault clips and where each one fits (Assets/Data/Parkour/VaultCatalog, built by Tools → Warrior Woke → Construir Catálogo de Vaults). Without it there is no vault.")]
    [SerializeField] private VaultCatalog vaultCatalog;

    /// <summary>The vault clips the planner chooses from (P36).</summary>
    public VaultCatalog VaultCatalog => vaultCatalog;

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
    /// <summary>The body: the only thing that moves the player (P30).</summary>
    public CharacterController Controller { get; private set; }
    private IGroundChecker  _groundChecker;
    private HealthSystem    _healthSystem;
    private Transform       _cameraTransform;
    public EnvironmentChecker EnvChecker { get; private set; }

    /// <summary>Motion matching locomotion (optional: without it the states' velocity moves the body).</summary>
    public PlayerMxMLocomotion Locomotion { get; private set; }

    /// <summary>
    /// Velocity of the body (m/s). The states write it at the physics rate; after every move it holds
    /// what the body really did, so a wall stops it and a landing ends its fall.
    /// </summary>
    public Vector3 Velocity { get; private set; }

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

    /// <summary>
    /// World Y of the feet: the bottom of the controller's capsule minus its skin (a
    /// CharacterController rests its skin width above the ground).
    /// </summary>
    public float FeetY => Controller != null
        ? transform.position.y + Controller.center.y - Controller.height * 0.5f - Controller.skinWidth - _actionRaise
        : transform.position.y;

    /// <summary>Horizontal speed of the body (m/s).</summary>
    public float HorizontalSpeed => new Vector2(Velocity.x, Velocity.z).magnitude;

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

    /// <summary>Set by PlayerAnimator. Attack and hit reaction states follow their clip and freeze it on a hit (hit stop) through it.</summary>
    public ICombatAnimation CombatAnimation { get; set; }

    /// <summary>
    /// Fraction of an attack's own step (its clip's root motion) the motor applies: the attack shortens it
    /// so the body stops in front of a close target (PlayerAttackState). 1 outside attacks.
    /// </summary>
    public float ActionRootMotionScale { get; set; } = 1f;

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
    /// <summary>True while the controller is off and the animation moves the body (vault, ledge grab, climb).</summary>
    public bool IsRootMotionDriven  { get; private set; }

    /// <summary>
    /// Horizontal velocity the animation is moving the body at (smoothed). Parkour actions hand it
    /// to the body when they end, so the run continues at the speed the clip was really moving.
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
    public PlayerHurtState        HurtState         { get; private set; }

    // ─── Collider Snapshots ───────────────────────────────────────────────────────
    private float   _originalColliderHeight;
    private Vector3 _originalColliderCenter;
    private bool    _wasGrounded;
    private float   _turnTargetYaw;
    private bool    _hasTurnTarget;
    private Vector3 _lastMotorPosition;
    private Vector3    _rootDelta;     // root motion of the frame (from the Animator, OnAnimatorMove)
    private Quaternion _rootRotation = Quaternion.identity;
    private Vector3    _actionDelta;   // an attack's own step this frame (its clip's root motion)
    private bool       _actionMotion;  // an attack reported its root motion this frame: it owns the motion
    private float      _actionYaw, _actionTurnRate;
    private bool       _hasActionTurn;

    // ─── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        CacheComponents();
        SnapshotColliderDimensions();
        BuildStateMachine();
        _lastMotorPosition = transform.position;
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
        UpdateFacingTarget();
        StateMachine.CurrentState?.PhysicsUpdate();
        UpdateLocomotionIntent();
    }

    /// <summary>
    /// The motor: one move per frame, after the animation produced its root motion. In Idle and Run
    /// the motion matching locomotion carries the body with its weight w: the frame's displacement is
    /// the Animator's root motion (motion matching's, the controller's own locomotion plays in place)
    /// plus (1 − w) of the states' velocity, and the turn is the root rotation plus (1 − w) of the
    /// facing turn. Gravity acts on the vertical velocity; the ground holds the body.
    /// </summary>
    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (Controller == null || dt <= 0f || IsRootMotionDriven || !Controller.enabled)
        {
            ClearRootMotion();
            return;
        }

        // Moved from outside (a spawn or a test): the physics scene must see it before moving
        if ((transform.position - _lastMotorPosition).sqrMagnitude > 1e-8f)
            Physics.SyncTransforms();

        float w = Locomotion != null ? Locomotion.Weight : 0f;
        Vector3 v = Velocity;
        Vector3 displacement = new Vector3(v.x, 0f, v.z) * ((1f - w) * dt);
        // An attack owns the motion from its first frame: while motion matching fades out under it, its
        // root motion (the Animator's whole delta: the idle's drift moved the body ±10 cm) is left out,
        // and once it is gone the attack's own step moves the body
        if (w > 0f && !_actionMotion) displacement += new Vector3(_rootDelta.x, 0f, _rootDelta.z);
        else if (w <= 0f) displacement += new Vector3(_actionDelta.x, 0f, _actionDelta.z) * ActionRootMotionScale;
        Vector3 edgeSlide = EdgeSlide();
        displacement += edgeSlide * (EdgeSlideSpeed * dt);
        v.y += Physics.gravity.y * dt;
        if (IsGrounded && v.y < 0f) v.y = Mathf.Max(v.y, -GroundStickSpeed);
        displacement.y = v.y * dt;

        float yaw = transform.eulerAngles.y + (w > 0f && !_actionMotion ? _rootRotation.eulerAngles.y : 0f);
        if (_hasTurnTarget && w < 1f)
        {
            float turned = Mathf.SmoothDampAngle(yaw, _turnTargetYaw, ref _turnSmoothVelocity, CurrentTurnSmoothTime(), MaxTurnRate(HorizontalSpeed), dt);
            yaw += Mathf.DeltaAngle(yaw, turned) * (1f - w);
        }
        // Oriented locomotion keeps the facing the camera asks for: the strafe takes turn a little on
        // their own (10–17° measured by MxMLocomotionProbe), and that drift is corrected under the mocap
        // Free locomotion: the mocap plans its own turns (a pivot, a curve), so only the residue of a
        // turn that motion matching's warping leaves (≤ ResidualHeadingAngle) is closed, more slowly
        if (w > 0f && _hasTurnTarget)
        {
            float error = Mathf.DeltaAngle(yaw, _turnTargetYaw);
            if (Locomotion.IsOriented)
                yaw += error * Mathf.Clamp01(OrientedFacingGain * dt) * w;
            else if (Mathf.Abs(error) < ResidualHeadingAngle && HorizontalSpeed > 0.5f)
                yaw += error * Mathf.Clamp01(ResidualHeadingGain * dt) * w;
        }
        // Attacks and hit reactions turn the body toward their target or the hit at a fixed rate
        if (_hasActionTurn) yaw = Mathf.MoveTowardsAngle(yaw, _actionYaw, _actionTurnRate * dt);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        Vector3 before = transform.position;
        bool groundedBefore = IsGrounded;
        // The step offset climbs curbs on the ground; in the air it let a 1 m jump mount a 1.4 m wall
        // (the controller climbs anything within its step offset of its bottom). "In the air" is the
        // jump and the fall, not a frame without ground under the feet: half-way up a curb the ground
        // check can miss it for a moment, and cutting the step offset there left the body hanging on the
        // curb's edge until it fell. A body resting on an edge with nothing under its center is in the
        // air too: with a step offset it climbed back onto the corner it was sliding off. A lowered body
        // never steps up (ShrinkCollider)
        bool standingTall = Controller.height >= _originalColliderHeight - 0.01f;
        PlayerState state = StateMachine.CurrentState;
        bool airborne = state == JumpState || state == FallState || edgeSlide != Vector3.zero;
        Controller.stepOffset = !airborne && standingTall ? StepHeight : 0f;
        Controller.Move(displacement);
        // What the body really did (CharacterController.velocity would also count a teleport since
        // its last move as speed). Horizontally: moved by the states (w = 0), the real motion, walls
        // included; carried by motion matching (w = 1), its motion smoothed over a few frames (one
        // frame of a blend of poses is noisy, and a jump or a vault takes its momentum from here);
        // while blending, the states' command stays (measuring there would feed the root motion back
        // into the (1 − w) part and multiply it by 1/w)
        Vector3 actual = (transform.position - before) / dt;
        Vector3 horizontal = new Vector3(actual.x, 0f, actual.z);
        Vector3 commanded = new Vector3(v.x, 0f, v.z);
        if (w >= 1f) horizontal = Vector3.Lerp(commanded, horizontal, 1f - Mathf.Exp(-dt / MeasuredVelocitySmoothing));
        else if (w > 0f || horizontal.sqrMagnitude > commanded.sqrMagnitude) horizontal = commanded;
        // A collision only takes speed away: a step climbed or the controller pushing itself out of
        // geometry (standing up next to a bar) moves the body, but it is not speed to keep
        float vertical = actual.y < v.y ? actual.y : v.y;
        if (Controller.isGrounded && v.y < 0f) vertical = v.y;
        Velocity = new Vector3(horizontal.x, vertical, horizontal.z);

        // The controller climbed a step in one move: the model eases after it (PlayerAnimator)
        float rise = transform.position.y - before.y - displacement.y;
        if (groundedBefore && rise > 0.05f) Stepped?.Invoke(rise);

        _lastMotorPosition = transform.position;
        ClearRootMotion();
    }

    /// <summary>Downward speed (m/s) that keeps a grounded controller pressed onto the ground.</summary>
    private const float GroundStickSpeed = 2f;

    /// <summary>Speed (m/s) at which a body resting on an edge with its center over a drop slides off it.</summary>
    private const float EdgeSlideSpeed = 2f;

    /// <summary>
    /// Direction to slide off an edge, or zero. The capsule's rounded bottom can rest on a corner (a
    /// jump that reached a top's edge): the ground check counts it as ground, but the center hangs over
    /// a drop deeper than a step and the animated legs went into the obstacle below the corner. The
    /// body slides off it, over the drop, and falls (a step down is supported: TryStepDown handles it).
    /// </summary>
    private Vector3 EdgeSlide()
    {
        Vector3 feet = new Vector3(transform.position.x, FeetY, transform.position.z);
        if (Physics.Raycast(feet + Vector3.up * 0.1f, Vector3.down, StepHeight + 0.15f, stepLayer, QueryTriggerInteraction.Ignore))
            return Vector3.zero; // supported under the center
        // The capsule's own radius: the body rests on a corner with its rounded bottom, and a thinner
        // probe missed the corner a body 0.30 m from the face was sitting on (it never slid off)
        float r = Controller.radius;
        if (!Physics.SphereCast(feet + Vector3.up * (r + 0.1f), r, Vector3.down, out RaycastHit hit, 0.15f, stepLayer, QueryTriggerInteraction.Ignore))
            return Vector3.zero; // touching nothing: already falling
        Vector3 away = feet - hit.point;
        away.y = 0f;
        return away.sqrMagnitude > 1e-6f ? away.normalized : Vector3.zero;
    }

    /// <summary>Time constant (s) of the smoothing of the velocity measured under motion matching.</summary>
    private const float MeasuredVelocitySmoothing = 0.05f;

    /// <summary>Fraction per second of the facing error removed in oriented locomotion (1/s).</summary>
    private const float OrientedFacingGain = 10f;

    /// <summary>Largest heading error (°) of free locomotion that the motor closes, and how fast (1/s).</summary>
    private const float ResidualHeadingAngle = 30f, ResidualHeadingGain = 5f;

    private void ClearRootMotion()
    {
        _rootDelta = Vector3.zero;
        _rootRotation = Quaternion.identity;
        _actionDelta = Vector3.zero;
        _actionMotion = false;
    }

    /// <summary>
    /// Root motion of this frame from the Animator (OnAnimatorMove, through PlayerAnimatorIK), with
    /// motion matching's warping already applied. The motor uses it in Idle and Run.
    /// </summary>
    public void QueueRootMotion(Vector3 deltaPosition, Quaternion deltaRotation)
    {
        _rootDelta += deltaPosition;
        _rootRotation = deltaRotation * _rootRotation;
    }

    /// <summary>
    /// An attack's own step this frame (the Animator Controller's root motion, OnAnimatorMove through
    /// PlayerAnimator): the motor moves the body by it, scaled by ActionRootMotionScale, with the
    /// controller's collisions (P30, P37).
    /// </summary>
    public void QueueActionRootMotion(Vector3 deltaPosition)
    {
        _actionDelta += deltaPosition;
        _actionMotion = true;
    }

    /// <summary>
    /// Turns the body toward <paramref name="direction"/> at <paramref name="degreesPerSecond"/>, applied by
    /// the motor every frame until StopTurning: attacks face their target, hit reactions the hit.
    /// </summary>
    public void TurnToward(Vector3 direction, float degreesPerSecond)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        _actionYaw = Quaternion.LookRotation(direction, Vector3.up).eulerAngles.y;
        _actionTurnRate = degreesPerSecond;
        _hasActionTurn = true;
    }

    /// <summary>Ends a TurnToward.</summary>
    public void StopTurning() => _hasActionTurn = false;

    /// <summary>Places the body (spawn, checkpoint, tests) at rest: no velocity, the locomotion in its idle, physics in sync.</summary>
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        Velocity = Vector3.zero;
        transform.SetPositionAndRotation(position, rotation);
        Physics.SyncTransforms();
        _lastMotorPosition = position;
        _turnSmoothVelocity = 0f;
        _hasTurnTarget = false;
        _hasActionTurn = false;
        Locomotion?.StopInPlace();
    }

    /// <summary>
    /// Feeds the motion matching locomotion every physics tick: in Idle and Run it gets the intention
    /// (direction, gait speed, oriented or free); in any other state motion matching fades out and the
    /// Animator Controller plays the action.
    /// </summary>
    private void UpdateLocomotionIntent()
    {
        if (Locomotion == null) return;
        PlayerState s = StateMachine.CurrentState;
        bool active = (s == IdleState || s == RunState) && !IsRootMotionDriven;
        bool moving = active && s == RunState && HasMoveInput;
        if (!moving) IsAgainstWall = false;
        float speed = IsWalking ? WalkSpeed : IsBackpedaling ? BackpedalSpeed : IsSprint ? SprintSpeed : BaseSpeed;
        // Oriented movement stays oriented until the body stops: the strafe takes brake facing the
        // same way (switching to the free set mid-stop would turn the body)
        bool oriented = moving ? IsOriented : active && Locomotion.IsOriented && HorizontalSpeed > 0.3f;
        Vector3 direction = moving ? AlongWalls(MoveDirection) : Vector3.zero;
        Locomotion.Drive(active, direction, speed * RecoverySpeedScale, oriented, CameraForwardFlat());
    }

    /// <summary>
    /// Lowest and highest point (m above the feet) and radius of the probe that finds a wall in front of
    /// the legs. It starts above a curb (ParkourStandard.StepMaxHeight), which the auto step climbs.
    /// </summary>
    private const float WallProbeBottom = ParkourStandard.StepMaxHeight + 0.05f, WallProbeTop = 1.15f, WallProbeRadius = 0.2f;

    /// <summary>A wall this close (m from the body's center) is in reach of the legs: a running stride reaches ~0.5 m ahead.</summary>
    private const float WallProbeReach = 0.6f;

    /// <summary>Share of the input along the wall below which pushing into it stops instead of running along it.</summary>
    private const float WallSlideMin = 0.5f;

    private bool _stoppedAtWall;

    /// <summary>The move input pushes into a wall in reach of the legs (the locomotion runs along it or stops).</summary>
    public bool IsAgainstWall { get; private set; }

    /// <summary>
    /// A wall in front of the body within <paramref name="reach"/> (m from its center) along
    /// <paramref name="direction"/>, between <paramref name="above"/> and WallProbeTop above the feet,
    /// not a slope and facing the movement. <paramref name="normal"/> is its horizontal normal.
    /// </summary>
    public bool TryFindWall(Vector3 direction, float reach, float above, out RaycastHit hit, out Vector3 normal)
    {
        normal = Vector3.zero;
        hit = default;
        if (direction.sqrMagnitude < 0.0001f) return false;
        Vector3 feet = new Vector3(transform.position.x, FeetY, transform.position.z);
        Vector3 bottom = feet + Vector3.up * (above + WallProbeRadius), top = feet + Vector3.up * (WallProbeTop - WallProbeRadius);
        if (!Physics.CapsuleCast(bottom, top, WallProbeRadius, direction.normalized, out hit, reach - WallProbeRadius, stepLayer, QueryTriggerInteraction.Ignore) ||
            hit.normal.y > 0.5f)
            return false;
        normal = new Vector3(hit.normal.x, 0f, hit.normal.z).normalized;
        return Vector3.Dot(direction, normal) < 0f;
    }

    /// <summary>Height (m above the feet) of the top of the wall found at <paramref name="hit"/> (TryFindWall).</summary>
    public float WallTopAboveFeet(RaycastHit hit, Vector3 normal)
    {
        Vector3 origin = hit.point - normal * 0.05f;
        origin.y = FeetY + 3f;
        return Physics.Raycast(origin, Vector3.down, out RaycastHit top, 3.5f, stepLayer, QueryTriggerInteraction.Ignore)
            ? top.point.y - FeetY : 3f;
    }

    /// <summary>Seconds ahead the air limiter looks, and the gap (m) at which the body comes to rest beside a wall.</summary>
    private const float AirWallLookahead = 0.25f, AirWallRest = 0.05f;

    /// <summary>
    /// In the air: the velocity into a wall that the jump does not clear slows down so the body comes
    /// to rest beside it. A jump at a block that no vault, mantle or ledge fits hit its face, or its
    /// controller rode up the top's corner and stood on it with the legs inside the obstacle. A wall
    /// the jump clears (ClearsWall: over it and landing on its top, not its corner) is left alone, and
    /// so is the approach to a ledge: the body still comes within the hands' reach (LedgeReachAir).
    /// </summary>
    public void LimitAirIntoWalls()
    {
        Vector3 v = Velocity;
        Vector3 h = new Vector3(v.x, 0f, v.z);
        float speed = h.magnitude;
        if (speed < 0.1f ||
            !TryFindWall(h, WallProbeReach + speed * AirWallLookahead, 0f, out RaycastHit hit, out Vector3 normal))
            return;
        float into = -Vector3.Dot(h, normal);
        float gap = Mathf.Max(0f, hit.distance + WallProbeRadius - Controller.radius - Controller.skinWidth - AirWallRest);
        if (ClearsWall(v.y, into, gap, WallTopAboveFeet(hit, normal))) return;
        float maxInto = gap / AirWallLookahead;
        if (into > maxInto)
        {
            h += normal * (into - maxInto);
            Velocity = new Vector3(h.x, v.y, h.z);
        }
    }

    /// <summary>How far (m) past a top's edge the body's center must come down for a jump to land on it (less is its corner).</summary>
    private const float MinLandingOverTop = 0.15f;

    /// <summary>
    /// The flight (rising <paramref name="vy"/>, moving <paramref name="into"/> toward a face <paramref name="gap"/> m
    /// away) clears a top <paramref name="top"/> m above the feet: the feet are above it when the body reaches
    /// the face, and when they come back down to it the center is well over it (not on its corner).
    /// </summary>
    private bool ClearsWall(float vy, float into, float gap, float top)
    {
        if (into < 0.1f) return true; // not really moving into it
        float g = -Physics.gravity.y;
        // When the feet rise above the top (with a margin; 0 if they already are), and when they come
        // back down to it
        float hUp = top + AirWallRest;
        float up = 0f;
        if (hUp > 0f)
        {
            float disc = vy * vy - 2f * g * hUp;
            if (disc < 0f) return false; // the feet never get above it
            up = (vy - Mathf.Sqrt(disc)) / g;
        }
        float discDown = vy * vy - 2f * g * top;
        if (discDown < 0f) return false;
        float down = (vy + Mathf.Sqrt(discDown)) / g;
        float overTop = into * down - (gap + Controller.radius + Controller.skinWidth + AirWallRest);
        return up <= gap / into && overTop >= MinLandingOverTop;
    }

    /// <summary>
    /// The locomotion's direction next to a wall. The controller (0.35 m radius) stops the body, but a
    /// stride reaches further and the legs went 4–10 cm into the wall while the mocap kept running in
    /// place. Pushing into a wall at an angle runs along it; head-on, the body stops at it, and once it
    /// touches it the locomotion goes straight to its idle instead of playing a braking take there.
    /// </summary>
    private Vector3 AlongWalls(Vector3 direction)
    {
        if (!TryFindWall(direction, WallProbeReach, WallProbeBottom, out RaycastHit hit, out Vector3 normal))
        {
            _stoppedAtWall = false;
            IsAgainstWall = false;
            return direction;
        }

        IsAgainstWall = true;
        float into = Vector3.Dot(direction, normal);
        Vector3 along = direction - normal * into;
        if (along.magnitude >= WallSlideMin)
        {
            _stoppedAtWall = false;
            return along.normalized;
        }

        bool touching = hit.distance + WallProbeRadius < Controller.radius + Controller.skinWidth + 0.05f;
        if (touching && !_stoppedAtWall && HorizontalSpeed > 0.5f) Locomotion.StopInPlace();
        _stoppedAtWall |= touching;
        return Vector3.zero;
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
        if (amount > 0 && ReactsToHit())
        {
            HurtState.Prepare(amount, source);
            StateMachine.ChangeState(HurtState);
        }
    }

    /// <summary>
    /// A hit interrupts what the body does on the ground: standing, moving, attacking, the end of a dodge
    /// (its invulnerability already spent) or another reaction; and the guard, if the hit came from
    /// behind. In the air, crouched, sliding or during a parkour action the action goes on.
    /// </summary>
    private bool ReactsToHit()
    {
        PlayerState s = StateMachine?.CurrentState;
        if (s == null || IsRootMotionDriven || !IsGrounded || (_healthSystem != null && _healthSystem.IsDead)) return false;
        if (s == BlockState) return !BlockState.LastHitBlocked;
        return s == IdleState || s == RunState || s == LightAttackState || s == HeavyAttackState || s == DodgeState || s == HurtState;
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
            Vector3 v = Velocity;
            float kept = LandedWithRoll ? RollSpeedKept : Mathf.Lerp(0.9f, HardLandingSpeedKept, LandingSeverity);
            if (LandedWithRoll) _recoveryDuration = RollRecovery;
            Velocity = new Vector3(v.x * kept, v.y, v.z * kept);
        }
    }

    // ─── Physics Helpers ─────────────────────────────────────────────────────────

    /// <summary>Sets horizontal (X/Z) and vertical (Y) velocity independently.</summary>
    public void SetVelocity(Vector3 horizontalVelocity, float verticalVelocity)
    {
        Velocity = new Vector3(horizontalVelocity.x, verticalVelocity, horizontalVelocity.z);
    }

    /// <summary>Zeroes horizontal (X/Z) velocity and sets the vertical (Y) velocity.</summary>
    public void StopHorizontal(float verticalVelocity)
    {
        Velocity = new Vector3(0f, verticalVelocity, 0f);
    }

    /// <summary>
    /// Moves the horizontal velocity toward <paramref name="targetHorizontal"/> using Acceleration
    /// (speeding up) or Deceleration (slowing down). Keeps the vertical velocity.
    /// </summary>
    public void AccelerateHorizontal(Vector3 targetHorizontal)
    {
        Vector3 v = Velocity;
        Vector3 current = new Vector3(v.x, 0f, v.z);
        float rate = targetHorizontal.sqrMagnitude >= current.sqrMagnitude ? Acceleration : Deceleration;
        Vector3 next = Vector3.MoveTowards(current, targetHorizontal, rate * Time.fixedDeltaTime);
        Velocity = new Vector3(next.x, v.y, next.z);
    }

    /// <summary>
    /// Ground locomotion with momentum: the velocity follows the body's facing instead of jumping
    /// to the input direction, so a turn curves the run (no sideways skating) and its speed drops
    /// with the sharpness of the turn. Input far behind the facing (a reversal) brakes first, the way a
    /// runner plants a foot, and the body turns while it slows down. Keeps the vertical velocity.
    /// </summary>
    public void AccelerateAlongFacing(Vector3 desiredDirection, float targetSpeed)
    {
        Vector3 v = Velocity;
        Vector3 current = new Vector3(v.x, 0f, v.z);
        Vector3 facing = transform.forward;
        facing.y = 0f;
        facing.Normalize();

        float angle = Vector3.Angle(facing, desiredDirection);
        float target = angle >= pivotAngle ? 0f : targetSpeed * Mathf.Lerp(1f, 0.6f, angle / pivotAngle);

        float speed = current.magnitude;
        Vector3 dir = speed > 0.05f ? current / speed : facing;
        dir = Vector3.RotateTowards(dir, facing, MaxTurnRate(speed) * 1.2f * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);
        speed = Mathf.MoveTowards(speed, target, (target >= speed ? Acceleration : Deceleration) * Time.fixedDeltaTime);
        Velocity = new Vector3(dir.x * speed, v.y, dir.z * speed);
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

    /// <summary>Height (m above the feet) and radius of the probe that finds a bar or a low ceiling a standing body would hit.</summary>
    private const float OverheadProbeHeight = 1.35f, OverheadProbeRadius = 0.2f;

    /// <summary>
    /// A bar or a low ceiling ahead along <paramref name="direction"/> within <paramref name="maxDistance"/>
    /// that a standing body would hit but a sliding one passes under (free space at slide height up to
    /// and under it): a slide obstacle. <paramref name="distance"/> is how far its front is (m from the center).
    /// </summary>
    public bool TryFindSlideObstacle(Vector3 direction, float maxDistance, out float distance)
    {
        distance = 0f;
        if (direction.sqrMagnitude < 0.0001f) return false;
        direction.y = 0f;
        direction.Normalize();
        Vector3 origin = new Vector3(transform.position.x, FeetY + OverheadProbeHeight, transform.position.z);
        if (!Physics.SphereCast(origin, OverheadProbeRadius, direction, out RaycastHit hit, maxDistance, ceilingLayer, QueryTriggerInteraction.Ignore) ||
            hit.normal.y < -0.5f)
            return false;
        distance = hit.distance + OverheadProbeRadius;
        // Under it there must be room to slide: at least through its front part
        return SlideClearance(direction, distance + 0.5f) >= distance + 0.5f;
    }

    /// <summary>
    /// Distance (m) a slide entered at <paramref name="speed"/> covers before its momentum is spent
    /// (friction with the input held; under a ceiling it goes on at SlideMinSpeed).
    /// </summary>
    public float SlideReach(float speed) =>
        Mathf.Max(0f, speed * speed - SlideMinSpeed * SlideMinSpeed) / (2f * SlideFriction);

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
        Vector3 v = Velocity;
        Vector3 current = new Vector3(v.x, 0f, v.z);
        Vector3 next = HasMoveInput
            ? Vector3.MoveTowards(current, targetHorizontal, AirAcceleration * Time.fixedDeltaTime)
            : Vector3.MoveTowards(current, Vector3.zero, AirDrag * Time.fixedDeltaTime);
        Velocity = new Vector3(next.x, v.y, next.z);
    }

    /// <summary>Turns the character instantly to face <paramref name="direction"/> (XZ). Used at the start of attacks.</summary>
    public void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;
        Quaternion rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = rotation;
        _turnSmoothVelocity = 0f;
        _hasTurnTarget = false;
        _hasActionTurn = false;
    }

    // ─── Root Motion (parkour) ───────────────────────────────────────────────────

    /// <summary>
    /// How much (m) the capsule's bottom rises during a parkour action. The vault's warper lowers the body
    /// up to 0.39 m for a top lower than the clip's (the catalog's lift) while the legs are off the ground:
    /// a full capsule stopped on the floor and the hand landed 20–33 cm over its point. The torso and the
    /// head still collide. FeetY discounts it.
    /// </summary>
    private const float ActionColliderRaise = 0.5f;

    private float _actionRaise;

    /// <summary>The obstacle colliders the body passes through during the current action.</summary>
    private readonly System.Collections.Generic.List<Collider> _passThrough = new System.Collections.Generic.List<Collider>(8);
    private static readonly System.Collections.Generic.List<Collider> ObstacleParts = new System.Collections.Generic.List<Collider>(8);

    /// <summary>
    /// Hands the body to the animation: ApplyRootMotion / SetRootMotionPose move it with the clip's
    /// (warped) motion through the controller. The action crosses <paramref name="face"/> and
    /// <paramref name="top"/> (and the rest of their obstacle): the controller lets them through until
    /// EndRootMotion; anything else (a wall behind the landing, a low ceiling) still stops the body.
    /// The controller does not climb steps during an action (its root motion places the feet), and its
    /// capsule covers the torso and the head (ActionColliderRaise).
    /// Called again by a chained action (the climb after the hang), it adds that action's obstacle.
    /// </summary>
    public void BeginRootMotion(Collider face, Collider top)
    {
        PassThrough(face);
        PassThrough(top);
        if (IsRootMotionDriven) return;
        Vector3 v = Velocity;
        RootMotionVelocity = new Vector3(v.x, 0f, v.z); // until the clip reports its own
        StopHorizontal(0f);
        Controller.stepOffset = 0f;
        _actionRaise = ActionColliderRaise;
        Controller.height = _originalColliderHeight - _actionRaise;
        Controller.center = _originalColliderCenter + Vector3.up * (_actionRaise * 0.5f);
        IsRootMotionDriven = true;
        if ((transform.position - _lastMotorPosition).sqrMagnitude > 1e-8f) Physics.SyncTransforms();
        _lastMotorPosition = transform.position;
    }

    /// <summary>
    /// Lets the body through <paramref name="part"/> and the other colliders of its obstacle (a
    /// standard obstacle can be made of several boxes).
    /// </summary>
    private void PassThrough(Collider part)
    {
        if (part == null || Controller == null) return;
        ParkourObstacle obstacle = part.GetComponentInParent<ParkourObstacle>();
        ObstacleParts.Clear();
        if (obstacle != null) obstacle.GetComponentsInChildren(ObstacleParts);
        else ObstacleParts.Add(part);
        foreach (Collider c in ObstacleParts)
        {
            if (c == null || c.isTrigger || _passThrough.Contains(c)) continue;
            Physics.IgnoreCollision(Controller, c, true);
            _passThrough.Add(c);
        }
    }

    /// <summary>
    /// Moves the body by an animation delta (called by PlayerAnimator from OnAnimatorMove, once per
    /// frame) through the controller. MatchTarget reads the real position every frame, so a body that
    /// something stopped is warped on from where it really is.
    /// </summary>
    public void ApplyRootMotion(Vector3 deltaPosition, Quaternion deltaRotation)
    {
        if (!IsRootMotionDriven) return;
        Vector3 moved = MoveBody(deltaPosition);
        if (Time.deltaTime > 0f)
        {
            Vector3 frame = new Vector3(moved.x, 0f, moved.z) / Time.deltaTime;
            RootMotionVelocity = Vector3.Lerp(RootMotionVelocity, frame, 0.3f);
        }
        float yaw = (deltaRotation * transform.rotation).eulerAngles.y;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    /// <summary>
    /// Moves the body to where an action's warper computed it for this frame (the vault, P36), through
    /// the controller like ApplyRootMotion; the motion feeds RootMotionVelocity so the run continues at
    /// the speed the body really carried.
    /// </summary>
    public void SetRootMotionPose(Vector3 position, Quaternion rotation)
    {
        if (!IsRootMotionDriven) return;
        Vector3 moved = MoveBody(position - transform.position);
        if (Time.deltaTime > 0f)
            RootMotionVelocity = Vector3.Lerp(RootMotionVelocity, new Vector3(moved.x, 0f, moved.z) / Time.deltaTime, 0.3f);
        transform.rotation = rotation;
    }

    /// <summary>One move of the controller during an action; returns what the body really moved.</summary>
    private Vector3 MoveBody(Vector3 delta)
    {
        // Placed from outside since the last move (an action's fallback, a test): the physics scene must see it
        if ((transform.position - _lastMotorPosition).sqrMagnitude > 1e-8f) Physics.SyncTransforms();
        Vector3 before = transform.position;
        if (delta.sqrMagnitude > 0f) Controller.Move(delta);
        _lastMotorPosition = transform.position;
        return transform.position - before;
    }

    /// <summary>Gives the body back to the motor at its current pose, with the obstacle solid again.</summary>
    public void EndRootMotion()
    {
        if (!IsRootMotionDriven) return;
        IsRootMotionDriven = false;
        foreach (Collider c in _passThrough)
            if (c != null) Physics.IgnoreCollision(Controller, c, false);
        _passThrough.Clear();
        _actionRaise = 0f;
        ResetCollider();
        Physics.SyncTransforms();
        _lastMotorPosition = transform.position;
        _turnSmoothVelocity = 0f;
        _hasTurnTarget = false;
    }

    /// <summary>
    /// Step down: walking off a step or curb (up to StepHeight) keeps the feet on the ground instead
    /// of dropping into the Fall state on every step. Steps up are climbed by the controller itself
    /// (its step offset is StepHeight); the motor reports both so the model eases after the body.
    /// </summary>
    public void TryStepDown()
    {
        if (Controller == null || !Controller.enabled || IsRootMotionDriven || IsGrounded || Velocity.y > 0.1f) return;

        float feet = FeetY;
        Vector3 origin = new Vector3(transform.position.x, feet + 0.05f, transform.position.z);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, StepHeight + 0.1f, stepLayer, QueryTriggerInteraction.Ignore))
            return;
        if (hit.normal.y < 0.7f) return;

        float drop = feet - hit.point.y;
        if (drop <= 0.02f || drop > StepHeight) return;

        Controller.Move(Vector3.down * (drop + 0.01f));
        _lastMotorPosition = transform.position;
        Vector3 v = Velocity;
        if (v.y < 0f) Velocity = new Vector3(v.x, 0f, v.z);
        Stepped?.Invoke(-drop);
        _groundChecker?.CheckGrounded();
    }

    /// <summary>Shrinks the collider keeping its bottom on the ground (the body crouches, it does not float or sink).</summary>
    public void ShrinkCollider(float factor)
    {
        if (Controller == null) return;
        float height = _originalColliderHeight * factor;
        // The controller climbs a step by sweeping up its step offset first: a lowered body (slide,
        // crouch) under a bar or a tunnel would hit it in that sweep, so it does not step up
        Controller.stepOffset = 0f;
        Controller.height = height;
        Controller.center = new Vector3(
            _originalColliderCenter.x,
            _originalColliderCenter.y - (_originalColliderHeight - height) * 0.5f,
            _originalColliderCenter.z);
    }

    public void ResetCollider()
    {
        if (Controller == null) return;
        Controller.height = _originalColliderHeight;
        Controller.center = _originalColliderCenter;
        Controller.stepOffset = StepHeight;
    }

    /// <summary>True if something is over the head of a standing body (measured from the feet).</summary>
    public bool HasCeilingOverhead()
    {
        if (Controller == null) return false;
        // The whole standing body, not a ray up its middle: a bar just ahead of the chest also stops it
        float r = Controller.radius * 0.95f;
        Vector3 feet = new Vector3(transform.position.x, FeetY, transform.position.z);
        Vector3 bottom = feet + Vector3.up * (r + StepHeight * 0.5f);
        Vector3 top = feet + Vector3.up * Mathf.Max(r + StepHeight * 0.5f, _originalColliderHeight + Controller.skinWidth - r);
        return Physics.CheckCapsule(bottom, top, r, ceilingLayer, QueryTriggerInteraction.Ignore);
    }

    // ─── Private Helpers ─────────────────────────────────────────────────────────

    private void CacheComponents()
    {
        Controller       = GetComponent<CharacterController>();
        Controller.stepOffset = StepHeight;
        Locomotion       = GetComponent<PlayerMxMLocomotion>();
        _groundChecker   = GetComponent<IGroundChecker>() ?? gameObject.AddComponent<GroundChecker>();
        _healthSystem    = GetComponent<HealthSystem>();
        EnvChecker       = GetComponent<EnvironmentChecker>() ?? gameObject.AddComponent<EnvironmentChecker>();

        if (Camera.main != null)
            _cameraTransform = Camera.main.transform;
    }

    private void SnapshotColliderDimensions()
    {
        if (Controller == null) return;
        _originalColliderHeight = Controller.height;
        _originalColliderCenter = Controller.center;
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
        HurtState         = new PlayerHurtState(this, StateMachine);
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
    /// The yaw the body turns toward (the motor applies it every frame, eased with SmoothDampAngle;
    /// faster bodies turn wider): the movement direction, or the camera's forward in oriented
    /// locomotion (walking, or running backward), which moves under the body instead of turning it.
    /// </summary>
    private void UpdateFacingTarget()
    {
        _hasTurnTarget = false;
        if (!faceMovementDirection || MoveDirection == Vector3.zero || IsRootMotionDriven || !CanRotateInCurrentState())
            return;

        Vector3 facing = MoveDirection;
        if (IsOriented && StateMachine.CurrentState == RunState)
        {
            facing = CameraForwardFlat();
            if (facing == Vector3.zero) return;
        }
        _turnTargetYaw = Quaternion.LookRotation(facing, Vector3.up).eulerAngles.y;
        _hasTurnTarget = true;
    }

    private float CurrentTurnSmoothTime()
    {
        float speed01 = SprintSpeed > BaseSpeed ? Mathf.InverseLerp(BaseSpeed, SprintSpeed, HorizontalSpeed) : 0f;
        return Mathf.Lerp(turnSmoothTime, sprintTurnSmoothTime, speed01);
    }

    public Vector3 CameraForwardFlat()
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
               current != HeavyAttackState&&
               current != HurtState;
    }
}
