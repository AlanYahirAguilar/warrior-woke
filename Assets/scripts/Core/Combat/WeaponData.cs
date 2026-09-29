using UnityEngine;

/// <summary>
/// Immutable data container for a weapon definition.
/// ScriptableObject — create via Assets > Create > WarriorWoke > Weapon Data.
///
/// Best practices:
///  - ScriptableObject avoids per-instance allocations; shared across all WeaponHolder instances.
///  - All fields are ReadOnly at runtime through properties.
///  - Follows SRP: pure data, zero behaviour.
/// </summary>
[CreateAssetMenu(fileName = "NewWeaponData", menuName = "WarriorWoke/Weapon Data")]
public class WeaponData : ScriptableObject
{
    // ─── Identity ─────────────────────────────────────────────────────────────────
    [Header("Identity")]
    [SerializeField] private string weaponName = "Scrap Sword";
    [SerializeField] private Sprite icon;

    // ─── Combat Stats ──────────────────────────────────────────────────────────────
    [Header("Combat Stats")]
    [Tooltip("Damage applied by a light attack with this weapon.")]
    [SerializeField] private int lightAttackDamage = 10;

    [Tooltip("Damage applied by a heavy attack with this weapon.")]
    [SerializeField] private int heavyAttackDamage = 25;

    [Tooltip("Hitbox sphere radius override (0 = use Hitbox default).")]
    [SerializeField] private float hitboxRadius = 0f;

    [Tooltip("Force applied to the enemy Rigidbody on a heavy hit.")]
    [SerializeField] private float knockbackForce = 5f;

    // ─── Pickup ───────────────────────────────────────────────────────────────────
    [Header("Pickup")]
    [Tooltip("Unique string ID used to reference this weapon type in gameplay systems.")]
    [SerializeField] private string weaponId;

    // ─── Read-Only Properties ─────────────────────────────────────────────────────
    public string WeaponName        => weaponName;
    public Sprite Icon              => icon;
    public int    LightAttackDamage => lightAttackDamage;
    public int    HeavyAttackDamage => heavyAttackDamage;
    public float  HitboxRadius      => hitboxRadius;
    public float  KnockbackForce    => knockbackForce;
    public string WeaponId          => weaponId;
}
