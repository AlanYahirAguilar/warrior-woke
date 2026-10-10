using UnityEngine;

/// <summary>Combat archetype of an enemy (GDD §12–§13).</summary>
public enum EnemyKind { Light, Heavy, Archer, Boss }

/// <summary>The two bosses' identities (GDD §13): the rival clan's leader (world 1) and the Commander (final).</summary>
public enum BossStyle { None, ClanLeader, Commander }

/// <summary>What an enemy holds (a primitive mesh until the team's models arrive, P4).</summary>
public enum EnemyWeaponKind { Katana, Kanabo, Bow }

/// <summary>
/// One attack of an enemy: its Animator state and the measured phases of its clip (P40: EnemySetup samples
/// each clip on Ch45 and finds where the weapon hand strikes). The weapon can only hurt between
/// <see cref="activeStart"/> and <see cref="activeEnd"/> (normalized clip time), once per target.
/// </summary>
[System.Serializable]
public class EnemyAttack
{
    public string name = "Attack";
    [Tooltip("Animator state of the attack (EnemyAnimator.controller).")]
    public string state;
    [Tooltip("Clip length (s) at rate 1, and the playback rate.")]
    public float length = 1f, rate = 1f;
    [Tooltip("Normalized clip time when the weapon starts and stops striking (measured: the weapon hand's fast part).")]
    public float activeStart = 0.3f, activeEnd = 0.5f;
    public int damage = 10;
    [Tooltip("A heavy blow: telegraphed (the wind-up is held), staggers the player, breaks a guard.")]
    public bool heavy;
    [Tooltip("Distance (m, from the enemy's center to the target's) from which the attack is started.")]
    public float reach = 1.8f;
    [Tooltip("The clip's own step (a dash, a charge) moves the body, stopped at arm's length from the target.")]
    public bool rootMotion;
    [Tooltip("A charge: the body itself strikes (its shoulder and chest), while the clip's travel is fastest.")]
    public bool bodyHit;
    [Tooltip("Extra metres the body travels toward the target while the weapon strikes (for clips without their own step).")]
    public float lunge;
    [Tooltip("Seconds the wind-up is held before the strike (the telegraph of a heavy blow).")]
    public float windupHold;
    [Tooltip("Seconds of extra recovery after the clip: the opening the player can punish.")]
    public float vulnerableAfter;
    [Tooltip("Index of the attack that may follow in a combo (−1: none).")]
    public int next = -1;
}

/// <summary>
/// Immutable data of an enemy archetype (D5): GDD values (health, damage, behavior) and the tuning of its
/// AI. ScriptableObject — the assets (Assets/Data/Enemies) are written by Tools → Warrior Woke → Configurar
/// Enemigos (EnemySetup), with the measured attack phases. Read-only at runtime.
/// </summary>
[CreateAssetMenu(fileName = "NewEnemyData", menuName = "WarriorWoke/Enemy Data")]
public class EnemyData : ScriptableObject
{
    [Header("Identity")]
    public string enemyName = "Enemy";
    public EnemyKind kind = EnemyKind.Light;
    public BossStyle bossStyle = BossStyle.None;
    public EnemyWeaponKind weapon = EnemyWeaponKind.Katana;
    [Tooltip("Tint of the placeholder model (Ch45) until the team's models arrive.")]
    public Color tint = Color.white;

    [Header("Vitals (GDD §5.11)")]
    public int maxHealth = 80;
    [Tooltip("Damage taken (within PoiseWindow s) that interrupts and staggers it: low = every blow flinches it, high = only heavy blows.")]
    public int poise = 15;
    [Tooltip("Seconds of invulnerability after a hit (below the player's 0.25 s chain cadence).")]
    public float iFrames = 0.1f;

    [Header("Movement")]
    public float walkSpeed = 1.6f, runSpeed = 4.5f, strafeSpeed = 1.8f;
    [Tooltip("Fastest turn (°/s) toward the target.")]
    public float turnRate = 540f;

    [Header("Perception (GDD §21)")]
    public float sightRange = 18f;
    [Tooltip("Half angle (°) of the field of view.")]
    public float sightAngle = 70f;
    [Tooltip("A target moving fast or fighting within this distance is heard (also behind or out of sight).")]
    public float hearingRange = 8f;
    [Tooltip("Seconds without seeing the target before it goes to look where it was last seen.")]
    public float loseTime = 3f;
    [Tooltip("Seconds it looks around where the target was before going back to its post.")]
    public float investigateTime = 4f;

    [Header("Combat")]
    [Tooltip("Distance band (m) it keeps to the target while waiting to attack.")]
    public float preferredMin = 2.2f, preferredMax = 3.2f;
    [Tooltip("Seconds between its attacks (GDD §5.14: every ~0.5–1.5 s).")]
    public float attackIntervalMin = 0.6f, attackIntervalMax = 1.3f;
    public EnemyAttack[] attacks = new EnemyAttack[0];
    [Range(0f, 1f), Tooltip("Chance to raise the guard when the target starts an attack in front of it.")]
    public float blockChance = 0.1f;
    [Range(0f, 1f), Tooltip("Chance to dodge when the target starts an attack in front of it.")]
    public float dodgeChance = 0.3f;
    [Range(0f, 1f), Tooltip("Chance to attack at once when the target is open (recovering from its own attack or a hit).")]
    public float punishChance = 0.5f;
    [Tooltip("Share of a frontal hit's damage that gets through its guard.")]
    public float guardDamageKept = 0.2f;

    [Header("Ranged (archer)")]
    public int arrowDamage = 10;
    public float arrowSpeed = 28f;
    [Tooltip("Seconds it aims (draws) before releasing.")]
    public float aimTime = 0.7f;
    [Tooltip("Closer than this (m) it backs away to its range.")]
    public float retreatDistance = 5f;
}
