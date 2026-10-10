using UnityEngine;

/// <summary>How the body passes over the top in a vault clip.</summary>
public enum VaultStyle
{
    /// <summary>Speed vault: one hand on the top, the body sideways over it, landing running.</summary>
    Speed,
    /// <summary>Lazy vault: the hands on the top and the hips sliding over it.</summary>
    Lazy,
    /// <summary>Dive (kong): the hands on the top, head first over it.</summary>
    Dive,
}

/// <summary>
/// One measured vault clip: an annotated vault of the Kinematica Demo mocap (possibly mirrored) and
/// everything the planner and the warper need from it, measured on Ch45 by VaultCatalogBuilder. All
/// positions are in the vault frame: along = the direction of travel over the obstacle, lateral = to
/// its right, heights above the floor. Never edited by hand: rebuild the catalog instead.
/// </summary>
[System.Serializable]
public class VaultVariant
{
    public string Id;
    /// <summary>Animator state that plays the clip.</summary>
    public string State;
    public bool Mirror;
    public VaultStyle Style;
    /// <summary>Clip length (s) at playback 1.</summary>
    public float Length;

    [Header("Key moments (s from the clip start)")]
    public float Takeoff;
    public float Plant;
    /// <summary>The planted hand leaves the top (it rises 5 cm, or the same hand plants again).</summary>
    public float PlantRelease;
    /// <summary>The last hand leaves the top: the end of the support.</summary>
    public float Release;
    public float Land;
    /// <summary>The run after the landing can be handed back to the locomotion from here (Kinematica's escape marker).</summary>
    public float Escape;
    /// <summary>The planted hand (after mirroring).</summary>
    public bool RightHand;
    /// <summary>Second hand contact (two-handed vaults), or −1; it may fall past a shallow top (VaultPlanner drops it then).</summary>
    public float SecondPlant = -1f;
    public float SecondRelease = -1f;
    public bool SecondRightHand;

    [Header("Natural motion")]
    /// <summary>Samples per second of the path, heading and foot phase tables.</summary>
    public float PathRate = 30f;
    /// <summary>
    /// The clip's root (the Animator's root transform, as root motion moves it) from the clip start (m),
    /// per sample: along the vault and to its right. The warper moves the body along it.
    /// </summary>
    public float[] PathAlong, PathLateral;
    /// <summary>
    /// Heading of the clip's root relative to the vault direction (°, positive to the right), per sample.
    /// The root follows the body's orientation (it turns sideways in a lazy vault), and the pose is drawn
    /// relative to it, so the warper turns the body by this much for the pose to face as captured.
    /// </summary>
    public float[] Heading;
    /// <summary>The planted hand's IK goal at the plant, relative to the root then (x lateral, y height above the floor, z along).</summary>
    public Vector3 HandOffset;
    /// <summary>Same for the second hand at its plant.</summary>
    public Vector3 SecondHandOffset;
    /// <summary>Horizontal speed of the run-up and of the run-out (m/s).</summary>
    public float ApproachSpeed, ExitSpeed;
    /// <summary>Hips apex above the hips at the take-off (m): the flight's arc, for the time warp.</summary>
    public float HipsRise;
    /// <summary>Along distance (m) from the planted hand to the landing foot.</summary>
    public float LandFromHand;
    /// <summary>Direction of travel over the obstacle relative to the clip root's start forward (°).</summary>
    public float FrameYaw;
    /// <summary>Lead-foot phase per path sample: left foot along − right foot along (m).</summary>
    public float[] FootPhase;
    /// <summary>
    /// Shoulder of the planted arm (and of the second contact's arm) per path sample, relative to the
    /// root: x lateral, y height above the floor, z along (m). With ArmLength it says until when a palm
    /// can stay on its plant point: Ch45's arm does not always reach as long as the actor's hand stayed.
    /// </summary>
    public Vector3[] PlantShoulder, SecondShoulder;
    /// <summary>Ch45's shoulder-to-wrist length (m).</summary>
    public float ArmLength;

    [Header("Envelope (per height × depth cell of the catalog grid)")]
    public bool[] Ok;
    public float[] Lift, Stretch, Inset;

    public bool IsSlow => ApproachSpeed < 2.6f;

    /// <summary>Natural root displacement at clip time <paramref name="t"/> (x lateral, y along).</summary>
    public Vector2 PathAt(float t)
    {
        if (PathAlong == null || PathAlong.Length == 0) return Vector2.zero;
        float f = Mathf.Clamp(t * PathRate, 0f, PathAlong.Length - 1);
        int i = Mathf.Min((int)f, PathAlong.Length - 2);
        if (i < 0) return new Vector2(PathLateral[0], PathAlong[0]);
        float k = f - i;
        return new Vector2(Mathf.Lerp(PathLateral[i], PathLateral[i + 1], k), Mathf.Lerp(PathAlong[i], PathAlong[i + 1], k));
    }

    public float PhaseAt(float t) => Sample(FootPhase, t);

    /// <summary>Shoulder of <paramref name="table"/> (PlantShoulder or SecondShoulder) at clip time <paramref name="t"/>, relative to the root.</summary>
    public Vector3 ShoulderAt(Vector3[] table, float t)
    {
        if (table == null || table.Length == 0) return Vector3.zero;
        float f = Mathf.Clamp(t * PathRate, 0f, table.Length - 1);
        int i = Mathf.Min((int)f, table.Length - 2);
        return i < 0 ? table[0] : Vector3.Lerp(table[i], table[i + 1], f - i);
    }

    /// <summary>Heading of the clip's root relative to the vault direction at clip time <paramref name="t"/> (°).</summary>
    public float HeadingAt(float t)
    {
        if (Heading == null || Heading.Length == 0) return 0f;
        float f = Mathf.Clamp(t * PathRate, 0f, Heading.Length - 1);
        int i = Mathf.Min((int)f, Heading.Length - 2);
        return i < 0 ? Heading[0] : Heading[i] + Mathf.DeltaAngle(Heading[i], Heading[i + 1]) * (f - i);
    }

    private float Sample(float[] table, float t)
    {
        if (table == null || table.Length == 0) return 0f;
        float f = Mathf.Clamp(t * PathRate, 0f, table.Length - 1);
        int i = Mathf.Min((int)f, table.Length - 2);
        return i < 0 ? table[0] : Mathf.Lerp(table[i], table[i + 1], f - i);
    }
}

/// <summary>
/// The vault clips and where each one fits (decision P36, docs/arquitectura.md §5.17): for every clip,
/// a grid of obstacle heights × depths with the lift, the stretch and the hand inset the warper needs —
/// or no solution, in which case the clip is never used for that obstacle. Generated by
/// VaultCatalogBuilder (Tools → Warrior Woke → Construir Catálogo de Vaults) into Assets/Data/Parkour.
/// </summary>
public class VaultCatalog : ScriptableObject
{
    /// <summary>Obstacle heights (m above the feet) and depths (m) of the envelope grid.</summary>
    public float[] Heights, Depths;
    public VaultVariant[] Variants;

    /// <summary>
    /// The warp a clip needs for an obstacle of <paramref name="height"/> × <paramref name="depth"/>,
    /// interpolated between the four surrounding cells of the grid. False if any of them has no
    /// solution (or the obstacle is outside the grid): the clip does not fit that obstacle.
    /// </summary>
    public bool TryEnvelope(VaultVariant v, float height, float depth, out float lift, out float stretch, out float inset)
    {
        lift = stretch = inset = 0f;
        if (Heights == null || Depths == null || v.Ok == null) return false;
        if (!Bracket(Heights, height, out int h0, out int h1, out float th)) return false;
        if (!Bracket(Depths, depth, out int d0, out int d1, out float td)) return false;
        int n = Depths.Length;
        int a = h0 * n + d0, b = h0 * n + d1, c = h1 * n + d0, d = h1 * n + d1;
        if (!v.Ok[a] || !v.Ok[b] || !v.Ok[c] || !v.Ok[d]) return false;
        lift    = Bilinear(v.Lift[a], v.Lift[b], v.Lift[c], v.Lift[d], th, td);
        stretch = Bilinear(v.Stretch[a], v.Stretch[b], v.Stretch[c], v.Stretch[d], th, td);
        inset   = Mathf.Min(Bilinear(v.Inset[a], v.Inset[b], v.Inset[c], v.Inset[d], th, td), Mathf.Max(0.05f, depth - 0.05f));
        return true;
    }

    private static bool Bracket(float[] grid, float x, out int i0, out int i1, out float t)
    {
        i0 = i1 = 0;
        t = 0f;
        // Within this of a grid line the obstacle is that line's (a measured top reads a few mm off the
        // standard height), and only that line's cells must have a solution
        const float Tolerance = 0.015f;
        if (grid.Length == 0 || x < grid[0] - Tolerance || x > grid[grid.Length - 1] + Tolerance) return false;
        for (int i = 0; i < grid.Length; i++)
        {
            if (Mathf.Abs(x - grid[i]) > Tolerance) continue;
            i0 = i1 = i;
            return true;
        }
        for (int i = 0; i < grid.Length - 1; i++)
        {
            if (x > grid[i + 1]) continue;
            i0 = i;
            i1 = i + 1;
            t = Mathf.InverseLerp(grid[i], grid[i + 1], x);
            return true;
        }
        return false;
    }

    private static float Bilinear(float a, float b, float c, float d, float th, float td) =>
        Mathf.Lerp(Mathf.Lerp(a, b, td), Mathf.Lerp(c, d, td), th);
}

/// <summary>
/// Time profiles of the vault warp, shared by the runtime warper (PlayerVaultState) and the Editor lab
/// that measured the envelope (VaultCatalogBuilder), so the game does exactly what was validated.
/// </summary>
public static class VaultWarpProfile
{
    /// <summary>Shortest time (s) the lift takes to go back to 0 before the landing: a vault whose hand leaves at the landing (a walking one) would otherwise drop the body in one frame.</summary>
    public const float MinLiftRamp = 0.2f;

    /// <summary>
    /// Weight of the vertical lift at clip time <paramref name="t"/>: 0 at the take-off (or 0.15 s before
    /// the plant, whichever comes first), full from the plant to the release, 0 again at the landing
    /// (over MinLiftRamp at least).
    /// </summary>
    public static float Lift(VaultVariant v, float t)
    {
        float start = Mathf.Min(v.Takeoff, v.Plant - 0.15f);
        float end = Mathf.Max(v.Plant, Mathf.Min(v.Release, v.Land - MinLiftRamp));
        if (t <= v.Plant) return Smooth(start, v.Plant, t);
        if (t <= end) return 1f;
        return 1f - Smooth(end, v.Land, t);
    }

    /// <summary>Share (0–1) of the horizontal stretch done by clip time <paramref name="t"/>: spread over the airborne window.</summary>
    public static float Stretch(VaultVariant v, float t) => Smooth(v.Takeoff, v.Land, t);

    /// <summary>Share (0–1) of the change of ground height (landing lower or higher than the take-off) done by <paramref name="t"/>.</summary>
    public static float Ground(VaultVariant v, float t) => Smooth(v.Release, v.Land, t);

    public static float Smooth(float a, float b, float x) =>
        Mathf.SmoothStep(0f, 1f, b > a ? Mathf.Clamp01((x - a) / (b - a)) : (x >= b ? 1f : 0f));
}
