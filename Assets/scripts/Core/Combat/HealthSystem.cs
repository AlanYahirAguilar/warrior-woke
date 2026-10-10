using UnityEngine;

/// <summary>
/// Generic health management component. Implements IDamageable.
/// Reusable across Player, Enemies, and interactive objects.
///
/// Best practices applied:
///  - Zero GC: events use System.Action, no delegates allocated per call.
///  - Iframes enforced here, not in the state machine.
///  - Health is always clamped between 0 and MaxHealth (GDD §5.11).
/// </summary>
public class HealthSystem : MonoBehaviour, IDamageable
{
    // ─── Inspector Configuration ─────────────────────────────────────────────────
    [Header("Configuration")]
    [SerializeField] private int maxHealth = 100;

    [Header("Invincibility Frames")]
    [Tooltip("Seconds of invincibility after receiving damage. Player: ~0.5s (GDD §5.11). Enemies must stay below the 0.25s light-attack cadence or combo hits are ignored.")]
    [SerializeField] private float iFramesDuration = 0.5f;

    // ─── Events ───────────────────────────────────────────────────────────────────
    /// <summary>Fires when health changes. Args: currentHealth, maxHealth.</summary>
    public event System.Action<int, int> OnHealthChanged;

    /// <summary>Fires when health reaches zero.</summary>
    public event System.Action OnDeath;

    /// <summary>Fires when damage is received. Args: amount, source position.</summary>
    public event System.Action<int, Vector3> OnDamageReceived;

    // ─── IDamageable ─────────────────────────────────────────────────────────────
    public int  CurrentHealth { get; private set; }
    public bool IsDead        => CurrentHealth <= 0;

    // ─── Runtime State ────────────────────────────────────────────────────────────
    private float _lastDamageTime = -999f;
    private IDamageModifier _damageModifier;

    // ─── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        CurrentHealth   = maxHealth;
        _damageModifier = GetComponent<IDamageModifier>();
    }

    // ─── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Overrides the max health and resets current health.
    /// Call this in Awake of the owning entity (e.g., Enemy.cs) BEFORE Start() runs.
    /// </summary>
    public void InitializeHealth(int max)
    {
        maxHealth     = max;
        CurrentHealth = max;
    }

    /// <summary>
    /// Applies damage with iframes check. Clamps health to [0, MaxHealth].
    /// An IDamageModifier on the same GameObject (e.g. the player's block) can reduce it first.
    /// </summary>
    public void TakeDamage(int amount, Vector3 source)
    {
        if (IsDead) return;
        if (IsInIFrames()) return;

        if (_damageModifier != null)
            amount = _damageModifier.ModifyIncomingDamage(amount, source);

        _lastDamageTime = Time.time;
        CurrentHealth   = Mathf.Clamp(CurrentHealth - amount, 0, maxHealth);

        OnDamageReceived?.Invoke(amount, source);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);

        if (IsDead)
            OnDeath?.Invoke();
    }

    /// <summary>Restores health, clamped to MaxHealth.</summary>
    public void Heal(int amount)
    {
        if (IsDead) return;
        CurrentHealth = Mathf.Clamp(CurrentHealth + amount, 0, maxHealth);
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    /// <summary>Back to full health after a death (a respawn): no invulnerability left over.</summary>
    public void Revive()
    {
        CurrentHealth   = maxHealth;
        _lastDamageTime = -999f;
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
    }

    /// <summary>Kills the entity immediately regardless of current health.</summary>
    public void InstantKill()
    {
        if (IsDead) return;
        CurrentHealth = 0;
        OnHealthChanged?.Invoke(CurrentHealth, maxHealth);
        OnDeath?.Invoke();
    }

    public int MaxHealth => maxHealth;

    /// <summary>
    /// Grants temporary invincibility for <paramref name="duration"/> seconds.
    /// Call from DodgeState, ability triggers, or cutscene handlers.
    /// Zero GC: no Coroutine — reuses the existing Time.time iframes gate.
    /// </summary>
    public void ActivateIFrames(float duration)
    {
        // Push _lastDamageTime forward so the iframes gate stays active for 'duration' seconds.
        // Max() keeps longer iframes that are already running (e.g. from a hit just taken).
        _lastDamageTime = Mathf.Max(_lastDamageTime, Time.time - iFramesDuration + duration);
    }

    // ─── Private Helpers ─────────────────────────────────────────────────────────

    private bool IsInIFrames()
    {
        return Time.time - _lastDamageTime < iFramesDuration;
    }
}
