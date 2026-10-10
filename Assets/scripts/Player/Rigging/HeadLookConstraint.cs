using Unity.Collections;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;

/// <summary>
/// Where the character looks, with Animation Rigging (decision P31): on the final pose, the head turns
/// toward a direction set by the host (PlayerRig: the target of an attack, where the body is about to go,
/// or where the camera looks), and the turn is spread over the chest, the neck and the head so the back
/// takes part instead of the head rotating alone. The look is limited to what a neck does (yaw and pitch
/// relative to the body) and keeps the clip's own head motion underneath (only the facing changes).
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Warrior Woke/Head Look Constraint")]
public class HeadLookConstraint : RigConstraint<HeadLookJob, HeadLookData, HeadLookBinder>
{
    private NativeArray<float> _input;

    /// <summary>Direction (world) the head should face; zero leaves the animation alone.</summary>
    public void LookToward(Vector3 direction)
    {
        if (!_input.IsCreated) return;
        bool valid = direction.sqrMagnitude > 1e-6f;
        if (valid) direction.Normalize();
        _input[0] = direction.x;
        _input[1] = direction.y;
        _input[2] = direction.z;
        _input[3] = valid ? 1f : 0f;
    }

    internal NativeArray<float> GetBuffer()
    {
        if (!_input.IsCreated) _input = new NativeArray<float>(HeadLookJob.InputSize, Allocator.Persistent);
        return _input;
    }

    private void OnDestroy()
    {
        if (_input.IsCreated) _input.Dispose();
    }
}

/// <summary>Configuration of the head look (written by PlayerAnimationSetup).</summary>
[System.Serializable]
public struct HeadLookData : IAnimationJobData
{
    [Tooltip("The animated model: its facing is the body's, against which the look is limited.")]
    public Transform root;
    public Transform chest, neck, head;
    [Tooltip("The head's local axis that points where the face looks (measured on the model's default pose).")]
    public Vector3 headForward;
    [Tooltip("Share of the turn taken by the chest, the neck and the head (they add up to 1).")]
    public Vector3 shares;
    [Tooltip("Largest look to each side, up and down (degrees, relative to the body).")]
    public float maxYaw, maxPitchUp, maxPitchDown;

    bool IAnimationJobData.IsValid() => root != null && chest != null && neck != null && head != null && headForward.sqrMagnitude > 0.5f;

    void IAnimationJobData.SetDefaultValues()
    {
        root = chest = neck = head = null;
        headForward = Vector3.forward;
        shares = new Vector3(0.2f, 0.3f, 0.5f);
        maxYaw = 70f;
        maxPitchUp = 25f;
        maxPitchDown = 35f;
    }
}

/// <summary>The animation job of <see cref="HeadLookConstraint"/> (not Burst-compiled, like GroundContactJob).</summary>
public struct HeadLookJob : IWeightedAnimationJob
{
    public const int InputSize = 4; // direction xyz, valid

    public TransformSceneHandle root;
    public ReadWriteTransformHandle chest, neck, head;
    public NativeArray<float> input;
    public Vector3 headForward, shares;
    public float maxYaw, maxPitchUp, maxPitchDown;

    public FloatProperty jobWeight { get; set; }

    public void ProcessRootMotion(AnimationStream stream) { }

    public void ProcessAnimation(AnimationStream stream)
    {
        float w = jobWeight.Get(stream);
        if (w <= 0f || input[3] <= 0f)
        {
            AnimationRuntimeUtils.PassThrough(stream, chest);
            AnimationRuntimeUtils.PassThrough(stream, neck);
            AnimationRuntimeUtils.PassThrough(stream, head);
            return;
        }

        // The wanted direction, limited relative to the body (yaw to each side, pitch up and down)
        Quaternion body = Quaternion.Euler(0f, root.GetRotation(stream).eulerAngles.y, 0f);
        Vector3 local = Quaternion.Inverse(body) * new Vector3(input[0], input[1], input[2]);
        float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -maxYaw, maxYaw);
        float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg, -maxPitchDown, maxPitchUp);
        Vector3 wanted = body * (Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward);

        // The turn from where the animated face looks, spread over chest, neck and head (about one axis,
        // so the shares add up to the whole turn)
        Vector3 face = head.GetRotation(stream) * headForward;
        Quaternion turn = Quaternion.FromToRotation(face, wanted);
        chest.SetRotation(stream, Quaternion.Slerp(Quaternion.identity, turn, shares.x * w) * chest.GetRotation(stream));
        neck.SetRotation(stream, Quaternion.Slerp(Quaternion.identity, turn, shares.y * w) * neck.GetRotation(stream));
        head.SetRotation(stream, Quaternion.Slerp(Quaternion.identity, turn, shares.z * w) * head.GetRotation(stream));
    }
}

/// <summary>Binds the bones and the input buffer of <see cref="HeadLookJob"/>.</summary>
public class HeadLookBinder : AnimationJobBinder<HeadLookJob, HeadLookData>
{
    public override HeadLookJob Create(Animator animator, ref HeadLookData data, Component component)
    {
        return new HeadLookJob
        {
            root  = animator.BindSceneTransform(data.root),
            chest = ReadWriteTransformHandle.Bind(animator, data.chest),
            neck  = ReadWriteTransformHandle.Bind(animator, data.neck),
            head  = ReadWriteTransformHandle.Bind(animator, data.head),
            input = ((HeadLookConstraint)component).GetBuffer(),
            headForward = data.headForward.normalized,
            shares = data.shares,
            maxYaw = data.maxYaw,
            maxPitchUp = data.maxPitchUp,
            maxPitchDown = data.maxPitchDown,
        };
    }

    // The buffer belongs to the constraint component
    public override void Destroy(HeadLookJob job) { }
}
