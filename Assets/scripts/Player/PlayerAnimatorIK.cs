using MxM;
using UnityEngine;

/// <summary>
/// Lives on the player's visual model next to its Animator. Unity only sends OnAnimatorIK and
/// OnAnimatorMove to the Animator's own GameObject, so this relay forwards them to PlayerAnimator on
/// the player root. Because OnAnimatorMove exists, Unity never applies root motion by itself.
/// It is also the MxMAnimator's root motion applicator (IMxMRootMotion, mode RootMotionApplicator):
/// motion matching's root motion and warping go to PlayerMovement's motor, which decides how much
/// of it moves the body (P29, P30). Parkour root motion goes through PlayerAnimator (P22).
/// Requires "IK Pass" on the controller's base layer (set by PlayerAnimationSetup).
/// </summary>
[RequireComponent(typeof(Animator))]
public class PlayerAnimatorIK : MonoBehaviour, IMxMRootMotion
{
    /// <summary>Set by PlayerAnimator in Awake.</summary>
    public PlayerAnimator Owner { get; set; }

    /// <summary>Set by PlayerAnimator in Awake.</summary>
    public PlayerMovement Movement { get; set; }

    private void OnAnimatorIK(int layerIndex)
    {
        if (Owner != null) Owner.ApplyIK();
    }

    private void OnAnimatorMove()
    {
        if (Owner != null) Owner.ApplyRootMotion();
    }

    // ─── IMxMRootMotion ──────────────────────────────────────────────────────────

    public void HandleRootMotion(Vector3 rootPosition, Quaternion rootRotation, Vector3 warp, Quaternion warpRot, float deltaTime)
    {
        if (Movement != null) Movement.QueueRootMotion(rootPosition + warp, rootRotation * warpRot);
    }

    public void HandleAngularErrorWarping(Quaternion warpRot)
    {
        if (Movement != null) Movement.QueueRootMotion(Vector3.zero, warpRot);
    }

    // MxM only calls these from its own tools and events (not used): the motor owns the transform
    public void SetPosition(Vector3 position) { }
    public void SetRotation(Quaternion rotation) { }
    public void SetPositionAndRotation(Vector3 position, Quaternion rotation) { }
    public void Translate(Vector3 delta) { }
    public void Rotate(Vector3 axis, float angle) { }
    public void FinalizeRootMotion() { }
}
