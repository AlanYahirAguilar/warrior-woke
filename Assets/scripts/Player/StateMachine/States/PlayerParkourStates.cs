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

    /// <summary>
    /// Checks for a vaultable obstacle in the facing direction (or the input direction) and stores
    /// it in PlayerMovement.PendingVault. Returns true if the vault can start.
    /// </summary>
    public static bool TryStart(PlayerMovement player)
    {
        Vector3 dir = player.HasMoveInput ? player.MoveDirection : player.transform.forward;
        if (!player.EnvChecker.TryFindVault(dir, player.FeetY, out VaultInfo info))
            return false;

        player.PendingVault = info;
        return true;
    }

    public override void Enter()
    {
        base.Enter();
        ApproachSpeed   = player.HorizontalSpeed;
        StartFeetY      = player.FeetY;
        SpeedMultiplier = Mathf.Clamp(ApproachSpeed / ParkourTimings.VaultClipSpeed, 0.8f, 1.5f);

        // Closer than the clip's run-up: skip the first frames instead of sliding backward
        float closeness = 1f - Mathf.Clamp01(player.PendingVault.HandDistance / ParkourTimings.VaultClipHandReach);
        StartOffset = Mathf.Min(ParkourTimings.VaultHandContact * closeness, ParkourTimings.VaultMaxStartOffset);

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

        // Keep the momentum (GDD §5.4): run on at the approach speed; without input, carry part of it
        float speed = player.HasMoveInput
            ? (player.IsSprint ? player.SprintSpeed : Mathf.Max(ApproachSpeed, player.BaseSpeed * 0.8f))
            : ApproachSpeed * 0.4f;
        player.SetVelocity(player.transform.forward * speed, 0f);
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
    /// <summary>Hang reach from the ground: rise (m) of the top above the feet.</summary>
    public const float GroundMinRise = 1.9f, GroundMaxRise = 2.7f;
    /// <summary>Hang reach in the air: rise (m) of the top above the feet at the moment of the grab.</summary>
    public const float AirMinRise = 1.5f, AirMaxRise = 2.6f;
    /// <summary>Duration of the grab used only when no animation reports progress.</summary>
    public const float FallbackGrabTime = 0.6f;

    private bool _climbQueued;
    private bool _nextFromGround;

    public PlayerLedgeGrabState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    /// <summary>True if this grab started on the ground (the clip plays its jump).</summary>
    public bool FromGround { get; private set; }

    /// <summary>True once both hands hold the edge and the climb may start.</summary>
    public bool IsAttached
    {
        get
        {
            float progress = player.ParkourProgress;
            return progress >= 0f ? progress >= ParkourTimings.GrabAttached : Time.time - startTime >= FallbackGrabTime;
        }
    }

    /// <summary>Space in front of a wall whose top is within reach from the ground.</summary>
    public static bool TryStartFromGround(PlayerMovement player)
    {
        if (Time.time - player.LedgeReleaseTime < player.LedgeRegrabDelay) return false;
        Vector3 dir = player.HasMoveInput ? player.MoveDirection : player.transform.forward;
        if (!player.EnvChecker.TryFindLedge(dir, player.FeetY, GroundMinRise, GroundMaxRise, true, out LedgeInfo ledge))
            return false;

        player.CurrentLedge = ledge;
        player.LedgeGrabState._nextFromGround = true;
        return true;
    }

    /// <summary>A jump or fall that meets a ledge in front of the hands.</summary>
    public static bool TryStartInAir(PlayerMovement player)
    {
        if (Time.time - player.LedgeReleaseTime < player.LedgeRegrabDelay) return false;
        Vector3 dir = player.transform.forward;
        // Do not grab walls the player is steering away from
        if (player.HasMoveInput && Vector3.Dot(player.MoveDirection, dir) < -0.2f) return false;
        if (!player.EnvChecker.TryFindLedge(dir, player.FeetY, AirMinRise, AirMaxRise, false, out LedgeInfo ledge))
            return false;

        player.CurrentLedge = ledge;
        player.LedgeGrabState._nextFromGround = false;
        return true;
    }

    public override void Enter()
    {
        base.Enter();
        FromGround = _nextFromGround;
        _climbQueued = false;
        player.IsSprint = false;
        player.BeginRootMotion();
        if (player.ParkourAnimation == null)
        {
            // No animation drives the body: hang directly below the edge
            LedgeInfo l = player.CurrentLedge;
            player.transform.SetPositionAndRotation(
                l.Edge + l.Normal * 0.35f + Vector3.down * (1.8f - player.StandingHalfHeight),
                l.FacingRotation);
        }
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        // Space during the grab is remembered: the climb follows on as soon as the hands hold
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
        player.StopHorizontal(0f);
    }
}
