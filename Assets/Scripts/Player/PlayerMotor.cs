using UnityEngine;

public class PlayerMotor : MonoBehaviour
{
    [SerializeField] private PlayerMovementConfig config;

    private Rigidbody       _rb;
    private PlayerInput     _input;
    private CapsuleCollider _capsule;
    private Vector3         _velocity;
    private bool            _grounded;
    private RaycastHit      _groundHit;

    private readonly Collider[] _overlapBuffer = new Collider[8];
    private readonly Vector3[]  _pushNormals   = new Vector3[8];

    private const float CapsuleHalfHeight = 0.9f;
    private const float CapsuleRadius     = 0.3f;
    private const float GroundCheckDist   = 0.2f;
    private const float SkinWidth         = 0.01f;

    void Awake()
    {
        _rb      = GetComponent<Rigidbody>();
        _input   = GetComponent<PlayerInput>();
        _capsule = GetComponent<CapsuleCollider>();
    }

    void FixedUpdate()
    {
        CheckGround();
        HandleGravity();
        HandleJump();
        HandleMovement();

        Vector3 target = _rb.position + _velocity * Time.fixedDeltaTime;

        ResolveCollisions(ref target);
        SnapToGround(ref target);

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
        _grounded = Physics.Raycast(
            _rb.position, Vector3.down, out _groundHit,
            CapsuleHalfHeight + GroundCheckDist,
            config.groundMask, QueryTriggerInteraction.Ignore);
    }

    void SnapToGround(ref Vector3 target)
    {
        // Don't snap while jumping or airborne
        if (!_grounded || _velocity.y > 0f) return;

        // Cast from TARGET — find the ground under where we're actually going,
        // not where we were. This is what keeps us on ramps and edges.
        if (Physics.Raycast(target, Vector3.down, out RaycastHit hit,
                CapsuleHalfHeight + GroundCheckDist,
                config.groundMask, QueryTriggerInteraction.Ignore))
        {
            target.y    = hit.point.y + CapsuleHalfHeight;
            _velocity.y = 0f;
        }
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

    // ─── Collision resolution ────────────────────────────────────────────────

    void ResolveCollisions(ref Vector3 position)
    {
        int normalCount = 0;

        // Phase 1 — settle position: push out of every overlapping surface,
        // re-check, repeat until clean or we hit the iteration cap.
        for (int iter = 0; iter < 3; iter++)
        {
            Vector3 bottom = position + Vector3.down * (CapsuleHalfHeight - CapsuleRadius);
            Vector3 top    = position + Vector3.up   * (CapsuleHalfHeight - CapsuleRadius);

            int count = Physics.OverlapCapsuleNonAlloc(
                bottom, top, CapsuleRadius,
                _overlapBuffer, config.collisionMask, QueryTriggerInteraction.Ignore);

            bool pushed = false;
            for (int i = 0; i < count; i++)
            {
                Collider other = _overlapBuffer[i];
                if (other == _capsule) continue;

                if (!Physics.ComputePenetration(
                        _capsule, position,              transform.rotation,
                        other,    other.transform.position, other.transform.rotation,
                        out Vector3 dir, out float dist))
                    continue;

                position += dir * (dist + SkinWidth);
                pushed = true;

                // Remember surface normal for the velocity pass
                if (normalCount < _pushNormals.Length)
                    _pushNormals[normalCount++] = dir;
            }

            if (!pushed) break;
        }

        // Phase 2 — clip velocity: for each surface we touched, remove only
        // the velocity component pressing INTO it. Parallel motion is kept.
        for (int i = 0; i < normalCount; i++)
        {
            Vector3 pushDir = _pushNormals[i];

            if (_grounded)
            {
                // Ground owns Y — only cancel horizontal velocity into the surface.
                Vector3 flatDir = new Vector3(pushDir.x, 0f, pushDir.z);
                if (flatDir.sqrMagnitude > 0.001f)
                {
                    flatDir.Normalize();
                    float velInto = _velocity.x * flatDir.x + _velocity.z * flatDir.z;
                    if (velInto < 0f)
                    {
                        _velocity.x -= velInto * flatDir.x;
                        _velocity.z -= velInto * flatDir.z;
                    }
                }
            }
            else
            {
                // Airborne — full 3D correction handles ceilings and walls in air
                float velInto = Vector3.Dot(_velocity, pushDir);
                if (velInto < 0f)
                    _velocity -= velInto * pushDir;
            }
        }
    }

    // ─── Debug ───────────────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        Vector3 origin = Application.isPlaying ? _rb.position : transform.position;
        float   length = CapsuleHalfHeight + GroundCheckDist;

        Gizmos.color = Application.isPlaying && _grounded ? Color.green : Color.red;
        Gizmos.DrawLine(origin, origin + Vector3.down * length);

        if (Application.isPlaying && _grounded)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(_groundHit.point, 0.05f);
        }
    }
}
