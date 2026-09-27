using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Captures and buffers all player inputs for a 3D action-platformer.
/// Input layout:
///   Movement  : A / D
///   Jump      : Space
///   Slide     : Left Shift
///   LightAtk  : Mouse1 (left click)
///   HeavyAtk  : Mouse2 (right click)
///   Block     : F (held)
///   Dodge     : E
///
/// Follows SRP — this class only reads hardware input and exposes it via IInputProvider.
/// Zero GC allocations per frame: no new() or string operations inside Update().
/// </summary>
public class PlayerInputHandler : MonoBehaviour, IInputProvider
{
    // ─── Input Action References (optional — New Input System) ──────────────────
    [Header("Optional Input Action References")]
    [SerializeField] private InputActionReference moveActionReference;
    [SerializeField] private InputActionReference jumpActionReference;
    [SerializeField] private InputActionReference slideActionReference;
    [SerializeField] private InputActionReference lightAttackActionReference;
    [SerializeField] private InputActionReference heavyAttackActionReference;
    [SerializeField] private InputActionReference blockActionReference;
    [SerializeField] private InputActionReference dodgeActionReference;

    // ─── Buffered State ─────────────────────────────────────────────────────────
    private float _horizontalMove;
    private bool _isJumpTriggered;
    private bool _isSlideTriggered;
    private bool _isLightAttackTriggered;
    private bool _isHeavyAttackTriggered;
    private bool _isBlockHeld;
    private bool _isDodgeTriggered;

    // ─── IInputProvider Properties ───────────────────────────────────────────────
    public float HorizontalMove => _horizontalMove;
    public bool IsJumpTriggered => _isJumpTriggered;
    public bool IsSlideTriggered => _isSlideTriggered;
    public bool IsLightAttackTriggered => _isLightAttackTriggered;
    public bool IsHeavyAttackTriggered => _isHeavyAttackTriggered;
    public bool IsBlockHeld => _isBlockHeld;
    public bool IsDodgeTriggered => _isDodgeTriggered;

    // ─── Lifecycle ───────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        moveActionReference?.action?.Enable();
        jumpActionReference?.action?.Enable();
        slideActionReference?.action?.Enable();
        lightAttackActionReference?.action?.Enable();
        heavyAttackActionReference?.action?.Enable();
        blockActionReference?.action?.Enable();
        dodgeActionReference?.action?.Enable();
    }

    private void OnDisable()
    {
        moveActionReference?.action?.Disable();
        jumpActionReference?.action?.Disable();
        slideActionReference?.action?.Disable();
        lightAttackActionReference?.action?.Disable();
        heavyAttackActionReference?.action?.Disable();
        blockActionReference?.action?.Disable();
        dodgeActionReference?.action?.Disable();
    }

    private void Update()
    {
        ReadMovementInput();
        ReadParkourInput();
        ReadCombatInput();
    }

    // ─── Private Readers ─────────────────────────────────────────────────────────

    private void ReadMovementInput()
    {
        if (moveActionReference?.action != null)
        {
            _horizontalMove = moveActionReference.action.ReadValue<Vector2>().x;
            return;
        }

        if (Keyboard.current == null) return;

        var kb = Keyboard.current;
        bool right = kb.dKey.isPressed || kb.rightArrowKey.isPressed;
        bool left  = kb.aKey.isPressed || kb.leftArrowKey.isPressed;

        if (right && left)
        {
            // Last-input-wins: keep current value when both held simultaneously
            if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame)
                _horizontalMove = 1f;
            else if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame)
                _horizontalMove = -1f;
        }
        else if (right)  _horizontalMove =  1f;
        else if (left)   _horizontalMove = -1f;
        else             _horizontalMove =  0f;
    }

    private void ReadParkourInput()
    {
        // Jump
        if (jumpActionReference?.action != null)
        {
            if (jumpActionReference.action.WasPressedThisFrame())
                _isJumpTriggered = true;
        }
        else if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            _isJumpTriggered = true;
        }

        // Slide — Left Shift
        if (slideActionReference?.action != null)
        {
            if (slideActionReference.action.WasPressedThisFrame())
                _isSlideTriggered = true;
        }
        else if (Keyboard.current != null &&
                 (Keyboard.current.leftShiftKey.wasPressedThisFrame ||
                  Keyboard.current.rightShiftKey.wasPressedThisFrame))
        {
            _isSlideTriggered = true;
        }
    }

    private void ReadCombatInput()
    {
        var mouse = Mouse.current;
        var kb    = Keyboard.current;

        // Light Attack — Mouse1
        if (lightAttackActionReference?.action != null)
        {
            if (lightAttackActionReference.action.WasPressedThisFrame())
                _isLightAttackTriggered = true;
        }
        else if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            _isLightAttackTriggered = true;
        }

        // Heavy Attack — Mouse2
        if (heavyAttackActionReference?.action != null)
        {
            if (heavyAttackActionReference.action.WasPressedThisFrame())
                _isHeavyAttackTriggered = true;
        }
        else if (mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            _isHeavyAttackTriggered = true;
        }

        // Block — F (held, not triggered)
        if (blockActionReference?.action != null)
        {
            _isBlockHeld = blockActionReference.action.IsPressed();
        }
        else
        {
            _isBlockHeld = kb != null && kb.fKey.isPressed;
        }

        // Dodge — E
        if (dodgeActionReference?.action != null)
        {
            if (dodgeActionReference.action.WasPressedThisFrame())
                _isDodgeTriggered = true;
        }
        else if (kb != null && kb.eKey.wasPressedThisFrame)
        {
            _isDodgeTriggered = true;
        }
    }

    // ─── Consume Methods (prevent double-consumption across Update/FixedUpdate) ──

    public bool ConsumeJumpTrigger()
    {
        bool value = _isJumpTriggered;
        _isJumpTriggered = false;
        return value;
    }

    public bool ConsumeSlideTrigger()
    {
        bool value = _isSlideTriggered;
        _isSlideTriggered = false;
        return value;
    }

    public bool ConsumeLightAttackTrigger()
    {
        bool value = _isLightAttackTriggered;
        _isLightAttackTriggered = false;
        return value;
    }

    public bool ConsumeHeavyAttackTrigger()
    {
        bool value = _isHeavyAttackTriggered;
        _isHeavyAttackTriggered = false;
        return value;
    }

    public bool ConsumeDodgeTrigger()
    {
        bool value = _isDodgeTriggered;
        _isDodgeTriggered = false;
        return value;
    }
}
