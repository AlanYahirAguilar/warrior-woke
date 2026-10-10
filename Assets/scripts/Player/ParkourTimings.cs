/// <summary>
/// Normalized times (0–1 of each clip) and contact offsets of the mantle and ledge animations
/// (Quaternius and Dynamic Parkour System clips on Ch45). Measured by sampling the clips on the model:
/// hand and foot positions per frame. Shared by the FSM (when an action may end or chain),
/// PlayerAnimator (MatchTarget windows) and PlayerContactIK (contact weights). The vault clips carry
/// their own measurements in the VaultCatalog (P36).
/// </summary>
public static class ParkourTimings
{
    // ─── ClimbUp_1m (Quaternius UAL2, mantle) ─────────────────────────────────────
    /// <summary>Height the clip climbs onto (the hips rise 1.02 m; the feet end on a 1 m top).</summary>
    public const float MantleClipRise = 1.0f;
    /// <summary>
    /// The supporting hand is matched onto the measured top by here: mid-way through its plant
    /// (0.17–0.5), so the warp is spread over the cross-fade and the plant instead of popping.
    /// </summary>
    public const float MantleHandMatchEnd = 0.4f;
    /// <summary>The left hand rests on the top between these moments (measured: ~1.2 m high, 0.75 m ahead).</summary>
    public const float MantleHandPlant = 0.17f, MantleHandRelease = 0.5f;
    /// <summary>The feet are over the top from here, and stand on it at the end.</summary>
    public const float MantleFeetOver = 0.55f, MantleStand = 1.0f;
    /// <summary>The FSM leaves the mantle here.</summary>
    public const float MantleExit = 0.95f;
    /// <summary>The supporting palm lands this far past the top's edge (m).</summary>
    public const float MantleHandInset = 0.12f;

    // ─── Crouch To Braced Hang (Braced Hang To Crouch reversed: the drop) ─────────
    /// <summary>The body turns its back to the drop before here (crouched on the top).</summary>
    public const float DropTurn = 0.2f;
    /// <summary>The hands are matched onto the edge from here...</summary>
    public const float DropMatchStart = 0.45f;
    /// <summary>...and the body hangs from it here (the climb's start, reversed).</summary>
    public const float DropHang = 0.97f;

    // ─── Idle To Braced Hang ─────────────────────────────────────────────────────
    /// <summary>Start offset when grabbing from the ground (skips the idle frames).</summary>
    public const float GrabGroundStart = 0.12f;
    /// <summary>MatchTarget starts when the feet leave the ground (from the ground).</summary>
    public const float GrabGroundMatchStart = 0.30f;
    /// <summary>Start offset when grabbing in the air: the arms are already reaching up.</summary>
    public const float GrabAirStart = 0.40f;
    /// <summary>Both hands reach the ledge.</summary>
    public const float GrabHandContact = 0.56f;
    /// <summary>The hang is stable enough to start the climb.</summary>
    public const float GrabAttached = 0.62f;

    // ─── Braced Hang To Crouch ───────────────────────────────────────────────────
    /// <summary>The hands stay on the edge until here, then fade out.</summary>
    public const float ClimbHandsRelease = 0.35f;
    /// <summary>MatchTarget of the root onto the top surface starts here.</summary>
    public const float ClimbMatchStart = 0.40f;
    /// <summary>The feet are on the top surface.</summary>
    public const float ClimbStand = 0.95f;
    /// <summary>The FSM leaves the climb here.</summary>
    public const float ClimbExit = 0.97f;

    // ─── Contact offsets (wrist and ankle are IK goals, not fingertips or soles) ──
    /// <summary>Height of the wrist above a surface the palm rests on.</summary>
    public const float WristAboveSurface = 0.06f;
    /// <summary>The wrist stays this far outside the wall face while the fingers wrap the edge.</summary>
    public const float WristOutOfFace = 0.05f;
    /// <summary>Lateral distance of each hand from the body center in the hang clips.</summary>
    public const float HandLateral = 0.30f;
    /// <summary>Distance from a foot's IK goal (ankle) to the wall while the toes rest on it.</summary>
    public const float AnkleFromWall = 0.13f;
}
