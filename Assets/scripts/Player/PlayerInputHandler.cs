using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Component responsible solely for capturing and exposing player input through the New Input System.
/// Adheres to Single Responsibility Principle (SRP) and implements IInputProvider.
/// Generates zero GC allocations per frame during the update loop.
/// </summary>
public class PlayerInputHandler : MonoBehaviour, IInputProvider
{
    [Header("Input Configuration")]
    [Tooltip("Optional reference to an InputAction in an .inputactions asset. If not assigned, attempts to find 'Move' action or falls back to direct Keyboard polling.")]
    [SerializeField] private InputActionReference moveActionReference;

    private InputAction _activeAction;
    private Vector2 _moveInput;
    private bool _hasMoveInput;

    public Vector2 MoveInput => _moveInput;
    public bool HasMoveInput => _hasMoveInput;

    private void Awake()
    {
        InitializeAction();
    }

    private void OnEnable()
    {
        _activeAction?.Enable();
    }

    private void OnDisable()
    {
        _activeAction?.Disable();
    }

    private void Update()
    {
        if (_activeAction != null)
        {
            // Reading Vector2 from InputAction generates zero garbage allocations
            _moveInput = _activeAction.ReadValue<Vector2>();
        }
        else if (Keyboard.current != null)
        {
            // Zero-allocation fallback for instant playability without manual Inspector binding
            float x = 0f;
            float y = 0f;

            var keyboard = Keyboard.current;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) y -= 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) x += 1f;

            _moveInput = new Vector2(x, y);
        }

        _hasMoveInput = _moveInput.sqrMagnitude > 0.001f;
    }

    /// <summary>
    /// Configures the active input action from the assigned reference or searches for a default action.
    /// </summary>
    private void InitializeAction()
    {
        if (moveActionReference != null && moveActionReference.action != null)
        {
            _activeAction = moveActionReference.action;
        }
        else
        {
            _activeAction = InputSystem.actions?.FindAction("Move");
        }
    }
}
