using UnityEngine;

/// <summary>
/// Master player coordinator acting as a Facade for player subsystems in 2.5D.
/// Orchestrates input consumption and physical movement across the Unity game loop.
/// Adheres to Dependency Inversion and Single Responsibility principles.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerInputHandler))]
[RequireComponent(typeof(GroundChecker))]
public class Player : MonoBehaviour
{
    public static Player Instance { get; private set; }
    public static event System.Action<Player> OnPlayerSpawned;

    private PlayerMovement _playerMovement;
    private IInputProvider _inputProvider;
    //[SerializeField] private int fpsObjetivo = 30;

    private void Awake()
    {
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
        {
            Instance = null;
        }
    }

    private void Start()
    {
        Debug.Log("[Player] 2.5D Subsystems initialized and ready.");
        OnPlayerSpawned?.Invoke(this);
        
        // Desactivamos VSync para poder limitar manualmente los FPS.
        //QualitySettings.vSyncCount = 0;

        // Limitamos los FPS para realizar nuestra prueba.
        //Application.targetFrameRate = fpsObjetivo;
    }

    private void FixedUpdate()
    {
        RouteInputToMovement();
    }

    private void InitializeSubsystems()
    {
        // Cache movement subsystem
        _playerMovement = GetComponent<PlayerMovement>();
        if (_playerMovement == null)
        {
            _playerMovement = gameObject.AddComponent<PlayerMovement>();
        }

        // Cache input subsystem
        _inputProvider = GetComponent<IInputProvider>();
        if (_inputProvider == null)
        {
            _inputProvider = gameObject.AddComponent<PlayerInputHandler>();
        }

        // Ensure ground detection subsystem is present
        if (GetComponent<IGroundChecker>() == null)
        {
            gameObject.AddComponent<GroundChecker>();
        }
    }

    private void RouteInputToMovement()
    {
        if (_playerMovement == null || _inputProvider == null) return;

        float horizontal = _inputProvider.HorizontalMove;
        bool isSprint = _inputProvider.IsSprintPressed;
        bool jumpTriggered = _inputProvider.ConsumeJumpTrigger();
        bool slideTriggered = _inputProvider.ConsumeSlideTrigger();

        _playerMovement.ProcessMovement(horizontal, isSprint, jumpTriggered, slideTriggered);
    }
}