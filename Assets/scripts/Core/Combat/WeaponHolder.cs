using UnityEngine;

/// <summary>
/// Manages the player's equipped weapon and routes damage values to the Hitbox.
///
/// Responsibilities:
///  - Holds the currently equipped WeaponData asset.
///  - Updates the Hitbox damage to match the active weapon on equip.
///  - Exposes Equip() for pickup interactions.
///  - Fires OnWeaponChanged so HUD / animation systems can react.
///
/// Adheres to SRP and OCP: swapping a weapon requires only a WeaponData asset swap,
/// no code changes needed.
/// </summary>
[RequireComponent(typeof(Hitbox))]
public class WeaponHolder : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────────
    [Header("Starting Weapon")]
    [Tooltip("Default weapon equipped at game start. Can be null (unarmed).")]
    [SerializeField] private WeaponData startingWeapon;

    // ─── Events ───────────────────────────────────────────────────────────────────
    /// <summary>Fires whenever the active weapon changes. Arg: new WeaponData (may be null).</summary>
    public event System.Action<WeaponData> OnWeaponChanged;

    // ─── Runtime State ────────────────────────────────────────────────────────────
    public WeaponData CurrentWeapon { get; private set; }

    // ─── Cached References ────────────────────────────────────────────────────────
    private Hitbox _hitbox;

    // ─── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        _hitbox = GetComponent<Hitbox>();
    }

    private void Start()
    {
        if (startingWeapon != null)
            Equip(startingWeapon);
    }

    // ─── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Equips a new weapon and updates the Hitbox accordingly.
    /// Passing null represents unarmed (bare-hand values applied).
    /// </summary>
    public void Equip(WeaponData newWeapon)
    {
        CurrentWeapon = newWeapon;

        // Update hitbox radius if the weapon specifies one
        if (_hitbox != null && newWeapon != null && newWeapon.HitboxRadius > 0f)
            _hitbox.SetRadius(newWeapon.HitboxRadius);

        OnWeaponChanged?.Invoke(CurrentWeapon);
        Debug.Log($"[WeaponHolder] Equipped: {(newWeapon != null ? newWeapon.WeaponName : "Unarmed")}");
    }

    /// <summary>
    /// Returns the correct damage for the current weapon and attack type.
    /// Falls back to bare-hand values if no weapon is equipped (GDD §5.11: punch 10, kick 20).
    /// </summary>
    public int GetLightDamage()  => CurrentWeapon != null ? CurrentWeapon.LightAttackDamage : 10;
    public int GetHeavyDamage()  => CurrentWeapon != null ? CurrentWeapon.HeavyAttackDamage : 20;
    public float GetKnockback()  => CurrentWeapon != null ? CurrentWeapon.KnockbackForce    : 3f;
}
