using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(PlayerInput))]
public class PlayerMotor : MonoBehaviour
{
    [SerializeField] private PlayerMovementConfig config;

    [Header("References")]
    [SerializeField] private PlayerHealth _playerHealth;
    [SerializeField] private EncumbranceSystem _encumbrance;

    [Header("Stamina")]
    [SerializeField] private float _sprintDrainRate = 8f;
    [Tooltip("Energy fraction (0-1) above which exhaustion clears. Default 0.2 = 20% of maxEnergy.")]
    [SerializeField, Range(0.01f, 0.5f)] private float _exhaustionRecoveryFraction = 0.2f;

    [Header("Mouse Look")]
    [SerializeField] private float mouseSensitivity = 2f;

    private Rigidbody       _rb;
    private PlayerInput     _input;
    private CapsuleCollider _capsule;

    private float   _halfH;
    private float   _radius;
    private Vector3 _velocity;
    private float   _pendingYaw;

    private bool       _grounded;
    private RaycastHit _groundHit;
    private float      _groundAngle;
    private bool       _steepGround;
    private float      _coyoteTimer;
    private bool       _jumpedThisFrame;
    private bool       _hitCeiling;
    private bool       _inSlopeMode;
    private float      _steepGroundTimer;

    private bool  _isCrouching;
    private float _currentHeight;
    private float _crouchVisualOffset;
    private float _jumpBufferTimer;
    private float _visualBaseY;
    private float _stepLerpOffset;

    private bool  _isSliding;
    private float _slopeSpeed;
    private float _slideSpeed;

    private bool _staminaExhausted;

    private float   _stepCooldown;
    private Vector3 _preCollisionHoriz;

    private const float SteepGroundGrace  = 0.08f;
    private const float SkinWidth         = 0.01f;
    private const float VeryCloseDistance = 0.005f;
    private const int   MaxBounces        = 3;
    private const int   MaxDepenetration  = 3;
    private const float StepSmoothSpeed   = 12f;
    private const float StepCooldownTime  = 0.1f;
    private const float StepCheckDepth    = 0.2f;
    private const float SlideMaxSpeed     = 10f;
    private const float SlopeDrainMin     = 2f;
    private const float SlopeDrainMax     = 20f;
    private const float SlideAccel        = 3f;
    private const float SlideDecel        = 8f;
    private const float SlideEntrySpeed   = 0.5f;
    private const float SlideStopThr      = 0.1f;
    private const float StrafeDeadzone    = 0.1f;

    private readonly Collider[] _overlapBuffer = new Collider[16];
    private readonly Vector3[]  _pushNormals   = new Vector3[24];
    private int _normalCount;

    // ─── Public API ───────────────────────────────────────────────────────
    public bool    IsGrounded             => _grounded;
    public bool    IsCrouching            => _isCrouching;
    public bool    IsSprinting            => _input.SprintHeld && _input.MoveInput.y > 0f && !_isCrouching
                                             && !_staminaExhausted
                                             && (_encumbrance == null || !_encumbrance.IsOverloaded);
    public bool    IsMoving               => new Vector3(_velocity.x, 0f, _velocity.z).sqrMagnitude > 0.01f;
    public float   VerticalVelocity       => _velocity.y;
    public Vector3 HorizontalVelocity     => new Vector3(_velocity.x, 0f, _velocity.z);
    public bool    CanJump                => (_grounded || _coyoteTimer <= config.coyoteTime)
                                             && !_inSlopeMode && !_hitCeiling && !_steepGround && !_jumpedThisFrame
                                             && !_isCrouching;
    public float   CrouchProgress         => Mathf.InverseLerp(config.standHeight, config.crouchHeight, _currentHeight);
    public float   SpeedMultiplier        => StatModifiers.Net(StatType.Speed);
    public float   WeaponWeightMultiplier      { get; set; } = 1f;
    public float   EncumbranceWeightMultiplier { get; set; } = 1f;
    public StatModifierStack StatModifiers { get; } = new StatModifierStack();
    public event System.Action OnJumped;

    // ─── Geometry helpers ─────────────────────────────────────────────────
    Vector3 GeomCenter(Vector3 fp) => fp + Vector3.up * _halfH;
    Vector3 GeomBottom(Vector3 fp) => fp + Vector3.up * _radius;
    Vector3 GeomTop   (Vector3 fp) => fp + Vector3.up * (_halfH * 2f - _radius);

    // ─────────────────────────────────────────────────────────────────────
    void Awake()
    {
        _rb      = GetComponent<Rigidbody>();
        _input   = GetComponent<PlayerInput>();
        _capsule = GetComponent<CapsuleCollider>();

        if (_playerHealth == null)
            Debug.LogError("[PlayerMotor] PlayerHealth reference is missing — assign in Inspector.", this);

        _rb.freezeRotation = true;
        _rb.interpolation  = RigidbodyInterpolation.Interpolate;
        _rb.isKinematic    = true;

        _currentHeight = config.standHeight;
        ApplyCapsuleGeometry(config.standHeight);

        if (transform.childCount > 0)
            _visualBaseY = transform.GetChild(0).localPosition.y;
    }

    void ApplyCapsuleGeometry(float height)
    {
        _capsule.height = height;
        _capsule.center = new Vector3(0f, height * 0.5f, 0f);
        _halfH          = height * 0.5f;
        _radius         = _capsule.radius;
    }

    // ─────────────────────────────────────────────────────────────────────
    void Update()
    {
        if (!GameInputState.GameplayBlocked)
            _pendingYaw += Input.GetAxisRaw("Mouse X") * mouseSensitivity;

        if (_jumpBufferTimer > 0f)
            _jumpBufferTimer -= Time.deltaTime;

        if (_input.JumpPressed && _jumpBufferTimer <= 0f)
        {
            _jumpBufferTimer = config.jumpBufferTime;
            _input.ConsumeJump();
        }

        if (_stepCooldown > 0f)
            _stepCooldown -= Time.deltaTime;
    }

    void FixedUpdate()
    {
        _jumpedThisFrame = false;

        if (!Mathf.Approximately(_pendingYaw, 0f))
        {
            _rb.MoveRotation(_rb.rotation * Quaternion.Euler(0f, _pendingYaw, 0f));
            _pendingYaw = 0f;
        }

        CheckGround();
        HandleCrouch();
        HandleGravity();
        HandleJump();
        HandleSteepSlope();
        HandleMovement();
        HandleStaminaDrain();

        _hitCeiling = false;

        _preCollisionHoriz = new Vector3(_velocity.x, 0f, _velocity.z);

        Vector3 moveDelta = new Vector3(_velocity.x, 0f, _velocity.z) * Time.fixedDeltaTime;
        moveDelta = CollideAndSlide(_rb.position, moveDelta, false, false);

        Vector3 gravDelta = new Vector3(0f, _velocity.y, 0f) * Time.fixedDeltaTime;
        gravDelta = CollideAndSlide(_rb.position + moveDelta, gravDelta, true, true);

        Vector3 newFeetPos = _rb.position + moveDelta + gravDelta;

        if (Time.fixedDeltaTime > 0f)
        {
            _velocity.x = moveDelta.x / Time.fixedDeltaTime;
            _velocity.z = moveDelta.z / Time.fixedDeltaTime;
            _velocity.y = gravDelta.y / Time.fixedDeltaTime;
        }

        SnapToGround(ref newFeetPos);
        SafetyDepenetrate(ref newFeetPos);
        TryStepUp(ref newFeetPos);
        _rb.MovePosition(newFeetPos);
    }

    // ─── Ground check ─────────────────────────────────────────────────────
    void CheckGround()
    {
        Vector3 origin    = GeomCenter(_rb.position);
        float   probeDist = (_halfH - _radius) + config.groundCheckExtra;

        bool hit = Physics.SphereCast(
            origin, _radius, Vector3.down, out _groundHit,
            probeDist, config.groundMask, QueryTriggerInteraction.Ignore);

        _groundAngle = hit ? Vector3.Angle(_groundHit.normal, Vector3.up) : 0f;

        bool rawSteep = hit && _groundAngle >= config.maxSlopeAngle;
        if (rawSteep) _steepGroundTimer += Time.fixedDeltaTime;
        else          _steepGroundTimer  = 0f;

        _steepGround = _steepGroundTimer >= SteepGroundGrace;
        _grounded    = hit && !_steepGround;

        if (_grounded) _coyoteTimer = 0f;
        else           _coyoteTimer += Time.fixedDeltaTime;
    }

    // ─── Crouch ───────────────────────────────────────────────────────────
    void HandleCrouch()
    {
        bool wantCrouch = _input.CrouchHeld;

        if (_isCrouching && !wantCrouch)
        {
            float   clearance  = config.standHeight - _currentHeight;
            Vector3 castOrigin = GeomTop(_rb.position);
            if (Physics.SphereCast(castOrigin, _radius, Vector3.up, out _,
                    clearance, config.collisionMask, QueryTriggerInteraction.Ignore))
                wantCrouch = true;
        }

        _isCrouching   = wantCrouch;
        float targetH  = _isCrouching ? config.crouchHeight : config.standHeight;
        _currentHeight = Mathf.Lerp(_currentHeight, targetH, config.crouchLerpSpeed * Time.fixedDeltaTime);
        ApplyCapsuleGeometry(_currentHeight);

        _crouchVisualOffset = _currentHeight - config.standHeight;
    }

    // ─── Gravity ──────────────────────────────────────────────────────────
    void HandleGravity()
    {
        if (_grounded && _velocity.y <= 0f) _velocity.y = 0f;
        else _velocity.y -= config.gravity * Time.fixedDeltaTime;
    }

    // ─── Jump ─────────────────────────────────────────────────────────────
    void HandleJump()
    {
        if (_jumpBufferTimer <= 0f) return;
        bool canJump = (_grounded || _coyoteTimer <= config.coyoteTime)
                       && !_inSlopeMode && !_hitCeiling && !_steepGround && !_isCrouching;
        if (!canJump) return;
        _jumpBufferTimer = 0f;
        _velocity.y      = config.jumpForce * WeaponWeightMultiplier;
        _jumpedThisFrame = true;
        _coyoteTimer     = config.coyoteTime + 1f;
        OnJumped?.Invoke();
    }

    // ─── Stamina drain ────────────────────────────────────────────────────
    void HandleStaminaDrain()
    {
        if (_playerHealth == null) return;

        if (IsSprinting)
            _playerHealth.UseEnergy(_sprintDrainRate * Time.fixedDeltaTime);

        // Exhaustion: block sprint at 0, re-enable above recovery fraction (hysteresis)
        float recoveryEnergy = _playerHealth.maxEnergy * _exhaustionRecoveryFraction;
        if (!_staminaExhausted && _playerHealth.CurrentEnergy <= 0f)
        {
            _staminaExhausted = true;
            StatModifiers.Remove("exhaustion.speed");
            StatModifiers.Add(new PlayerStatModifier
                { Id = "exhaustion.speed", Stat = StatType.Speed, Value = 0.5f, IsMultiplier = true });
        }
        else if (_staminaExhausted && _playerHealth.CurrentEnergy >= recoveryEnergy)
        {
            _staminaExhausted = false;
            StatModifiers.Remove("exhaustion.speed");
        }
    }

    // ─── Movement ─────────────────────────────────────────────────────────
    void HandleMovement()
    {
        if (_inSlopeMode) return;
        Vector2 move   = _input.MoveInput;
        bool    sprint = IsSprinting;
        float speedNet = StatModifiers.Net(StatType.Speed);
        if (sprint) speedNet *= StatModifiers.Net(StatType.SprintSpeed);
        float   targetSpeed = (_isCrouching ? config.crouchSpeed
                              : sprint      ? config.sprintSpeed
                                            : config.walkSpeed) * speedNet * WeaponWeightMultiplier;

        Vector3 fwd   = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(transform.right,   Vector3.up).normalized;
        Vector3 wish  = fwd * move.y + right * move.x;
        if (wish.sqrMagnitude > 1f) wish.Normalize();

        Vector3 curH = new Vector3(_velocity.x, 0f, _velocity.z);
        float accel  = _grounded
            ? (wish.sqrMagnitude > 0.01f ? config.acceleration : config.deceleration)
            : config.airDeceleration;

        Vector3 newH = Vector3.MoveTowards(curH, wish * targetSpeed, accel * Time.fixedDeltaTime);
        _velocity.x  = newH.x;
        _velocity.z  = newH.z;
    }

    // ─── Steep slope ──────────────────────────────────────────────────────
    void HandleSteepSlope()
    {
        if (_steepGround)
        {
            if (!_inSlopeMode)
            {
                _inSlopeMode = true; _isSliding = false; _slideSpeed = 0f;
                _slopeSpeed = new Vector3(_velocity.x, 0f, _velocity.z).magnitude;
            }
            float steep = Mathf.InverseLerp(config.maxSlopeAngle, 90f, _groundAngle);
            if (!_isSliding)
            {
                _slopeSpeed = Mathf.Max(0f, _slopeSpeed - Mathf.Lerp(SlopeDrainMin, SlopeDrainMax, steep) * Time.fixedDeltaTime);
                Vector2 mv = _input.MoveInput;
                Vector3 d  = (Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized * mv.y
                             + Vector3.ProjectOnPlane(transform.right,   Vector3.up).normalized * mv.x).normalized;
                _velocity.x = d.x * _slopeSpeed; _velocity.z = d.z * _slopeSpeed;
                if (_slopeSpeed <= SlideStopThr) { _isSliding = true; _slideSpeed = SlideEntrySpeed; }
            }
            if (_isSliding)
            {
                _slideSpeed = Mathf.Min(SlideMaxSpeed, _slideSpeed + SlideAccel * Time.fixedDeltaTime);
                Vector3 sd = Vector3.ProjectOnPlane(Vector3.down, _groundHit.normal).normalized;
                _velocity.x = sd.x * _slideSpeed; _velocity.z = sd.z * _slideSpeed;
                float strafe = _input.MoveInput.x;
                if (Mathf.Abs(strafe) > StrafeDeadzone)
                {
                    Vector3 w = Vector3.ProjectOnPlane(transform.right, _groundHit.normal).normalized;
                    _velocity.x += w.x * strafe * config.slideWiggleSpeed;
                    _velocity.z += w.z * strafe * config.slideWiggleSpeed;
                }
            }
        }
        else if (_inSlopeMode)
        {
            if      (_grounded && _groundAngle <= config.slideStopAngle) ExitSlope();
            else if (_grounded && _isSliding)
            {
                _slideSpeed = Mathf.Max(0f, _slideSpeed - SlideDecel * Time.fixedDeltaTime);
                Vector3 sd = Vector3.ProjectOnPlane(Vector3.down, _groundHit.normal).normalized;
                _velocity.x = sd.x * _slideSpeed; _velocity.z = sd.z * _slideSpeed;
                if (_slideSpeed <= 0f) ExitSlope();
            }
            else ExitSlope();
        }
    }
    void ExitSlope() { _inSlopeMode = false; _isSliding = false; _slideSpeed = 0f; _slopeSpeed = 0f; }

    // ─── Collide and Slide ────────────────────────────────────────────────
    Vector3 CollideAndSlide(Vector3 feetPos, Vector3 vel, bool isGravPass, bool allowGroundSet,
                             int depth = 0, Vector3 prevSlidePlane = default)
    {
        if (depth >= MaxBounces || vel.magnitude < VeryCloseDistance)
            return vel.magnitude < VeryCloseDistance ? Vector3.zero : vel;

        float castDist = vel.magnitude + SkinWidth;
        bool hit = Physics.CapsuleCast(
            GeomBottom(feetPos), GeomTop(feetPos),
            _radius - SkinWidth,
            vel.normalized, out RaycastHit ch, castDist,
            config.collisionMask, QueryTriggerInteraction.Ignore);

        if (!hit) return vel;

        // Walkable slopes in the horizontal pass would trigger wallDot=0 (the slope's
        // XZ-projected normal faces directly opposite to uphill movement). Skip them here
        // and let SnapToGround handle the vertical lift instead.
        if (!isGravPass && _grounded && ch.normal.y >= Mathf.Cos(config.maxSlopeAngle * Mathf.Deg2Rad))
            return vel;

        float   snapDist   = Mathf.Max(ch.distance - SkinWidth, 0f);
        Vector3 snapVel    = vel.normalized * snapDist;
        Vector3 newFeetPos = feetPos + snapVel;
        Vector3 leftover   = vel - snapVel;

        Vector3 slideNormal = ch.normal;

        if (isGravPass)
        {
            if (ch.normal.y > Mathf.Cos(config.maxSlopeAngle * Mathf.Deg2Rad))
            {
                if (allowGroundSet) { _velocity.y = 0f; _grounded = true; }
                return snapVel;
            }
            if (ch.normal.y < -0.1f)
            {
                if (allowGroundSet) { _hitCeiling = true; _velocity.y = 0f; }
                return snapVel;
            }
        }

        Vector3 projected = Vector3.ProjectOnPlane(leftover, slideNormal).normalized * leftover.magnitude;

        if (!isGravPass)
        {
            if (depth == 0)
            {
                Vector3 vH = new Vector3(vel.x, 0f, vel.z), nH = new Vector3(slideNormal.x, 0f, slideNormal.z);
                float wallDot = 1f - Mathf.Abs(Vector3.Dot(
                    vH.sqrMagnitude > 0f ? vH.normalized : Vector3.forward,
                    nH.sqrMagnitude > 0f ? nH.normalized : Vector3.right));
                projected *= wallDot;
            }
            if (depth >= 1 && prevSlidePlane != default(Vector3))
            {
                Vector3 crease = Vector3.Cross(prevSlidePlane, slideNormal).normalized;
                if (crease.sqrMagnitude > 0.001f) projected = Vector3.Project(leftover, crease);
            }
        }

        return snapVel + CollideAndSlide(newFeetPos, projected, isGravPass, allowGroundSet, depth + 1, slideNormal);
    }

    // ─── Snap to ground ───────────────────────────────────────────────────
    void SnapToGround(ref Vector3 feetPos)
    {
        if (!_grounded || _velocity.y > 0f || _jumpedThisFrame || _inSlopeMode) return;

        Vector3 origin    = GeomCenter(feetPos);
        float   probeDist = (_halfH - _radius) + config.groundCheckExtra;

        if (Physics.SphereCast(origin, _radius, Vector3.down, out RaycastHit hit,
                probeDist, config.groundMask, QueryTriggerInteraction.Ignore))
        {
            float snapDelta = hit.point.y - feetPos.y;
            if (snapDelta > config.maxStepHeight) return;
            feetPos.y   = hit.point.y;
            _velocity.y = 0f;
        }
    }

    // ─── Step up ──────────────────────────────────────────────────────────
    void TryStepUp(ref Vector3 feetPos)
    {
        if (!_grounded || _jumpedThisFrame) return;
        if (_stepCooldown > 0f) return;

        Vector3 horiz = new Vector3(_velocity.x, 0f, _velocity.z);
        if (horiz.sqrMagnitude < 0.001f) return;

        Vector3 dir    = horiz.normalized;
        float   ankleY = feetPos.y + SkinWidth * 2f;
        float   topY   = feetPos.y + config.maxStepHeight;

        if (!Physics.Raycast(new Vector3(feetPos.x, ankleY, feetPos.z), dir,
                out RaycastHit lowHit, _radius + StepCheckDepth,
                config.collisionMask, QueryTriggerInteraction.Ignore)) return;
        if (lowHit.normal.y > 0.25f) return;

        if (Physics.Raycast(new Vector3(feetPos.x, topY, feetPos.z), dir,
                _radius + StepCheckDepth, config.collisionMask, QueryTriggerInteraction.Ignore)) return;

        Vector3 probeOrigin = new Vector3(
            lowHit.point.x + dir.x * SkinWidth,
            topY,
            lowHit.point.z + dir.z * SkinWidth);

        if (!Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit topHit,
                config.maxStepHeight + SkinWidth, config.collisionMask, QueryTriggerInteraction.Ignore)) return;
        if (Vector3.Angle(topHit.normal, Vector3.up) >= config.maxSlopeAngle) return;

        float stepH = topHit.point.y - feetPos.y;
        if (stepH < SkinWidth * 4f || stepH > config.maxStepHeight) return;
        if (topHit.normal.y < 0.9f) return;

        Vector3 steppedPos = new Vector3(feetPos.x, topHit.point.y, feetPos.z);
        if (Physics.CheckCapsule(
                GeomBottom(steppedPos), GeomTop(steppedPos), _radius - SkinWidth,
                config.collisionMask, QueryTriggerInteraction.Ignore)) return;

        feetPos.y     = topHit.point.y;
        _velocity.y   = 0f;
        _stepCooldown = StepCooldownTime;

        Transform vis = transform.childCount > 0 ? transform.GetChild(0) : null;
        float cur = vis != null ? vis.localPosition.y - _visualBaseY : 0f;
        _stepLerpOffset = Mathf.Clamp(cur - stepH, -config.maxStepHeight, 0f);
    }

    // ─── Safety depenetration ─────────────────────────────────────────────
    void SafetyDepenetrate(ref Vector3 feetPos)
    {
        _normalCount = 0;
        for (int iter = 0; iter < MaxDepenetration; iter++)
        {
            int count = Physics.OverlapCapsuleNonAlloc(
                GeomBottom(feetPos), GeomTop(feetPos), _radius,
                _overlapBuffer, config.collisionMask, QueryTriggerInteraction.Ignore);

            bool pushed = false;
            for (int i = 0; i < count; i++)
            {
                if (_overlapBuffer[i] == _capsule) continue;
                if (!Physics.ComputePenetration(
                        _capsule, feetPos, transform.rotation,
                        _overlapBuffer[i], _overlapBuffer[i].transform.position, _overlapBuffer[i].transform.rotation,
                        out Vector3 dir, out float dist)) continue;
                feetPos += dir * (dist + SkinWidth);
                if (_normalCount < _pushNormals.Length) _pushNormals[_normalCount++] = dir;
                if (dir.y < -0.1f) _hitCeiling = true;
                pushed = true;
            }
            if (!pushed) break;
        }

        if (_hitCeiling && _velocity.y > 0f) _velocity.y = 0f;

        for (int i = 0; i < _normalCount; i++)
        {
            Vector3 pd = _pushNormals[i];
            if (pd.y < -0.1f) continue;
            if (_grounded)
            {
                Vector3 flat = new Vector3(pd.x, 0f, pd.z);
                if (flat.sqrMagnitude > 0.001f)
                {
                    flat.Normalize();
                    float into = _velocity.x * flat.x + _velocity.z * flat.z;
                    if (into < 0f) { _velocity.x -= into * flat.x; _velocity.z -= into * flat.z; }
                }
            }
            else { float into = Vector3.Dot(_velocity, pd); if (into < 0f) _velocity -= into * pd; }
        }
    }

    // ─── LateUpdate ───────────────────────────────────────────────────────
    void LateUpdate()
    {
        if (transform.childCount == 0) return;
        Transform vis = transform.GetChild(0);

        float targetY = _visualBaseY + _crouchVisualOffset;

        if (!Mathf.Approximately(_stepLerpOffset, 0f))
            _stepLerpOffset = Mathf.MoveTowards(_stepLerpOffset, 0f, StepSmoothSpeed * Time.deltaTime);

        Vector3 p = vis.localPosition;
        p.y = targetY + _stepLerpOffset;
        vis.localPosition = p;
    }
}