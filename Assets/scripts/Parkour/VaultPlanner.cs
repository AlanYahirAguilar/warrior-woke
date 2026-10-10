using UnityEngine;

/// <summary>
/// A vault decided by VaultPlanner: the clip, where it enters, and the warp that fits it onto the real
/// obstacle (docs/arquitectura.md §5.17). Plain data; PlayerVaultState plays it.
/// </summary>
public sealed class VaultPlan
{
    public VaultVariant Variant;
    /// <summary>Clip time (s) the vault starts at: where the clip's run-up is as far from the hand plant as the body is.</summary>
    public float EntryTime;
    /// <summary>Vertical lift (m) at the plant, horizontal stretch (m) over the airborne window, hand inset past the front edge (m).</summary>
    public float Lift, Stretch, Inset;
    /// <summary>Playback of the run-up (the approach speed over the clip's) and of the airborne window (keeps the clip's gravity after the lift).</summary>
    public float ApproachRate, AirRate;
    /// <summary>Direction over the obstacle (into its front face), its front edge on the body's line and its top.</summary>
    public Vector3 Direction, FrontEdge;
    public float TopY, Depth;
    /// <summary>Feet height at the start and at the landing.</summary>
    public float StartFeetY, LandFeetY;
    /// <summary>Wrist goals of the planted hand(s) on the top, and the landing point of the feet.</summary>
    public Vector3 PlantPoint, SecondPlantPoint, LandingPoint;
    /// <summary>The clip's second hand contact falls on this top (on a shallow one it would land past it: no second contact).</summary>
    public bool HasSecondPlant;
    /// <summary>Clip time each palm leaves its point: the clip's release, or earlier when Ch45's arm no longer reaches it.</summary>
    public float PlantRelease, SecondRelease;
    /// <summary>How far the body was from where the clip expects it at the entry (m): the approach correction.</summary>
    public float EntryError;
    /// <summary>Cross-fade (s) from the locomotion into the clip: longer from a standstill, so the speed ramps up.</summary>
    public float CrossFade;
    /// <summary>Why the last evaluation found no vault (for tests and tuning).</summary>
    public string Reason = "";
    /// <summary>What the last evaluation measured: the top above the feet, the depth (m) and the approach speed (m/s).</summary>
    public float MeasuredHeight, MeasuredDepth, MeasuredSpeed;
}

/// <summary>
/// Picks the vault clip that fits the obstacle, the approach and the body (decision P36): a vault is
/// played only if a measured clip covers the obstacle's height and depth with a bounded warp (catalog
/// envelope), its run-up suits the approach speed, the body is within its run-up (not too close: the
/// lead leg would hit the obstacle), and its landing spot is free ground. Among the candidates it
/// prefers the smallest warp and the clip whose stride matches the body's (the lead foot), so the
/// locomotion flows into the vault. With no candidate the obstacle is not vaulted: the caller falls
/// back to the other actions (mantle, ledge, jump), never to a clip that does not fit.
/// </summary>
public static class VaultPlanner
{
    /// <summary>Result of an evaluation: no vault, ready to start now, or ahead and approaching its entry point.</summary>
    public enum Result { None, Ready, Approaching }

    /// <summary>Wrist height above a surface the palm rests on (m).</summary>
    private const float WristAboveTop = 0.06f;

    /// <summary>Largest gap (m) between the body and the clip's run-up the approach correction closes.</summary>
    private const float MaxEntryError = 0.3f;

    /// <summary>The clip's run-up is entered at least this long before its take-off (s): room for the cross-fade and the stride.</summary>
    private const float MinRunUp = 0.18f;

    /// <summary>Cross-fade into the clip when moving (the run already moves like the clip's run-up) and from a standstill.</summary>
    private const float MovingCrossFade = 0.1f, StandingCrossFade = 0.25f;

    /// <summary>
    /// How far (fraction of Ch45's arm) a shoulder may be from its palm's point before the palm lets go.
    /// The arm's own length: the Humanoid IK does not stretch it, so with the warp lab's 5 % margin the
    /// palm fell short of its point by up to 7 cm in the last frames before the release.
    /// </summary>
    private const float ArmStretch = 1.0f;

    /// <summary>
    /// First clip time from <paramref name="plant"/> to <paramref name="release"/> at which the planned body
    /// puts the shoulder of <paramref name="shoulder"/> farther from <paramref name="point"/> than the arm reaches
    /// (or <paramref name="release"/> if it always reaches; <paramref name="plant"/> if it never does).
    /// </summary>
    private static float ReachRelease(VaultPlan plan, VaultVariant v, Vector3[] shoulder, float plant, float release,
                                      Vector3 point, Vector3 rootAtPlant, Vector3 f, Vector3 right)
    {
        if (shoulder == null || shoulder.Length == 0 || v.ArmLength <= 0f) return release;
        float reach = v.ArmLength * ArmStretch, dt = 1f / v.PathRate;
        Vector2 atPlant = v.PathAt(v.Plant);
        float sPlant = VaultWarpProfile.Stretch(v, v.Plant);
        for (float t = plant; t < release; t += dt)
        {
            Vector2 path = v.PathAt(t) - atPlant;
            Vector3 sh = v.ShoulderAt(shoulder, t);
            float along = path.y + plan.Stretch * (VaultWarpProfile.Stretch(v, t) - sPlant) + sh.z;
            Vector3 world = rootAtPlant + right * (path.x + sh.x) + f * along;
            world.y = plan.StartFeetY + plan.Lift * VaultWarpProfile.Lift(v, t)
                    + (plan.LandFeetY - plan.StartFeetY) * VaultWarpProfile.Ground(v, t) + sh.y;
            if (Vector3.Distance(world, point) > reach) return t;
        }
        return release;
    }

    /// <summary>
    /// Slowest playback of the airborne window: the lift's extra height is fallen at the clip's pace,
    /// and slower than this the flight reads as slow motion (measured: ~5 m/s² at 0.85).
    /// </summary>
    private const float MinAirRate = 0.92f;

    /// <summary>A second palm lands at least this far (m) inside the top's front and back edges, or it is not a contact.</summary>
    private const float SecondPlantMargin = 0.03f;

    /// <summary>Below this speed (m/s) the body counts as standing: the clip's speed ramps in over the longer cross-fade.</summary>
    private const float StandingSpeed = 0.8f;

    public static Result Evaluate(VaultCatalog catalog, PlayerMovement player, in VaultInfo info, float speed, float footPhase, VaultPlan plan)
    {
        plan.Reason = "";
        if (catalog == null || catalog.Variants == null || catalog.Variants.Length == 0) { plan.Reason = "sin catálogo"; return Result.None; }

        Vector3 f = info.Direction;
        Vector3 right = Vector3.Cross(Vector3.up, f);
        Vector3 body = player.transform.position;
        float height = info.TopY - player.FeetY;
        float toFront = Vector3.Dot(new Vector3(info.FrontPoint.x - body.x, 0f, info.FrontPoint.z - body.z), f);
        plan.MeasuredHeight = height;
        plan.MeasuredDepth = info.Depth;
        plan.MeasuredSpeed = speed;

        float crossFade = speed < StandingSpeed ? StandingCrossFade : MovingCrossFade;
        VaultVariant best = null;
        float bestScore = float.MaxValue, bestEntry = 0f, bestLift = 0f, bestStretch = 0f, bestInset = 0f, bestError = 0f;
        Vector3 bestLanding = Vector3.zero;
        bool approaching = false;
        string reason = "ningún clip cubre el obstáculo";

        foreach (VaultVariant v in catalog.Variants)
        {
            // The run-up must suit the approach: a walk or a standstill uses the slow clips, a run the
            // run's, a sprint the sprint's (with some overlap)
            float lo = v.IsSlow ? 0f : v.ApproachSpeed * 0.6f, hi = v.ApproachSpeed * 1.35f;
            if (speed < lo || speed > hi) continue;
            if (!catalog.TryEnvelope(v, height, info.Depth, out float lift, out float stretch, out float inset))
                continue;

            // Where the clip's hand plant is from the root at clip time t, and where ours is from the body
            float sPlant = VaultWarpProfile.Stretch(v, v.Plant);
            Vector2 atPlant = v.PathAt(v.Plant);
            float Remaining(float t) => atPlant.y - v.PathAt(t).y + v.HandOffset.z + stretch * sPlant;
            float required = toFront + inset;
            float latest = Mathf.Max(0f, v.Takeoff - Mathf.Max(MinRunUp, crossFade + 0.08f));
            float rate = Mathf.Clamp(speed / Mathf.Max(0.5f, v.ApproachSpeed), 0.75f, 1.25f);
            if (required > Remaining(0f) + MaxEntryError)
            {
                // Farther than the clip's whole run-up: keep running toward it (the entry point is ahead)
                if (speed > 0.5f) approaching = true;
                reason = "lejos";
                continue;
            }
            if (required < Remaining(latest) - MaxEntryError)
            {
                reason = "demasiado cerca";
                continue;
            }

            // Entry: the clip time whose run-up distance matches the body's, and among the close ones
            // the one whose lead foot matches the body's stride and whose root moves at the body's speed
            // (from a standstill: the slowest stretch of the run-up)
            float entry = 0f, entryScore = float.MaxValue, entryError = 0f;
            float dt = 1f / v.PathRate;
            for (float t = 0f; t <= latest + 1e-4f; t += dt)
            {
                float error = Remaining(t) - required;
                if (Mathf.Abs(error) > MaxEntryError) continue;
                float phase = Mathf.Abs(v.PhaseAt(t) - footPhase);
                float clipSpeed = (v.PathAt(t + dt).y - v.PathAt(Mathf.Max(0f, t - dt)).y) / (t >= dt ? 2f * dt : dt + t);
                float score = Mathf.Abs(error) + 0.6f * phase + 0.25f * Mathf.Abs(clipSpeed * rate - speed);
                if (score < entryScore) { entryScore = score; entry = t; entryError = error; }
            }
            if (entryScore == float.MaxValue) { reason = "sin entrada"; continue; }

            // The landing spot must be free ground (no wall right behind the obstacle, no drop)
            float landAlong = toFront + inset + v.LandFromHand + stretch * (1f - sPlant);
            Vector3 landing = new Vector3(body.x, info.FrontPoint.y, body.z) + f * landAlong;
            if (!player.EnvChecker.TryFindLanding(landing, info.TopY, player.FeetY, out Vector3 ground))
            {
                reason = "aterrizaje bloqueado";
                continue;
            }

            // Smallest warp, and the clip whose run is closest to the body's: played much faster or slower
            // its run-out leaves at another speed than the body arrived with
            float score2 = 2f * Mathf.Abs(lift) + Mathf.Abs(stretch) + 2.5f * Mathf.Abs(rate - 1f) + entryScore
                         + (height < 0.8f && v.Style == VaultStyle.Dive ? 1f : 0f); // a low obstacle is not worth a dive
            if (score2 < bestScore)
            {
                bestScore = score2;
                best = v;
                bestEntry = entry;
                bestLift = lift;
                bestStretch = stretch;
                bestInset = inset;
                bestError = entryError;
                bestLanding = ground;
            }
        }

        if (best == null)
        {
            plan.Reason = reason;
            return approaching ? Result.Approaching : Result.None;
        }

        plan.Variant = best;
        plan.EntryTime = bestEntry;
        plan.Lift = bestLift;
        plan.Stretch = bestStretch;
        plan.Inset = bestInset;
        plan.EntryError = bestError;
        plan.CrossFade = crossFade;
        plan.ApproachRate = Mathf.Clamp(speed / Mathf.Max(0.5f, best.ApproachSpeed), 0.75f, 1.25f);
        // A lift raises the flight's arc: the airborne window plays slower so the higher fall keeps the
        // clip's gravity. A lowered body (a lower top) keeps the clip's own flight, only shifted down:
        // played faster it fell at ~19 m/s². A lazy vault rests on the top: no flight
        plan.AirRate = best.Style == VaultStyle.Lazy || best.HipsRise < 0.05f || bestLift <= 0f ? 1f
                     : Mathf.Clamp(Mathf.Sqrt(best.HipsRise / (best.HipsRise + bestLift)), MinAirRate, 1f);
        plan.Direction = f;
        plan.FrontEdge = info.FrontPoint;
        plan.TopY = info.TopY;
        plan.Depth = info.Depth;
        plan.StartFeetY = player.FeetY;
        plan.LandFeetY = bestLanding.y;
        plan.LandingPoint = bestLanding;
        // The palms on the top: the planted hand at the inset, on the body's line plus the clip's lateral
        // offset; the second hand where the clip puts it relative to the first, kept on the top
        Vector3 line = new Vector3(body.x, info.TopY + WristAboveTop, body.z) + f * toFront;
        plan.PlantPoint = line + f * bestInset + right * best.HandOffset.x;
        // The second hand where the clip puts it relative to the first; only a contact if that is on the
        // top (a kong over a long table plants it further along; over a shallow wall it is in the air)
        plan.HasSecondPlant = false;
        plan.SecondPlantPoint = plan.PlantPoint;
        if (best.SecondPlant >= 0f)
        {
            float second = bestInset + best.SecondHandOffset.z - best.HandOffset.z + (best.PathAt(best.SecondPlant).y - best.PathAt(best.Plant).y)
                         + bestStretch * (VaultWarpProfile.Stretch(best, best.SecondPlant) - VaultWarpProfile.Stretch(best, best.Plant));
            if (second >= SecondPlantMargin && second <= info.Depth - SecondPlantMargin)
            {
                plan.HasSecondPlant = true;
                plan.SecondPlantPoint = line + f * second + right * best.SecondHandOffset.x;
            }
        }

        // Each palm stays on its point while the arm reaches it. The actor's hand stayed planted while
        // the body went on over the top; Ch45's arm is relatively shorter, so held there by IK it would
        // stretch the arm and pull the hand off the point: the palm leaves when the shoulder is too far
        Vector3 rootAtPlant = plan.PlantPoint - right * best.HandOffset.x - f * best.HandOffset.z;
        plan.PlantRelease = ReachRelease(plan, best, best.PlantShoulder, best.Plant, best.PlantRelease, plan.PlantPoint, rootAtPlant, f, right);
        plan.SecondRelease = plan.HasSecondPlant
            ? ReachRelease(plan, best, best.SecondShoulder, best.SecondPlant, best.SecondRelease, plan.SecondPlantPoint, rootAtPlant, f, right)
            : best.SecondRelease;
        if (plan.HasSecondPlant && plan.SecondRelease <= best.SecondPlant) plan.HasSecondPlant = false; // out of reach already
        plan.Reason = "";
        return Result.Ready;
    }
}
