using UnityEngine;

/// <summary>
/// GunSway — attach to the same GameObject as GunController (your gun root).
///
/// ─── SETUP ──────────────────────────────────────────────────────────────────
///  1. Add this script to your gun root (same object as GunController).
///  2. In Inspector → assign "Gun Pivot": create an empty child GameObject
///     directly under the gun root, name it "GunPivot", parent your actual
///     gun mesh/rig under it. All sway runs on GunPivot's local position/rotation.
///  3. Assign "Player Motor" → drag your player's PlayerMotor component.
///  4. Assign "Player Cam" → drag your FPS camera Transform.
///  5. Tweak values live in Play mode until it feels right.
///
/// ─── WHAT IT DOES ───────────────────────────────────────────────────────────
///  • Idle breathing   — gentle sine-wave bob when standing still
///  • Walk bob         — vertical + lateral bob scaled to walk speed
///  • Sprint bob       — faster, wider bob + gun tilts inward
///  • Mouse sway       — gun lags behind look direction (feels weighted)
///  • Mouse tilt       — gun rolls slightly on horizontal mouse movement
///  • Airborne rise    — gun floats up when jumping/falling
///  • Landing slam     — sudden downward punch on landing, then springs back
///  • Step sway        — tiny lateral nudge on each footstep
/// ────────────────────────────────────────────────────────────────────────────
public class GunSway : MonoBehaviour
{
    [Header("=== References ===")]
    [Tooltip("Empty child GameObject that is the parent of your gun mesh. All motion is applied here.")]
    public Transform gunPivot;
    public PlayerMotor playerMotor;
    public Transform playerCam;

    // ─── Mouse sway ──────────────────────────────────────────────────────────
    [Header("=== Mouse Sway ===")]
    [Tooltip("How strongly the gun lags behind mouse movement")]
    public float swayAmount   = 0.04f;
    [Tooltip("How quickly the gun returns to centre after sway")]
    public float swaySmooth   = 8f;
    [Tooltip("Max offset distance in each axis from mouse sway")]
    public float swayMaxDelta = 0.06f;

    [Header("=== Mouse Tilt (Roll) ===")]
    [Tooltip("Degrees of roll per unit of horizontal mouse input")]
    public float tiltAmount = 4f;
    public float tiltSmooth = 8f;

    // ─── Breathing ───────────────────────────────────────────────────────────
    [Header("=== Idle Breathing ===")]
    public float breatheAmplitudeY   = 0.0015f;
    public float breatheAmplitudeX   = 0.0008f;
    public float breatheFrequency    = 0.8f;   // cycles per second

    // ─── Walk bob ────────────────────────────────────────────────────────────
    [Header("=== Walk Bob ===")]
    public float walkBobSpeedThreshold = 0.5f;  // min speed to start bobbing
    public float walkBobFrequency      = 2.2f;  // steps per second
    public float walkBobAmplitudeY     = 0.006f;
    public float walkBobAmplitudeX     = 0.003f;

    // ─── Sprint bob ──────────────────────────────────────────────────────────
    [Header("=== Sprint Bob ===")]
    public float sprintBobFrequency  = 3.2f;
    public float sprintBobAmplitudeY = 0.012f;
    public float sprintBobAmplitudeX = 0.006f;
    [Tooltip("Degrees the gun tilts inward (Z roll) while sprinting")]
    public float sprintTiltZ         = 5f;
    public float sprintTiltSmooth    = 6f;

    // ─── Airborne ────────────────────────────────────────────────────────────
    [Header("=== Airborne ===")]
    [Tooltip("How high the gun floats up when airborne (metres)")]
    public float airborneRiseAmount = 0.04f;
    [Tooltip("Smooth time for rising when airborne")]
    public float airborneRiseSmooth = 0.15f;
    [Tooltip("Smooth time for returning when grounded")]
    public float airborneReturnSmooth = 0.08f;

    // ─── Landing slam ────────────────────────────────────────────────────────
    [Header("=== Landing Slam ===")]
    [Tooltip("How far down the gun punches on landing (metres)")]
    public float landSlamAmount     = 0.025f;
    [Tooltip("How fast the gun recovers upward after landing (smooth time)")]
    public float landRecoverSmooth  = 0.06f;
    [Tooltip("Minimum downward velocity to trigger a land slam")]
    public float landVelocityThresh = -3f;

    // ─── Step nudge ──────────────────────────────────────────────────────────
    [Header("=== Footstep Nudge ===")]
    [Tooltip("Lateral nudge each step (metres), alternates left/right")]
    public float stepNudgeAmount = 0.003f;
    public float stepNudgeSmooth = 10f;

    // ─── General smoothing ───────────────────────────────────────────────────
    [Header("=== General ===")]
    [Tooltip("Master multiplier — turn down if everything feels too much")]
    [Range(0f, 2f)]
    public float masterIntensity = 1f;
    public float returnSmooth    = 12f;

    // ── private state ─────────────────────────────────────────────────────────
    private Vector3    _targetPos;
    private Quaternion _targetRot;

    private Vector3    _currentPos;
    private Quaternion _currentRot;

    private Vector3 _posVelocity;
    private float   _rotXVel, _rotYVel, _rotZVel;

    // Bob
    private float _bobTimer;
    private int   _lastBobStep;   // tracks footstep crossings
    private float _stepNudgeOffset;
    private float _stepNudgeVelocity;

    // Airborne / landing
    private float _airborneOffset;
    private float _airborneVelocity;
    private float _landOffset;
    private float _landVelocity;
    private bool  _wasGrounded;
    private float _lastVerticalVelocity;

    // Sprint tilt
    private float _sprintTiltCurrent;
    private float _sprintTiltVelocity;

    // Mouse tilt
    private float _mouseTiltCurrent;
    private float _mouseTiltVelocity;

    // Cached rest pose
    private Vector3    _restPos;
    private Quaternion _restRot;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    void Start()
    {
        if (!gunPivot)
        {
            Debug.LogError("GunSway: gunPivot not assigned!", this);
            enabled = false;
            return;
        }

        _restPos      = gunPivot.localPosition;
        _restRot      = gunPivot.localRotation;
        _currentPos   = _restPos;
        _currentRot   = _restRot;
        _wasGrounded  = playerMotor ? playerMotor.IsGrounded : true;
    }

    void Update()
    {
        if (!gunPivot || !playerMotor) return;

        float dt = Time.deltaTime;

        bool   grounded  = playerMotor.IsGrounded;
        bool   sprinting = playerMotor.IsSprinting;
        float  vertVel   = playerMotor.VerticalVelocity;
        float  horizSpeed = playerMotor.HorizontalVelocity.magnitude;

        // ── 1. Mouse sway + tilt ──────────────────────────────────────────────
        float mouseX = Input.GetAxisRaw("Mouse X");
        float mouseY = Input.GetAxisRaw("Mouse Y");

        Vector3 swayOffset = new Vector3(
            -mouseX * swayAmount,
            -mouseY * swayAmount,
            0f);
        swayOffset = Vector3.ClampMagnitude(swayOffset, swayMaxDelta);

        // Mouse tilt (roll on Z)
        float tiltTarget = -mouseX * tiltAmount;
        _mouseTiltCurrent = Mathf.SmoothDamp(
            _mouseTiltCurrent, tiltTarget, ref _mouseTiltVelocity, 1f / tiltSmooth);

        // ── 2. Breathing (idle only) ──────────────────────────────────────────
        float breathT    = Time.time * breatheFrequency * Mathf.PI * 2f;
        bool  isIdle     = horizSpeed < walkBobSpeedThreshold && grounded;

        Vector3 breatheOffset = isIdle ? new Vector3(
            Mathf.Sin(breathT * 0.7f) * breatheAmplitudeX,
            Mathf.Sin(breathT)        * breatheAmplitudeY,
            0f) : Vector3.zero;

        // ── 3. Walk / sprint bob ──────────────────────────────────────────────
        bool   isWalking = horizSpeed >= walkBobSpeedThreshold && grounded && !sprinting;
        bool   isSprinting = sprinting && grounded;

        float  bobFreq  = isSprinting ? sprintBobFrequency : walkBobFrequency;
        float  bobAmpY  = isSprinting ? sprintBobAmplitudeY : walkBobAmplitudeY;
        float  bobAmpX  = isSprinting ? sprintBobAmplitudeX : walkBobAmplitudeX;
        bool   shouldBob = (isWalking || isSprinting);

        if (shouldBob)
            _bobTimer += dt * bobFreq * Mathf.PI * 2f;
        else
            _bobTimer = Mathf.MoveTowards(_bobTimer,
                Mathf.Round(_bobTimer / (Mathf.PI)) * Mathf.PI,
                dt * 8f); // ease back to zero crossing

        // Detect footstep (every half-cycle of Y bob)
        int currentStep = Mathf.FloorToInt(_bobTimer / Mathf.PI);
        if (shouldBob && currentStep != _lastBobStep)
        {
            _lastBobStep = currentStep;
            _stepNudgeOffset = (currentStep % 2 == 0 ? 1f : -1f) * stepNudgeAmount;
        }
        _stepNudgeOffset = Mathf.SmoothDamp(
            _stepNudgeOffset, 0f, ref _stepNudgeVelocity, 1f / stepNudgeSmooth);

        Vector3 bobOffset = shouldBob ? new Vector3(
            Mathf.Sin(_bobTimer)        * bobAmpX + _stepNudgeOffset,
            Mathf.Abs(Mathf.Sin(_bobTimer)) * bobAmpY,   // always goes down, peaks at step
            0f) : Vector3.zero;

        // ── 4. Sprint tilt ────────────────────────────────────────────────────
        float sprintTiltTarget = isSprinting ? sprintTiltZ : 0f;
        _sprintTiltCurrent = Mathf.SmoothDamp(
            _sprintTiltCurrent, sprintTiltTarget,
            ref _sprintTiltVelocity, 1f / sprintTiltSmooth);

        // ── 5. Airborne rise ──────────────────────────────────────────────────
        float airTarget = grounded ? 0f : airborneRiseAmount;
        float airSmooth = grounded ? airborneReturnSmooth : airborneRiseSmooth;
        _airborneOffset = Mathf.SmoothDamp(
            _airborneOffset, airTarget, ref _airborneVelocity, airSmooth);

        // ── 6. Landing slam ───────────────────────────────────────────────────
        bool justLanded = grounded && !_wasGrounded;
        if (justLanded && _lastVerticalVelocity < landVelocityThresh)
        {
            // Velocity is proportional to how hard we hit
            float impactRatio = Mathf.Clamp01(_lastVerticalVelocity / landVelocityThresh);
            _landOffset   = -landSlamAmount * impactRatio;
            _landVelocity = 0f;
        }
        _landOffset = Mathf.SmoothDamp(_landOffset, 0f, ref _landVelocity, landRecoverSmooth);

        _wasGrounded         = grounded;
        _lastVerticalVelocity = vertVel;

        // ── 7. Compose final target ───────────────────────────────────────────
        _targetPos = _restPos
                   + swayOffset       * masterIntensity
                   + breatheOffset    * masterIntensity
                   + bobOffset        * masterIntensity
                   + new Vector3(0f, _airborneOffset + _landOffset, 0f) * masterIntensity;

        float totalRollZ = (_mouseTiltCurrent + _sprintTiltCurrent) * masterIntensity;
        _targetRot = _restRot * Quaternion.Euler(0f, 0f, totalRollZ);

        // ── 8. Smooth to target ───────────────────────────────────────────────
        _currentPos = Vector3.SmoothDamp(
            _currentPos, _targetPos, ref _posVelocity, 1f / returnSmooth);

        // Smooth each euler axis independently to avoid gimbal weirdness
        Vector3 currentE = _currentRot.eulerAngles;
        Vector3 targetE  = _targetRot.eulerAngles;

        float ex = Mathf.SmoothDampAngle(currentE.x, targetE.x, ref _rotXVel, 1f / returnSmooth);
        float ey = Mathf.SmoothDampAngle(currentE.y, targetE.y, ref _rotYVel, 1f / returnSmooth);
        float ez = Mathf.SmoothDampAngle(currentE.z, targetE.z, ref _rotZVel, 1f / returnSmooth);
        _currentRot = Quaternion.Euler(ex, ey, ez);

        gunPivot.localPosition = _currentPos;
        gunPivot.localRotation = _currentRot;
    }
}
