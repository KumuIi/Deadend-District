using UnityEngine;

/// <summary>
/// GunSway — mouse sway, breathing, walk/sprint bob, airborne, and landing effects.
///
/// All player-level refs (gunPivot, playerMotor, playerCam, cameraController,
/// gunController) are injected by Weapon.Initialize() before this object is
/// enabled. Gun-specific tuning lives entirely in the Inspector.
/// </summary>
public class GunSway : MonoBehaviour
{
    [Header("=== Mouse Sway ===")]
    public float swayAmount   = 0.04f;
    public float swaySmooth   = 8f;
    public float swayMaxDelta = 0.06f;

    [Header("=== Mouse Tilt (Roll) ===")]
    public float tiltAmount        = 4f;
    public float tiltSmooth        = 8f;
    [Tooltip("Tilt multiplier while ADS (set lower to reduce roll when aiming)")]
    public float adsTiltMultiplier = 0.2f;

    [Header("=== Lean Gun Tilt ===")]
    [Tooltip("Extra Z-roll added to the gun on top of camera lean (cosmetic feel)")]
    public float leanGunTiltAmount = 5f;

    [Header("=== Idle Breathing ===")]
    public float breatheAmplitudeY = 0.0015f;
    public float breatheAmplitudeX = 0.0008f;
    public float breatheFrequency  = 0.8f;

    [Header("=== Walk Bob ===")]
    public float walkBobSpeedThreshold = 0.5f;
    public float walkBobFrequency      = 2.2f;
    public float walkBobAmplitudeY     = 0.006f;
    public float walkBobAmplitudeX     = 0.003f;

    [Header("=== Sprint Bob ===")]
    public float sprintBobFrequency  = 3.2f;
    public float sprintBobAmplitudeY = 0.012f;
    public float sprintBobAmplitudeX = 0.006f;
    public float sprintTiltZ         = 5f;
    public float sprintTiltSmooth    = 6f;

    [Header("=== Airborne ===")]
    public float airborneRiseAmount   = 0.04f;
    public float airborneRiseSmooth   = 0.15f;
    public float airborneReturnSmooth = 0.08f;

    [Header("=== Landing Slam ===")]
    public float landSlamAmount     = 0.025f;
    public float landRecoverSmooth  = 0.06f;
    public float landVelocityThresh = -3f;

    [Header("=== Footstep Nudge ===")]
    public float stepNudgeAmount = 0.003f;
    public float stepNudgeSmooth = 10f;

    [Header("=== General ===")]
    [Range(0f, 2f)]
    public float masterIntensity = 1f;
    public float returnSmooth    = 12f;

    // ── Injected refs (set by Weapon.Initialize before Start) ────────────
    private Transform        _gunPivot;
    private PlayerMotor      _playerMotor;
    private Transform        _playerCam;
    private CameraController _cameraController;
    private GunController    _gunController;

    // ── Private state ─────────────────────────────────────────────────────
    private Vector3    _currentPos;
    private Quaternion _currentRot;
    private Vector3    _posVelocity;
    private float      _rotXVel, _rotYVel, _rotZVel;

    private float _bobTimer;
    private int   _lastBobStep;
    private float _stepNudgeOffset;
    private float _stepNudgeVelocity;

    private float _airborneOffset;
    private float _airborneVelocity;
    private float _landOffset;
    private float _landVelocity;
    private bool  _wasGrounded;
    private float _lastVerticalVelocity;

    private float _sprintTiltCurrent;
    private float _sprintTiltVelocity;
    private float _mouseTiltCurrent;
    private float _mouseTiltVelocity;

    private Vector3    _restPos;
    private Quaternion _restRot;

    // ── Injection ─────────────────────────────────────────────────────────

    /// <summary>Called by Weapon.Initialize while the object is disabled.</summary>
    public void Initialize(
        Transform gunPivot, PlayerMotor motor,
        Transform playerCam, CameraController cc, GunController gc)
    {
        _gunPivot         = gunPivot;
        _playerMotor      = motor;
        _playerCam        = playerCam;
        _cameraController = cc;
        _gunController    = gc;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    void Start()
    {
        if (!_gunPivot)
        {
            Debug.LogError("GunSway: gunPivot not injected — call Initialize() before enabling.", this);
            enabled = false;
            return;
        }

        _restPos     = _gunPivot.localPosition;
        _restRot     = _gunPivot.localRotation;
        _currentPos  = _restPos;
        _currentRot  = _restRot;
        _wasGrounded = _playerMotor ? _playerMotor.IsGrounded : true;
    }

    void Update()
    {
        if (!_gunPivot || !_playerMotor) return;

        float dt         = Time.deltaTime;
        bool  grounded   = _playerMotor.IsGrounded;
        bool  sprinting  = _playerMotor.IsSprinting;
        float vertVel    = _playerMotor.VerticalVelocity;
        float horizSpeed = _playerMotor.HorizontalVelocity.magnitude;

        // hipWeight suppresses mouse-driven sway (precision matters when aiming).
        // bobWeight uses a softer curve so movement sway persists at half strength
        // when ADS — naturally zero when standing still since shouldBob is false.
        float adsWeight = _gunController ? _gunController.AdsWeight : 0f;
        float hipWeight = 1f - adsWeight;
        float bobWeight = Mathf.Lerp(1f, 0.5f, adsWeight);

        // ── Mouse sway + tilt ─────────────────────────────────────────────
        float mouseX = Input.GetAxisRaw("Mouse X");
        float mouseY = Input.GetAxisRaw("Mouse Y");

        Vector3 swayOffset = Vector3.ClampMagnitude(
            new Vector3(-mouseX * swayAmount, -mouseY * swayAmount, 0f),
            swayMaxDelta) * hipWeight;

        float tiltMult   = Mathf.Lerp(1f, adsTiltMultiplier, adsWeight);
        float tiltTarget = -mouseX * tiltAmount * tiltMult;
        _mouseTiltCurrent = Mathf.SmoothDamp(
            _mouseTiltCurrent, tiltTarget, ref _mouseTiltVelocity, 1f / tiltSmooth);

        // ── Breathing ─────────────────────────────────────────────────────
        float breathT = Time.time * breatheFrequency * Mathf.PI * 2f;
        bool  isIdle  = horizSpeed < walkBobSpeedThreshold && grounded;
        Vector3 breatheOffset = isIdle
            ? new Vector3(
                Mathf.Sin(breathT * 0.7f) * breatheAmplitudeX,
                Mathf.Sin(breathT)        * breatheAmplitudeY,
                0f) * hipWeight
            : Vector3.zero;

        // ── Walk / sprint bob ─────────────────────────────────────────────
        bool isWalking   = horizSpeed >= walkBobSpeedThreshold && grounded && !sprinting;
        bool isSprinting = sprinting && grounded;
        bool shouldBob   = isWalking || isSprinting;

        float bobFreq = isSprinting ? sprintBobFrequency : walkBobFrequency;
        float bobAmpY = isSprinting ? sprintBobAmplitudeY : walkBobAmplitudeY;
        float bobAmpX = isSprinting ? sprintBobAmplitudeX : walkBobAmplitudeX;

        if (shouldBob)
            _bobTimer += dt * bobFreq * Mathf.PI * 2f;
        else
            _bobTimer = Mathf.MoveTowards(_bobTimer,
                Mathf.Round(_bobTimer / Mathf.PI) * Mathf.PI, dt * 8f);

        int currentStep = Mathf.FloorToInt(_bobTimer / Mathf.PI);
        if (shouldBob && currentStep != _lastBobStep)
        {
            _lastBobStep     = currentStep;
            _stepNudgeOffset = (currentStep % 2 == 0 ? 1f : -1f) * stepNudgeAmount;
        }
        _stepNudgeOffset = Mathf.SmoothDamp(
            _stepNudgeOffset, 0f, ref _stepNudgeVelocity, 1f / stepNudgeSmooth);

        Vector3 bobOffset = shouldBob
            ? new Vector3(
                Mathf.Sin(_bobTimer)            * bobAmpX + _stepNudgeOffset,
                Mathf.Abs(Mathf.Sin(_bobTimer)) * bobAmpY,
                0f) * bobWeight
            : Vector3.zero;

        // ── Sprint tilt ───────────────────────────────────────────────────
        float sprintTiltTarget = isSprinting ? sprintTiltZ * hipWeight : 0f;
        _sprintTiltCurrent = Mathf.SmoothDamp(
            _sprintTiltCurrent, sprintTiltTarget, ref _sprintTiltVelocity, 1f / sprintTiltSmooth);

        // ── Lean gun tilt ─────────────────────────────────────────────────
        float leanTilt = _cameraController != null
            ? _cameraController.LeanWeight * leanGunTiltAmount
            : 0f;

        // ── Airborne rise ─────────────────────────────────────────────────
        float airTarget = grounded ? 0f : airborneRiseAmount;
        float airSmooth = grounded ? airborneReturnSmooth : airborneRiseSmooth;
        _airborneOffset = Mathf.SmoothDamp(_airborneOffset, airTarget, ref _airborneVelocity, airSmooth);

        // ── Landing slam ──────────────────────────────────────────────────
        if (grounded && !_wasGrounded && _lastVerticalVelocity < landVelocityThresh)
        {
            float impact = Mathf.Clamp01(_lastVerticalVelocity / landVelocityThresh);
            _landOffset   = -landSlamAmount * impact;
            _landVelocity = 0f;
        }
        _landOffset           = Mathf.SmoothDamp(_landOffset, 0f, ref _landVelocity, landRecoverSmooth);
        _wasGrounded          = grounded;
        _lastVerticalVelocity = vertVel;

        // ── Compose target ────────────────────────────────────────────────
        Vector3 targetPos = _restPos
            + (swayOffset + breatheOffset + bobOffset) * masterIntensity
            + new Vector3(0f, (_airborneOffset + _landOffset) * masterIntensity, 0f);

        float      totalZ     = (_mouseTiltCurrent + _sprintTiltCurrent) * masterIntensity + leanTilt;
        Quaternion targetRot  = _restRot * Quaternion.Euler(0f, 0f, totalZ);

        // ── Smooth to target ──────────────────────────────────────────────
        _currentPos = Vector3.SmoothDamp(_currentPos, targetPos, ref _posVelocity, 1f / returnSmooth);

        Vector3 ce = _currentRot.eulerAngles;
        Vector3 te = targetRot.eulerAngles;
        float ex = Mathf.SmoothDampAngle(ce.x, te.x, ref _rotXVel, 1f / returnSmooth);
        float ey = Mathf.SmoothDampAngle(ce.y, te.y, ref _rotYVel, 1f / returnSmooth);
        float ez = Mathf.SmoothDampAngle(ce.z, te.z, ref _rotZVel, 1f / returnSmooth);
        _currentRot = Quaternion.Euler(ex, ey, ez);

        // Write to pivot — GunController.Update will override with ADS on top
        _gunPivot.localPosition = _currentPos;
        _gunPivot.localRotation = _currentRot;
    }
}
