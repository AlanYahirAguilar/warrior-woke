/// <summary>
/// The combat side of the presentation layer, implemented by PlayerAnimator (decision P37). Like
/// IParkourAnimationProgress for parkour, the attack and hit reaction states follow their clip through
/// it instead of a fixed time, so their phases always match what the animation shows; and the hit stop
/// goes through it: freezing the animation also holds the state, whose phases follow the clip.
/// </summary>
public interface ICombatAnimation
{
    /// <summary>Normalized time (0–1) of the clip of the current attack or hit reaction, or −1 if it is not playing yet.</summary>
    float ActionProgress { get; }

    /// <summary>Freezes the animation for <paramref name="seconds"/> (hit stop): the clip, its root motion and the state's phases pause.</summary>
    void HitStop(float seconds);

    /// <summary>True while a hit stop holds the animation.</summary>
    bool IsHitStopped { get; }
}
