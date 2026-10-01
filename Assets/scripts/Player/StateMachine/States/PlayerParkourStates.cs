using UnityEngine;

// ────────────────────────────────────────────────────────────────────────────────
// PlayerVaultState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Vault over a low obstacle with Space (GDD §5.4), keeping the momentum.
/// Adapted from Dynamic Parkour System's VaultObstacle (MIT, Èric Canela): the body becomes
/// kinematic and is carried from the start to the landing point behind the obstacle while the
/// VaultFence animation plays; the left hand is pinned on the obstacle's top with IK, weighted by
/// the clip's "LHandCurve". Unlike the original, the obstacle is measured with rays
/// (EnvironmentChecker.TryFindVault) and an extra arc lifts the body over tall obstacles.
/// </summary>
public class PlayerVaultState : PlayerState
{
    /// <summary>Seconds the vault lasts. The animation is sped up to match.</summary>
    public const float VaultDuration = 0.6f;

    // How high the feet must clear the obstacle's top (m) beyond what the animation already lifts.
    private const float Clearance    = 0.15f;
    private const float AnimatedHop  = 0.45f; // hips rise of the VaultFence clip

    private Vector3 _startPos;
    private Vector3 _targetPos;
    private float   _arcHeight;

    public PlayerVaultState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    /// <summary>Left-hand IK target on the obstacle's top (valid while this state is active).</summary>
    public Vector3 HandTarget => player.PendingVault.HandPoint;

    /// <summary>Hand IK rotation: palm down, fingers along the vault direction.</summary>
    public Quaternion HandRotation => Quaternion.LookRotation(player.PendingVault.Direction, Vector3.up);

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
        VaultInfo info = player.PendingVault;

        player.FaceDirection(info.Direction);
        player.SetKinematic(true);
        _startPos  = player.Rb.position;
        _targetPos = info.LandingPoint + Vector3.up * player.StandingHalfHeight;

        float startFeet = _startPos.y - player.StandingHalfHeight;
        _arcHeight = Mathf.Max(0f, info.TopY + Clearance - startFeet - AnimatedHop);
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        float t = (Time.time - startTime) / VaultDuration;

        if (t >= 1f)
        {
            // Snap to the landing through the Rigidbody, never bypass physics
            player.Rb.MovePosition(_targetPos);
            stateMachine.ChangeState(player.HasMoveInput ? (PlayerState)player.RunState : player.IdleState);
            return;
        }

        Vector3 pos = Vector3.Lerp(_startPos, _targetPos, t);
        pos.y += Mathf.Sin(t * Mathf.PI) * _arcHeight;
        player.Rb.MovePosition(pos);
    }

    public override void Exit()
    {
        base.Exit();
        player.SetKinematic(false);

        // Keep the momentum (GDD §5.4): leave the obstacle at least at run speed
        float speed = player.IsSprint ? player.SprintSpeed : player.BaseSpeed;
        player.SetVelocity(player.PendingVault.Direction * speed, 0f);
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerLedgeGrabState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Hanging from a ledge (outside the GDD, kept by decision P2). Space climbs, the opposite
/// direction lets go. Animation: Dynamic Parkour System's "Idle To Braced Hang" → "Hanging Idle".
/// </summary>
public class PlayerLedgeGrabState : PlayerState
{
    public PlayerLedgeGrabState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        player.StopHorizontal(0f);
        player.SetKinematic(true);

        // Snap to ledge offset — always route through Rigidbody so the physics engine stays aware
        float grabOffsetX = 0.4f;
        float grabOffsetY = 1f;
        Vector3 snapPos = player.CurrentLedgeCorner - player.transform.forward * grabOffsetX - Vector3.up * grabOffsetY;
        player.Rb.MovePosition(snapPos);
    }

    public override void LogicUpdate()
    {
        base.LogicUpdate();

        if (player.JumpTriggered)
        {
            // Espacio presionado -> Subir el borde
            stateMachine.ChangeState(player.LedgeClimbState);
        }
        else if (player.HasMoveInput && Vector3.Dot(player.MoveDirection, player.transform.forward) < -0.5f)
        {
            // Soltarse si presiona la dirección opuesta al forward
            stateMachine.ChangeState(player.FallState);
        }
    }

    public override void Exit()
    {
        base.Exit();
        // Solo restaurar kinematic si NO vamos al ClimbState
        if (stateMachine.CurrentState != player.LedgeClimbState)
        {
            player.SetKinematic(false);
        }
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// PlayerLedgeClimbState
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Climbs from the hang onto the ledge (outside the GDD, kept by decision P2).
/// Animation: Dynamic Parkour System's "Braced Hang To Crouch", sped up to ClimbDuration.
/// </summary>
public class PlayerLedgeClimbState : PlayerState
{
    /// <summary>Seconds the climb lasts. The animation is sped up to match.</summary>
    public const float ClimbDuration = 0.9f;

    private Vector3 startPos;
    private Vector3 climbTargetPos;

    public PlayerLedgeClimbState(PlayerMovement player, PlayerStateMachine stateMachine) : base(player, stateMachine) { }

    public override void Enter()
    {
        base.Enter();
        startPos = player.Rb.position;
        // Stand on top of the ledge: the origin (torso center) ends half a body above the top
        // surface, a little past the corner so the feet are fully on it.
        climbTargetPos = player.CurrentLedgeCorner + player.transform.forward * 0.5f
                       + Vector3.up * (player.StandingHalfHeight + 0.05f);
    }

    public override void PhysicsUpdate()
    {
        base.PhysicsUpdate();
        float t = (Time.time - startTime) / ClimbDuration;

        if (t >= 1f)
        {
            // Final snap through the Rigidbody — never bypass physics
            player.Rb.MovePosition(climbTargetPos);
            stateMachine.ChangeState(player.IdleState);
        }
        else
        {
            // Two-phase movement: first go up, then slide forward onto the ledge
            Vector3 currentPos = startPos;
            if (t < 0.6f)
            {
                currentPos.y = Mathf.Lerp(startPos.y, climbTargetPos.y, t / 0.6f);
            }
            else
            {
                currentPos.y = climbTargetPos.y;
                Vector3 startXZ = new Vector3(startPos.x, 0, startPos.z);
                Vector3 targetXZ = new Vector3(climbTargetPos.x, 0, climbTargetPos.z);
                Vector3 currentXZ = Vector3.Lerp(startXZ, targetXZ, (t - 0.6f) / 0.4f);
                currentPos.x = currentXZ.x;
                currentPos.z = currentXZ.z;
            }
            player.Rb.MovePosition(currentPos);
        }
    }

    public override void Exit()
    {
        base.Exit();
        player.SetKinematic(false);
    }
}
