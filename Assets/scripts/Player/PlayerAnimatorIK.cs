using UnityEngine;

/// <summary>
/// Lives on the player's visual model next to its Animator. Unity only sends OnAnimatorIK to the
/// Animator's own GameObject, so this relay forwards it to PlayerAnimator on the player root.
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
}
