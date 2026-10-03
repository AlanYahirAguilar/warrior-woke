using UnityEngine;

// ────────────────────────────────────────────────────────────────────────────────
// PlayerVaultState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Vault over a low obstacle with Space (GDD §5.4), keeping the momentum.
/// Adapted from Dynamic Parkour System's VaultObstacle (MIT, Èric Canela), but driven by the
/// VaultFence clip's root motion (decision P22): PlayerAnimator warps it with MatchTarget so the
/// left hand lands on the measured hand point and the feet on the measured landing point, and pins
/// the hand with IK while it rests on the obstacle. The clip plays faster or slower with the approach
/// speed and starts later when the obstacle is closer than the clip expects, so the take-off, the
/// contact and the landing match the real obstacle.
/// </summary>
public class PlayerVaultState : PlayerState
{
    /// <summary>Duration used only when no animation reports progress.</summary>
    public const float FallbackDuration = 0.8f;

    public PlayerVaultState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    /// <summary>Horizontal speed when the vault started (m/s).</summary>
    public float ApproachSpeed   { get; private set; }

    /// <summary>Playback speed of the clip: the approach speed relative to the clip's own speed.</summary>
    public float SpeedMultiplier { get; private set; } = 1f;

    /// <summary>Normalized time the clip starts at (later when the obstacle is close).</summary>
    public float StartOffset     { get; private set; }

    /// <summary>Feet height when the vault started.</summary>
    public float StartFeetY      { get; private set; }

    /// <summary>
    /// Extra take-off height (m) for obstacles taller than the clip's fence: the body rises this much
    /// more before the lead leg reaches the obstacle.
    /// </summary>
    public float TakeoffRaise => Mathf.Max(0f, player.PendingVault.TopY - StartFeetY - ParkourTimings.VaultClipFenceHeight);

    /// <summary>Wrist goal for the planted left hand (on the obstacle's top surface).</summary>
    public Vector3 HandTarget => player.PendingVault.HandPoint + Vector3.up * ParkourTimings.WristAboveSurface;

    /// <summary>Rotation facing the obstacle (perpendicular to its face).</summary>
    public Quaternion FacingRotation => Quaternion.LookRotation(player.PendingVault.Direction, Vector3.up);

    /// <summary>Result of evaluating a vault: no valid vault, ready now, or ahead and approaching its take-off point.</summary>
    public enum Approach { None, Ready, Approaching }

    /// <summary>Vault that can start now (stored in PlayerMovement.PendingVault).</summary>
    public static bool TryStart(PlayerMovement player) => Evaluate(player) == Approach.Ready;

    /// <summary>
    /// Evaluates the geometry ahead for a vault, by context: the obstacle (height, depth, landing,
    /// angle), the speed and the distance. Running, Space looks farther ahead (ParkourStandard
    /// .VaultSpotReach): an obstacle beyond the ideal take-off distance is "Approaching" and the run
    /// keeps the intention until the body reaches it, so the clip always meets the obstacle at its
    /// own take-off instead of starting late. A standing or walking vault starts from where it is.
    /// </summary>
    public static Approach Evaluate(PlayerMovement player)
    {
        Vector3 dir = player.HasMoveInput ? player.MoveDirection : player.transform.forward;
        bool fast = player.HorizontalSpeed >= ParkourTimings.VaultClipSpeed * MinPlaybackSpeed;
        float reach = fast ? ParkourStandard.VaultSpotReach(player.HorizontalSpeed) : ParkourStandard.VaultReach;
        if (!player.EnvChecker.TryFindVault(dir, player.FeetY, reach, out VaultInfo info))
            return Approach.None;

        if (fast)
        {
            Vector3 toFace = info.FrontPoint - player.transform.position;
            float distance = Vector3.Dot(new Vector3(toFace.x, 0f, toFace.z), info.Direction);
            if (distance > ParkourStandard.VaultTakeoffDistance + player.HorizontalSpeed * Time.fixedDeltaTime)
                return Approach.Approaching;
        }

        // A taller obstacle needs the take-off warp. Running, the momentum carries the body up in time;
        // from a standstill or a walk too close to it, the lead leg would already be at the obstacle
        // when the clip starts and cut through it, so Space jumps instead (~0.91 m of room needed).
        float raise = info.TopY - player.FeetY - ParkourTimings.VaultClipFenceHeight;
        bool slow = player.HorizontalSpeed < ParkourTimings.VaultClipSpeed * MinPlaybackSpeed;
        if (slow && raise > MinTakeoffRaise &&
            CloseStartOffset(info.HandDistance) > ParkourTimings.VaultTakeoff - ParkourTimings.VaultTakeoffWindow)
            return Approach.None;

        player.PendingVault = info;
        return Approach.Ready;
    }

    /// <summary>Slowest playback of the clip: below this approach speed the vault is a standing one.</summary>
    private const float MinPlaybackSpeed = 0.8f;

    /// <summary>Smallest extra take-off (m) that is worth a warp phase.</summary>
    private const float MinTakeoffRaise = 0.02f;

    /// <summary>Start offset that skips the run-up the body no longer needs when it is close to the obstacle.</summary>
    private static float CloseStartOffset(float handDistance)
    {
        float closeness = 1f - Mathf.Clamp01(handDistance / ParkourTimings.VaultClipHandReach);
        return Mathf.Min(ParkourTimings.VaultHandContact * closeness, ParkourTimings.VaultMaxStartOffset);
    }

    public override void Enter()
    {
        base.Enter();
        ApproachSpeed   = player.HorizontalSpeed;
        StartFeetY      = player.FeetY;
        SpeedMultiplier = Mathf.Clamp(ApproachSpeed / ParkourTimings.VaultClipRunSpeed, MinPlaybackSpeed, 1.5f);

        // Closer than the clip's run-up: skip the first frames instead of sliding backward. A taller
        // obstacle always keeps the take-off window, or the warp would lift the body in a visible pop
        // (TryStart already refused it from a standstill when too close)
        StartOffset = CloseStartOffset(player.PendingVault.HandDistance);
        if (TakeoffRaise > MinTakeoffRaise)
        {
            // The cross-fade comes first and MatchTarget cannot run during it: leave room for both
            float fade = ParkourTimings.VaultCrossFade * SpeedMultiplier / ParkourTimings.VaultClipLength;
            StartOffset = Mathf.Min(StartOffset, Mathf.Max(0f, ParkourTimings.VaultTakeoff - ParkourTimings.VaultTakeoffWindow - fade));
        }

        player.BeginRootMotion();
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();
        float progress = player.ParkourProgress;
        bool done = progress >= 0f
            ? progress >= ParkourTimings.VaultExit
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
            player.transform.position = player.PendingVault.LandingPoint + Vector3.up * player.StandingHalfHeight;
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
        player.BeginRootMotion();
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
        player.BeginRootMotion(); // already driven when coming from the hang
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
                 - top.Normal * ParkourTimings.VaultHandInset + Vector3.up * ParkourTimings.WristAboveSurface;
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
        bool fast = player.HorizontalSpeed >= ParkourTimings.VaultClipSpeed * 0.8f;
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
        player.BeginRootMotion();
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
        player.BeginRootMotion();
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
