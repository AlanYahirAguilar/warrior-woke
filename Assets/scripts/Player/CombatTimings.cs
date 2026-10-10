using UnityEngine;

/// <summary>
/// One unarmed attack (decision P37): its clip, playback rate and the moments measured on Ch45 by
/// Tools → Warrior Woke → Revisar Clips de Combate (CombatClipReview, Logs/CombatClips/timeline.csv), as
/// normalized time (0–1) of the clip. The attack states follow the clip's progress through these
/// phases: anticipation until HitStart, the strike (the limb sweeps for targets) until HitEnd, then the
/// recovery, where the next attack of the chain may start from ChainOpen and moving or dodging
/// interrupts it from MoveCancel.
/// </summary>
public sealed class AttackData
{
    /// <summary>Name for logs and tests.</summary>
    public readonly string Name;
    /// <summary>Sub-clip name (the Animator state that plays it is chosen by PlayerAnimator).</summary>
    public readonly string Clip;
    /// <summary>Length of the clip at rate 1 (s).</summary>
    public readonly float Length;
    /// <summary>Playback rate of the clip (Animator state speed).</summary>
    public readonly float Rate;
    /// <summary>Where the clip starts playing (normalized): a long wind-up is entered later.</summary>
    public readonly float StartAt;
    /// <summary>The striking limb: its bone sweeps for targets during the strike.</summary>
    public readonly HumanBodyBones Limb;
    /// <summary>Radius (m) of the sphere swept along the limb's bone (wrist or ankle) for targets: the fist or the foot around it.</summary>
    public readonly float LimbRadius;
    /// <summary>Strike window (normalized): the limb leaves the guard … starts coming back.</summary>
    public readonly float HitStart, HitEnd;
    /// <summary>Full extension of the strike (normalized): the body closes the distance to the target until here.</summary>
    public readonly float Impact;
    /// <summary>From here the next attack of the chain starts (a press buffered earlier fires here).</summary>
    public readonly float ChainOpen;
    /// <summary>From here moving, dodging or blocking ends the attack (the limb is back in the guard).</summary>
    public readonly float MoveCancel;
    /// <summary>Horizontal distance (m) from the hips to the striking limb's bone at full extension.</summary>
    public readonly float Reach;
    /// <summary>Root travel (m, forward) of the clip from StartAt to the impact: the step the clip itself takes.</summary>
    public readonly float StepToImpact;
    /// <summary>Seconds the attacker freezes when the strike connects (hit stop).</summary>
    public readonly float HitStop;

    public AttackData(string name, string clip, float length, float rate, float startAt, HumanBodyBones limb, float limbRadius,
                      float hitStart, float impact, float hitEnd, float chainOpen, float moveCancel, float reach, float stepToImpact, float hitStop)
    {
        Name = name; Clip = clip; Length = length; Rate = rate; StartAt = startAt; Limb = limb; LimbRadius = limbRadius;
        HitStart = hitStart; Impact = impact; HitEnd = hitEnd; ChainOpen = chainOpen; MoveCancel = moveCancel; Reach = reach;
        StepToImpact = stepToImpact; HitStop = hitStop;
    }

    /// <summary>Seconds from the start of the attack to normalized time <paramref name="n"/> of its clip.</summary>
    public float SecondsTo(float n) => Mathf.Max(0f, n - StartAt) * Length / Rate;
}

/// <summary>
/// The unarmed attacks and hit reactions (decision P37): the light chain jab → cross → hook (Quaternius
/// UAL CC0 jab and cross, CMU mocap hook), the heavy front kick (CMU mocap) and the reactions to a hit
/// (Quaternius). Moments measured on Ch45 (CombatClipReview). Shared by the combat states,
/// PlayerAnimator and the Editor tool that builds the controller.
/// </summary>
public static class CombatTimings
{
    // ─── Clips (sub-clip names) ──────────────────────────────────────────────────
    public const string JabClip      = "Punch_Jab";
    public const string CrossClip    = "Punch_Cross";
    public const string HookClip     = "Punch_Hook";
    public const string KickClip     = "Kick_Front";
    public const string HitChestClip = "Hit_Chest";
    public const string HitHeadClip  = "Hit_Head";

    /// <summary>Playback rate of the hit reactions.</summary>
    public const float HitRate = 1f;

    // ─── Attacks ─────────────────────────────────────────────────────────────────
    // GDD §5.6: light attacks ~0.25 s apart, up to 3; §5.7: the kick is slower (0.8 s) and leaves the
    // player open if it misses; §5.9: J → J → K. The rates make the chain land a blow every ~0.25 s.
    // Measured (rate 1): jab — the left fist goes out at 0.15–0.20 and holds 0.59 m out to 0.35;
    // cross — the right fist goes out at 0.15–0.25 and holds 0.52 m out to 0.50; hook — a step in of
    // 0.38 m, then the right fist sweeps from the right side to the center, 0.56 m out at head height, at
    // 0.42–0.60; kick — the right foot rises from 0.30, reaches 0.77 m out at hip height at 0.48 while the
    // body steps 0.37 m in, and comes down by 0.80.

    /// <summary>J1: left jab, straight to the head.</summary>
    public static readonly AttackData Jab = new AttackData("Jab", JabClip, 0.867f, 1.15f, 0f, HumanBodyBones.LeftHand, 0.12f,
        0.15f, 0.25f, 0.36f, 0.30f, 0.62f, 0.60f, 0.06f, 0.06f);

    /// <summary>J2: right cross, straight, the hips turning in.</summary>
    public static readonly AttackData Cross = new AttackData("Cross", CrossClip, 1.0f, 1.25f, 0f, HumanBodyBones.RightHand, 0.12f,
        0.17f, 0.28f, 0.38f, 0.32f, 0.66f, 0.53f, 0.05f, 0.06f);

    /// <summary>J3: right hook, a horizontal arc that ends the chain (no fourth light blow). Its wind-up is entered at 0.15.</summary>
    public static readonly AttackData Hook = new AttackData("Hook", HookClip, 1.067f, 1.35f, 0.15f, HumanBodyBones.RightHand, 0.13f,
        0.44f, 0.55f, 0.62f, 0.64f, 0.78f, 0.60f, 0.29f, 0.07f);

    /// <summary>K: right front kick (weight shift, chamber, kick, step down into the stance).</summary>
    public static readonly AttackData Kick = new AttackData("Kick", KickClip, 1.133f, 1.2f, 0f, HumanBodyBones.RightFoot, 0.15f,
        0.38f, 0.48f, 0.58f, 0.82f, 0.82f, 0.77f, 0.37f, 0.09f);

    /// <summary>Most light attacks in a chain (GDD §5.6).</summary>
    public const int MaxLightChain = 3;

    /// <summary>The chain restarts if more than this passes between two attack inputs (GDD §5.9).</summary>
    public const float ComboResetTime = 0.5f;
}
