using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInput : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputActions;

    public Vector2 MoveInput { get; private set; }
    public Vector2 LookInput { get; private set; }
    public bool JumpPressed { get; private set; }
    public bool SprintHeld { get; private set; }
    public bool CrouchPressed { get; private set; }

    private InputActionMap _playerMap;
    private InputAction _moveAction;
    private InputAction _lookAction;
    private InputAction _jumpAction;
    private InputAction _sprintAction;
    private InputAction _crouchAction;

    void Awake()
    {
        _playerMap = inputActions.FindActionMap("Player");
        _moveAction = _playerMap.FindAction("Move");
        _lookAction = _playerMap.FindAction("Look");
        _jumpAction = _playerMap.FindAction("Jump");
        _sprintAction = _playerMap.FindAction("Sprint");
        _crouchAction = _playerMap.FindAction("Crouch");
    }

    void OnEnable()  => _playerMap?.Enable();
    void OnDisable() => _playerMap?.Disable();

    void Update()
    {
        MoveInput = _moveAction.ReadValue<Vector2>();
        LookInput = _lookAction.ReadValue<Vector2>();
        SprintHeld = _sprintAction.IsPressed();

        if (_jumpAction.WasPressedThisFrame())
            JumpPressed = true;

        if (_crouchAction.WasPressedThisFrame())
            CrouchPressed = true;
    }

    public void ConsumeJump()
    {
        JumpPressed = false;
    }

    public void ConsumeCrouch()
    {
        CrouchPressed = false;
    }
}
