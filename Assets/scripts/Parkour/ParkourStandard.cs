using UnityEngine;

/// <summary>Kinds of standard parkour obstacles (docs/arquitectura.md §5.13).</summary>
public enum ParkourObstacleType
{
    Step,
    LowVault,
    MediumVault,
    HighVault,
    Mantle,
    Barrier,
    Ledge,
    ClimbWall,
    SlideBar,
    JumpGap,
    Combined,
}

/// <summary>
/// Measurements of one obstacle type (m). For a SlideBar, Height is the clearance under the bar and
/// Depth the bar's depth; for a JumpGap, Height is the platforms' height and Depth the gap.
/// </summary>
public readonly struct ParkourObstacleSpec
{
    public readonly float Height, MinHeight, MaxHeight;
    public readonly float Depth, MinDepth, MaxDepth;
    public readonly float Width, MinWidth;
    /// <summary>Layer of the obstacle's colliders: "Obstacle" (vault, ledge, slide) or "Ground" (never a parkour action).</summary>
    public readonly string Layer;

    public ParkourObstacleSpec(float height, float minHeight, float maxHeight, float depth, float minDepth, float maxDepth,
                               float minWidth, string layer)
    {
        Height = height; MinHeight = minHeight; MaxHeight = maxHeight;
        Depth = depth; MinDepth = minDepth; MaxDepth = maxDepth;
        Width = ParkourStandard.PrefabWidth; MinWidth = minWidth;
        Layer = layer;
    }
}

/// <summary>
/// Parkour Obstacle Standard: the single place where the game's obstacle measurements and the
/// detection limits of the parkour live. EnvironmentChecker, the parkour states, PlayerMovement's
/// auto step, the obstacle prefabs, their validation and the Parkour Test Area all read these
/// values, so the standard changes here and nowhere else.
///
/// Every value derives from the character and the clips, not from taste: the Player's capsule is
/// 1.975 m tall (0.54 m radius), the jump rises ~1 m (JumpSpeed 4.5), the vault clip clears 0.8 m on
/// its own and lands 1.6 m behind the obstacle, and the hang clips put the hands ~2.1 m above the
/// feet (ParkourTimings has the clip measurements). The obstacle heights sit inside the detection
/// ranges with a margin on both sides, so a standard obstacle never lands on a limit.
/// Up to 1.5 m a block deep enough to stand on is climbed onto (mantle, P28); between 1.5 m and
/// 1.9 m (lowest grab) there is no action: that band is the Barrier, which blocks the way on purpose.
/// </summary>
public static class ParkourStandard
{
    // ─── Character (Player.prefab) ───────────────────────────────────────────────
    /// <summary>Radius of the standing body used for free-space checks (slightly below the real collider).</summary>
    public const float StandCheckRadius = 0.3f;
    /// <summary>Height of the standing body used for free-space checks.</summary>
    public const float StandCheckHeight = 1.8f;
    /// <summary>Feet height reached by a standing jump (JumpSpeed² / 2g with JumpSpeed 4.5).</summary>
    public const float JumpRise = 1.0f;

    // ─── Auto step (PlayerMovement) ──────────────────────────────────────────────
    /// <summary>Highest rise (m) climbed or descended automatically while running.</summary>
    public const float StepMaxHeight = 0.4f;

    // ─── Vault detection (EnvironmentChecker.TryFindVault) ───────────────────────
    /// <summary>Lowest top (m above the feet) that counts as a vault; lower ones are stepped or jumped.</summary>
    public const float VaultMinHeight = 0.45f;
    /// <summary>Highest top (m above the feet) that can be vaulted (GDD §5.4: low obstacles only).</summary>
    public const float VaultMaxHeight = 1.2f;
    /// <summary>Deepest obstacle (m along the vault) crossed in one move.</summary>
    public const float VaultMaxDepth = 1.5f;
    /// <summary>How far ahead (m from the feet) an obstacle is detected when Space is pressed.</summary>
    public const float VaultReach = 1.1f;
    /// <summary>Closest landing (m behind the back face) accepted when the clip's natural landing is blocked.</summary>
    public const float VaultMinLanding = 0.6f;
    /// <summary>Height of the knee ray that finds the front face (m above the feet).</summary>
    public const float VaultKneeRay = VaultMinHeight * 0.6f;
    /// <summary>
    /// Ideal take-off: body this far (m) from the face, where the clip's run-up meets the obstacle
    /// with no frames skipped (hand reach 1.34 m − hand inset 0.12 m). Closer, the clip must start
    /// later and a tall obstacle has no time for its take-off.
    /// </summary>
    public const float VaultTakeoffDistance = 1.2f;
    /// <summary>Seconds of run-up that Space looks ahead when running: the vault waits for the take-off point.</summary>
    public const float VaultSpotTime = 0.35f;

    /// <summary>How far ahead (m) Space finds a vault at <paramref name="speed"/>: farther when running fast.</summary>
    public static float VaultSpotReach(float speed) => Mathf.Max(VaultReach, VaultTakeoffDistance + speed * VaultSpotTime);

    // ─── Mantle (EnvironmentChecker.TryFindMantle, PlayerMantleState) ───────────
    /// <summary>
    /// Rise (m) of a top the body climbs onto with the mantle clip (ClimbUp_1m: 1.0 m measured),
    /// warped within ±0.5 m at most — beyond that the pose would no longer match the obstacle.
    /// </summary>
    public const float MantleMinRise = 0.8f, MantleMaxRise = 1.5f;
    /// <summary>How far ahead (m from the body) the face is found for a mantle.</summary>
    public const float MantleReach = 1.0f;
    /// <summary>Heights (m above the feet) of the rays that find the face of a mantle block.</summary>
    public const float MantleLowRay = 0.5f, MantleHighRay = 1.0f;
    /// <summary>Inset (m) from the edge where the feet end after the mantle (the clip lands ~1 m past it).</summary>
    public const float MantleStandInset = 0.9f;
    /// <summary>Shallowest top a mantle can end on (stand inset + body radius).</summary>
    public const float MantleMinDepth = MantleStandInset + StandCheckRadius + 0.05f;

    // ─── Ledge detection (EnvironmentChecker.TryFindLedge, PlayerLedgeGrabState) ─
    /// <summary>Rise (m) of the ledge's top above the feet that is grabbed from the ground (the clip jumps).</summary>
    public const float LedgeGroundMinRise = 1.9f, LedgeGroundMaxRise = 2.7f;
    /// <summary>Rise (m) of the ledge's top above the feet at the moment of a grab in the air.</summary>
    public const float LedgeAirMinRise = 1.5f, LedgeAirMaxRise = 2.6f;
    /// <summary>
    /// How far ahead (m from the body) the wall is found for a grab from the ground / in the air. In
    /// the air 0.8: a body standing 0.75 m from a wall (an arm's length) still grabs after the
    /// idle's own sway (the mocap moves the root ~1 cm, P29).
    /// </summary>
    public const float LedgeReachGround = 1.0f, LedgeReachAir = 0.8f;
    /// <summary>Heights (m above the feet) of the chest and head rays that find the wall face.</summary>
    public const float LedgeChestRay = 1.2f, LedgeHeadRay = 1.75f;
    /// <summary>Inset (m) from the edge where the feet stand after climbing.</summary>
    public const float LedgeStandInset = 0.45f;
    /// <summary>How far ahead (m from the body) the edge of a top can be to lower onto a hang (P28).</summary>
    public const float DropReach = 0.9f;
    /// <summary>Smallest drop (m) below an edge that is lowered onto with a hang instead of stepped down or walked off.</summary>
    public const float DropMinHeight = 1.6f;
    /// <summary>Horizontal push (m/s) away from the wall and upward speed of a jump from a hang.</summary>
    public const float HangJumpOut = 3.5f, HangJumpUp = 4f;

    /// <summary>Shallowest top that leaves room to stand after climbing (inset + body radius).</summary>
    public const float LedgeMinDepth = LedgeStandInset + StandCheckRadius + 0.05f;

    // ─── Detection pre-filter ────────────────────────────────────────────────────
    /// <summary>
    /// Radius of the overlap sphere (around the body center) that gates the rays. It must cover the
    /// farthest point the rays reach: an obstacle 1.1 m ahead at knee height.
    /// </summary>
    public const float ProximityRadius = 1.4f;

    // ─── Obstacle catalog ────────────────────────────────────────────────────────
    /// <summary>Width of the standard prefabs (m). Width is free in a level, above the type's minimum.</summary>
    public const float PrefabWidth = 4f;
    /// <summary>Highest obstacle a jump plus an air grab reaches with margin (jump rise + air reach − 0.3 m).</summary>
    public const float ClimbWallMaxHeight = 3.3f;
    /// <summary>Thickness of a slide bar (m).</summary>
    public const float SlideBarThickness = 0.3f;
    /// <summary>
    /// Farthest point (m before the bar) to press C: from a run (3.4 m/s of P33, friction 2.5 m/s²)
    /// the slide covers ~1.8 m before its momentum is spent, and a sprint ~3–4 m, so starting within
    /// 1 m keeps the body sliding when it reaches the bar (under it the ceiling keeps it going).
    /// </summary>
    public const float SlideEntryDistance = 1f;
    /// <summary>Depth (m) of each JumpGap platform.</summary>
    public const float JumpPlatformDepth = 4f;
    /// <summary>
    /// Free floor (m) between two chained actions: the vault lands 1.6 m behind the obstacle, the
    /// next one is detected 1.1 m ahead, and the body needs about one stride to recover in between.
    /// </summary>
    public const float ChainSpacing = 5f;
    /// <summary>Measurement tolerance (m) of the validation.</summary>
    public const float Tolerance = 0.01f;

    private const string Obstacle = "Obstacle", Ground = "Ground";

    /// <summary>Measurements of each obstacle type. Heights in m above the floor the obstacle stands on.</summary>
    public static ParkourObstacleSpec Spec(ParkourObstacleType type)
    {
        switch (type)
        {
            // Curb: auto step (never a vault: below VaultMinHeight, and on Ground)
            case ParkourObstacleType.Step:        return new ParkourObstacleSpec(0.25f, 0.05f, 0.35f, 0.6f, 0.3f, 100f, 1.0f, Ground);
            // Vaults: low = the clip clears it alone; medium = small take-off warp; high = top of the vault range
            case ParkourObstacleType.LowVault:    return new ParkourObstacleSpec(0.6f, VaultMinHeight, ParkourTimings.VaultClipFenceHeight, 0.4f, 0.2f, VaultMaxDepth, 1.0f, Obstacle);
            case ParkourObstacleType.MediumVault: return new ParkourObstacleSpec(1.0f, ParkourTimings.VaultClipFenceHeight, 1.1f, 0.5f, 0.2f, VaultMaxDepth, 1.0f, Obstacle);
            case ParkourObstacleType.HighVault:   return new ParkourObstacleSpec(1.2f, 1.1f, VaultMaxHeight, 0.6f, 0.2f, VaultMaxDepth, 1.0f, Obstacle);
            // Mantle block: deep enough to stand on, climbed onto (slow) — a fast run vaults what it can
            case ParkourObstacleType.Mantle:      return new ParkourObstacleSpec(1.3f, MantleMinRise, MantleMaxRise, 1.5f, MantleMinDepth, 100f, 1.0f, Obstacle);
            // Dead band between the mantle and the grab, on Ground so it is never climbed either
            case ParkourObstacleType.Barrier:     return new ParkourObstacleSpec(1.7f, MantleMaxRise + 0.05f, LedgeGroundMinRise - 0.05f, 0.5f, 0.2f, 100f, 0.2f, Ground);
            // Ledges: grab from the ground and climb; climb wall: jump, grab in the air and climb
            case ParkourObstacleType.Ledge:       return new ParkourObstacleSpec(2.2f, LedgeGroundMinRise, LedgeGroundMaxRise, 1.5f, LedgeMinDepth, 100f, 1.0f, Obstacle);
            case ParkourObstacleType.ClimbWall:   return new ParkourObstacleSpec(3.0f, LedgeGroundMaxRise, ClimbWallMaxHeight, 1.5f, LedgeMinDepth, 100f, 1.0f, Obstacle);
            // Slide: clearance above the sliding body (half collider, ~1 m) and below a standing one
            case ParkourObstacleType.SlideBar:    return new ParkourObstacleSpec(1.2f, 1.1f, 1.5f, 1.0f, 0.3f, 6f, 1.5f, Obstacle);
            // Jump: platforms above the auto step (so the gap cannot be walked), gap a running jump clears
            case ParkourObstacleType.JumpGap:     return new ParkourObstacleSpec(1.0f, StepMaxHeight + 0.1f, 3.0f, 2.0f, 1.0f, 3.0f, 1.5f, Ground);
            default:                              return default;
        }
    }

    /// <summary>Spanish label used by the validation and the gizmos.</summary>
    public static string Label(ParkourObstacleType type)
    {
        switch (type)
        {
            case ParkourObstacleType.Step:        return "Escalón";
            case ParkourObstacleType.LowVault:    return "Vault bajo";
            case ParkourObstacleType.MediumVault: return "Vault medio";
            case ParkourObstacleType.HighVault:   return "Vault alto";
            case ParkourObstacleType.Mantle:      return "Mantle";
            case ParkourObstacleType.Barrier:     return "Barrera";
            case ParkourObstacleType.Ledge:       return "Cornisa";
            case ParkourObstacleType.ClimbWall:   return "Muro de escalada";
            case ParkourObstacleType.SlideBar:    return "Barra de slide";
            case ParkourObstacleType.JumpGap:     return "Hueco de salto";
            case ParkourObstacleType.Combined:    return "Combinado";
            default:                              return type.ToString();
        }
    }

    /// <summary>True for the types crossed with a vault.</summary>
    public static bool IsVault(ParkourObstacleType type) =>
        type == ParkourObstacleType.LowVault || type == ParkourObstacleType.MediumVault || type == ParkourObstacleType.HighVault;

    /// <summary>True for the types grabbed and climbed.</summary>
    public static bool IsLedge(ParkourObstacleType type) =>
        type == ParkourObstacleType.Ledge || type == ParkourObstacleType.ClimbWall;
}
