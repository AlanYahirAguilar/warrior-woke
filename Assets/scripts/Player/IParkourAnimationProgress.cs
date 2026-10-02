/// <summary>
/// Read-only view of the parkour animation, implemented by the presentation layer (PlayerAnimator).
/// Parkour states end or chain when their clip reaches a measured moment (ParkourTimings) instead of
/// after a fixed time, so the body never leaves an action before its animation has finished it.
/// </summary>
public interface IParkourAnimationProgress
{
    /// <summary>Normalized time (0–1) of the animation of the current parkour state, or −1 if it is not playing yet.</summary>
    float Progress { get; }
}
