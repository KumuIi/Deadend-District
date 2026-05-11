using UnityEngine;

/// <summary>
/// Attach directly to the arms/weapon Empty.
/// Reads movement state from PlayerInput on the parent
/// and switches between: Idle (breathe), Walk (bob), Sprint (faster bob).
/// </summary>
public class WalkCycleArms : MonoBehaviour
{
    // ─── References ───────────────────────────────────────────────────
    [Header("References")]
    [Tooltip("The PlayerInput on the Player root. Auto-found if left empty.")]
    public PlayerInput playerInput;

    // ─── Idle / Breathing ─────────────────────────────────────────────
    [Header("Idle — Breathing")]
    public float idleBobAmount   = 0.008f;  // very subtle up/down
    public float idleBobSpeed    = 1.2f;    // slow breath cycle

    // ─── Walking ──────────────────────────────────────────────────────
    [Header("Walking")]
    public float walkBobAmount   = 0.04f;
    public float walkBobSpeed    = 8f;

    // ─── Sprinting ────────────────────────────────────────────────────
    [Header("Sprinting")]
    public float sprintBobAmount = 0.07f;
    public float sprintBobSpeed  = 13f;

    // ─── Blending ─────────────────────────────────────────────────────
    [Header("Blending")]
    [Tooltip("How fast the bob amplitude blends between states.")]
    public float blendSpeed      = 8f;

    // ─── Private ──────────────────────────────────────────────────────
    private Vector3 _restPosition;
    private float   _cycleTime;

    // Current blended values
    private float _currentAmount;
    private float _currentSpeed;

    private enum MoveState { Idle, Walk, Sprint }
    private MoveState _state = MoveState.Idle;

    // ──────────────────────────────────────────────────────────────────

    void Start()
    {
        _restPosition = transform.localPosition;

        if (playerInput == null)
            playerInput = GetComponentInParent<PlayerInput>();

        if (playerInput == null)
            Debug.LogError("[WalkCycleArms] No PlayerInput found in parent!", this);

        // Start blended values at idle
        _currentAmount = idleBobAmount;
        _currentSpeed  = idleBobSpeed;
    }

    void Update()
    {
        if (playerInput == null) return;

        bool isMoving   = playerInput.MoveInput.magnitude > 0.1f;
        bool isSprinting = playerInput.SprintHeld && isMoving;

        // Determine target state
        if (isSprinting)      _state = MoveState.Sprint;
        else if (isMoving)    _state = MoveState.Walk;
        else                  _state = MoveState.Idle;

        // Target values per state
        float targetAmount, targetSpeed;
        switch (_state)
        {
            case MoveState.Sprint:
                targetAmount = sprintBobAmount;
                targetSpeed  = sprintBobSpeed;
                break;
            case MoveState.Walk:
                targetAmount = walkBobAmount;
                targetSpeed  = walkBobSpeed;
                break;
            default: // Idle
                targetAmount = idleBobAmount;
                targetSpeed  = idleBobSpeed;
                break;
        }

        // Smoothly blend current values toward target
        _currentAmount = Mathf.Lerp(_currentAmount, targetAmount, blendSpeed * Time.deltaTime);
        _currentSpeed  = Mathf.Lerp(_currentSpeed,  targetSpeed,  blendSpeed * Time.deltaTime);

        // Always advance cycle time (idle keeps breathing, no hard stop)
        _cycleTime += Time.deltaTime * _currentSpeed;

        float bob = Mathf.Sin(_cycleTime) * _currentAmount;
        transform.localPosition = _restPosition + Vector3.up * bob;
    }
}