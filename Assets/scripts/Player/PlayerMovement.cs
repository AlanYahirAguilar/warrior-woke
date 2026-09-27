using UnityEngine;

/// <summary>
/// Context for the PlayerStateMachine in a 3D action-platformer.
/// Owns all state instances and exposes physics helpers consumed by states.
/// Enforces Z-axis and full rotation constraints (2.5D plane movement).
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

    // ─── Cached Components (set once in Awake, never in the loop) ────────────────
    public Rigidbody        Rb         { get; private set; }
    private CapsuleCollider _capsuleCollider;
    private IGroundChecker  _groundChecker;
    public EnvironmentChecker EnvChecker { get; private set; }

    // ─── Movement Input State ─────────────────────────────────────────────────────
    public float InputX         { get; private set; }
    public bool  JumpTriggered  { get; private set; }
    public bool  SlideTriggered { get; private set; }

    // ─── Combat Input State ───────────────────────────────────────────────────────
    public bool LightAttackTriggered { get; private set; }
    public bool HeavyAttackTriggered { get; private set; }
    public bool IsBlockHeld          { get; private set; }
    public bool DodgeTriggered       { get; private set; }

    // ─── Runtime State ────────────────────────────────────────────────────────────
    public bool  IsSprint           { get; set; }
    public float FacingDirection    { get; private set; } = 1f;
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
        StateMachine.CurrentState?.PhysicsUpdate();
    }

    // ─── Input Entry Point (called by Player.cs from FixedUpdate) ────────────────

    /// <summary>
    /// Receives all input values from Player.cs and ticks the current state's logic.
    /// </summary>
    public void ProcessMovement(
        float horizontal,
        bool  jumpTriggered,
        bool  slideTriggered,
        bool  lightAttack,
        bool  heavyAttack,
        bool  blockHeld,
        bool  dodgeTriggered)
    {
        InputX               = horizontal;
        JumpTriggered        = jumpTriggered;
        SlideTriggered       = slideTriggered;
        LightAttackTriggered = lightAttack;
        HeavyAttackTriggered = heavyAttack;
        IsBlockHeld          = blockHeld;
        DodgeTriggered       = dodgeTriggered;

        _groundChecker?.CheckGrounded();
        UpdateFacingDirection();

        StateMachine.CurrentState?.LogicUpdate();
    }

    // ─── Physics Helpers ─────────────────────────────────────────────────────────

    public void SetVelocity(float x, float y)
    {
        Rb.linearVelocity = new Vector3(x, y, 0f);
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
        _groundChecker   = GetComponent<IGroundChecker>() ?? gameObject.AddComponent<GroundChecker>();
        EnvChecker       = GetComponent<EnvironmentChecker>() ?? gameObject.AddComponent<EnvironmentChecker>();
    }

    private void SnapshotColliderDimensions()
    {
        if (_capsuleCollider == null) return;
        _originalColliderHeight = _capsuleCollider.height;
        _originalColliderCenter = _capsuleCollider.center;
    }

    private void ConfigureRigidbody()
    {
        Rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
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

    private void UpdateFacingDirection()
    {
        if (!faceMovementDirection || Mathf.Abs(InputX) <= 0.05f) return;

        // Lock visual direction during kinematic/parkour states
        var current = StateMachine.CurrentState;
        if (current == WallJumpState   ||
            current == LedgeGrabState  ||
            current == LedgeClimbState ||
            current == VaultState      ||
            current == BlockState      ||
            current == LightAttackState||
            current == HeavyAttackState) return;

        FacingDirection    = Mathf.Sign(InputX);
        transform.rotation = Quaternion.Euler(0f, FacingDirection > 0 ? 90f : -90f, 0f);
    }
}
