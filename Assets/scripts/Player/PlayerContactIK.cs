using UnityEngine;

/// <summary>
/// Physical contact of hands and feet with the environment, solved with the Humanoid IK pass
/// (decision P22). MatchTarget (PlayerAnimator) brings the body to the right place; this keeps the
/// limbs on the real surfaces measured by EnvironmentChecker:
///  - Vault: the left hand rests on the obstacle's top while the clip's LHandCurve says it is planted.
///  - Ledge grab / hang: both hands on the edge, the feet against the wall, and the body is nudged
///    so the animated hands meet the edge (the IK only does the last centimeters).
///  - Climb: the hands stay on the edge while the body pulls up, then rest on the top surface.
///  - On the ground: the feet follow the terrain (steps, curbs) and never sink into it; the pelvis
///    drops when one foot stands lower (approach of Dynamic Parkour System's foot IK, MIT).
/// Called by PlayerAnimator.ApplyIK from OnAnimatorIK. No allocations.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerContactIK : MonoBehaviour
{
    [Header("Layers")]
    [Tooltip("Surfaces the feet stand on.")]
    [SerializeField] private LayerMask groundLayer;
    [Tooltip("Walls and ledges the hands and feet touch while hanging.")]
    [SerializeField] private LayerMask wallLayer;

    [Header("Feet on the ground")]
    [Tooltip("Ray origin above the body's floor when looking for the ground under a foot (m).")]
    [SerializeField] private float footRayAbove = 0.5f;
    [Tooltip("How far below the body's floor a foot may reach for lower ground (m).")]
    [SerializeField] private float footRayBelow = 0.45f;
    [Tooltip("Largest pelvis drop to reach a lower foothold (m).")]
    [SerializeField] private float maxPelvisDrop = 0.35f;
    [Tooltip("Speed of the pelvis adjustment (1/s).")]
    [SerializeField] private float pelvisSpeed = 10f;
    [Tooltip("Speed at which the ground IK fades out when the body leaves the ground (1/s).")]
    [SerializeField] private float groundFadeSpeed = 8f;

    [Header("Hang")]
    [Tooltip("Speed (1/s) at which the hanging body is nudged so its hands meet the edge.")]
    [SerializeField] private float hangAlignSpeed = 6f;

    private PlayerMovement _movement;
    private float _pelvisOffset;
    private float _groundWeight;

    private void Awake()
    {
        _movement = GetComponent<PlayerMovement>();
        if (groundLayer.value == 0) groundLayer = LayerMask.GetMask("Ground", "Obstacle");
        if (wallLayer.value == 0)   wallLayer   = LayerMask.GetMask("Obstacle");
    }

    /// <summary>Solves every IK goal for this frame.</summary>
    public void Solve(Animator animator)
    {
        ClearGoals(animator);
        PlayerState state = _movement.StateMachine.CurrentState;
        float progress = _movement.ParkourProgress;

        bool onGround = _movement.IsGrounded && !_movement.IsRootMotionDriven &&
                        (state == _movement.IdleState || state == _movement.RunState || state == _movement.SlideState ||
                         state == _movement.BlockState || state == _movement.LightAttackState || state == _movement.HeavyAttackState);
        // On the ground the feet are solved at once (a landing must not sink); leaving it fades out,
        // except into a parkour action, whose own contacts take over the feet immediately
        _groundWeight = onGround ? 1f
                      : _movement.IsRootMotionDriven ? 0f
                      : Mathf.MoveTowards(_groundWeight, 0f, groundFadeSpeed * Time.deltaTime);

        if (state == _movement.VaultState)
            SolveVault(animator);
        else if (state == _movement.LedgeGrabState)
            SolveHang(animator, progress);
        else if (state == _movement.LedgeClimbState)
            SolveClimb(animator, progress);

        if (_groundWeight > 0f)
            SolveGroundFeet(animator, _groundWeight);
        else
            _pelvisOffset = 0f;
    }

    private static void ClearGoals(Animator animator)
    {
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
        animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
        animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
        animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
    }

    // ─── Vault ───────────────────────────────────────────────────────────────────

    private void SolveVault(Animator animator)
    {
        float weight = Mathf.Clamp01(animator.GetFloat(PlayerAnimatorIds.LHandCurveParam));
        if (weight > 0f)
            SetGoal(animator, AvatarIKGoal.LeftHand, _movement.VaultState.HandTarget, weight);

        FootOverObstacle(animator, AvatarIKGoal.LeftFoot, animator.leftFeetBottomHeight);
        FootOverObstacle(animator, AvatarIKGoal.RightFoot, animator.rightFeetBottomHeight);
    }

    /// <summary>A foot passing over the obstacle (or about to) is kept above its top surface.</summary>
    private void FootOverObstacle(Animator animator, AvatarIKGoal goal, float bottomHeight)
    {
        VaultInfo vault = _movement.PendingVault;
        Vector3 anim = animator.GetIKPosition(goal);
        float along = Vector3.Dot(anim - vault.FrontPoint, vault.Direction); // > 0: past the front face
        if (along < -0.2f || along > vault.Depth + 0.1f) return;

        float minY = vault.TopY + bottomHeight + 0.03f;
        if (anim.y >= minY) return;
        float weight = along >= 0f ? 1f : Mathf.InverseLerp(-0.2f, 0f, along);
        SetGoal(animator, goal, new Vector3(anim.x, minY, anim.z), weight);
    }

    // ─── Ledge ───────────────────────────────────────────────────────────────────

    private void SolveHang(Animator animator, float progress)
    {
        LedgeInfo ledge = _movement.CurrentLedge;
        float handWeight = Mathf.InverseLerp(ParkourTimings.GrabHandContact - 0.12f, ParkourTimings.GrabHandContact, progress);
        float footWeight = Mathf.InverseLerp(ParkourTimings.GrabAttached, ParkourTimings.GrabAttached + 0.15f, progress);

        Vector3 animLeft  = animator.GetIKPosition(AvatarIKGoal.LeftHand);
        Vector3 animRight = animator.GetIKPosition(AvatarIKGoal.RightHand);
        Vector3 left  = HandOnEdge(ledge, animLeft);
        Vector3 right = HandOnEdge(ledge, animRight);

        // Once the hands arrived, move the body so the animated hands meet the edge by themselves
        if (progress >= ParkourTimings.GrabHandContact)
            AlignBodyToHands(animLeft, animRight, left, right, hangAlignSpeed);

        SetGoal(animator, AvatarIKGoal.LeftHand, left, InsideWall(ledge, animLeft) ? 1f : handWeight);
        SetGoal(animator, AvatarIKGoal.RightHand, right, InsideWall(ledge, animRight) ? 1f : handWeight);
        FootOnWall(animator, AvatarIKGoal.LeftFoot, ledge, footWeight);
        FootOnWall(animator, AvatarIKGoal.RightFoot, ledge, footWeight);
    }

    private void SolveClimb(Animator animator, float progress)
    {
        if (progress < 0f) progress = 0f;
        LedgeInfo ledge = _movement.CurrentLedge;

        // While pulling up the body follows the hands, which stay where they grabbed
        if (progress < ParkourTimings.ClimbHandsRelease)
        {
            Vector3 animLeft  = animator.GetIKPosition(AvatarIKGoal.LeftHand);
            Vector3 animRight = animator.GetIKPosition(AvatarIKGoal.RightHand);
            AlignBodyToHands(animLeft, animRight, HandOnEdge(ledge, animLeft), HandOnEdge(ledge, animRight), hangAlignSpeed * 2f);
        }

        // Hands: on the edge while pulling up, then resting on the top surface while the body goes over
        float toSurface = Mathf.InverseLerp(ParkourTimings.ClimbHandsRelease, ParkourTimings.ClimbHandsRelease + 0.15f, progress);
        float handWeight = 1f - Mathf.InverseLerp(0.8f, 0.92f, progress);
        SolveClimbHand(animator, AvatarIKGoal.LeftHand, ledge, toSurface, handWeight);
        SolveClimbHand(animator, AvatarIKGoal.RightHand, ledge, toSurface, handWeight);

        // Feet push on the wall at the start of the climb, then step up freely
        float footWeight = 1f - Mathf.InverseLerp(0.15f, 0.35f, progress);
        FootOnWall(animator, AvatarIKGoal.LeftFoot, ledge, footWeight);
        FootOnWall(animator, AvatarIKGoal.RightFoot, ledge, footWeight);
    }

    private static void SolveClimbHand(Animator animator, AvatarIKGoal goal, LedgeInfo ledge, float toSurface, float weight)
    {
        if (weight <= 0f) return;
        Vector3 anim = animator.GetIKPosition(goal);
        Vector3 onEdge = HandOnEdge(ledge, anim);

        // On the top surface: keep the animated position but never below the surface while over it
        Vector3 onTop = anim;
        float wristY = ledge.TopY + ParkourTimings.WristAboveSurface;
        if (Vector3.Dot(anim - ledge.Edge, ledge.Normal) < ParkourTimings.WristOutOfFace)
            onTop.y = Mathf.Max(anim.y, wristY);
        else
            onTop = onEdge;

        SetGoal(animator, goal, Vector3.Lerp(onEdge, onTop, toSurface), weight);
    }

    /// <summary>Moves the (kinematic) body by part of the gap between the animated hands and their goals.</summary>
    private void AlignBodyToHands(Vector3 animLeft, Vector3 animRight, Vector3 goalLeft, Vector3 goalRight, float speed)
    {
        Vector3 error = (goalLeft + goalRight) * 0.5f - (animLeft + animRight) * 0.5f;
        _movement.ApplyRootMotion(error * Mathf.Clamp01(speed * Time.deltaTime), Quaternion.identity);
    }

    private static bool InsideWall(LedgeInfo ledge, Vector3 point) =>
        point.y < ledge.TopY && Vector3.Dot(point - ledge.Edge, ledge.Normal) < 0f;

    /// <summary>Wrist goal on the edge at the lateral position of the animated hand.</summary>
    private static Vector3 HandOnEdge(LedgeInfo ledge, Vector3 animatedHand)
    {
        return ledge.EdgeAt(animatedHand)
             + Vector3.up * ParkourTimings.WristAboveSurface
             + ledge.Normal * ParkourTimings.WristOutOfFace;
    }

    /// <summary>
    /// Places a foot against the wall below the ledge (braced hang), keeping its animated height. A
    /// foot the animation puts inside the wall is always pushed out, whatever the weight.
    /// </summary>
    private void FootOnWall(Animator animator, AvatarIKGoal goal, LedgeInfo ledge, float weight)
    {
        Vector3 anim = animator.GetIKPosition(goal);
        if (anim.y > ledge.TopY - 0.3f) return; // the foot is above the wall's face: nothing to rest on

        Vector3 origin = anim + ledge.Normal * 0.6f;
        if (!Physics.Raycast(origin, -ledge.Normal, out RaycastHit wall, 1.2f, wallLayer, QueryTriggerInteraction.Ignore))
            return; // free hang: no wall under the ledge

        float distance = Vector3.Dot(anim - wall.point, ledge.Normal);
        if (distance < ParkourTimings.AnkleFromWall) weight = 1f; // inside or touching: never through the wall
        if (weight <= 0f) return;
        Vector3 target = anim - ledge.Normal * (distance - ParkourTimings.AnkleFromWall);
        SetGoal(animator, goal, target, weight);
    }

    // ─── Ground ──────────────────────────────────────────────────────────────────

    private void SolveGroundFeet(Animator animator, float weight)
    {
        float floorY = animator.transform.position.y; // the model root stands on the body's floor
        bool leftHit  = FootOnGround(animator, AvatarIKGoal.LeftFoot, animator.leftFeetBottomHeight, floorY, weight, out float leftOffset);
        bool rightHit = FootOnGround(animator, AvatarIKGoal.RightFoot, animator.rightFeetBottomHeight, floorY, weight, out float rightOffset);

        // Lower the pelvis so the foot on the lower foothold can reach it (never raise it)
        float drop = 0f;
        if (leftHit)  drop = Mathf.Min(drop, leftOffset);
        if (rightHit) drop = Mathf.Min(drop, rightOffset);
        drop = Mathf.Max(drop, -maxPelvisDrop);
        _pelvisOffset = Mathf.Lerp(_pelvisOffset, drop, Mathf.Clamp01(pelvisSpeed * Time.deltaTime));
        animator.bodyPosition += Vector3.up * (_pelvisOffset * weight);
    }

    /// <summary>
    /// Follows the terrain under the foot (offset from the body's floor) and keeps the sole above the
    /// ground. On flat ground the animated pose is left as it is.
    /// </summary>
    private bool FootOnGround(Animator animator, AvatarIKGoal goal, float bottomHeight, float floorY, float weight, out float offset)
    {
        offset = 0f;
        Vector3 anim = animator.GetIKPosition(goal);
        Vector3 origin = new Vector3(anim.x, floorY + footRayAbove, anim.z);
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, footRayAbove + footRayBelow, groundLayer, QueryTriggerInteraction.Ignore) ||
            hit.normal.y < 0.6f)
            return false;

        offset = hit.point.y - floorY;
        float targetY = Mathf.Max(anim.y + offset, hit.point.y + bottomHeight);
        SetGoal(animator, goal, new Vector3(anim.x, targetY, anim.z), weight);

        // Align a foot that touches the ground with its slope
        if (anim.y + offset - hit.point.y < bottomHeight + 0.08f)
        {
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * animator.GetIKRotation(goal);
            animator.SetIKRotationWeight(goal, weight);
            animator.SetIKRotation(goal, rotation);
        }
        return true;
    }

    private static void SetGoal(Animator animator, AvatarIKGoal goal, Vector3 position, float weight)
    {
        animator.SetIKPositionWeight(goal, weight);
        animator.SetIKPosition(goal, position);
    }
}
