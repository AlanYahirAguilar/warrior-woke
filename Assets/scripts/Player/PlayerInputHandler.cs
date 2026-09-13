using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles player input capture for 2.5D movement using Unity's New Input System with keyboard fallbacks.
/// Adheres to Single Responsibility Principle (SRP) and generates zero GC allocations per frame.
/// </summary>
public class PlayerInputHandler : MonoBehaviour, IInputProvider
{
    [Header("Optional Input Actions")]
    [Tooltip("Optional reference to a move action (Vector2 or Axis).")]
    [SerializeField] private InputActionReference moveActionReference;

    [Tooltip("Optional reference to jump action.")]
    [SerializeField] private InputActionReference jumpActionReference;

    [Tooltip("Optional reference to sprint action.")]
    [SerializeField] private InputActionReference sprintActionReference;

    [Tooltip("Optional reference to slide action.")]
    [SerializeField] private InputActionReference slideActionReference;

    private float _horizontalMove;
    private bool _isSprintPressed;
    private bool _isJumpTriggered;
    private bool _isSlideTriggered;

    public float HorizontalMove => _horizontalMove;
    public bool IsSprintPressed => _isSprintPressed;
    public bool IsJumpTriggered => _isJumpTriggered;
    public bool IsSlideTriggered => _isSlideTriggered;

    private void OnEnable()
    {
        moveActionReference?.action?.Enable();
        jumpActionReference?.action?.Enable();
        sprintActionReference?.action?.Enable();
        slideActionReference?.action?.Enable();
    }

    private void OnDisable()
    {
        moveActionReference?.action?.Disable();
        jumpActionReference?.action?.Disable();
        sprintActionReference?.action?.Disable();
        slideActionReference?.action?.Disable();
    }

    private void Update()
    {
        ReadInput();
    }

    private void ReadInput()
    {
        // 1. Horizontal Movement
        if (moveActionReference != null && moveActionReference.action != null)
        {
            Vector2 moveVec = moveActionReference.action.ReadValue<Vector2>();
            _horizontalMove = moveVec.x;
        }
        else if (Keyboard.current != null)
        {
            var keyboard = Keyboard.current;
            float move = 0f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move -= 1f;
            _horizontalMove = move;
        }

        // 2. Sprint Input
        if (sprintActionReference != null && sprintActionReference.action != null)
        {
            _isSprintPressed = sprintActionReference.action.IsPressed();
        }
        else if (Keyboard.current != null)
        {
            var keyboard = Keyboard.current;
            _isSprintPressed = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        }

        // 3. Jump Trigger
        if (jumpActionReference != null && jumpActionReference.action != null)
        {
            if (jumpActionReference.action.WasPressedThisFrame())
            {
                _isJumpTriggered = true;
            }
        }
        else if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            _isJumpTriggered = true;
        }

        // 4. Slide Trigger
        if (slideActionReference != null && slideActionReference.action != null)
        {
            if (slideActionReference.action.WasPressedThisFrame())
            {
                _isSlideTriggered = true;
            }
        }
        else if (Keyboard.current != null)
        {
            var keyboard = Keyboard.current;
            if (keyboard.leftCtrlKey.wasPressedThisFrame || keyboard.rightCtrlKey.wasPressedThisFrame)
            {
                _isSlideTriggered = true;
            }
        }
    }

    public bool ConsumeJumpTrigger()
    {
        bool triggered = _isJumpTriggered;
        _isJumpTriggered = false;
        return triggered;
    }

    public bool ConsumeSlideTrigger()
    {
        bool triggered = _isSlideTriggered;
        _isSlideTriggered = false;
        return triggered;
    }
}
