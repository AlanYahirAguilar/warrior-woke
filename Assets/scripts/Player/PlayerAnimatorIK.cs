using UnityEngine;

/// <summary>
/// Lives on the player's visual model next to its Animator. Unity only sends OnAnimatorIK and
/// OnAnimatorMove to the Animator's own GameObject, so this relay forwards them to PlayerAnimator on
/// the player root. Because OnAnimatorMove exists, Unity never applies root motion by itself:
/// PlayerAnimator decides when it moves the body (only during parkour, decision P22).
/// Requires "IK Pass" on the controller's base layer (set by PlayerAnimationSetup).
/// </summary>
[RequireComponent(typeof(Animator))]
public class PlayerAnimatorIK : MonoBehaviour
{
    /// <summary>Set by PlayerAnimator in Awake.</summary>
    public PlayerAnimator Owner { get; set; }

    private void OnAnimatorIK(int layerIndex)
    {
        if (Owner != null) Owner.ApplyIK();
    }

    private void OnAnimatorMove()
    {
        if (Owner != null) Owner.ApplyRootMotion();
    }
}
