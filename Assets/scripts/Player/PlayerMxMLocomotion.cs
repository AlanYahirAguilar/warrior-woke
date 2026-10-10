using MxM;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Motion matching locomotion of the player (decision P29): MxM picks the mocap pose that best fits
/// the intention (Kinematica for free running, 100STYLE for oriented movement; see
/// MxMLocomotionBuilder) and its root motion moves the body through PlayerMovement's motor.
///
/// MxM plays through its own PlayableGraph on the model's Animator, and the weight of that graph's
/// output blends it over the Animator Controller, which keeps running underneath: in Idle and Run
/// motion matching fades in; in any other state (actions: parkour, combat, air, slide, crouch) it
/// fades out and the controller's clip shows, with MatchTarget and the IK pass working as before
/// (P22). While faded out MxM is paused, so it neither searches nor moves.
/// PlayerMovement feeds the intention every physics tick (Drive) and reads <see cref="Weight"/>.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerMxMLocomotion : MonoBehaviour
{
    [Tooltip("MxMAnimator on the model (next to the Animator). Found in children if left empty.")]
    [SerializeField] private MxMAnimator mxm;

    [Tooltip("Trajectory generator on the model; it receives the intention as custom input.")]
    [SerializeField] private MxMTrajectoryGenerator trajectory;

    [Tooltip("Seconds motion matching takes to take over from an action (the controller's clip fades out under it).")]
    [SerializeField] private float blendInTime = 0.25f;

    [Tooltip("Seconds motion matching takes to give way to an action. Short: parkour actions start their contact matching right away.")]
    [SerializeField] private float blendOutTime = 0.06f;

    /// <summary>MxM tag of the oriented (strafe and backward) takes: MxMLocomotionBuilder.StrafeTag.</summary>
    public const ETags StrafeTag = ETags.Tag1;

    /// <summary>Slowest speed (m/s) carried into the trajectory's past when motion matching takes over again.</summary>
    private const float MinCarriedSpeed = 0.5f;

    /// <summary>0 = the Animator Controller shows, 1 = motion matching shows and moves the body.</summary>
    public float Weight { get; private set; }

    /// <summary>True while PlayerMovement wants motion matching (Idle and Run).</summary>
    public bool IsActive { get; private set; }

    /// <summary>MxM is set up and its graph exists.</summary>
    public bool IsReady => mxm != null && trajectory != null && mxm.IsInitialized && _hasOutput;

    /// <summary>Oriented locomotion (strafe mode) this tick.</summary>
    public bool IsOriented { get; private set; }

    public MxMAnimator Animator => mxm;

    private AnimationPlayableOutput _output;
    private bool _hasOutput;
    private PlayerMovement _movement;
    private float _targetSpeed;
    private Vector3 _direction;
    private float _playbackScale = 1f;

    /// <summary>Slowest the speed governor plays the mocap (fraction of its own speed).</summary>
    private const float MinPlaybackScale = 0.88f;

    private void Awake()
    {
        _movement = GetComponent<PlayerMovement>();
        if (mxm == null) mxm = GetComponentInChildren<MxMAnimator>();
        if (trajectory == null && mxm != null) trajectory = mxm.GetComponent<MxMTrajectoryGenerator>();
    }

    /// <summary>
    /// The intention, every physics tick. <paramref name="active"/>: motion matching should show.
    /// <paramref name="direction"/>: world direction of the movement (zero to stop).
    /// <paramref name="speed"/>: gait speed (m/s). <paramref name="oriented"/>: move under the body
    /// facing <paramref name="facing"/> (strafe, backward) instead of turning toward the movement.
    /// </summary>
    public void Drive(bool active, Vector3 direction, float speed, bool oriented, Vector3 facing)
    {
        IsActive = active;
        _targetSpeed = speed;
        _direction = direction;
        if (trajectory == null || mxm == null || !mxm.IsInitialized) return;

        trajectory.InputVector = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
        trajectory.MaxSpeed = Mathf.Max(0.1f, speed);

        if (oriented != IsOriented)
        {
            IsOriented = oriented;
            trajectory.TrajectoryMode = oriented ? ETrajectoryMoveMode.Strafe : ETrajectoryMoveMode.Normal;
            if (oriented) mxm.SetRequiredTags(StrafeTag);
            else mxm.ClearRequiredTags();
        }
        if (oriented && facing.sqrMagnitude > 0.0001f)
            trajectory.StrafeDirection = facing;
    }

    /// <summary>
    /// Forgets the trajectory history (after a teleport, a respawn or an action), facing where the
    /// body faces now. (MxMAnimator.ResetMotion would reset the facing to world yaw 0 and the
    /// character would turn on the spot toward it.)
    /// </summary>
    public void ResetMotion()
    {
        if (trajectory != null && mxm != null && mxm.IsInitialized)
            trajectory.ResetMotion(trajectory.transform.eulerAngles.y);
    }

    /// <summary>
    /// A teleport or a respawn: forgets the trajectory and goes straight to the idle, so the take that
    /// was playing (a stop, a run) does not keep moving the body from its new place.
    /// </summary>
    public void StopInPlace()
    {
        ResetMotion();
        if (mxm != null && mxm.IsInitialized)
            mxm.ForceBeginIdle();
    }

    private void Update()
    {
        if (!_hasOutput && mxm != null && mxm.IsInitialized && mxm.MxMPlayableGraph.IsValid())
        {
            _output = (AnimationPlayableOutput)mxm.MxMPlayableGraph.GetOutputByType<AnimationPlayableOutput>(0);
            _hasOutput = _output.IsOutputValid();
            if (_hasOutput) _output.SetWeight(Weight);
        }
        if (!IsReady)
        {
            Weight = 0f;
            return;
        }

        float target = IsActive ? 1f : 0f;
        float time = IsActive ? blendInTime : blendOutTime;
        Weight = time > 0f ? Mathf.MoveTowards(Weight, target, Time.deltaTime / time) : target;

        // Paused while hidden: no searches and no root motion of its own under an action. On the way
        // back, the trajectory's past and its predicted future carry the body's real motion (an action
        // ending at a run continues as a run, not as a start from standing: with only the past, the
        // future started at rest and motion matching dropped to ~2 m/s before running again)
        if (Weight > 0f && mxm.IsPaused)
        {
            mxm.UnPause();
            ResetMotion();
            Vector3 v = _movement != null ? _movement.Velocity : Vector3.zero;
            v.y = 0f;
            if (v.sqrMagnitude > MinCarriedSpeed * MinCarriedSpeed)
            {
                trajectory.ForcePastTrajectoryByVelocity(v);
                float facing = IsOriented ? trajectory.transform.eulerAngles.y : Vector3.SignedAngle(Vector3.forward, v, Vector3.up);
                trajectory.ForceFutureTrajectoryByVelocity(v, facing);
            }
        }
        else if (Weight <= 0f && !mxm.IsPaused)
        {
            mxm.Pause();
        }
        _output.SetWeight(Weight);
        UpdatePlaybackScale();
    }

    /// <summary>
    /// Speed governor: some takes run faster than the gait asked for (the mocap's sprint peaks at
    /// ~5.1–5.5 m/s for a 4.8 m/s sprint, P33). Running straight, the mocap plays up to 12 % slower
    /// until the body holds the gait; it never speeds a take up and stays out of turns, stops and
    /// oriented movement, where the mocap's own timing is the motion (MxM's speed warping, which
    /// does both ways everywhere, broke the pivots).
    /// </summary>
    private void UpdatePlaybackScale()
    {
        float target = 1f;
        if (IsActive && !IsOriented && _direction.sqrMagnitude > 0.0001f && _movement != null)
        {
            Vector3 v = _movement.Velocity;
            v.y = 0f;
            float speed = v.magnitude;
            bool straight = speed > 0.5f && Vector3.Angle(v, _direction) < 25f;
            if (straight && speed > _targetSpeed * 1.03f)
                target = Mathf.Clamp(_targetSpeed / speed * _playbackScale, MinPlaybackScale, 1f);
        }
        _playbackScale = Mathf.MoveTowards(_playbackScale, target, Time.deltaTime);
        mxm.UserPlaybackSpeedMultiplier = _playbackScale;
    }
}
