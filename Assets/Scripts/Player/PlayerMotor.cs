using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class PlayerMotor : MonoBehaviour
{
    [SerializeField] private PlayerMovementConfig config;
    [SerializeField] private PlayerInput input;

    // Public state for camera / animation / UI
    public Vector3 Velocity => _velocity;
    public bool IsGrounded { get; private set; }
    public bool IsCrouching => _isCrouching;
    public bool IsSprinting => _isSprinting;
    public float CurrentHeight => _currentHeight;

    private Rigidbody _rb;
    private CapsuleCollider _capsule;

    private Vector3 _velocity;
    private bool _isCrouching;
    private bool _isSprinting;
    private float _currentHeight;

    // Ground state (computed each FixedUpdate)
    private Vector3 _groundNormal;
    private float _groundAngle;
    private bool _isOnSteepSlope;

    // Jump state
    private float _coyoteTimer;
    private float _jumpBufferTimer;
    private bool _jumpConsumed = true;

    // Capsule cast cache (set once per FixedUpdate, reused by all queries)
    private Vector3 _cp1, _cp2;
    private float _castRadius;

    private const float Skin = 0.015f;

    // Debug
    private Vector3 _dbgGroundPt;

    // ───────────── Editor ─────────────

    void Reset() => ConfigureCapsule();
    void OnValidate() => ConfigureCapsule();

    private void ConfigureCapsule()
    {
        var c = GetComponent<CapsuleCollider>();
        if (c == null) return;
        float h = config ? config.standHeight : 1.8f;
        float r = config ? config.capsuleRadius : 0.3f;
        c.height = h;
        c.center = Vector3.up * (h * 0.5f);
        c.radius = r;
    }

    // ───────────── Init ─────────────

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _capsule = GetComponent<CapsuleCollider>();

        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        _rb.freezeRotation = true;

        _currentHeight = config.standHeight;
        SetHeight(_currentHeight);
    }

    // ───────────── Physics tick ─────────────

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 pos = _rb.position;

        // Disable own collider for ALL physics queries this tick.
        // Re-enabled once at the end, before MovePosition.
        _capsule.enabled = false;

        RefreshCapsuleCache(pos);

        //  1 ── ground
        DetectGround(pos);

        //  2 ── jump timers
        if (IsGrounded) { _coyoteTimer = config.coyoteTime; _jumpConsumed = false; }
        else            { _coyoteTimer -= dt; }

        if (input.JumpPressed) _jumpBufferTimer = config.jumpBufferTime;
        else                   _jumpBufferTimer -= dt;

        //  3 ── crouch
        HandleCrouch(pos);

        //  4 ── target speed
        _isSprinting = input.SprintHeld && !_isCrouching;
        float tSpeed = _isCrouching ? config.crouchSpeed
                     : (_isSprinting && input.MoveInput.y > 0) ? config.sprintSpeed
                     : config.walkSpeed;

        //  5 ── wish direction
        Vector3 wish = WishDir();

        //  6 ── horizontal velocity
        HorizontalMove(wish, tSpeed, dt);

        //  7 ── jump
        if (_coyoteTimer > 0f && !_jumpConsumed && !_isOnSteepSlope && _jumpBufferTimer > 0f)
        {
            _velocity.y = config.jumpForce;
            _jumpConsumed = true;
            _coyoteTimer = 0f;
            _jumpBufferTimer = 0f;
        }

        //  8 ── gravity / vertical reset
        if (!IsGrounded)
        {
            float m = _velocity.y < 0f ? config.fallMultiplier : 1f;
            _velocity.y -= config.gravity * m * dt;
            _velocity.y = Mathf.Max(_velocity.y, -config.maxFallSpeed);
        }
        else if (!_jumpConsumed)
        {
            _velocity.y = 0f;
        }

        //  9 ── steep slope slide
        if (_isOnSteepSlope)
        {
            Vector3 slide = Vector3.ProjectOnPlane(Vector3.down, _groundNormal).normalized;
            _velocity.x = slide.x * config.slopeSlideSpeed;
            _velocity.z = slide.z * config.slopeSlideSpeed;
        }

        // 10 ── displacement (project onto slope surface, not velocity)
        Vector3 disp = _velocity * dt;
        if (IsGrounded && !_jumpConsumed && _groundAngle > 1f)
        {
            Vector3 hDisp = new Vector3(disp.x, 0f, disp.z);
            float hMag = hDisp.magnitude;
            if (hMag > 0.001f)
                disp = Vector3.ProjectOnPlane(hDisp.normalized, _groundNormal).normalized * hMag;
        }

        // 11 ── step-up or collide & slide
        RefreshCapsuleCache(pos);
        Vector3 finalDisp;
        if (IsGrounded && !_jumpConsumed && TryStepUp(pos, disp, out Vector3 stepDisp))
            finalDisp = stepDisp;
        else
            finalDisp = CollideAndSlide(pos, disp);
        Vector3 newPos = pos + finalDisp;

        // 12 ── ground snap (not after jump)
        if (IsGrounded && !_jumpConsumed)
        {
            if (SnapCast(newPos, out float snapY))
                newPos.y = snapY;
        }

        // ── re-enable collider & apply ──
        _capsule.enabled = true;
        _rb.MovePosition(newPos);

        // 13 ── consume one-shot inputs
        input.ConsumeJump();
        input.ConsumeCrouch();
    }

    // ───────────── Ground detection ─────────────

    private void DetectGround(Vector3 feet)
    {
        Vector3 o = feet + Vector3.up * config.capsuleRadius;
        float r = config.capsuleRadius - 0.02f;

        if (Physics.SphereCast(o, r, Vector3.down, out RaycastHit h,
            config.groundCheckDistance + 0.02f, config.groundMask, QueryTriggerInteraction.Ignore))
        {
            _groundNormal = h.normal;
            _groundAngle = Vector3.Angle(_groundNormal, Vector3.up);
            IsGrounded = _groundAngle <= config.maxWalkableAngle;
            _isOnSteepSlope = !IsGrounded;
            _dbgGroundPt = h.point;
        }
        else
        {
            IsGrounded = false;
            _isOnSteepSlope = false;
            _groundNormal = Vector3.up;
            _groundAngle = 0f;
            _dbgGroundPt = feet;
        }
    }

    private bool SnapCast(Vector3 feet, out float snapY)
    {
        Vector3 o = feet + Vector3.up * config.capsuleRadius;
        float r = config.capsuleRadius - 0.02f;

        if (Physics.SphereCast(o, r, Vector3.down, out RaycastHit h,
            config.groundSnapDistance, config.groundMask, QueryTriggerInteraction.Ignore))
        {
            if (Vector3.Angle(h.normal, Vector3.up) <= config.maxWalkableAngle)
            { snapY = h.point.y; return true; }
        }
        snapY = feet.y;
        return false;
    }

    // ───────────── Movement helpers ─────────────

    private Vector3 WishDir()
    {
        Vector2 m = input.MoveInput;
        if (m.sqrMagnitude <= 0.001f) return Vector3.zero;
        Vector3 d = transform.right * m.x + transform.forward * m.y;
        d.y = 0f;
        return d.normalized;
    }

    private void HorizontalMove(Vector3 wish, float target, float dt)
    {
        Vector3 h = new Vector3(_velocity.x, 0f, _velocity.z);
        bool hasInput = wish.sqrMagnitude > 0.001f;

        if (IsGrounded && !_isOnSteepSlope)
            h = hasInput ? Clamp(Accel(h, wish, target, config.acceleration, dt), target)
                         : Decel(h, config.deceleration, dt);
        else if (hasInput)
            h = Clamp(Accel(h, wish, target, config.airAcceleration, dt), target);

        _velocity.x = h.x;
        _velocity.z = h.z;
    }

    private static Vector3 Accel(Vector3 v, Vector3 dir, float ws, float a, float dt)
    {
        float add = ws - Vector3.Dot(v, dir);
        if (add <= 0f) return v;
        return v + dir * Mathf.Min(a * dt * ws, add);
    }

    private static Vector3 Decel(Vector3 v, float d, float dt)
    {
        float s = v.magnitude;
        if (s < 0.1f) return Vector3.zero;
        return v * (Mathf.Max(s - d * dt, 0f) / s);
    }

    private static Vector3 Clamp(Vector3 v, float max)
    {
        return v.sqrMagnitude > max * max ? v.normalized * max : v;
    }

    // ───────────── Capsule ─────────────

    private void SetHeight(float h)
    {
        _capsule.height = h;
        _capsule.center = Vector3.up * (h * 0.5f);
    }

    private void RefreshCapsuleCache(Vector3 feet)
    {
        float half = _currentHeight * 0.5f;
        float off = half - config.capsuleRadius;
        Vector3 c = feet + Vector3.up * half;
        _cp1 = c + Vector3.up * off;
        _cp2 = c - Vector3.up * off;
        _castRadius = config.capsuleRadius - Skin;
    }

    private void HandleCrouch(Vector3 pos)
    {
        if (!input.CrouchPressed) return;

        if (_isCrouching)
        {
            Vector3 bot = pos + Vector3.up * config.capsuleRadius;
            Vector3 top = pos + Vector3.up * (config.standHeight - config.capsuleRadius);
            if (!Physics.CheckCapsule(bot, top, config.capsuleRadius - 0.02f,
                config.groundMask, QueryTriggerInteraction.Ignore))
            {
                _isCrouching = false;
                _currentHeight = config.standHeight;
                SetHeight(_currentHeight);
                RefreshCapsuleCache(pos);
            }
        }
        else
        {
            _isCrouching = true;
            _currentHeight = config.crouchHeight;
            SetHeight(_currentHeight);
            RefreshCapsuleCache(pos);
        }
    }

    // ───────────── Step climbing ─────────────

    private bool TryStepUp(Vector3 pos, Vector3 disp, out Vector3 result)
    {
        result = Vector3.zero;

        // Only step when there's horizontal movement
        Vector3 hDisp = new Vector3(disp.x, 0f, disp.z);
        float hDist = hDisp.magnitude;
        if (hDist < 0.001f) return false;

        Vector3 hDir = hDisp / hDist;

        // Check if we actually hit a wall ahead — if not, no step needed
        RefreshCapsuleCache(pos);
        if (!Physics.CapsuleCast(_cp1, _cp2, _castRadius, hDir, out _,
            hDist + Skin, config.groundMask, QueryTriggerInteraction.Ignore))
            return false;

        // 1. Cast UP to find headroom
        float upDist = config.maxStepHeight;
        RefreshCapsuleCache(pos);
        if (Physics.CapsuleCast(_cp1, _cp2, _castRadius, Vector3.up, out RaycastHit upHit,
            upDist + Skin, config.groundMask, QueryTriggerInteraction.Ignore))
            upDist = Mathf.Max(upHit.distance - Skin, 0f);

        if (upDist < 0.01f) return false;

        Vector3 raised = pos + Vector3.up * upDist;

        // 2. Cast FORWARD from the raised position
        RefreshCapsuleCache(raised);
        float fwdDist = hDist;
        if (Physics.CapsuleCast(_cp1, _cp2, _castRadius, hDir, out RaycastHit fwdHit,
            fwdDist + Skin, config.groundMask, QueryTriggerInteraction.Ignore))
            fwdDist = Mathf.Max(fwdHit.distance - Skin, 0f);

        if (fwdDist < Skin) return false;

        Vector3 forward = raised + hDir * fwdDist;

        // 3. Cast DOWN to find ground
        float downDist = upDist + config.groundSnapDistance;
        RefreshCapsuleCache(forward);
        if (!Physics.CapsuleCast(_cp1, _cp2, _castRadius, Vector3.down, out RaycastHit downHit,
            downDist, config.groundMask, QueryTriggerInteraction.Ignore))
            return false;

        if (Vector3.Angle(downHit.normal, Vector3.up) > config.maxWalkableAngle)
            return false;

        float feetY = downHit.point.y;

        // Only step up, never step below starting position
        if (feetY < pos.y - Skin) return false;

        result = new Vector3(hDir.x * fwdDist, feetY - pos.y, hDir.z * fwdDist);
        return true;
    }

    // ───────────── Collision ─────────────

    private Vector3 CollideAndSlide(Vector3 pos, Vector3 disp, int depth = 0)
    {
        if (depth >= 3) return Vector3.zero;
        float dist = disp.magnitude;
        if (dist < 0.001f) return Vector3.zero;

        Vector3 dir = disp / dist;
        RefreshCapsuleCache(pos);

        if (!Physics.CapsuleCast(_cp1, _cp2, _castRadius, dir, out RaycastHit hit,
            dist + Skin, config.groundMask, QueryTriggerInteraction.Ignore))
            return disp;

        float safe = Mathf.Max(hit.distance - Skin, 0f);
        Vector3 safeMove = dir * safe;
        Vector3 leftover = Vector3.ProjectOnPlane(disp - dir * hit.distance, hit.normal);
        return safeMove + CollideAndSlide(pos + safeMove, leftover, depth + 1);
    }

    // ───────────── Debug gizmos ─────────────

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = IsGrounded ? Color.green : (_isOnSteepSlope ? Color.yellow : Color.red);
        Gizmos.DrawWireSphere(_dbgGroundPt, 0.05f);
        if (_rb != null)
            Gizmos.DrawLine(_rb.position, _rb.position + Vector3.down * 0.15f);
    }
}
