/// <summary>
/// Normalized times (0–1 of each clip) and contact offsets of the parkour animations (Dynamic
/// Parkour System clips on Ch45). Measured by sampling the clips on the model: hand and foot
/// positions per frame. Shared by the FSM (when an action may end or chain), PlayerAnimator
/// (MatchTarget windows) and PlayerContactIK (contact weights).
/// </summary>
public static class ParkourTimings
{
    // ─── Vault1 (VaultFence) ─────────────────────────────────────────────────────
    /// <summary>Take-off: by here the body must be high enough for the lead leg to clear the obstacle.</summary>
    public const float VaultTakeoff = 0.16f;
    /// <summary>Root rise of the clip from its start to VaultTakeoff (m).</summary>
    public const float VaultClipTakeoffRise = 0.25f;
    /// <summary>Obstacle height the clip clears without help (its fence is ~0.95 m, but a compressed run-up brings the lead leg in lower).</summary>
    public const float VaultClipFenceHeight = 0.8f;
    /// <summary>The left hand is planted on the obstacle (LHandCurve reaches 1 at 0.22).</summary>
    public const float VaultHandContact = 0.30f;
    /// <summary>The hand leaves the obstacle (LHandCurve drops after 0.53).</summary>
    public const float VaultHandRelease = 0.53f;
    /// <summary>First foot touches the ground behind the obstacle.</summary>
    public const float VaultFeetLand = 0.78f;
    /// <summary>The FSM leaves the vault here: the clip is already in its run-out.</summary>
    public const float VaultExit = 0.82f;
    /// <summary>Root travel of the clip (4.26 m in 0.98 s): its average, slow over the obstacle.</summary>
    public const float VaultClipSpeed = 4.35f;
    /// <summary>
    /// Speed of the clip's run-in and run-out at playback 1 (measured in Play Mode: 5.4–5.5 m/s).
    /// The playback follows the approach speed through this value, so the body enters and leaves the
    /// vault at the speed it arrived with instead of surging.
    /// </summary>
    public const float VaultClipRunSpeed = 5.45f;
    /// <summary>Horizontal distance from the hips at the clip's start to the planted hand.</summary>
    public const float VaultClipHandReach = 1.34f;
    /// <summary>Latest start offset used when the obstacle is closer than the clip expects.</summary>
    public const float VaultMaxStartOffset = 0.2f;
    /// <summary>Length (s) of the Vault1 clip at playback 1.</summary>
    public const float VaultClipLength = 0.98f;
    /// <summary>Cross-fade (s) into the vault. MatchTarget cannot run during it, so it eats into the first match window.</summary>
    public const float VaultCrossFade = 0.05f;
    /// <summary>Clip time the take-off warp needs before VaultTakeoff to lift the body for a tall obstacle.</summary>
    public const float VaultTakeoffWindow = 0.1f;
    /// <summary>Distance from the obstacle's back face to the landing of the clip at run speed.</summary>
    public const float VaultClipLandDistance = 1.6f;
    /// <summary>The clip plants the left hand this far past the obstacle's front edge (m)...</summary>
    public const float VaultHandInset = 0.12f;
    /// <summary>...and this far left of the body line (m).</summary>
    public const float VaultHandLateral = 0.3f;

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
