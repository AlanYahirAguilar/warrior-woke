using UnityEngine;

/// <summary>
/// Master player coordinator acting as a Facade for player subsystems.
/// Orchestrates input acquisition and physical movement across the Unity game loop.
/// Adheres to Dependency Inversion and Single Responsibility principles.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(PlayerInputHandler))]
public class Player : MonoBehaviour
{
    private PlayerMovement _playerMovement;
    private IInputProvider _inputProvider;

    private void Awake()
    {
        // Cache subsystem dependencies once to eliminate loop queries
        _playerMovement = GetComponent<PlayerMovement>();
        if (_playerMovement == null)
        {
            _playerMovement = gameObject.AddComponent<PlayerMovement>();
        }

        _inputProvider = GetComponent<IInputProvider>();
        if (_inputProvider == null)
        {
            _inputProvider = gameObject.AddComponent<PlayerInputHandler>();
        }
    }

    private void Start()
    {
        Debug.Log("[Player] Subsystems initialized and ready.");
    }

    private void FixedUpdate()
    {
        // Route input to physics simulation during the fixed timestep loop
        if (_playerMovement != null && _inputProvider != null)
        {
            _playerMovement.ProcessMovement(_inputProvider.MoveInput);
        }
    }
}