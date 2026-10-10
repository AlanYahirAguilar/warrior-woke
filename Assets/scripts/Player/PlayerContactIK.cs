using UnityEngine;

/// <summary>
/// Physical contact of hands and feet with the environment, solved with the Humanoid IK pass
/// (decision P22). MatchTarget (PlayerAnimator) brings the body to the right place; this keeps the
/// limbs on the real surfaces measured by EnvironmentChecker:
///  - Vault: each planted palm rests on its plant point on the top while the clip has it down (P36).
///  - Ledge grab / hang: both hands on the edge, the feet against the wall, and the body is nudged
///    so the animated hands meet the edge (the IK only does the last centimeters). Since P39
///    (2026-10-10) the grips are fixed where the hands arrive, an anatomical width apart and inside the
///    edge's ends, so a swaying hang no longer slides them (it slid 2.7 cm). On the final pose
///    (LateUpdate) each hand on the edge turns about its wrist so the palm rests on the top and the
///    fingers go over the edge (the clip held them upright, palms against the edge's corner).
///  - Climb: the hands stay on their grips while the body pulls up, then rest on the top surface.
///  - Mantle and slide: the supporting hand on the top or the floor, nothing through the block.
/// The feet on the ground (terrain, pelvis, foot lock) are not solved here since phase 3 of the motion
/// matching plan: GroundContactConstraint does it with Animation Rigging on the final pose (P31), also
/// while motion matching carries the body, where this IK pass of the controller was blended away (T25).
/// Called by PlayerAnimator.ApplyIK from OnAnimatorIK. No allocations.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerContactIK : MonoBehaviour
{
    [Header("Layers")]
    [Tooltip("Surfaces under the feet in a vault and under the hands in the slide.")]
    [SerializeField] private LayerMask groundLayer;
    [Tooltip("Walls and ledges the hands and feet touch while hanging.")]
    [SerializeField] private LayerMask wallLayer;

    [Header("Hang")]
    [Tooltip("Speed (1/s) at which the hanging body is nudged so its hands meet the edge.")]
    [SerializeField] private float hangAlignSpeed = 6f;

    /// <summary>Narrowest and widest distance (m) between the two grips on an edge (shoulder width and an arm's spread).</summary>
    public const float MinGripSpacing = 0.35f, MaxGripSpacing = 0.75f;

    /// <summary>Closest a grip gets to an end of the edge (m): the whole hand on the edge, not over its corner.</summary>
    public const float GripEndMargin = 0.08f;

    private static readonly AvatarIKGoal[] Hands = { AvatarIKGoal.LeftHand, AvatarIKGoal.RightHand };

    private PlayerMovement _movement;
    private float _gripL = float.NaN, _gripR = float.NaN; // fixed grips (m along the edge's tangent), NaN = free
    private Vector3 _gripEdge;                             // the edge they were fixed on
    private float _gripWeight;                             // how much the hands hold the edge this frame
    private Transform _handL, _handR, _indexL, _indexR, _littleL, _littleR, _middleL, _middleR;

    /// <summary>How far down (°) the fingers point over the top from the horizontal.</summary>
    private const float FingerPitch = 25f;

    /// <summary>The grips on the current edge (m along its tangent from LedgeInfo.Edge; NaN while not fixed), for the tests.</summary>
    public Vector2 Grips => new Vector2(_gripL, _gripR);

    private void Awake()
    {
        _movement = GetComponent<PlayerMovement>();
        if (groundLayer.value == 0) groundLayer = LayerMask.GetMask("Ground", "Obstacle");
        if (wallLayer.value == 0)   wallLayer   = LayerMask.GetMask("Obstacle");
        Animator animator = GetComponentInChildren<Animator>();
        if (animator != null && animator.isHuman)
        {
            _handL   = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _handR   = animator.GetBoneTransform(HumanBodyBones.RightHand);
            _indexL  = animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            _indexR  = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            _littleL = animator.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
            _littleR = animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            _middleL = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            _middleR = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
        }
    }

    /// <summary>
    /// On the final pose: a hand holding the edge turns about its wrist so the palm faces the top and the
    /// fingers point over the edge (into the top, FingerPitch down), as much as it holds the edge. The
    /// wrist does not move, so the contact the IK placed stays.
    /// </summary>
    private void LateUpdate()
    {
        if (_gripWeight <= 0f || _handL == null) return;
        PlayerState state = _movement.StateMachine.CurrentState;
        if (state != _movement.LedgeGrabState && state != _movement.LedgeClimbState && state != _movement.LedgeDropState)
        {
            _gripWeight = 0f;
            return;
        }
        LedgeInfo ledge = _movement.CurrentLedge;
        Vector3 over = -ledge.Normal;
        Vector3 finger = (over * Mathf.Cos(FingerPitch * Mathf.Deg2Rad) + Vector3.down * Mathf.Sin(FingerPitch * Mathf.Deg2Rad)).normalized;
        Vector3 palm = Vector3.ProjectOnPlane(Vector3.down, finger).normalized;
        Quaternion wanted = Quaternion.LookRotation(finger, -palm); // up = the back of the hand
        GripHand(_handL, _indexL, _littleL, _middleL, true, wanted);
        GripHand(_handR, _indexR, _littleR, _middleR, false, wanted);
    }

    private void GripHand(Transform hand, Transform index, Transform little, Transform middle, bool left, Quaternion wanted)
    {
        if (hand == null || index == null || little == null || middle == null) return;
        Vector3 h = hand.position;
        Vector3 finger = middle.position - h;
        // The palm's side: for a left hand, index × little points out of the palm; mirrored for the right
        Vector3 palm = Vector3.Cross(index.position - h, little.position - h);
        if (!left) palm = -palm;
        if (finger.sqrMagnitude < 1e-6f || palm.sqrMagnitude < 1e-8f) return;
        Quaternion current = Quaternion.LookRotation(finger.normalized, -palm.normalized);
        Quaternion turn = wanted * Quaternion.Inverse(current);
        hand.rotation = Quaternion.Slerp(Quaternion.identity, turn, _gripWeight) * hand.rotation;
    }

    /// <summary>
    /// Fixes the grips where the animated hands are (m along the edge), at least MinGripSpacing and at most
    /// MaxGripSpacing apart around their middle, and GripEndMargin inside the edge's ends.
    /// </summary>
    private void FixGrips(LedgeInfo ledge, Vector3 animLeft, Vector3 animRight)
    {
        Vector3 t = ledge.Tangent;
        float left = Vector3.Dot(animLeft - ledge.Edge, t), right = Vector3.Dot(animRight - ledge.Edge, t);
        if (left > right) (left, right) = (right, left); // the left hand on the left
        float middle = (left + right) * 0.5f;
        float half = Mathf.Clamp((right - left) * 0.5f, MinGripSpacing * 0.5f, MaxGripSpacing * 0.5f);
        if (EdgeExtent(ledge, out float min, out float max))
        {
            min += GripEndMargin + half;
            max -= GripEndMargin + half;
            middle = min <= max ? Mathf.Clamp(middle, min, max) : (min + max) * 0.5f;
        }
        _gripL = middle - half;
        _gripR = middle + half;
        _gripEdge = ledge.Edge;
    }

    /// <summary>
    /// Where the edge ends (m along its tangent from LedgeInfo.Edge): the measured face collider's box (its
    /// own orientation, so a turned ledge is exact), or its bounds.
    /// </summary>
    private static bool EdgeExtent(LedgeInfo ledge, out float min, out float max)
    {
        min = float.MinValue;
        max = float.MaxValue;
        Collider face = ledge.Face;
        if (face == null) return false;
        Vector3 t = ledge.Tangent;
        min = float.MaxValue;
        max = float.MinValue;
        if (face is BoxCollider box)
        {
            Transform tr = box.transform;
            Vector3 e = box.size * 0.5f;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = box.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                float d = Vector3.Dot(tr.TransformPoint(corner) - ledge.Edge, t);
                min = Mathf.Min(min, d);
                max = Mathf.Max(max, d);
            }
        }
        else
        {
            Bounds b = face.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                float d = Vector3.Dot(corner - ledge.Edge, t);
                min = Mathf.Min(min, d);
                max = Mathf.Max(max, d);
            }
        }
        return true;
    }

    /// <summary>The wrist on the edge for a hand: at its fixed grip, or at the animated hand's place along the edge.</summary>
    private Vector3 GripOnEdge(LedgeInfo ledge, bool left, Vector3 animatedHand)
    {
        float grip = left ? _gripL : _gripR;
        if (float.IsNaN(grip)) return HandOnEdge(ledge, animatedHand);
        return ledge.Edge + ledge.Tangent * grip + Vector3.up * ParkourTimings.WristAboveSurface + ledge.Normal * ParkourTimings.WristOutOfFace;
    }

    /// <summary>Solves every IK goal for this frame.</summary>
    public void Solve(Animator animator)
    {
        ClearGoals(animator);
        PlayerState state = _movement.StateMachine.CurrentState;
        float progress = _movement.ParkourProgress;
        bool ledgeState = state == _movement.LedgeGrabState || state == _movement.LedgeClimbState || state == _movement.LedgeDropState;
        if (!ledgeState || (_movement.CurrentLedge.Edge - _gripEdge).sqrMagnitude > 0.0001f) _gripL = _gripR = float.NaN;

        if (state == _movement.VaultState)
            SolveVault(animator);
        else if (state == _movement.LedgeGrabState)
            SolveHang(animator, progress);
        else if (state == _movement.LedgeClimbState)
            SolveClimb(animator, progress);
        else if (state == _movement.LedgeDropState)
            SolveClimb(animator, 1f - progress); // the climb in reverse: hands onto the edge at the end
        else if (state == _movement.MantleState)
            SolveMantle(animator, progress);

        if (state == _movement.SlideState)
            SolveHandsOnGround(animator);
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

    /// <summary>
    /// Vault (P36): each planted palm rests on its plant point on the top while the clip has it down
    /// (it eases in just before the plant and out after that hand's release, which the planner brings
    /// forward when the arm no longer reaches; a second contact only if the planner put it on this
    /// top), a hand over the top never goes through it, and the feet follow
    /// the clip's own goals (the mocap's Humanoid Foot IK) but never go inside the obstacle or under
    /// the floor (a lowered body for a low obstacle bends the stance leg instead).
    /// </summary>
    private void SolveVault(Animator animator)
    {
        PlayerVaultState vault = _movement.VaultState;
        VaultPlan plan = vault.Plan;
        VaultVariant v = plan.Variant;
        if (v == null) return;
        float t = vault.ClipTime;
        AvatarIKGoal first = v.RightHand ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;
        AvatarIKGoal second = v.SecondRightHand ? AvatarIKGoal.RightHand : AvatarIKGoal.LeftHand;
        float w1 = ContactWeight(t, v.Plant, plan.PlantRelease);
        float w2 = plan.HasSecondPlant ? ContactWeight(t, v.SecondPlant, plan.SecondRelease) : 0f;
        foreach (AvatarIKGoal goal in Hands)
        {
            if (goal == second && w2 > 0f && (second != first || w2 >= w1))
                SetGoal(animator, goal, plan.SecondPlantPoint, w2);
            else if (goal == first && w1 > 0f)
                SetGoal(animator, goal, plan.PlantPoint, w1);
            else
                HandAboveTop(animator, goal, plan);
        }

        FootOverObstacle(animator, AvatarIKGoal.LeftFoot, animator.leftFeetBottomHeight);
        FootOverObstacle(animator, AvatarIKGoal.RightFoot, animator.rightFeetBottomHeight);
    }

    /// <summary>Seconds the palm takes to settle on the top before the plant and to leave it after the release.</summary>
    private const float ContactEase = 0.08f;

    private static float ContactWeight(float t, float plant, float release) =>
        Mathf.InverseLerp(plant - ContactEase, plant, t) * (1f - Mathf.InverseLerp(release, release + ContactEase, t));

    /// <summary>A free hand over the top is kept on or above its surface.</summary>
    private static void HandAboveTop(Animator animator, AvatarIKGoal goal, VaultPlan plan)
    {
        Vector3 hand = animator.GetIKPosition(goal);
        float along = Vector3.Dot(hand - plan.FrontEdge, plan.Direction);
        float minY = plan.TopY + ParkourTimings.WristAboveSurface;
        if (along >= 0f && along <= plan.Depth && hand.y < minY)
            SetGoal(animator, goal, new Vector3(hand.x, minY, hand.z), 1f);
    }

    /// <summary>
    /// A foot at the clip's goal (weight 1: the mocap's own Foot IK), lifted onto the top while it is
    /// over the obstacle (or about to be), so a leg never cuts through it, and never below the surface
    /// under it.
    /// </summary>
    private void FootOverObstacle(Animator animator, AvatarIKGoal goal, float bottomHeight)
    {
        VaultPlan plan = _movement.VaultState.Plan;
        Vector3 anim = animator.GetIKPosition(goal);
        float minY = float.NegativeInfinity;
        float along = Vector3.Dot(anim - plan.FrontEdge, plan.Direction); // > 0: past the front face
        if (along >= -0.15f && along <= plan.Depth + 0.1f)
            minY = plan.TopY + bottomHeight + 0.03f;
        Vector3 origin = new Vector3(anim.x, Mathf.Max(anim.y, plan.TopY) + 0.3f, anim.z);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 3f, groundLayer, QueryTriggerInteraction.Ignore))
            minY = Mathf.Max(minY, hit.point.y + bottomHeight);
        SetGoal(animator, goal, new Vector3(anim.x, Mathf.Max(anim.y, minY), anim.z), 1f);
    }

    // ─── Ledge ───────────────────────────────────────────────────────────────────

    private void SolveHang(Animator animator, float progress)
    {
        LedgeInfo ledge = _movement.CurrentLedge;
        float handWeight = Mathf.InverseLerp(ParkourTimings.GrabHandContact - 0.12f, ParkourTimings.GrabHandContact, progress);
        float footWeight = Mathf.InverseLerp(ParkourTimings.GrabAttached, ParkourTimings.GrabAttached + 0.15f, progress);

        Vector3 animLeft  = animator.GetIKPosition(AvatarIKGoal.LeftHand);
        Vector3 animRight = animator.GetIKPosition(AvatarIKGoal.RightHand);
        bool held = progress >= ParkourTimings.GrabHandContact || _movement.LedgeGrabState.FromDrop;
        if (held && float.IsNaN(_gripL)) FixGrips(ledge, animLeft, animRight);
        Vector3 left  = GripOnEdge(ledge, true, animLeft);
        Vector3 right = GripOnEdge(ledge, false, animRight);

        // Once the hands arrived, move the body so the animated hands meet the grips by themselves
        if (held)
            AlignBodyToHands(animLeft, animRight, left, right, hangAlignSpeed);
        _gripWeight = handWeight;

        SetGoal(animator, AvatarIKGoal.LeftHand, left, InsideWall(ledge, animLeft) ? 1f : handWeight);
        SetGoal(animator, AvatarIKGoal.RightHand, right, InsideWall(ledge, animRight) ? 1f : handWeight);
        FootOnWall(animator, AvatarIKGoal.LeftFoot, ledge, footWeight);
        FootOnWall(animator, AvatarIKGoal.RightFoot, ledge, footWeight);
    }

    private void SolveClimb(Animator animator, float progress)
    {
        if (progress < 0f) progress = 0f;
        LedgeInfo ledge = _movement.CurrentLedge;

        // While pulling up the body follows the hands, which stay where they grabbed (the drop fixes its
        // grips as its hands come onto the edge)
        Vector3 animLeft  = animator.GetIKPosition(AvatarIKGoal.LeftHand);
        Vector3 animRight = animator.GetIKPosition(AvatarIKGoal.RightHand);
        if (float.IsNaN(_gripL) && progress < ParkourTimings.ClimbHandsRelease + 0.15f) FixGrips(ledge, animLeft, animRight);
        if (progress < ParkourTimings.ClimbHandsRelease)
            AlignBodyToHands(animLeft, animRight, GripOnEdge(ledge, true, animLeft), GripOnEdge(ledge, false, animRight), hangAlignSpeed * 2f);

        // Hands: on their grips while pulling up, then resting on the top surface while the body goes over
        float toSurface = Mathf.InverseLerp(ParkourTimings.ClimbHandsRelease, ParkourTimings.ClimbHandsRelease + 0.15f, progress);
        float handWeight = 1f - Mathf.InverseLerp(0.8f, 0.92f, progress);
        _gripWeight = handWeight * (1f - toSurface); // gripping the edge until the hands move onto the top
        SolveClimbHand(animator, AvatarIKGoal.LeftHand, true, ledge, toSurface, handWeight);
        SolveClimbHand(animator, AvatarIKGoal.RightHand, false, ledge, toSurface, handWeight);

        // Feet push on the wall at the start of the climb, then step up freely
        float footWeight = 1f - Mathf.InverseLerp(0.15f, 0.35f, progress);
        FootOnWall(animator, AvatarIKGoal.LeftFoot, ledge, footWeight);
        FootOnWall(animator, AvatarIKGoal.RightFoot, ledge, footWeight);
    }

    private void SolveClimbHand(Animator animator, AvatarIKGoal goal, bool left, LedgeInfo ledge, float toSurface, float weight)
    {
        if (weight <= 0f) return;
        Vector3 anim = animator.GetIKPosition(goal);
        Vector3 onEdge = GripOnEdge(ledge, left, anim);

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

    // ─── Mantle and slide ────────────────────────────────────────────────────────

    /// <summary>
    /// Mantle: the supporting hand rests on the top while the clip plants it (no floating hand on a
    /// taller or lower top), and a foot over the block never sinks into it.
    /// </summary>
    private void SolveMantle(Animator animator, float progress)
    {
        LedgeInfo top = _movement.CurrentLedge;
        float plant = Mathf.InverseLerp(ParkourTimings.MantleHandPlant, ParkourTimings.MantleHandPlant + 0.05f, progress)
                    * (1f - Mathf.InverseLerp(ParkourTimings.MantleHandRelease - 0.05f, ParkourTimings.MantleHandRelease, progress));
        foreach (AvatarIKGoal goal in Hands)
        {
            Vector3 hand = animator.GetIKPosition(goal);
            float y = top.TopY + ParkourTimings.WristAboveSurface;
            if (goal == AvatarIKGoal.LeftHand && plant > 0f)
            {
                // The supporting palm on its point of the top from the moment it plants. The body's warp
                // toward the measured top runs on until MantleHandMatchEnd (spread out so it does not
                // pop in a 0.67 s clip), and without this the "planted" palm slid onto the top while it
                // did (still at the edge at 0.35)
                SetGoal(animator, goal, _movement.MantleState.HandTarget, plant);
                continue;
            }
            if (Vector3.Dot(hand - top.Edge, top.Normal) > -0.03f) continue; // not over the top
            if (hand.y < y)
                SetGoal(animator, goal, new Vector3(hand.x, y, hand.z), 1f);      // never through the top
        }
        FootAboveTop(animator, AvatarIKGoal.LeftFoot, animator.leftFeetBottomHeight, top);
        FootAboveTop(animator, AvatarIKGoal.RightFoot, animator.rightFeetBottomHeight, top);
    }

    private static void FootAboveTop(Animator animator, AvatarIKGoal goal, float bottom, LedgeInfo top)
    {
        Vector3 foot = animator.GetIKPosition(goal);
        if (Vector3.Dot(foot - top.Edge, top.Normal) > 0.05f) return;    // still in front of the face
        float minY = top.TopY + bottom + 0.02f;
        if (foot.y < minY) SetGoal(animator, goal, new Vector3(foot.x, minY, foot.z), 1f);
    }

    /// <summary>
    /// Sliding, a hand that trails along the floor rests on it instead of sinking into it: the wrist
    /// stays HandOnGround above the surface under it. Lying back, the shoulder's joint limits keep the
    /// arm from lifting the hand by itself, so the body leans on that hand: the torso rises the
    /// missing centimeters (up to MaxHandSupport). A hand in the air is left alone.
    /// </summary>
    private void SolveHandsOnGround(Animator animator)
    {
        float support = 0f;
        foreach (AvatarIKGoal goal in Hands)
        {
            Vector3 anim = animator.GetIKPosition(goal);
            if (!Physics.Raycast(anim + Vector3.up * 0.4f, Vector3.down, out RaycastHit hit, 0.8f, groundLayer, QueryTriggerInteraction.Ignore))
                continue;
            float minY = hit.point.y + HandOnGround;
            if (anim.y >= minY) continue;
            support = Mathf.Max(support, minY - anim.y);
            SetGoal(animator, goal, new Vector3(anim.x, minY, anim.z), 1f);
        }
        animator.bodyPosition += Vector3.up * Mathf.Min(support, MaxHandSupport);
    }

    /// <summary>Most the torso rises to lean on a hand that rests on the floor (m).</summary>
    private const float MaxHandSupport = 0.06f;

    /// <summary>Height of the wrist above a surface the palm rests flat on (m).</summary>
    private const float HandOnGround = 0.05f;

    private static void SetGoal(Animator animator, AvatarIKGoal goal, Vector3 position, float weight)
    {
        animator.SetIKPositionWeight(goal, weight);
        animator.SetIKPosition(goal, position);
    }
}
