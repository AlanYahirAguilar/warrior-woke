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
/// Since P39 (2026-10-10) the mocap plays <see cref="TimeScale"/> times faster than captured (×1.25): its
/// root motion moves the body that much faster at the same pace per step, so starts, stops and turns are
/// shorter too. The trajectory still asks MxM for speeds in the mocap's own units: its future at
/// ScaleAdjustment 1/TimeScale (and its response SimulationSpeedScale = TimeScale), and the body's real
/// history scaled by PastScale 1/TimeScale (a patch of the embedded MxM, T26), or it would pull the search to
/// faster takes.
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

    [Tooltip("How much faster than captured the mocap plays (P39): the body moves this much faster, with the mocap's own root motion.")]
    [SerializeField] private float timeScale = 1.25f;

    /// <summary>How much faster than captured the mocap plays (P39).</summary>
    public float TimeScale => timeScale;

    /// <summary>MxM tag of the oriented (strafe and backward) takes: MxMLocomotionBuilder.StrafeTag.</summary>
    public const ETags StrafeTag = ETags.Tag1;

    /// <summary>MxM favour tag of the oriented runs (100STYLE BR and SR): MxMLocomotionBuilder.RunFavourTag.</summary>
    public const ETags RunFavourTag = ETags.Tag1;

    /// <summary>
    /// Cost multiplier of the oriented runs while the oriented gait is a run (P39): from the real history
    /// of a start, the backward walk matched the past best and kept itself under a 2.5 m/s request.
    /// </summary>
    private const float RunFavour = 0.6f;

    /// <summary>Oriented gaits from this speed (m/s) are runs (the walk is 1.6, the backpedal 2.5).</summary>
    private const float OrientedRunSpeed = 2f;

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
    private bool _favourRun;

    /// <summary>Slowest and fastest the speed governor plays the mocap (fraction of TimeScale).</summary>
    private const float MinPlaybackScale = 0.88f, MaxPlaybackScale = 1.3f;

    /// <summary>Share of the gait's speed from which the body counts as holding it (the governor stays out of starts).</summary>
    private const float CruiseShare = 0.7f;

    /// <summary>
    /// Gaits from this speed (m/s) are played at most SprintHurry faster: the sprint takes are the mocap's
    /// fastest, and speeding them up 30 % in the troughs of each stride (a sprint swings ±1.2 m/s) ran it
    /// 10 % over the gait; not at all, a slow sprint take stayed 11 % under it.
    /// </summary>
    private const float NoHurrySpeed = 5f, SprintHurry = 1.1f;

    /// <summary>
    /// How much faster the mocap plays while the body stops from a free gait (no input, still moving): the
    /// actor's stops take 1.3–1.6 s (T27); played faster they keep their footwork and end sooner (P39).
    /// </summary>
    private const float StopHurry = 1.15f;

    private void Awake()
    {
        _movement = GetComponent<PlayerMovement>();
        if (mxm == null) mxm = GetComponentInChildren<MxMAnimator>();
        if (trajectory == null && mxm != null) trajectory = mxm.GetComponent<MxMTrajectoryGenerator>();
        if (trajectory != null)
        {
            // The trajectory in the mocap's units and at its pace: the body's speed is the mocap's × TimeScale
            trajectory.ScaleAdjustment = 1f / Mathf.Max(0.1f, timeScale);
            trajectory.SimulationSpeedScale = timeScale;
            trajectory.PastScale = 1f / Mathf.Max(0.1f, timeScale);
        }
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
        // Only straight back, to a side or forward (forward is oriented only while a backpedal turns
        // around, PlayerMovement): the runs are those, and favoured under a diagonal request they ran
        // straight back (100STYLE has no diagonal runs; the diagonals come from the other takes)
        bool favourRun = oriented && speed >= OrientedRunSpeed && OnRunAxis(direction, facing);
        if (favourRun != _favourRun)
        {
            _favourRun = favourRun;
            if (favourRun) mxm.SetFavourTags(RunFavourTag, RunFavour);
            else mxm.ClearFavourTags();
        }
    }

    /// <summary>Maximum angle (°) between a request and straight back or a side for the runs to be favoured.</summary>
    private const float RunAxisAngle = 25f;

    /// <summary>The direction is (nearly) straight back, forward or to one side of <paramref name="facing"/>.</summary>
    private static bool OnRunAxis(Vector3 direction, Vector3 facing)
    {
        direction.y = 0f;
        facing.y = 0f;
        if (direction.sqrMagnitude < 0.0001f || facing.sqrMagnitude < 0.0001f) return false;
        float angle = Vector3.Angle(facing, direction); // 0 = forward, 90 = a side, 180 = back
        return angle > 180f - RunAxisAngle || angle < RunAxisAngle || Mathf.Abs(angle - 90f) < RunAxisAngle;
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
                // The past is recorded in world units (PastScale brings it to the mocap's); the future is
                // the mocap's units already
                trajectory.ForcePastTrajectoryByVelocity(v);
                float facing = IsOriented ? trajectory.transform.eulerAngles.y : Vector3.SignedAngle(Vector3.forward, v, Vector3.up);
                trajectory.ForceFutureTrajectoryByVelocity(v / Mathf.Max(0.1f, timeScale), facing);
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
    /// ~5.1–5.5 m/s for a 4.8 m/s sprint, P33; ×TimeScale since P39) and others slower (the walks at
    /// ~1.1 m/s of mocap for a 1.28 request, the Rushed backward run at ~1.6 for 2.0: measured 2026-10-10,
    /// 12–20 % under the gait). Moving straight and holding the gait (from CruiseShare of it), the mocap
    /// plays between 12 % slower and 30 % faster (never faster in a sprint) until the body moves at the gait's speed: faster steps,
    /// never sliding feet (the root motion scales with the playback). It stays out of starts, turns and
    /// stops, where the mocap's own timing is the motion (MxM's speed warping, which does both ways
    /// everywhere, broke the pivots).
    /// </summary>
    private void UpdatePlaybackScale()
    {
        float target = 1f;
        if (IsActive && _direction.sqrMagnitude > 0.0001f && _movement != null)
        {
            Vector3 v = _movement.Velocity;
            v.y = 0f;
            float speed = v.magnitude;
            bool straight = speed > 0.5f && Vector3.Angle(v, _direction) < 25f;
            bool cruising = speed > _targetSpeed * CruiseShare;
            float fastest = _targetSpeed < NoHurrySpeed ? MaxPlaybackScale : SprintHurry;
            if (straight && cruising && (speed > _targetSpeed * 1.03f || speed < _targetSpeed * 0.97f))
                target = Mathf.Clamp(_targetSpeed / speed * _playbackScale, MinPlaybackScale, fastest);
            else if (straight && cruising)
                target = _playbackScale; // holding the gait: keep the scale that holds it
        }
        else if (IsActive && !IsOriented && _targetSpeed < NoHurrySpeed && _movement != null && _movement.HorizontalSpeed > 0.3f)
        {
            // Stopping: the stop take, sooner (not the oriented stops: played faster, the 100STYLE
            // backpedal stop surged to 3.2 m/s before braking; nor the sprint's: 23 m/s² of braking)
            target = StopHurry;
        }
        // Released, a speed-up still running carried into the stop take (a surge to 3 m/s out of a
        // backpedal): back to the stop's own scale at once
        bool released = _direction.sqrMagnitude <= 0.0001f;
        float rate = released && target < _playbackScale ? 5f : 1f;
        _playbackScale = Mathf.MoveTowards(_playbackScale, target, Time.deltaTime * rate);
        mxm.UserPlaybackSpeedMultiplier = timeScale * _playbackScale;
    }
}
