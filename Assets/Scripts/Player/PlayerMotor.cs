using UnityEngine;

public class PlayerMotor : MonoBehaviour
{
    [SerializeField] private PlayerMovementConfig config;

    private Rigidbody   _rb;
    private PlayerInput _input;
    private Vector3     _velocity;
    private bool        _grounded;
    private RaycastHit  _groundHit;

    // Capsule: height 1.8, radius 0.3 → half-height = 0.9
    private const float CapsuleHalfHeight = 0.9f;
    private const float GroundCheckDist   = 0.1f;   // how far below feet to look

    void Awake()
    {
        _rb    = GetComponent<Rigidbody>();
        _input = GetComponent<PlayerInput>();
    }

    void FixedUpdate()
    {
        CheckGround();
        HandleGravity();
        HandleJump();
        HandleMovement();

        Vector3 target = _rb.position + _velocity * Time.fixedDeltaTime;

        // Snap to ground — kinematic body has no collision resolution,
        // so we pin the player to the surface ourselves
        if (_grounded && _velocity.y <= 0f)
            target.y = _groundHit.point.y + CapsuleHalfHeight;

        _rb.MovePosition(target);
    }

    void LateUpdate()
    {
        float mouseX = Input.GetAxisRaw("Mouse X") * config.mouseSensitivity;
        transform.Rotate(0f, mouseX, 0f);
    }

    // ─── Ground ──────────────────────────────────────────────────────────────

    void CheckGround()
    {
        // Raycast from capsule center straight down
        // Max distance = half-height + tolerance → hits ground under our feet
        _grounded = Physics.Raycast(
            _rb.position, Vector3.down, out _groundHit,
            CapsuleHalfHeight + GroundCheckDist,
            config.groundMask, QueryTriggerInteraction.Ignore);
    }

    // ─── Gravity ─────────────────────────────────────────────────────────────

    void HandleGravity()
    {
        if (_grounded && _velocity.y <= 0f)
            _velocity.y = 0f;
        else
            _velocity.y -= config.gravity * Time.fixedDeltaTime;
    }

    // ─── Jump ────────────────────────────────────────────────────────────────

    void HandleJump()
    {
        if (_input.JumpPressed)
        {
            if (_grounded)
                _velocity.y = config.jumpForce;

            _input.ConsumeJump();
        }
    }

    // ─── Horizontal movement ─────────────────────────────────────────────────

    void HandleMovement()
    {
        Vector2 move      = _input.MoveInput;
        bool    sprinting = _input.SprintHeld && move.y > 0f;
        float   speed     = sprinting ? config.sprintSpeed : config.walkSpeed;

        Vector3 forward    = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 right      = Vector3.ProjectOnPlane(transform.right,   Vector3.up).normalized;
        Vector3 horizontal = forward * move.y + right * move.x;

        if (horizontal.sqrMagnitude > 1f) horizontal.Normalize();

        _velocity.x = horizontal.x * speed;
        _velocity.z = horizontal.z * speed;
    }

    // ─── Debug ───────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        Vector3 origin = Application.isPlaying ? _rb.position : transform.position;
        float   length = CapsuleHalfHeight + GroundCheckDist;

        // Ray line
        Gizmos.color = Application.isPlaying && _grounded ? Color.green : Color.red;
        Gizmos.DrawLine(origin, origin + Vector3.down * length);

        // Hit point marker
        if (Application.isPlaying && _grounded)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(_groundHit.point, 0.05f);
        }
    }
}
