using UnityEngine;

/// <summary>
/// Drives the player's Animation Rigging constraints from the FSM (decision P31), as the presentation
/// layer: the states never touch the rig.
///  - Feet (<see cref="GroundContactConstraint"/>): on the ground the feet follow the terrain at once (a
///    landing must not sink); leaving the ground they fade out, and a parkour action, whose own contacts
///    (PlayerContactIK, MatchTarget) take over the limbs, switches them off immediately. A planted foot
///    locks only in the locomotion, the guard and the crouch (an attack or a slide moves the feet on purpose).
///    The rig also learns how much the animation uses the Humanoid Foot IK: motion matching and the mocap
///    blows do (their retarget is only clean with it), the other clips of the Animator Controller do not.
///  - Look (<see cref="HeadLookConstraint"/>): toward a target in front (the one an attack turns to, or the
///    nearest within LookTargetRange), else toward where the body is about to go while running, else where
///    the camera looks. Off in the air, in parkour, sliding and dodging: the clip owns the head there.
/// The constraints live under the model (Model/ContactRig), built by PlayerAnimationSetup.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerRig : MonoBehaviour
{
    [Tooltip("Speed at which the feet's ground contact fades out when the body leaves the ground (1/s).")]
    [SerializeField] private float groundFadeSpeed = 8f;
    [Tooltip("Seconds the feet take to switch between a clip with Foot IK and one without (the Animator's cross-fade).")]
    [SerializeField] private float footIKBlendTime = 0.12f;

    [Header("Look")]
    [Tooltip("A target within this distance (m) and in front of the body draws the look.")]
    [SerializeField] private float lookTargetRange = 5f;
    [Tooltip("How far to each side (degrees) of the body's facing a target draws the look.")]
    [SerializeField] private float lookTargetAngle = 100f;
    [Tooltip("Height above the feet (m) the eyes look from and at (a target is looked at at the same height).")]
    [SerializeField] private float eyeHeight = 1.6f;
    [Tooltip("Rate (1/s) at which the look direction follows what draws it.")]
    [SerializeField] private float lookFollowRate = 8f;
    [Tooltip("Seconds the look takes to fade in or out.")]
    [SerializeField] private float lookFadeTime = 0.25f;

    private PlayerMovement _movement;
    private Hitbox _hitbox;
    private GroundContactConstraint _feet;
    private HeadLookConstraint _look;
    private float _groundWeight, _lookWeight, _actionFootIK;
    private Vector3 _lookDirection;
    private Transform _nearTarget;
    private float _nextTargetSearch;

    /// <summary>The feet constraint (null if the rig is not built).</summary>
    public GroundContactConstraint Feet => _feet;

    /// <summary>The look constraint (null if the rig is not built).</summary>
    public HeadLookConstraint Look => _look;

    /// <summary>Where the look is going (world, horizontal unless a target is above or below), for tests.</summary>
    public Vector3 LookDirection => _lookDirection;

    private void Awake()
    {
        _movement = GetComponent<PlayerMovement>();
        _hitbox = GetComponent<Hitbox>();
        _feet = GetComponentInChildren<GroundContactConstraint>(true);
        _look = GetComponentInChildren<HeadLookConstraint>(true);
        _lookDirection = transform.forward;
    }

    private void OnEnable()
    {
        // From the pool or a respawn: no lock or pelvis offset survives from before
        if (_feet != null) _feet.ResetContacts();
    }

    /// <summary>Before the animation: the weights of this frame's evaluation follow this frame's state.</summary>
    private void Update()
    {
        if (_movement.StateMachine == null) return;
        PlayerState state = _movement.StateMachine.CurrentState;
        float dt = Time.deltaTime;

        if (_feet != null)
        {
            // A one-tick gap in the ground check (an auto step lifts the body at once) is not leaving the
            // ground: the FSM ignores it for FallGraceTime, and so do the feet
            bool grounded = _movement.IsGrounded || _movement.AirTime < _movement.FallGraceTime;
            bool onGround = grounded && !_movement.IsRootMotionDriven &&
                            (state == _movement.IdleState || state == _movement.RunState || state == _movement.SlideState ||
                             state == _movement.CrouchState || state == _movement.BlockState || state == _movement.HurtState ||
                             state is PlayerAttackState);
            _groundWeight = onGround ? 1f
                          : _movement.IsRootMotionDriven ? 0f
                          : Mathf.MoveTowards(_groundWeight, 0f, groundFadeSpeed * dt);
            _feet.weight = _groundWeight;
            _feet.LockAllowed = onGround && (state == _movement.IdleState || state == _movement.RunState ||
                                             state == _movement.CrouchState || state == _movement.BlockState);
            // Motion matching's weight, or a mocap blow of the controller (switched with its cross-fade)
            bool mocapAction = state is PlayerAttackState attack && attack.Data != null && CombatTimings.UsesFootIK(attack.Data);
            _actionFootIK = Mathf.MoveTowards(_actionFootIK, mocapAction ? 1f : 0f, dt / Mathf.Max(0.01f, footIKBlendTime));
            float mxm = _movement.Locomotion != null ? _movement.Locomotion.Weight : 0f;
            _feet.FootIKWeight = Mathf.Max(mxm, _actionFootIK);
        }

        if (_look != null) UpdateLook(state, dt);
    }

    private void UpdateLook(PlayerState state, float dt)
    {
        bool free = state == _movement.IdleState || state == _movement.RunState || state == _movement.CrouchState || state == _movement.BlockState;
        bool combat = state is PlayerAttackState || state == _movement.HurtState;
        Vector3 want = Vector3.zero;
        float weight = 0f;

        if (free || combat)
        {
            Transform target = state is PlayerAttackState attack && attack.Target != null ? attack.Target : NearTarget();
            if (target != null)
            {
                want = LookPoint(target) - EyePosition;
                weight = 1f;
            }
            else if (free)
            {
                // Running free, the head leads the body into where it is going; standing, walking oriented
                // or backing up, it looks where the camera looks (if that is not behind the body)
                Vector3 camera = _movement.CameraForwardFlat();
                if (state == _movement.RunState && _movement.HasMoveInput && !_movement.IsOriented)
                    want = _movement.MoveDirection;
                else if (camera != Vector3.zero && Vector3.Angle(camera, transform.forward) < 110f)
                    want = camera;
                weight = want != Vector3.zero ? 1f : 0f;
            }
        }

        if (want != Vector3.zero)
        {
            want.Normalize();
            _lookDirection = Vector3.Slerp(_lookDirection, want, 1f - Mathf.Exp(-lookFollowRate * dt));
        }
        _lookWeight = Mathf.MoveTowards(_lookWeight, weight, dt / Mathf.Max(0.01f, lookFadeTime));
        _look.weight = _lookWeight;
        _look.LookToward(_lookDirection);
    }

    private Vector3 EyePosition => new Vector3(transform.position.x, _movement.FeetY + eyeHeight, transform.position.z);

    /// <summary>A target is looked at at eye height (its transform stands on the ground).</summary>
    private Vector3 LookPoint(Transform target) => target.position + Vector3.up * eyeHeight;

    /// <summary>The nearest target in front, searched ten times a second (it changes slowly).</summary>
    private Transform NearTarget()
    {
        if (_hitbox == null) return null;
        if (Time.time >= _nextTargetSearch)
        {
            _nextTargetSearch = Time.time + 0.1f;
            _nearTarget = _hitbox.FindTarget(transform.position, transform.forward, lookTargetRange, lookTargetAngle, out Transform t, out _) ? t : null;
        }
        return _nearTarget;
    }
}
