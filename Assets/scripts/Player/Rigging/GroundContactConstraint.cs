using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;

/// <summary>
/// Feet on the ground with Animation Rigging (decision P31): a custom constraint that runs on the final
/// pose, after the Animator Controller and the motion matching graph have blended (so it also works while
/// motion matching carries the body, where the Humanoid IK pass of the controller was blended away, T25).
/// Every frame, for each foot:
///  - the terrain under it shifts the animated foot by its height difference from the floor the animation
///    stands on (a curb, a step), and neither the sole nor the toes end below the ground; a foot that
///    touches the ground turns with its slope;
///  - the pelvis drops so the foot on the lower foothold can reach it (approach of the Dynamic Parkour
///    System's foot IK, MIT, which PlayerContactIK used before);
///  - a planted foot is locked where it planted while the animation keeps it down, so the stance foot does
///    not skate (released when the clip lifts it, or when the animation drifts away from the lock). The
///    lock holds the point the foot stands on: the heel, or the ball of the foot once the heel rises, so a
///    foot pivoting on its ball (a turn, a strafe step) turns about it instead of dragging it.
/// The job works on the Humanoid IK goals of the feet and solves them (AnimationHumanStream.SolveIK), not
/// on the bones: the Humanoid Foot IK of the mocap clips is solved after every output, so bones moved by
/// this rig were put back by it (measured: MxMLocomotionProbe gave the same feet with and without a bone
/// IK). The animated foot is the clip's Foot IK goal as far as the source uses Foot IK
/// (<see cref="FootIKWeight"/>: the mocap's retarget is only clean with it) and the pose's foot otherwise.
/// The ground under each foot is sampled on the main thread at the end of the frame (LateUpdate: physics
/// queries are not allowed in animation jobs) and used by the next evaluation. The body can move after
/// the pose is evaluated (root motion applied afterwards, the player's motor in LateUpdate): that move is
/// measured every frame and predicted for the next, so a locked foot stays still once the body has moved.
/// The weight (0 in the air and in parkour actions), whether a foot may lock and the Foot IK of the source
/// are set by the host (PlayerRig on the player; the motion matching probe uses the defaults).
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)] // samples the ground after the motor and PlayerAnimator's final-pose work
[AddComponentMenu("Warrior Woke/Ground Contact Constraint")]
public class GroundContactConstraint : RigConstraint<GroundContactJob, GroundContactData, GroundContactBinder>
{
    /// <summary>A foot may lock where it plants (host decision: locomotion, guard; not attacks or the slide).</summary>
    public bool LockAllowed
    {
        get => _lockAllowed;
        set
        {
            _lockAllowed = value;
            if (_input.IsCreated) _input[GroundContactJob.InLock] = value ? 1f : 0f;
        }
    }

    /// <summary>
    /// How much the playing animation uses the Humanoid Foot IK (0 … 1): motion matching and the mocap
    /// clips do; the rest of the Animator Controller's clips do not.
    /// </summary>
    public float FootIKWeight
    {
        get => _footIK;
        set
        {
            _footIK = Mathf.Clamp01(value);
            if (_input.IsCreated) _input[GroundContactJob.InFootIK] = _footIK;
        }
    }

    private bool _lockAllowed = true;
    private float _footIK = 1f;

    /// <summary>Pelvis offset applied in the last evaluation (m, negative = lowered).</summary>
    public float PelvisOffset => _state.IsCreated ? _state[GroundContactJob.StatePelvis] : 0f;

    /// <summary>Blend of the left / right foot's lock in the last evaluation (0 free … 1 locked).</summary>
    public float LockWeight(bool left) => _state.IsCreated ? _state[GroundContactJob.FootState(left) + GroundContactJob.FootLockWeight] : 0f;

    /// <summary>Height (m) of the foot's lowest contact above its ground, and the speed (m/s) of the point it stands on, in the last evaluation (diagnostic).</summary>
    public Vector2 Contact(bool left) => _state.IsCreated
        ? new Vector2(_state[GroundContactJob.FootState(left) + GroundContactJob.FootHeight], _state[GroundContactJob.FootState(left) + GroundContactJob.FootSpeed])
        : Vector2.zero;

    /// <summary>Times a foot engaged its lock (diagnostic for the Play Mode tests).</summary>
    public int LockCount => _state.IsCreated ? (int)_state[GroundContactJob.StateLockCount] : 0;

    private NativeArray<float> _input, _state;
    private Vector3 _lastFootL, _lastFootR, _lastToeL, _lastToeR;
    private bool _hasLast;

    /// <summary>
    /// The buffers shared with the job (the rig may rebuild its jobs; the buffers live as long as this
    /// component). The main thread writes the inputs between evaluations, never during one.
    /// </summary>
    internal void GetBuffers(out NativeArray<float> input, out NativeArray<float> state)
    {
        if (!_input.IsCreated) _input = new NativeArray<float>(GroundContactJob.InputSize, Allocator.Persistent);
        if (!_state.IsCreated) _state = new NativeArray<float>(GroundContactJob.StateSize, Allocator.Persistent);
        _input[GroundContactJob.InLock] = LockAllowed ? 1f : 0f;
        _input[GroundContactJob.InFootIK] = _footIK;
        _input[GroundContactJob.InReset] = 1f;
        _hasLast = false;
        input = _input;
        state = _state;
    }

    private void OnDestroy()
    {
        if (_input.IsCreated) _input.Dispose();
        if (_state.IsCreated) _state.Dispose();
    }

    /// <summary>Forgets the locks and the measured motion (a teleport, a respawn).</summary>
    public void ResetContacts()
    {
        _hasLast = false;
        if (_input.IsCreated) _input[GroundContactJob.InReset] = 1f;
    }

    private void LateUpdate()
    {
        if (!_input.IsCreated || m_Data.root == null || Time.deltaTime <= 0f) return;
        float dt = Time.deltaTime;
        Transform root = m_Data.root;
        // The body that carries the model (the player's root; the model itself when it has no parent): the
        // model's own position also has the final-pose guards' nudges of this frame
        Transform body = root.parent != null ? root.parent : root;
        Vector3 footL = m_Data.leftFoot.position, footR = m_Data.rightFoot.position;

        // How far the body moved after the last evaluation saw it: the next evaluation predicts the same
        // move. A jump of more than a metre is a teleport: start over
        Vector3 velocity = Vector3.zero;
        float yawRate = 0f;
        if (_state[GroundContactJob.StateEvalValid] > 0f)
        {
            Vector3 after = body.position - new Vector3(_state[GroundContactJob.StateEvalX], 0f, _state[GroundContactJob.StateEvalZ]);
            after.y = 0f;
            if (after.sqrMagnitude > 1f)
                ResetContacts();
            else
            {
                velocity = after / dt;
                yawRate = Mathf.DeltaAngle(_state[GroundContactJob.StateEvalYaw], body.eulerAngles.y) / dt;
            }
            _state[GroundContactJob.StateEvalValid] = 0f;
        }
        _input[GroundContactJob.InVelocity]     = velocity.x;
        _input[GroundContactJob.InVelocity + 1] = velocity.z;
        _input[GroundContactJob.InYawRate]      = yawRate;

        bool toes = m_Data.leftToes != null && m_Data.rightToes != null;
        Vector3 toeL = toes ? m_Data.leftToes.position : footL, toeR = toes ? m_Data.rightToes.position : footR;
        Vector3 vFootL = Vector3.zero, vFootR = Vector3.zero, vToeL = Vector3.zero, vToeR = Vector3.zero;
        if (_hasLast)
        {
            vFootL = (footL - _lastFootL) / dt;
            vFootR = (footR - _lastFootR) / dt;
            vToeL = (toeL - _lastToeL) / dt;
            vToeR = (toeR - _lastToeR) / dt;
        }
        _lastFootL = footL;
        _lastFootR = footR;
        _lastToeL = toeL;
        _lastToeR = toeR;
        _hasLast = true;

        // Under the heel and under the ball of each foot: a stride that steps up a curb has its toes over
        // the curb while the heel is still over the floor
        float floor = root.position.y - m_Data.rootAboveFloor;
        SampleGround(footL, vFootL, floor, GroundContactJob.InLeft, true);
        SampleGround(footR, vFootR, floor, GroundContactJob.InRight, true);
        if (toes)
        {
            SampleGround(toeL, vToeL, floor, GroundContactJob.InLeft + GroundContactJob.InToe, false);
            SampleGround(toeR, vToeR, floor, GroundContactJob.InRight + GroundContactJob.InToe, false);
        }
    }

    /// <summary>
    /// The ground under a foot for the next evaluation: under the foot now and, for a foot that is moving
    /// (a stride), also where it will be next frame; the higher of the two, so a foot that crosses onto a
    /// step is lifted onto it in time instead of entering its riser.
    /// </summary>
    /// <param name="withNormal">Also write the surface normal (the heel's sample; the ball's has no normal slot).</param>
    private void SampleGround(Vector3 foot, Vector3 footVelocity, float floor, int at, bool withNormal)
    {
        bool hit = Ground(foot, floor, out float height, out Vector3 normal);
        footVelocity.y = 0f;
        if (footVelocity.sqrMagnitude > 0.25f &&
            Ground(foot + footVelocity * Time.deltaTime, floor, out float aheadHeight, out Vector3 aheadNormal) &&
            (!hit || aheadHeight > height))
        {
            hit = true;
            height = aheadHeight;
            normal = aheadNormal;
        }
        if (withNormal)
        {
            _input[at]     = normal.x;
            _input[at + 1] = normal.y;
            _input[at + 2] = normal.z;
        }
        _input[at + 3] = height;
        _input[at + 4] = hit ? 1f : 0f;
    }

    private bool Ground(Vector3 foot, float floor, out float height, out Vector3 normal)
    {
        height = floor;
        normal = Vector3.up;
        float above = m_Data.rayAbove;
        Vector3 origin = new Vector3(foot.x, Mathf.Max(foot.y, floor) + above, foot.z);
        float length = above + Mathf.Max(0f, Mathf.Max(foot.y, floor) - floor) + m_Data.rayBelow;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, length, m_Data.groundLayers, QueryTriggerInteraction.Ignore) ||
            hit.normal.y < 0.6f)
            return false;
        height = hit.point.y;
        normal = hit.normal;
        return true;
    }
}

/// <summary>Configuration of the ground contact constraint (written by PlayerRigSetup).</summary>
[System.Serializable]
public struct GroundContactData : IAnimationJobData
{
    [Tooltip("The animated model (the Animator's GameObject): its position is the floor the animation stands on.")]
    public Transform root;
    [Tooltip("The feet: the ground is sampled under them (the job moves their Humanoid IK goals, not the bones).")]
    public Transform leftFoot, rightFoot;
    [Tooltip("The toes (optional): a foot whose toe joint would end under the ground is lifted.")]
    public Transform leftToes, rightToes;

    [Tooltip("Height of each ankle above its sole (Animator.leftFeetBottomHeight / rightFeetBottomHeight).")]
    public float leftSole, rightSole;
    [Tooltip("Height (m) of the model root above the floor its animation stands on (the idle soles).")]
    public float rootAboveFloor;

    [Header("Ground")]
    public LayerMask groundLayers;
    [Tooltip("Ray origin above the higher of the foot and the floor (m).")]
    public float rayAbove;
    [Tooltip("How far below the floor a foot may reach for lower ground (m).")]
    public float rayBelow;
    [Tooltip("Lowest the toe joint may be above the ground (m).")]
    public float toeClearance;
    [Tooltip("Largest pelvis drop to reach a lower foothold (m).")]
    public float maxPelvisDrop;
    [Tooltip("Speed of the pelvis adjustment (1/s).")]
    public float pelvisSpeed;

    [Header("Foot lock")]
    [Tooltip("A sole at most this high above its ground (m) is down; it lifts at this plus 2 cm.")]
    public float lockHeight;
    [Tooltip("Horizontal speed (m/s) of the animated foot under which a foot that is down locks.")]
    public float lockEngageSpeed;
    [Tooltip("Horizontal speed (m/s) of the animated foot over which the lock lets go.")]
    public float lockReleaseSpeed;
    [Tooltip("Farthest (m) the animated foot may drift from its lock before the lock lets go.")]
    public float lockMaxDrift;
    [Tooltip("Seconds the lock takes to blend in and out.")]
    public float lockBlendTime;

    bool IAnimationJobData.IsValid() => root != null && leftFoot != null && rightFoot != null;

    void IAnimationJobData.SetDefaultValues()
    {
        root = leftFoot = rightFoot = leftToes = rightToes = null;
        leftSole = rightSole = 0.08f;
        rootAboveFloor = 0f;
        groundLayers = ~0;
        rayAbove = 0.5f;
        rayBelow = 0.45f;
        toeClearance = 0.005f;
        maxPelvisDrop = 0.35f;
        pelvisSpeed = 10f;
        lockHeight = 0.04f;
        lockEngageSpeed = 0.5f;
        lockReleaseSpeed = 1.2f;
        lockMaxDrift = 0.12f;
        lockBlendTime = 0.1f;
    }
}

/// <summary>
/// The animation job of <see cref="GroundContactConstraint"/>. Not Burst-compiled (one character; it uses
/// UnityEngine math that Burst does not take).
/// </summary>
public struct GroundContactJob : IWeightedAnimationJob
{
    // Input buffer (main thread → job): per foot normal xyz, ground height, valid under the heel, and ground
    // height, valid under the ball (at InToe + 3, + 4); the predicted body move; the host's switches
    public const int InLeft = 0, InRight = 7, InToe = 2, InVelocity = 14, InYawRate = 16, InLock = 17, InReset = 18, InFootIK = 19, InputSize = 20;
    // State buffer (kept by the job across frames; read by the main thread): pelvis, lock count, where the
    // body stood when the pose was evaluated, and per foot its lock
    public const int StatePelvis = 0, StateLockCount = 1, StateEvalX = 2, StateEvalZ = 3, StateEvalYaw = 4, StateEvalValid = 5,
                     StateFeet = 6, FootStateSize = 12, StateSize = StateFeet + 2 * FootStateSize;
    public const int FootLockX = 0, FootLockZ = 1, FootLockWeight = 2, FootLocked = 3, FootPrevX = 4, FootPrevZ = 5, FootHasPrev = 6,
                     FootPrevToeX = 7, FootPrevToeZ = 8, FootPivotToe = 9, FootHeight = 10, FootSpeed = 11;
    public static int FootState(bool left) => StateFeet + (left ? 0 : FootStateSize);

    public TransformSceneHandle root;
    public ReadOnlyTransformHandle toeL, toeR;
    public bool hasToes;
    public NativeArray<float> input, state;
    public float soleL, soleR, rootAboveFloor, toeClearance, maxPelvisDrop, pelvisSpeed;
    public float lockHeight, lockEngageSpeed, lockReleaseSpeed, lockMaxDrift, lockBlendTime;

    public FloatProperty jobWeight { get; set; }

    public void ProcessRootMotion(AnimationStream stream) { }

    public void ProcessAnimation(AnimationStream stream)
    {
        float w = jobWeight.Get(stream);
        float dt = Mathf.Max(0f, stream.deltaTime);
        root.GetGlobalTR(stream, out Vector3 rootPos, out Quaternion rootRot);
        // Where the body stood for this evaluation: the main thread measures how far it moves afterwards
        state[StateEvalX] = rootPos.x;
        state[StateEvalZ] = rootPos.z;
        state[StateEvalYaw] = rootRot.eulerAngles.y;
        state[StateEvalValid] = 1f;

        if (input[InReset] > 0f || w <= 0f)
        {
            input[InReset] = 0f;
            ClearFoot(FootState(true));
            ClearFoot(FootState(false));
            if (w <= 0f) state[StatePelvis] = 0f;
        }
        // Nothing written: the pose passes as it is (only Humanoid streams carry the IK goals)
        if (w <= 0f || !stream.isHumanStream) return;

        AnimationHumanStream human = stream.AsHuman();
        float floor = rootPos.y - rootAboveFloor;
        // The body's move after this evaluation, predicted from the last one
        Vector3 move = new Vector3(input[InVelocity], 0f, input[InVelocity + 1]) * dt;
        Quaternion turn = Quaternion.AngleAxis(input[InYawRate] * dt, Vector3.up);
        bool lockAllowed = input[InLock] > 0f;
        float footIK = input[InFootIK];

        // Where the animation puts each foot: its Foot IK goal (the mocap's own foot, which the Humanoid IK
        // enforces) as far as the source uses Foot IK, else the foot of the pose
        Vector3 animL = FootGoal(human, AvatarIKGoal.LeftFoot, footIK, out Quaternion animRotL);
        Vector3 animR = FootGoal(human, AvatarIKGoal.RightFoot, footIK, out Quaternion animRotR);
        Quaternion rotL = animRotL, rotR = animRotR;
        Vector3 toeOffsetL = ToeOffset(stream, human, AvatarIKGoal.LeftFoot, toeL);
        Vector3 toeOffsetR = ToeOffset(stream, human, AvatarIKGoal.RightFoot, toeR);
        Vector3 targetL = FootTarget(animL, ref rotL, toeOffsetL, soleL, InLeft, FootState(true), floor, rootPos, move, turn, lockAllowed, dt, out float plantL);
        Vector3 targetR = FootTarget(animR, ref rotR, toeOffsetR, soleR, InRight, FootState(false), floor, rootPos, move, turn, lockAllowed, dt, out float plantR);

        // Lower the pelvis so the foot on the lower foothold reaches it (never raise it: stepping up, the
        // knee bends while the body rises after the foot). Settling a planted foot does not count: the
        // stance knee takes those centimetres
        float drop = Mathf.Min(0f, Mathf.Min(targetL.y + plantL - animL.y, targetR.y + plantR - animR.y));
        drop = Mathf.Max(drop, -maxPelvisDrop);
        float pelvis = state[StatePelvis];
        pelvis = dt > 0f ? Mathf.Lerp(pelvis, drop, Mathf.Clamp01(pelvisSpeed * dt)) : pelvis;
        state[StatePelvis] = pelvis;
        human.bodyPosition += Vector3.up * (pelvis * w);

        // The goals blend in with the weight from where the final IK would have put the feet anyway
        SetFootGoal(human, AvatarIKGoal.LeftFoot, Vector3.Lerp(animL, targetL, w), Quaternion.Slerp(animRotL, rotL, w));
        SetFootGoal(human, AvatarIKGoal.RightFoot, Vector3.Lerp(animR, targetR, w), Quaternion.Slerp(animRotR, rotR, w));
        human.SolveIK();
    }

    /// <summary>
    /// The animated foot: the clip's Foot IK goal as far as <paramref name="footIK"/> goes, else the pose's
    /// own foot. (The stream carries the clips' goals with a weight of 0: the Foot IK is applied from the
    /// clip's setting, not from the stream's weight.)
    /// </summary>
    private static Vector3 FootGoal(AnimationHumanStream human, AvatarIKGoal goal, float footIK, out Quaternion rotation)
    {
        rotation = Quaternion.Slerp(human.GetGoalRotationFromPose(goal), human.GetGoalRotation(goal), footIK);
        return Vector3.Lerp(human.GetGoalPositionFromPose(goal), human.GetGoalPosition(goal), footIK);
    }

    private static void SetFootGoal(AnimationHumanStream human, AvatarIKGoal goal, Vector3 position, Quaternion rotation)
    {
        human.SetGoalPosition(goal, position);
        human.SetGoalRotation(goal, rotation);
        human.SetGoalWeightPosition(goal, 1f);
        human.SetGoalWeightRotation(goal, 1f);
    }

    /// <summary>
    /// The toe joint relative to the foot's IK goal, in the goal's frame (zero without toes): where the pose
    /// has it, so the ball of the foot follows the goal wherever the rig places it.
    /// </summary>
    private Vector3 ToeOffset(AnimationStream stream, AnimationHumanStream human, AvatarIKGoal goal, ReadOnlyTransformHandle toe)
    {
        if (!hasToes) return Vector3.zero;
        return Quaternion.Inverse(human.GetGoalRotationFromPose(goal)) * (toe.GetPosition(stream) - human.GetGoalPositionFromPose(goal));
    }

    /// <summary>
    /// Where a foot goes this frame (in the evaluation's world, before the body's move): on the terrain
    /// under it, with neither the sole nor the ball of the foot below the ground (a foot pivoting on its
    /// ball pressed the toes 5 cm into the ground in the mocap's 90 degree turn and strafe), and held at
    /// its lock while it is planted, settled onto the ground (the mocap's Foot IK leaves a stance foot 1–2 cm
    /// over it; <paramref name="plant"/> is how much it was lowered). Turns the foot with the slope it touches.
    /// </summary>
    private Vector3 FootTarget(Vector3 anim, ref Quaternion rotation, Vector3 toeOffset, float sole, int inAt, int st, float floor,
                               Vector3 rootPos, Vector3 move, Quaternion turn, bool lockAllowed, float dt, out float plant)
    {
        plant = 0f;
        Vector3 target = anim;
        bool valid = input[inAt + 4] > 0f;
        float ground = input[inAt + 3];
        bool hasToe = toeOffset.sqrMagnitude > 1e-6f;
        if (valid)
        {
            float offset = ground - floor;
            if (Mathf.Abs(offset) < 0.01f) offset = 0f; // flat ground: the animated pose as it is
            target.y = Mathf.Max(anim.y + offset, ground + sole);

            // A foot touching the ground turns with its slope
            Vector3 normal = new Vector3(input[inAt], input[inAt + 1], input[inAt + 2]);
            float slope = 1f - Mathf.InverseLerp(sole + 0.04f, sole + 0.08f, anim.y + offset - ground);
            if (slope > 0f && normal.y < 0.999f)
                rotation = Quaternion.Slerp(rotation, Quaternion.FromToRotation(Vector3.up, normal) * rotation, slope);

        }
        // The ball of the foot on the ground under it (lifting the foot onto a curb its toes reach)
        bool toeValid = hasToe && input[inAt + InToe + 4] > 0f;
        float toeGround = toeValid ? input[inAt + InToe + 3] : ground;
        if (toeValid)
            target.y += Mathf.Max(0f, toeGround + toeClearance - (target.y + (rotation * toeOffset).y));

        // Foot lock, worked out where the foot will be seen (after the body's move). The point held is the
        // one the foot stands on: the heel, or the ball once the heel is raised above it
        Vector3 seenAnkle = rootPos + move + turn * (target - rootPos);
        Vector3 seenToeOffset = turn * (rotation * toeOffset);
        Vector3 seenToe = seenAnkle + seenToeOffset;
        float heel = valid ? target.y - sole - ground : float.MaxValue;
        float ball = toeValid ? target.y + (rotation * toeOffset).y - toeGround : float.MaxValue;
        bool onBall = toeValid && heel > ball + BallPivotRise;
        float height = Mathf.Min(heel, ball); // the lowest contact above its ground

        // Speed of each contact point (seen), and of the one the foot stands on
        float heelSpeed = float.MaxValue, ballSpeed = float.MaxValue;
        if (state[st + FootHasPrev] > 0f && dt > 0f)
        {
            heelSpeed = new Vector2(seenAnkle.x - state[st + FootPrevX], seenAnkle.z - state[st + FootPrevZ]).magnitude / dt;
            ballSpeed = new Vector2(seenToe.x - state[st + FootPrevToeX], seenToe.z - state[st + FootPrevToeZ]).magnitude / dt;
        }
        if (dt > 0f)
        {
            state[st + FootPrevX] = seenAnkle.x;
            state[st + FootPrevZ] = seenAnkle.z;
            state[st + FootPrevToeX] = seenToe.x;
            state[st + FootPrevToeZ] = seenToe.z;
            state[st + FootHasPrev] = 1f;
        }
        Vector3 pivot = onBall ? seenToe : seenAnkle;
        float speed = onBall ? ballSpeed : heelSpeed;
        state[st + FootHeight] = height;
        state[st + FootSpeed] = speed;

        Vector2 lockXZ = new Vector2(state[st + FootLockX], state[st + FootLockZ]);
        float lockWeight = state[st + FootLockWeight];
        bool locked = state[st + FootLocked] > 0f;
        bool wasOnBall = state[st + FootPivotToe] > 0f;
        // The contact moves from the heel to the ball (or back) while the lock holds: the lock moves to the
        // new point where the held foot shows it now, so nothing jumps
        if (lockWeight > 0f && onBall != wasOnBall)
        {
            Vector2 toe = new Vector2(seenToeOffset.x, seenToeOffset.z);
            lockXZ += onBall ? toe : -toe;
        }
        if (!lockAllowed || !valid)
            locked = false;
        else if (!locked && height < lockHeight && speed < lockEngageSpeed)
        {
            // From where the point is seen now (a lock still blending out starts from its blend)
            lockXZ = Vector2.Lerp(new Vector2(pivot.x, pivot.z), lockXZ, lockWeight);
            locked = true;
            state[StateLockCount] += 1f;
        }
        else if (locked && (height > lockHeight + 0.02f || speed > lockReleaseSpeed ||
                            (lockXZ - new Vector2(pivot.x, pivot.z)).magnitude > lockMaxDrift))
            locked = false;

        if (dt > 0f)
            lockWeight = Mathf.MoveTowards(lockWeight, locked ? 1f : 0f, dt / Mathf.Max(1e-3f, lockBlendTime));
        state[st + FootLockX] = lockXZ.x;
        state[st + FootLockZ] = lockXZ.y;
        state[st + FootLockWeight] = lockWeight;
        state[st + FootLocked] = locked ? 1f : 0f;
        state[st + FootPivotToe] = onBall ? 1f : 0f;

        if (lockWeight > 0f)
        {
            // Settled on the ground: down until its lowest contact touches it (no point goes under)
            if (valid)
            {
                plant = Mathf.Max(0f, Mathf.Min(heel, toeValid ? ball - toeClearance : heel)) * lockWeight;
                target.y -= plant;
            }

            // The held point, and the ankle from it; back from the seen world to the evaluation's world
            Vector2 held = Vector2.Lerp(new Vector2(pivot.x, pivot.z), lockXZ, lockWeight);
            if (onBall) held -= new Vector2(seenToeOffset.x, seenToeOffset.z);
            Vector3 eval = rootPos + Quaternion.Inverse(turn) * (new Vector3(held.x, 0f, held.y) - (rootPos + move));
            target.x = eval.x;
            target.z = eval.z;
        }
        return target;
    }

    /// <summary>How far (m) the heel must rise above the ball of the foot for the foot to stand on its ball.</summary>
    private const float BallPivotRise = 0.015f;

    private void ClearFoot(int st)
    {
        state[st + FootLockWeight] = 0f;
        state[st + FootLocked] = 0f;
        state[st + FootHasPrev] = 0f;
    }
}

/// <summary>Binds the root, the toes and the buffers of <see cref="GroundContactJob"/>.</summary>
public class GroundContactBinder : AnimationJobBinder<GroundContactJob, GroundContactData>
{
    public override GroundContactJob Create(Animator animator, ref GroundContactData data, Component component)
    {
        var job = new GroundContactJob
        {
            root = animator.BindSceneTransform(data.root),
            hasToes = data.leftToes != null && data.rightToes != null,
            soleL = data.leftSole,
            soleR = data.rightSole,
            rootAboveFloor = data.rootAboveFloor,
            toeClearance = data.toeClearance,
            maxPelvisDrop = data.maxPelvisDrop,
            pelvisSpeed = data.pelvisSpeed,
            lockHeight = data.lockHeight,
            lockEngageSpeed = data.lockEngageSpeed,
            lockReleaseSpeed = data.lockReleaseSpeed,
            lockMaxDrift = data.lockMaxDrift,
            lockBlendTime = data.lockBlendTime,
        };
        if (job.hasToes)
        {
            job.toeL = ReadOnlyTransformHandle.Bind(animator, data.leftToes);
            job.toeR = ReadOnlyTransformHandle.Bind(animator, data.rightToes);
        }
        ((GroundContactConstraint)component).GetBuffers(out job.input, out job.state);
        return job;
    }

    // The buffers belong to the constraint component (they outlive a rebuild of the rig)
    public override void Destroy(GroundContactJob job) { }
}
