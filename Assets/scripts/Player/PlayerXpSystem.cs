using UnityEngine;

/// <summary>
/// Manages the player's XP and fires events when thresholds are reached.
/// Attaches to the Player GameObject alongside HealthSystem and WeaponHolder.
///
/// Implements IXpReceiver — enemies call AddXp() on death without needing
/// to know anything about the player's internal systems (Dependency Inversion).
///
/// Best practices applied:
///  - Events use System.Action — zero allocation per invocation.
///  - No Update() loop — XP only changes on explicit AddXp() calls (dirty flag pattern).
/// </summary>
public class PlayerXpSystem : MonoBehaviour, IXpReceiver
{
    // ─── Inspector Configuration ─────────────────────────────────────────────────
    [Header("Progression")]
    [Tooltip("Starting XP required to reach level 2. Each subsequent level scales by LevelScaleFactor.")]
    [SerializeField] private int baseXpToNextLevel = 100;

    [Tooltip("Multiplier applied to XpToNextLevel per level (e.g. 1.3 = 30% more XP each level).")]
    [SerializeField] private float levelScaleFactor = 1.3f;

    // ─── Events ───────────────────────────────────────────────────────────────────
    /// <summary>Fires whenever XP changes. Args: currentXp, xpToNextLevel.</summary>
    public event System.Action<int, int> OnXpChanged;

    /// <summary>Fires whenever the player gains a level. Arg: new level.</summary>
    public event System.Action<int> OnLevelUp;

    // ─── IXpReceiver ─────────────────────────────────────────────────────────────
    public int CurrentXp { get; private set; }

    // ─── Runtime State ────────────────────────────────────────────────────────────
    public int CurrentLevel      { get; private set; } = 1;
    public int XpToNextLevel     { get; private set; }

    // ─── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        XpToNextLevel = baseXpToNextLevel;
    }

    // ─── IXpReceiver Implementation ───────────────────────────────────────────────

    /// <summary>
    /// Adds XP and processes level-ups. Called by EnemyDeadState or reward triggers.
    /// </summary>
    public void AddXp(int amount)
    {
        if (amount <= 0) return;

        CurrentXp += amount;
        OnXpChanged?.Invoke(CurrentXp, XpToNextLevel);

        // Handle multiple level-ups from a single XP grant
        while (CurrentXp >= XpToNextLevel)
        {
            CurrentXp    -= XpToNextLevel;
            CurrentLevel++;
            XpToNextLevel = Mathf.RoundToInt(XpToNextLevel * levelScaleFactor);

            OnLevelUp?.Invoke(CurrentLevel);
            Debug.Log($"[PlayerXpSystem] Level Up! Now level {CurrentLevel}. Next: {XpToNextLevel} XP.");

            // Fire updated XP values after deducting the threshold
            OnXpChanged?.Invoke(CurrentXp, XpToNextLevel);
        }
    }
}
