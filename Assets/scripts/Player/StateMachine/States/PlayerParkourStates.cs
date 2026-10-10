using UnityEngine;

// ────────────────────────────────────────────────────────────────────────────────
// PlayerVaultState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Vault over an obstacle with Space (GDD §5.4), keeping the momentum (decision P36, docs/arquitectura.md
/// §5.17). The motion is a measured mocap vault of the Kinematica Demo (VaultCatalog) chosen by
/// VaultPlanner for this obstacle, approach speed, distance and stride, and warped onto the real
/// geometry by this state, every frame, from the clip's own root path and heading (measured on Ch45 in
/// the catalog; PlayerAnimator reports the clip time):
///  - run-up: the remaining distance error to the hand's plant point is closed over the run-up, so the
///    stride adjusts instead of the body sliding, and the clip plays at the approach's pace;
///  - over the obstacle: the body rises (or lowers) by the plan's lift and travels the extra depth spread
///    over the airborne window; the airborne window plays at the rate that keeps the clip's gravity;
///  - landing: the feet come down on the measured ground behind it, and the run continues from the
///    clip's escape moment with the speed the body really carries.
/// The CharacterController moves the body and lets it through the obstacle being vaulted (which was
/// validated before starting; anything else still stops it), and the hands and feet keep their contacts
/// with IK (PlayerContactIK).
/// Replaces the Dynamic Parkour System "Vault1" clip and its MatchTarget phases (P22).
/// </summary>
public class PlayerVaultState : PlayerState
{
    /// <summary>Duration used only when no animation reports progress.</summary>
    public const float FallbackDuration = 1.2f;

    private VaultPlan _plan = new VaultPlan();
    private VaultPlan _scratch = new VaultPlan(); // evaluations never touch the running plan
    private float _prevClipTime, _startRootY, _vaultYaw, _entryYawOffset;
    private static bool _warnedNoCatalog;

    public PlayerVaultState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    /// <summary>The vault being played (valid between Enter and Exit; the last one afterwards).</summary>
    public VaultPlan Plan => _plan;

    /// <summary>Why the last evaluation found no vault, with what it measured (for tests and tuning).</summary>
    public string LastReason => _scratch.Reason;

    /// <summary>The last evaluation, accepted or not (its measurements: VaultPlan.MeasuredHeight, …).</summary>
    public VaultPlan LastEvaluation => _scratch.Reason.Length > 0 ? _scratch : _plan;

    /// <summary>Horizontal speed when the vault started (m/s).</summary>
    public float ApproachSpeed   { get; private set; }

    /// <summary>Clip time (s) of the playing vault, as PlayerAnimator reports it.</summary>
    public float ClipTime        { get; private set; }

    /// <summary>Playback rate of the vault clip this frame (PlayerAnimator writes it to the Animator).</summary>
    public float PlaybackRate    { get; private set; } = 1f;

    /// <summary>Rotation facing the obstacle (perpendicular to its face).</summary>
    public Quaternion FacingRotation => Quaternion.LookRotation(_plan.Direction, Vector3.up);

    /// <summary>Result of evaluating a vault: no valid vault, ready now, or ahead and approaching its entry point.</summary>
    public enum Approach { None, Ready, Approaching }

    /// <summary>Vault that can start now (its plan becomes Plan).</summary>
    public static bool TryStart(PlayerMovement player) => Evaluate(player) == Approach.Ready;

    /// <summary>
    /// Evaluates the geometry ahead for a vault, by context: the obstacle (height, depth, landing), the
    /// speed, the distance and the stride (VaultPlanner). Running, Space looks farther ahead
    /// (ParkourStandard.VaultSpotReach): an obstacle beyond the clip's run-up is "Approaching" and the
    /// run keeps the intention until the body reaches it. No clip fits → None: the obstacle is not
    /// vaulted (the caller tries the mantle, the ledge or the jump).
    /// </summary>
    public static Approach Evaluate(PlayerMovement player) => Evaluate(player, player.HorizontalSpeed);

    /// <summary>
    /// Same as Evaluate, judging the approach at <paramref name="approachSpeed"/>: a slide braking
    /// toward the obstacle still carries the momentum of its entry into the vault.
    /// </summary>
    public static Approach Evaluate(PlayerMovement player, float approachSpeed)
    {
        PlayerVaultState state = player.VaultState;
        if (player.VaultCatalog == null)
        {
            if (!_warnedNoCatalog) Debug.LogWarning("[PlayerVaultState] Sin VaultCatalog en PlayerMovement: el vault está desactivado. Corre 'Construir Catálogo de Vaults'.");
            _warnedNoCatalog = true;
            state._scratch.Reason = "sin catálogo";
            return Approach.None;
        }
        Vector3 dir = player.HasMoveInput ? player.MoveDirection : player.transform.forward;
        if (!player.EnvChecker.TryFindVault(dir, player.FeetY, ParkourStandard.VaultSpotReach(approachSpeed), out VaultInfo info))
        {
            state._scratch.Reason = "sin obstáculo";
            return Approach.None;
        }

        float phase = player.ParkourAnimation != null ? player.ParkourAnimation.FootPhase : 0f;
        VaultPlanner.Result result = VaultPlanner.Evaluate(player.VaultCatalog, player, info, approachSpeed, phase, state._scratch);
        if (result != VaultPlanner.Result.Ready)
            return result == VaultPlanner.Result.Approaching ? Approach.Approaching : Approach.None;

        (state._plan, state._scratch) = (state._scratch, state._plan);
        state._scratch.Reason = "";
        return Approach.Ready;
    }

    /// <summary>
    /// Running approach (above the walk, ≈ 2.2 m/s): Space looks ahead for the vault and the mantle and
    /// waits for their entry point instead of starting where it is.
    /// </summary>
    public static bool IsRunning(float speed) => speed >= RunningSpeed;

    /// <summary>Between the walk (1.3 m/s) and the run (3.4 m/s) of P33.</summary>
    private const float RunningSpeed = 2.2f;

    public override void Enter()
    {
        base.Enter();
        ApproachSpeed  = player.HorizontalSpeed;
        ClipTime       = _plan.EntryTime;
        _prevClipTime  = _plan.EntryTime;
        PlaybackRate   = _plan.ApproachRate;
        _startRootY    = player.transform.position.y;
        _vaultYaw      = Quaternion.LookRotation(_plan.Direction, Vector3.up).eulerAngles.y;
        // What the body's heading differs from the clip's at the entry (an approach at an angle), faded
        // out over the run-up
        _entryYawOffset = Mathf.DeltaAngle(_vaultYaw + _plan.Variant.HeadingAt(_plan.EntryTime), player.transform.eulerAngles.y);
        player.BeginRootMotion(_plan.Front, _plan.Top);
    }

    /// <summary>Seconds over which the body's heading at the entry blends into the clip's.</summary>
    private const float EntryAlignTime = 0.3f;

    /// <summary>Seconds before the escape over which the actor's residual heading is straightened along the vault.</summary>
    private const float ExitAlignTime = 0.2f;

    /// <summary>
    /// Moves the body for this frame (PlayerAnimator calls it from OnAnimatorMove with the vault clip's
    /// time). The motion is the clip's own root path and heading, measured on Ch45 (VaultCatalog), in
    /// the vault frame: the Animator's root delta is not used, because it is expressed along the root's
    /// heading, which turns with the body (sideways in a lazy vault). On top of it: the stretch over the
    /// airborne window, the approach correction toward the hand's plant point, the lift and the landing
    /// height. Also sets the playback rate for the next frame.
    /// </summary>
    public void Warp(float clipTime)
    {
        VaultPlan p = _plan;
        VaultVariant v = p.Variant;
        if (v == null) return;
        float t0 = _prevClipTime, t1 = Mathf.Max(clipTime, t0);
        Transform tr = player.transform;
        Vector3 f = p.Direction, right = Vector3.Cross(Vector3.up, f);

        // The clip's root path between the two clip times, in the vault frame, plus the stretch
        Vector2 d = v.PathAt(t1) - v.PathAt(t0);
        Vector3 pos = tr.position + right * d.x + f * d.y;
        pos += f * (p.Stretch * (VaultWarpProfile.Stretch(v, t1) - VaultWarpProfile.Stretch(v, t0)));

        // Run-up: the hand must meet its plant point on the top. The predicted miss is closed over the
        // run-up still to play (the stride lengthens or shortens a little; nothing teleports)
        if (t1 < v.Plant)
        {
            Vector2 now = v.PathAt(t1), atPlant = v.PathAt(v.Plant);
            float sNow = VaultWarpProfile.Stretch(v, t1), sPlant = VaultWarpProfile.Stretch(v, v.Plant);
            Vector3 predicted = pos + right * (atPlant.x - now.x + v.HandOffset.x)
                                    + f * (atPlant.y - now.y + v.HandOffset.z + p.Stretch * (sPlant - sNow));
            Vector3 miss = p.PlantPoint - predicted;
            miss.y = 0f;
            pos += miss * Mathf.Clamp01((t1 - t0) / Mathf.Max(1e-3f, v.Plant - t0));
        }

        // Height: the lift over the obstacle, and the landing ground (lower or higher than the start)
        pos.y = _startRootY + p.Lift * VaultWarpProfile.Lift(v, t1) + (p.LandFeetY - p.StartFeetY) * VaultWarpProfile.Ground(v, t1);

        // Facing: the clip root's heading over the vault direction (the pose turns as captured), the
        // entry's difference faded out over the run-up, and the actor's residual heading straightened
        // before the escape, so the run continues along the vault
        float entryFade = 1f - VaultWarpProfile.Smooth(p.EntryTime, Mathf.Min(p.EntryTime + EntryAlignTime, v.Plant), t1);
        float exitFade = 1f - VaultWarpProfile.Smooth(Mathf.Min(v.Land, v.Escape - ExitAlignTime), v.Escape, t1);
        float yaw = _vaultYaw + v.HeadingAt(t1) * exitFade + _entryYawOffset * entryFade;
        player.SetRootMotionPose(pos, Quaternion.Euler(0f, yaw, 0f));

        _prevClipTime = t1;
        ClipTime = t1;
        // The run-up and the run-out at the approach's pace; the airborne window at the rate that keeps
        // the clip's gravity after the lift, back to the run's pace by the landing (some clips hand over
        // to the locomotion on their landing frame: the run must already carry the approach's speed)
        float air = VaultWarpProfile.Smooth(v.Takeoff - 0.1f, v.Takeoff, t1) * (1f - VaultWarpProfile.Smooth(v.Land - 0.1f, v.Land, t1));
        PlaybackRate = Mathf.Lerp(p.ApproachRate, p.AirRate, air);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();
        bool done = player.ParkourAnimation != null && _plan.Variant != null
            ? ClipTime >= _plan.Variant.Escape
            : Time.time - startTime >= FallbackDuration;

        if (done)
            stateMachine.ChangeState(player.HasMoveInput ? (PlayerState)player.RunState : player.IdleState);
    }

    public override void Exit()
    {
        base.Exit();
        if (player.ParkourAnimation == null)
        {
            // No animation drove the body: put it on the landing point
            player.transform.position = _plan.LandingPoint + Vector3.up * player.StandingHalfHeight;
        }
        player.EndRootMotion();

        // Keep the momentum (GDD §5.4): the run continues at the speed the clip was really moving the
        // body, along the facing; Run (or Idle, braking) takes it from there. No speed is invented.
        float speed = player.ParkourAnimation != null
            ? Vector3.Dot(player.RootMotionVelocity, player.transform.forward)
            : ApproachSpeed;
        player.SetVelocity(player.transform.forward * Mathf.Max(0f, speed), 0f);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerLedgeGrabState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Grab a ledge and hang from it (outside the GDD, kept by decision P2). Space climbs (it can be
/// pressed during the grab and the climb follows on), the direction away from the wall lets go.
/// Started from the ground (Space in front of a wall whose top is within reach) or in the air (a
/// jump or fall that meets a ledge). The "Idle To Braced Hang" clip's root motion carries the body
/// and PlayerAnimator warps it with MatchTarget so the hands reach the measured edge; then the
/// "Hanging Idle" loop holds the body still while IK keeps both hands on the edge and the feet on the wall.
/// </summary>
public class PlayerLedgeGrabState : PlayerState
{
    /// <summary>Duration of the grab used only when no animation reports progress.</summary>
    public const float FallbackGrabTime = 0.6f;

    private bool _climbQueued;
    private bool _nextFromGround;
    private bool _nextFromDrop;

    public PlayerLedgeGrabState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    /// <summary>True if this grab started on the ground (the clip plays its jump).</summary>
    public bool FromGround { get; private set; }

    /// <summary>True if the body lowered onto the hang from the top (PlayerLedgeDropState): already holding.</summary>
    public bool FromDrop { get; private set; }

    /// <summary>Enters the hang straight after a drop (the hands already hold the edge).</summary>
    public void PrepareFromDrop() { _nextFromGround = false; _nextFromDrop = true; }

    /// <summary>True once both hands hold the edge and the climb may start.</summary>
    public bool IsAttached
    {
        get
        {
            float progress = player.ParkourProgress;
            if (FromDrop) return true;
            return progress >= 0f ? progress >= ParkourTimings.GrabAttached : Time.time - startTime >= FallbackGrabTime;
        }
    }

    /// <summary>Space in front of a wall whose top is within reach from the ground.</summary>
    public static bool TryStartFromGround(PlayerMovement player)
    {
        if (Time.time - player.LedgeReleaseTime < player.LedgeRegrabDelay) return false;
        Vector3 dir = player.HasMoveInput ? player.MoveDirection : player.transform.forward;
        if (!player.EnvChecker.TryFindLedge(dir, player.FeetY, ParkourStandard.LedgeGroundMinRise, ParkourStandard.LedgeGroundMaxRise, true, out LedgeInfo ledge))
            return false;

        player.CurrentLedge = ledge;
        player.LedgeGrabState._nextFromGround = true;
        player.LedgeGrabState._nextFromDrop = false;
        return true;
    }

    /// <summary>A jump or fall that meets a ledge in front of the hands.</summary>
    public static bool TryStartInAir(PlayerMovement player)
    {
        if (Time.time - player.LedgeReleaseTime < player.LedgeRegrabDelay) return false;
        Vector3 dir = player.transform.forward;
        // Do not grab walls the player is steering away from
        if (player.HasMoveInput && Vector3.Dot(player.MoveDirection, dir) < -0.2f) return false;
        if (!player.EnvChecker.TryFindLedge(dir, player.FeetY, ParkourStandard.LedgeAirMinRise, ParkourStandard.LedgeAirMaxRise, false, out LedgeInfo ledge))
            return false;

        player.CurrentLedge = ledge;
        player.LedgeGrabState._nextFromGround = false;
        player.LedgeGrabState._nextFromDrop = false;
        return true;
    }

    public override void Enter()
    {
        base.Enter();
        FromGround = _nextFromGround;
        FromDrop = _nextFromDrop;
        _nextFromDrop = false;
        _climbQueued = false;
        player.IsSprint = false;
        player.BeginRootMotion(player.CurrentLedge.Face, player.CurrentLedge.Top);
        if (player.ParkourAnimation == null)
        {
            // No animation drives the body: hang directly below the edge
            LedgeInfo l = player.CurrentLedge;
            player.transform.SetPositionAndRotation(
                l.Edge + l.Normal * 0.35f + Vector3.down * (ParkourStandard.StandCheckHeight - player.StandingHalfHeight),
                l.FacingRotation);
        }
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        bool away = player.HasMoveInput && Vector3.Dot(player.MoveDirection, player.CurrentLedge.Normal) > 0.5f;

        // Space with the direction away from the wall: push off and jump away (P28); the body turns
        // toward the input in the air. Space alone climbs (remembered during the grab).
        if (player.JumpTriggered && away && IsAttached)
        {
            player.EndRootMotion();
            player.LedgeReleaseTime = Time.time;
            player.SetVelocity(player.CurrentLedge.Normal * ParkourStandard.HangJumpOut, ParkourStandard.HangJumpUp);
            stateMachine.ChangeState(player.FallState);
            return;
        }
        if (player.JumpTriggered) _climbQueued = true;
        if (!IsAttached) return;

        if (_climbQueued)
        {
            stateMachine.ChangeState(player.LedgeClimbState);
        }
        else if (player.HasMoveInput && Vector3.Dot(player.MoveDirection, player.CurrentLedge.Normal) > 0.5f)
        {
            // Pushing away from the wall lets go
            player.EndRootMotion();
            player.LedgeReleaseTime = Time.time;
            stateMachine.ChangeState(player.FallState);
        }
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerLedgeClimbState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Climbs from the hang onto the ledge (outside the GDD, kept by decision P2).
/// Driven by the root motion of "Braced Hang To Crouch": the hands stay on the edge with IK while
/// the body pulls up, and MatchTarget lands the feet on the measured stand point on top.
/// </summary>
public class PlayerLedgeClimbState : PlayerState
{
    /// <summary>Duration used only when no animation reports progress.</summary>
    public const float FallbackDuration = 1.0f;

    public PlayerLedgeClimbState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        player.BeginRootMotion(player.CurrentLedge.Face, player.CurrentLedge.Top); // already driven when coming from the hang
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();
        float progress = player.ParkourProgress;
        bool done = progress >= 0f
            ? progress >= ParkourTimings.ClimbExit
            : Time.time - startTime >= FallbackDuration;

        if (done)
            stateMachine.ChangeState(player.HasMoveInput ? (PlayerState)player.RunState : player.IdleState);
    }

    public override void Exit()
    {
        base.Exit();
        // Stand exactly on top: the root was matched onto the stand point, correct any residue
        Vector3 standOrigin = player.CurrentLedge.StandPoint + Vector3.up * player.StandingHalfHeight;
        Vector3 offset = standOrigin - player.transform.position;
        if (player.ParkourAnimation == null || offset.sqrMagnitude < 0.25f * 0.25f)
            player.transform.position = standOrigin;
        player.EndRootMotion();
        // Stand up with the little momentum the climb's last step carries (no dead stop)
        float speed = Mathf.Max(0f, Vector3.Dot(player.RootMotionVelocity, player.transform.forward));
        player.SetVelocity(player.transform.forward * speed, 0f);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerMantleState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Climbs onto a block 0.8–1.5 m high that has room to stand on (P28). Driven by the root motion of
/// "ClimbUp_1m" (Quaternius, CC0): PlayerAnimator warps it so the extra height of a taller top is
/// lifted before the hand plants (or a lower top is reached with less lift) and the feet end on the
/// measured stand point; PlayerContactIK keeps the supporting hand on the top and the feet out of the
/// block. The run continues on top at the speed the clip carries.
/// </summary>
public class PlayerMantleState : PlayerState
{
    /// <summary>Duration used only when no animation reports progress.</summary>
    public const float FallbackDuration = 0.7f;

    public PlayerMantleState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    /// <summary>Feet height when the mantle started.</summary>
    public float StartFeetY { get; private set; }

    /// <summary>Height (m) the top differs from the clip's own climb: positive lifts more, negative less.</summary>
    public float ExtraRise => player.CurrentLedge.TopY - StartFeetY - ParkourTimings.MantleClipRise;

    /// <summary>
    /// Wrist goal of the supporting (left) hand: on the top, just past the edge, left of the body line,
    /// where the clip plants it (measured: 0.3 m to the left).
    /// </summary>
    public Vector3 HandTarget
    {
        get
        {
            LedgeInfo top = player.CurrentLedge;
            Vector3 left = Vector3.Cross(-top.Normal, Vector3.up);
            return top.EdgeAt(player.transform.position) + left * ParkourTimings.HandLateral
                 - top.Normal * ParkourTimings.MantleHandInset + Vector3.up * ParkourTimings.WristAboveSurface;
        }
    }

    /// <summary>A block to climb onto that can start now; stored in CurrentLedge.</summary>
    public static bool TryStart(PlayerMovement player) => Evaluate(player) == PlayerVaultState.Approach.Ready;

    /// <summary>
    /// A block to climb onto in the movement (or facing) direction. Running, Space looks farther ahead
    /// (like the vault): a block beyond the mantle's reach is "Approaching" and the run keeps the
    /// intention until the body gets there, instead of jumping into it.
    /// </summary>
    public static PlayerVaultState.Approach Evaluate(PlayerMovement player)
    {
        Vector3 dir = player.HasMoveInput ? player.MoveDirection : player.transform.forward;
        bool fast = PlayerVaultState.IsRunning(player.HorizontalSpeed);
        float reach = fast ? ParkourStandard.VaultSpotReach(player.HorizontalSpeed) : ParkourStandard.MantleReach;
        if (!player.EnvChecker.TryFindMantle(dir, player.FeetY, reach, out LedgeInfo top))
            return PlayerVaultState.Approach.None;

        Vector3 toEdge = top.Edge - player.transform.position;
        float distance = Vector3.Dot(new Vector3(toEdge.x, 0f, toEdge.z), -top.Normal);
        if (distance > ParkourStandard.MantleReach)
            return PlayerVaultState.Approach.Approaching;

        player.CurrentLedge = top;
        return PlayerVaultState.Approach.Ready;
    }

    public override void Enter()
    {
        base.Enter();
        StartFeetY = player.FeetY;
        player.IsSprint = false;
        player.BeginRootMotion(player.CurrentLedge.Face, player.CurrentLedge.Top);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();
        float progress = player.ParkourProgress;
        bool done = progress >= 0f
            ? progress >= ParkourTimings.MantleExit
            : Time.time - startTime >= FallbackDuration;

        if (done)
            stateMachine.ChangeState(player.HasMoveInput ? (PlayerState)player.RunState : player.IdleState);
    }

    public override void Exit()
    {
        base.Exit();
        // On top: the root was matched onto the stand point, correct any residue
        Vector3 standOrigin = player.CurrentLedge.StandPoint + Vector3.up * player.StandingHalfHeight;
        Vector3 offset = standOrigin - player.transform.position;
        if (player.ParkourAnimation == null || offset.sqrMagnitude < 0.25f * 0.25f)
            player.transform.position = standOrigin;
        player.EndRootMotion();
        float speed = player.ParkourAnimation != null
            ? Mathf.Max(0f, Vector3.Dot(player.RootMotionVelocity, player.transform.forward))
            : 0f;
        player.SetVelocity(player.transform.forward * speed, 0f);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerLedgeDropState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Lowers from a top onto a hang on its edge (P28): C near an edge with a drop of at least
/// ParkourStandard.DropMinHeight below. The clip is "Braced Hang To Crouch" played backward (a clip
/// generated by PlayerAnimationSetup): the body turns its back to the drop, crouches and lowers
/// itself, and PlayerAnimator matches the hands onto the measured edge. It ends hanging
/// (PlayerLedgeGrabState), from where Space climbs back, the direction away from the wall plus Space
/// jumps off, and the direction away alone lets go.
/// </summary>
public class PlayerLedgeDropState : PlayerState
{
    /// <summary>Duration used only when no animation reports progress.</summary>
    public const float FallbackDuration = 1.0f;

    public PlayerLedgeDropState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    /// <summary>An edge to lower onto in the movement (or facing) direction; stores it in CurrentLedge.</summary>
    public static bool TryStart(PlayerMovement player)
    {
        Vector3 dir = player.HasMoveInput ? player.MoveDirection : player.transform.forward;
        if (!player.IsGrounded || !player.EnvChecker.TryFindDrop(dir, player.FeetY, out LedgeInfo ledge))
            return false;
        player.CurrentLedge = ledge;
        return true;
    }

    public override void Enter()
    {
        base.Enter();
        player.IsSprint = false;
        player.BeginRootMotion(player.CurrentLedge.Face, player.CurrentLedge.Top);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();
        float progress = player.ParkourProgress;
        bool done = progress >= 0f
            ? progress >= ParkourTimings.DropHang
            : Time.time - startTime >= FallbackDuration;
        if (!done) return;

        if (player.ParkourAnimation == null)
        {
            LedgeInfo l = player.CurrentLedge;
            player.transform.SetPositionAndRotation(
                l.Edge + l.Normal * 0.35f + Vector3.down * (ParkourStandard.StandCheckHeight - player.StandingHalfHeight),
                l.FacingRotation);
        }
        player.LedgeGrabState.PrepareFromDrop();
        stateMachine.ChangeState(player.LedgeGrabState);
    }
}
