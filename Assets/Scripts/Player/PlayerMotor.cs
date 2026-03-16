using UnityEngine;

public class PlayerMotor : MonoBehaviour
{
    [SerializeField] private PlayerMovementConfig config;

    private Rigidbody       _rb;
    private PlayerInput     _input;
    private CapsuleCollider _capsule;
    private Vector3         _velocity;
    private bool            _grounded;
    private bool            _steepGround;
    private float           _groundAngle;
    private RaycastHit      _groundHit;
    private bool            _hitCeiling;

    // Coyote time
    private float           _coyoteTimer;   // time since last grounded, allows late jumps

    // Slope mode state
    private bool            _inSlopeMode;   // steep slope has taken over movement
    private bool            _isSliding;     // sub-state: speed drained, now sliding downhill
    private float           _slopeSpeed;    // draining speed during slope mode (starts at walk/sprint)
    private float           _slideSpeed;    // accelerating speed while sliding down

    private readonly Collider[] _overlapBuffer = new Collider[8];
    private readonly Vector3[]  _pushNormals   = new Vector3[8];

    private const float CapsuleHalfHeight = 0.9f;
    private const float CapsuleRadius     = 0.3f;
    private const float GroundCheckDist   = 0.2f;
    private const float SkinWidth         = 0.01f;
    private const float SlideMaxSpeed     = 10f;

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
        HandleSteepSlope();
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
        bool hit = Physics.Raycast(
            _rb.position, Vector3.down, out _groundHit,
            CapsuleHalfHeight + GroundCheckDist,
            config.groundMask, QueryTriggerInteraction.Ignore);

        _groundAngle = hit ? Vector3.Angle(_groundHit.normal, Vector3.up) : 0f;
        _steepGround = hit && _groundAngle >= config.maxSlopeAngle;
        _grounded    = hit && !_steepGround;

        // Coyote timer: reset while grounded, tick up while airborne
        if (_grounded)
            _coyoteTimer = 0f;
        else
            _coyoteTimer += Time.fixedDeltaTime;
    }

    void SnapToGround(ref Vector3 target)
    {
        if (!_grounded || _velocity.y > 0f) return;

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
            bool canJump = (_grounded || _coyoteTimer <= config.coyoteTime)
                        && !_inSlopeMode && !_hitCeiling && !_steepGround;

            if (canJump)
            {
                _velocity.y  = config.jumpForce;
                _coyoteTimer = config.coyoteTime + 1f; // expire so you can't double-jump
            }

            _input.ConsumeJump();
        }
    }

    // ─── Normal movement — only when NOT in slope mode ───────────────────────

    void HandleMovement()
    {
        // Slope mode completely owns movement — normal movement is locked out
        if (_inSlopeMode) return;

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

    // ─── Steep slope — separate movement system ──────────────────────────────

    void HandleSteepSlope()
    {
        if (_steepGround)
        {
            // --- ENTER slope mode ---
            if (!_inSlopeMode)
            {
                _inSlopeMode = true;
                _isSliding   = false;
                _slideSpeed  = 0f;
                // Capture current horizontal speed as starting slope speed
                _slopeSpeed  = Mathf.Sqrt(_velocity.x * _velocity.x + _velocity.z * _velocity.z);
            }

            float steepness = Mathf.InverseLerp(config.maxSlopeAngle, 90f, _groundAngle);

            // --- DRAIN phase: player can still move but speed is diminishing ---
            if (!_isSliding)
            {
                // Steeper = faster drain
                float drain = Mathf.Lerp(2f, 20f, steepness) * Time.fixedDeltaTime;
                _slopeSpeed = Mathf.Max(0f, _slopeSpeed - drain);

                // Same input controls as normal movement, but using the draining speed
                Vector2 move    = _input.MoveInput;
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                Vector3 right   = Vector3.ProjectOnPlane(transform.right,   Vector3.up).normalized;
                Vector3 dir     = forward * move.y + right * move.x;
                if (dir.sqrMagnitude > 1f) dir.Normalize();

                _velocity.x = dir.x * _slopeSpeed;
                _velocity.z = dir.z * _slopeSpeed;

                // Speed bottomed out → start sliding
                if (_slopeSpeed <= 0.1f)
                {
                    _isSliding  = true;
                    _slideSpeed = 0.5f;
                }
            }

            // --- SLIDE phase: accelerate down the slope face like ice ---
            if (_isSliding)
            {
                _slideSpeed = Mathf.Min(SlideMaxSpeed, _slideSpeed + 3f * Time.fixedDeltaTime);

                Vector3 slideDir = Vector3.ProjectOnPlane(Vector3.down, _groundHit.normal).normalized;
                _velocity.x = slideDir.x * _slideSpeed;
                _velocity.z = slideDir.z * _slideSpeed;

                // Allow small left/right wiggle so player can steer off the slope
                float strafe = _input.MoveInput.x;
                if (Mathf.Abs(strafe) > 0.1f)
                {
                    Vector3 wiggleDir = Vector3.ProjectOnPlane(transform.right, _groundHit.normal).normalized;
                    _velocity.x += wiggleDir.x * strafe * config.slideWiggleSpeed;
                    _velocity.z += wiggleDir.z * strafe * config.slideWiggleSpeed;
                }
            }
        }
        else if (_inSlopeMode)
        {
            // --- LEFT the steep slope ---
            // On gentle ground (≤ slideStopAngle): exit immediately, carry momentum
            // Between slideStopAngle and maxSlopeAngle: keep decelerating
            if (_grounded && _groundAngle <= config.slideStopAngle)
            {
                // Exit slope mode — velocity stays as-is so momentum carries through
                _inSlopeMode = false;
                _isSliding   = false;
                _slideSpeed  = 0f;
                _slopeSpeed  = 0f;
            }
            else if (_grounded && _isSliding)
            {
                // Mid-range slope (30–45°): decelerate but keep sliding
                _slideSpeed = Mathf.Max(0f, _slideSpeed - 8f * Time.fixedDeltaTime);

                Vector3 slideDir = Vector3.ProjectOnPlane(Vector3.down, _groundHit.normal).normalized;
                _velocity.x = slideDir.x * _slideSpeed;
                _velocity.z = slideDir.z * _slideSpeed;
            }
            else if (!_grounded && !_steepGround)
            {
                // Went airborne (walked off edge) — exit, let gravity + momentum handle it
                _inSlopeMode = false;
                _isSliding   = false;
                _slideSpeed  = 0f;
                _slopeSpeed  = 0f;
            }
        }
    }

    // ─── Collision resolution ────────────────────────────────────────────────

    void ResolveCollisions(ref Vector3 position)
    {
        _hitCeiling  = false;
        int normalCount = 0;

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
                        _capsule, position,               transform.rotation,
                        other,    other.transform.position, other.transform.rotation,
                        out Vector3 dir, out float dist))
                    continue;

                // On walkable ground, ground-like pushes go straight up only
                // to prevent horizontal drift down slopes
                if (_grounded && dir.y > 0.5f)
                    position.y += dist + SkinWidth;
                else
                    position += dir * (dist + SkinWidth);

                pushed = true;

                if (dir.y < -0.1f)
                {
                    _hitCeiling = true;
                    if (_velocity.y > 0f)
                        _velocity.y = 0f;
                }

                if (normalCount < _pushNormals.Length)
                    _pushNormals[normalCount++] = dir;
            }

            if (!pushed) break;
        }

        for (int i = 0; i < normalCount; i++)
        {
            Vector3 pushDir = _pushNormals[i];

            if (_grounded)
            {
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

        Gizmos.color = Application.isPlaying && _grounded ? Color.green
                     : (_steepGround ? Color.yellow : Color.red);
        Gizmos.DrawLine(origin, origin + Vector3.down * length);

        if (!Application.isPlaying || (!_grounded && !_steepGround)) return;

        Gizmos.color = _steepGround ? Color.red : Color.green;
        Gizmos.DrawWireSphere(_groundHit.point, 0.05f);
        Gizmos.DrawRay(_groundHit.point, _groundHit.normal * 0.5f);

        if (_isSliding)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawRay(_groundHit.point,
                Vector3.ProjectOnPlane(Vector3.down, _groundHit.normal).normalized * 0.7f);
        }
    }
}
