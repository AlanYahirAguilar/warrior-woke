using UnityEngine;

/// <summary>
/// Master player coordinator — Facade for all player subsystems.
/// Orchestrates input routing to movement and combat subsystems.
/// Adheres to Single Responsibility, Dependency Inversion, and Open/Closed principles.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerInputHandler))]
[RequireComponent(typeof(GroundChecker))]
public class Player : MonoBehaviour
{
    public static Player Instance { get; private set; }
    public static event System.Action<Player> OnPlayerSpawned;

    // ─── Subsystem References (cached in Awake — never looked up in the loop) ───
    private PlayerMovement _playerMovement;
    private IInputProvider _inputProvider;

    // ─── Lifecycle ───────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        InitializeSubsystems();
    }

    private void OnEnable()
    {
        OnPlayerSpawned?.Invoke(this);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        Debug.Log("[Player] Subsystems initialized and ready.");
    }

    // ─── Game Loop ───────────────────────────────────────────────────────────────

    private void FixedUpdate()
    {
        RouteInputToMovement();
    }

    // ─── Initialization ──────────────────────────────────────────────────────────

    private void InitializeSubsystems()
    {
        _playerMovement = GetComponent<PlayerMovement>();
        if (_playerMovement == null)
            _playerMovement = gameObject.AddComponent<PlayerMovement>();

        _inputProvider = GetComponent<IInputProvider>();
        if (_inputProvider == null)
            _inputProvider = gameObject.AddComponent<PlayerInputHandler>();

        if (GetComponent<IGroundChecker>() == null)
            gameObject.AddComponent<GroundChecker>();
    }

    // ─── Input Routing ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reads from the input provider and forwards consumed values to movement and combat.
    /// Called in FixedUpdate so it aligns with physics ticks.
    /// </summary>
    private void RouteInputToMovement()
    {
        if (_playerMovement == null || _inputProvider == null) return;

        _playerMovement.ProcessMovement(
            _inputProvider.HorizontalMove,
            _inputProvider.ConsumeJumpTrigger(),
            _inputProvider.ConsumeSlideTrigger(),
            _inputProvider.ConsumeLightAttackTrigger(),
            _inputProvider.ConsumeHeavyAttackTrigger(),
            _inputProvider.IsBlockHeld,
            _inputProvider.ConsumeDodgeTrigger()
        );
    }
}