using UnityEngine;

/// <summary>
/// PlayerMotor — kinematic Rigidbody character controller.
///
/// FixedUpdate pipeline each physics tick:
///   1. CheckGround        – SphereCast ground probe
///   2. HandleGravity      – gravity accumulation on _velocity.y
///   3. HandleJump         – buffered jump with coyote-time guard
///   4. HandleSteepSlope   – drain → slide state machine
///   5. HandleMovement     – normal XZ movement
///   6. Integrate          – candidate target position
///   7. ResolveCollisions  – depenetration loop (max 3 iterations)
///   8. TryStepUp          – lift over stair risers when grounded + moving
///   9. SnapToGround       – flush with walkable surfaces
///  10. MovePosition       – commit to physics sim
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(PlayerInput))]
public class PlayerMotor : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────
    [SerializeField] private PlayerMovementConfig config;

    // ─── Component refs ───────────────────────────────────────────────────
    private Rigidbody       _rb;
    private PlayerInput     _input;
    private CapsuleCollider _capsule;

    // Derived from CapsuleCollider at runtime — always in sync with Inspector
    private float _capsuleHalfHeight;
    private float _capsuleRadius;

    // ─── Velocity state ───────────────────────────────────────────────────
    private Vector3 _velocity;

    // ─── Ground state ─────────────────────────────────────────────────────
    private bool       _grounded;
    private bool       _steepGround;
    private float      _groundAngle;
    private RaycastHit _groundHit;

    // ─── Ceiling state ────────────────────────────────────────────────────
    private bool _hitCeiling;

    // ─── Jump timing ──────────────────────────────────────────────────────
    private float _coyoteTimer;
    private float _jumpBufferTimer;
    private bool  _jumpedThisFrame;

    // ─── Slope-mode state ─────────────────────────────────────────────────
    private bool  _inSlopeMode;
    private bool  _isSliding;
    private float _slopeSpeed;
    private float _slideSpeed;

    // ─── Step-up state ────────────────────────────────────────────────────
    /// <summary>Current vertical offset being smoothly lerped toward 0 as the visual
    /// body catches up with the physics position after a step.</summary>
    private float _stepLerpOffset;
    private float _visualBaseY;

    // ─── Rotation caching ─────────────────────────────────────────────────
    private float _pendingYaw;

    // ─── Buffers ──────────────────────────────────────────────────────────
    private readonly Collider[] _overlapBuffer = new Collider[8];
    private readonly Vector3[]  _pushNormals   = new Vector3[24];
    private int                 _normalCount;

    // ─── Constants ────────────────────────────────────────────────────────
    
    
    private const float GroundCheckDist      = 0.12f;
    private const float SkinWidth            = 0.01f;
    private const float SlideMaxSpeed        = 10f;
    private const int   MaxDepenetrationIter = 3;

    // Step-up tuning
    private const float MaxStepHeight       = 0.4f;   // tallest riser the player climbs
    private const float StepCheckDepth      = 0.2f;   // how far forward to probe for a riser
    private const float StepSmoothSpeed     = 12f;    // visual lerp speed (higher = snappier)

    // Slope-drain tuning
    private const float SlopeDrainMin       = 2f;
    private const float SlopeDrainMax       = 20f;
    private const float SlideAcceleration   = 3f;
    private const float SlideDeceleration   = 8f;
    private const float SlideEntrySpeed     = 0.5f;
    private const float SlideStopThreshold  = 0.1f;
    private const float StrafeDeadzone      = 0.1f;

    // ─────────────────────────────────────────────────────────────────────
    
    public bool  IsGrounded   => _grounded;
    public bool  IsSprinting  => _input.SprintHeld && _input.MoveInput.y > 0f;
    public bool  IsMoving     => new Vector3(_velocity.x, 0f, _velocity.z).sqrMagnitude > 0.01f;
    public float VerticalVelocity => _velocity.y;
    public Vector3 HorizontalVelocity => new Vector3(_velocity.x, 0f, _velocity.z);
// ─────────────────────────────────────────────────────────────────────────────

    void Awake()
    {
        _rb      = GetComponent<Rigidbody>();
        _input   = GetComponent<PlayerInput>();
        _capsule = GetComponent<CapsuleCollider>();
        _rb.freezeRotation = true;

        // Read collider dimensions so all probe math matches the actual shape.
        _capsuleRadius     = _capsule.radius;
        _capsuleHalfHeight = _capsule.height / 2f;

        if (transform.childCount > 0)
            _visualBaseY = transform.GetChild(0).localPosition.y;
    }

    void Update()
    {
        if (!GameInputState.GameplayBlocked)
            _pendingYaw += Input.GetAxisRaw("Mouse X") * config.mouseSensitivity;

        if (_jumpBufferTimer > 0f)
            _jumpBufferTimer -= Time.deltaTime;

        if (_input.JumpPressed && _jumpBufferTimer <= 0f)
            _jumpBufferTimer = config.jumpBufferTime;
    }

    void FixedUpdate()
    {
        _jumpedThisFrame = false;

        // Apply yaw before collision so depenetration uses correct orientation.
        if (!Mathf.Approximately(_pendingYaw, 0f))
        {
            _rb.MoveRotation(_rb.rotation * Quaternion.Euler(0f, _pendingYaw, 0f));
            _pendingYaw = 0f;
        }

        CheckGround();
        HandleGravity();
        HandleJump();
        HandleSteepSlope();
        HandleMovement();

        Vector3 target = _rb.position + _velocity * Time.fixedDeltaTime;

        TryStepUp(ref target);
        ResolveCollisions(ref target);
        SnapToGround(ref target);

        _rb.MovePosition(target);
    }

    // ─── Ground check ────────────────────────────────────────────────────

    void CheckGround()
    {
        // Start at pivot (capsule centre) — sphere is fully inside capsule,
        // cannot intersect floor geometry before the cast begins.
        Vector3 origin = _rb.position;
        float   dist   = _capsuleHalfHeight + GroundCheckDist;

        bool hit = Physics.SphereCast(
            origin, _capsuleRadius, Vector3.down, out _groundHit,
            dist, config.groundMask, QueryTriggerInteraction.Ignore);

        _groundAngle = hit ? Vector3.Angle(_groundHit.normal, Vector3.up) : 0f;
        _steepGround = hit && _groundAngle >= config.maxSlopeAngle;
        _grounded    = hit && !_steepGround;

        if (_grounded) _coyoteTimer = 0f;
        else           _coyoteTimer += Time.fixedDeltaTime;
    }

    void SnapToGround(ref Vector3 target)
    {
        if (!_grounded || _velocity.y > 0f || _jumpedThisFrame) return;

        Vector3 origin = target;
        float   dist   = _capsuleHalfHeight + GroundCheckDist;

        if (Physics.SphereCast(origin, _capsuleRadius, Vector3.down, out RaycastHit hit,
            dist, config.groundMask, QueryTriggerInteraction.Ignore))
        {
            target.y    = hit.point.y + _capsuleHalfHeight;
            _velocity.y = 0f;
        }
    }

    // ─── Step-up ─────────────────────────────────────────────────────────

    /// <summary>
    /// Detects stair risers in the movement direction and lifts the player over
    /// them rather than treating them as walls.
    ///
    /// Algorithm (three-raycast staircase probe):
    ///   1. LOW  ray  — fires horizontally at ankle height in the move direction.
    ///                  A hit here means there is a riser ahead.
    ///   2. HIGH ray  — fires horizontally at MaxStepHeight above the ankle.
    ///                  No hit means the top of the riser is below MaxStepHeight,
    ///                  i.e. it is a step, not a wall.
    ///   3. DOWN ray  — fires from above the step landing point straight down to
    ///                  find the actual surface height to land on.
    ///   If all three conditions pass the player's target.y is raised instantly
    ///   (physics position) while a _stepLerpOffset visual counter smooths the
    ///   visual pop over StepSmoothSpeed frames.
    ///
    /// Guards:
    ///   • Only runs when grounded (no mid-air stair climbing).
    ///   • Only runs when there is actual horizontal movement.
    ///   • Ignores triggers and non-collision-mask objects.
    ///   • Skips if jump was initiated this frame.
    /// </summary>
    void TryStepUp(ref Vector3 target)
    {
        // Only step while grounded, moving horizontally, and not jumping.
        if (!_grounded || _jumpedThisFrame) return;

        Vector3 horizontal = new Vector3(_velocity.x, 0f, _velocity.z);
        if (horizontal.sqrMagnitude < 0.001f) return;

        Vector3 moveDir = horizontal.normalized;

        // Ankle position — just above the foot, low enough to catch small steps
        // but above SkinWidth so it doesn't fire on flush floor seams.
        float   ankleY      = _rb.position.y - _capsuleHalfHeight + SkinWidth * 2f;
        Vector3 ankleOrigin = new Vector3(_rb.position.x, ankleY, _rb.position.z);

        // 1. LOW ray — is there a riser in front of us?
        if (!Physics.Raycast(ankleOrigin, moveDir, out RaycastHit lowHit,
            _capsuleRadius + StepCheckDepth, config.collisionMask, QueryTriggerInteraction.Ignore))
            return; // nothing blocking at ankle height, no step needed

        if (lowHit.normal.y > 0.25f) return;

        // 2. HIGH ray — is the top of the riser below MaxStepHeight?
        float   stepTopY   = _rb.position.y - _capsuleHalfHeight + MaxStepHeight;
        Vector3 highOrigin = new Vector3(_rb.position.x, stepTopY, _rb.position.z);

        if (Physics.Raycast(highOrigin, moveDir, _capsuleRadius + StepCheckDepth,
            config.collisionMask, QueryTriggerInteraction.Ignore))
            return; // obstacle continues above MaxStepHeight — it's a wall, not a step

        // 3. DOWN ray — find the exact surface height on top of the step.
        //    Probe from slightly past the riser face so we land on its top face.
        Vector3 probeOrigin = new Vector3(
            _rb.position.x + moveDir.x * (_capsuleRadius + StepCheckDepth),
            stepTopY,
            _rb.position.z + moveDir.z * (_capsuleRadius + StepCheckDepth));

        if (!Physics.Raycast(probeOrigin, Vector3.down, out RaycastHit topHit,
            MaxStepHeight + SkinWidth, config.collisionMask, QueryTriggerInteraction.Ignore))
            return; // no surface found on top of the step (e.g. hollow geometry)

        if (Vector3.Angle(topHit.normal, Vector3.up) >= config.maxSlopeAngle) return;

        float stepHeight = topHit.point.y - (_rb.position.y - _capsuleHalfHeight);

        if (stepHeight < SkinWidth || stepHeight > MaxStepHeight) return;

        target.y = topHit.point.y + _capsuleHalfHeight;
        _velocity.y = 0f;

        // Start the visual offset from wherever the child currently sits so
        // mid-lerp steps do not stack debt. Clamp to MaxStepHeight as a hard backstop.
        Transform stepVisual = transform.childCount > 0 ? transform.GetChild(0) : null;
        float currentVisualOffset = stepVisual != null ? stepVisual.localPosition.y - _visualBaseY : 0f;
        _stepLerpOffset = Mathf.Clamp(currentVisualOffset - stepHeight, -MaxStepHeight, 0f);
    }

    // ─── Visual step smoothing ────────────────────────────────────────────

    /// <summary>
    /// Smooths the visual body position after a step-up so the camera doesn't
    /// snap. The physics Rigidbody moves instantly (required for correct
    /// collision), but the rendered transform is offset downward by _stepLerpOffset
    /// and that offset is lerped back to zero each LateUpdate.
    ///
    /// NOTE: This requires the visual mesh / camera to be on a child object.
    /// If your camera is directly on this GameObject, remove this method and
    /// accept the instant step (still works, just less smooth).
    /// </summary>
    void LateUpdate()
    {
        if (transform.childCount == 0) return;
        Transform visual = transform.GetChild(0);

        // No offset pending — hard-reset Y to 0 to eliminate any float residue.
        if (Mathf.Approximately(_stepLerpOffset, 0f))
        {
            Vector3 lp = visual.localPosition;
            if (!Mathf.Approximately(lp.y, _visualBaseY))
            {
                lp.y = _visualBaseY;
                visual.localPosition = lp;
            }
            return;
        }

        // Lerp offset back to 0. TryStepUp always writes _stepLerpOffset based
        // on the child's CURRENT localPosition.y so rapid stair climbing never
        // stacks debt — each step restarts from the current visual position.
        _stepLerpOffset = Mathf.MoveTowards(
            _stepLerpOffset, 0f, StepSmoothSpeed * Time.deltaTime);

        Vector3 pos = visual.localPosition;
        pos.y = _visualBaseY + _stepLerpOffset;
        visual.localPosition = pos;
    }

    // ─── Gravity ─────────────────────────────────────────────────────────

    void HandleGravity()
    {
        if (_grounded && _velocity.y <= 0f)
            _velocity.y = 0f;
        else
            _velocity.y -= config.gravity * Time.fixedDeltaTime;
    }

    // ─── Jump ────────────────────────────────────────────────────────────

    void HandleJump()
    {
        if (_jumpBufferTimer <= 0f) return;

        bool withinCoyote = _coyoteTimer <= config.coyoteTime;
        bool canJump      = (_grounded || withinCoyote)
                          && !_inSlopeMode
                          && !_hitCeiling
                          && !_steepGround;

        // Always consume both tokens regardless of jump success.
        _jumpBufferTimer = 0f;
        _input.ConsumeJump();

        if (canJump)
        {
            _velocity.y      = config.jumpForce;
            _jumpedThisFrame = true;
            _coyoteTimer     = config.coyoteTime + 1f;
        }
    }

    // ─── Normal movement ─────────────────────────────────────────────────

    void HandleMovement()
    {
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

    // ─── Steep slope state machine ────────────────────────────────────────

    void HandleSteepSlope()
    {
        if (_steepGround)
        {
            if (!_inSlopeMode)
            {
                _inSlopeMode = true;
                _isSliding   = false;
                _slideSpeed  = 0f;
                float vx = _velocity.x, vz = _velocity.z;
                _slopeSpeed = Mathf.Sqrt(vx * vx + vz * vz);
            }

            float steepness = Mathf.InverseLerp(config.maxSlopeAngle, 90f, _groundAngle);

            if (!_isSliding)
            {
                float drain = Mathf.Lerp(SlopeDrainMin, SlopeDrainMax, steepness)
                            * Time.fixedDeltaTime;
                _slopeSpeed = Mathf.Max(0f, _slopeSpeed - drain);

                Vector2 move    = _input.MoveInput;
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                Vector3 right   = Vector3.ProjectOnPlane(transform.right,   Vector3.up).normalized;
                Vector3 dir     = forward * move.y + right * move.x;
                if (dir.sqrMagnitude > 1f) dir.Normalize();

                _velocity.x = dir.x * _slopeSpeed;
                _velocity.z = dir.z * _slopeSpeed;

                if (_slopeSpeed <= SlideStopThreshold)
                {
                    _isSliding  = true;
                    _slideSpeed = SlideEntrySpeed;
                }
            }

            if (_isSliding)
            {
                _slideSpeed = Mathf.Min(SlideMaxSpeed,
                    _slideSpeed + SlideAcceleration * Time.fixedDeltaTime);

                Vector3 slideDir = Vector3.ProjectOnPlane(Vector3.down, _groundHit.normal).normalized;
                _velocity.x = slideDir.x * _slideSpeed;
                _velocity.z = slideDir.z * _slideSpeed;

                float strafe = _input.MoveInput.x;
                if (Mathf.Abs(strafe) > StrafeDeadzone)
                {
                    Vector3 wiggleDir = Vector3.ProjectOnPlane(
                        transform.right, _groundHit.normal).normalized;
                    _velocity.x += wiggleDir.x * strafe * config.slideWiggleSpeed;
                    _velocity.z += wiggleDir.z * strafe * config.slideWiggleSpeed;
                }
            }
        }
        else if (_inSlopeMode)
        {
            if (_grounded && _groundAngle <= config.slideStopAngle)
                ExitSlopeMode();
            else if (_grounded && _isSliding)
            {
                _slideSpeed = Mathf.Max(0f, _slideSpeed - SlideDeceleration * Time.fixedDeltaTime);
                Vector3 slideDir = Vector3.ProjectOnPlane(Vector3.down, _groundHit.normal).normalized;
                _velocity.x = slideDir.x * _slideSpeed;
                _velocity.z = slideDir.z * _slideSpeed;
                if (_slideSpeed <= 0f) ExitSlopeMode();
            }
            else if (!_grounded && !_steepGround)
                ExitSlopeMode();
            else
                ExitSlopeMode(); // failsafe
        }
    }

    private void ExitSlopeMode()
    {
        _inSlopeMode = false;
        _isSliding   = false;
        _slideSpeed  = 0f;
        _slopeSpeed  = 0f;
    }

    // ─── Collision resolution ─────────────────────────────────────────────

    void ResolveCollisions(ref Vector3 position)
    {
        _hitCeiling  = false;
        _normalCount = 0;

        for (int iter = 0; iter < MaxDepenetrationIter; iter++)
        {
            Vector3 bottom = position + Vector3.down * (_capsuleHalfHeight - _capsuleRadius);
            Vector3 top    = position + Vector3.up   * (_capsuleHalfHeight - _capsuleRadius);

            int count = Physics.OverlapCapsuleNonAlloc(
                bottom, top, _capsuleRadius,
                _overlapBuffer, config.collisionMask, QueryTriggerInteraction.Ignore);

            bool pushed = false;

            for (int i = 0; i < count; i++)
            {
                Collider other = _overlapBuffer[i];
                if (other == _capsule) continue;

                if (!Physics.ComputePenetration(
                    _capsule, position, transform.rotation,
                    other,   other.transform.position, other.transform.rotation,
                    out Vector3 dir, out float dist))
                    continue;

                if (_grounded && dir.y > 0.5f)
                    position.y += dist + SkinWidth;
                else
                    position   += dir * (dist + SkinWidth);

                pushed = true;

                if (_normalCount < _pushNormals.Length)
                    _pushNormals[_normalCount++] = dir;

                if (dir.y < -0.1f)
                    _hitCeiling = true;
            }

            if (!pushed) break;
        }

        // Velocity cancel pass — all normals, single ceiling clamp.
        if (_hitCeiling && _velocity.y > 0f)
            _velocity.y = 0f;

        for (int i = 0; i < _normalCount; i++)
        {
            Vector3 pushDir = _pushNormals[i];
            if (pushDir.y < -0.1f) continue;

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

    // ─── Debug gizmos ─────────────────────────────────────────────────────

    void OnDrawGizmosSelected()
    {
        Vector3 origin = Application.isPlaying ? _rb.position : transform.position;

        float probeOriginY = origin.y;
        float probeDist    = _capsuleHalfHeight + GroundCheckDist;

        Gizmos.color = Application.isPlaying
            ? (_grounded ? Color.green : _steepGround ? Color.yellow : Color.red)
            : Color.cyan;

        Gizmos.DrawWireSphere(new Vector3(origin.x, probeOriginY, origin.z),            _capsuleRadius);
        Gizmos.DrawWireSphere(new Vector3(origin.x, probeOriginY - probeDist, origin.z), _capsuleRadius);
        Gizmos.DrawLine(
            new Vector3(origin.x, probeOriginY, origin.z),
            new Vector3(origin.x, probeOriginY - probeDist, origin.z));

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

        // Step-up probe visualisation (cyan = ankle ray, white = high ray)
        if (_grounded)
        {
            Vector3 horizontal = new Vector3(_velocity.x, 0f, _velocity.z);
            if (horizontal.sqrMagnitude > 0.001f)
            {
                Vector3 moveDir  = horizontal.normalized;
                float   ankleY   = origin.y - _capsuleHalfHeight + SkinWidth * 2f;
                float   stepTopY = origin.y - _capsuleHalfHeight + MaxStepHeight;

                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(new Vector3(origin.x, ankleY,   origin.z), moveDir * (_capsuleRadius + StepCheckDepth));

                Gizmos.color = Color.white;
                Gizmos.DrawRay(new Vector3(origin.x, stepTopY, origin.z), moveDir * (_capsuleRadius + StepCheckDepth));
            }
        }

        if (_jumpBufferTimer > 0f)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(origin + Vector3.up * (_capsuleHalfHeight + 0.2f), 0.08f);
        }
    }
}



