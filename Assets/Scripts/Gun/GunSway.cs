using UnityEngine;

/// <summary>
/// Applies mouse sway, breathing, walk/sprint bob, airborne, and landing effects
/// to the gun pivot. All tuning values come from WeaponSO.feel — no per-prefab
/// sway fields here any more.
///
/// Execution order pipeline:
///   WeaponWallPushback.LateUpdate  (order -100)  →  parent offset stable
///   GunSway.LateUpdate             (default 0)   →  writes sway base position
///   GunController.LateUpdate       (order 10000) →  adds ADS shift on top
///   Animation Rigging IK           (after all)   →  sees correct final position
/// </summary>
public class GunSway : MonoBehaviour
{
    // ── Injected refs ──────────────────────────────────────────────────────
    private Transform _gunPivot;
    private PlayerMotor _playerMotor;
    private Transform _playerCam;
    private CameraController _cameraController;
    private GunController _gunController;
    private WeaponFeelData _feel;

    // ── Private state ──────────────────────────────────────────────────────
    private Vector3 _currentPos;
    private Quaternion _currentRot;
    private Vector3 _posVelocity;
    private float _rotXVel, _rotYVel, _rotZVel;

    // Model kick (visual gun mesh recoil — separate from camera recoil)
    private Vector3 _modelKickRotTarget;
    private Vector3 _modelKickRotCurrent;
    private float _modelKickBackTarget;
    private float _modelKickBackCurrent;

    private float _bobTimer;
    private int _lastBobStep;
    private float _stepNudgeOffset;
    private float _stepNudgeVelocity;

    private float _airborneOffset;
    private float _airborneVelocity;
    private float _landOffset;
    private float _landVelocity;
    private bool _wasGrounded;
    private float _lastVerticalVelocity;

    private float _sprintTiltCurrent;
    private float _sprintTiltVelocity;
    private float _mouseTiltCurrent;
    private float _mouseTiltVelocity;

    private Vector3 _adsInertiaOffset;
    private Vector3 _adsInertiaVelocity;
    private Vector3 _prevLocalVel;

    private Vector3 _adsAimLag;

    private Vector3 _restPos;
    private Quaternion _restRot;

    // ── Model kick API ─────────────────────────────────────────────────────

    /// <summary>
    /// Called by GunController.FireShot() AFTER direction is sampled.
    /// Adds a visual rotation + backward impulse to the gun mesh without affecting ballistics.
    /// Ticked in LateUpdate so it's always captured in the same frame (GunController runs at order 10000).
    /// </summary>
    public void AddModelKick(WeaponRecoilData data, bool isAiming)
    {
        if (data == null) return;
        float mult = isAiming ? data.adsModelKickMultiplier : 1f;

        _modelKickRotTarget.x -= data.modelKickPitch * mult;
        _modelKickRotTarget.y += Random.Range(-data.modelKickYawRandom, data.modelKickYawRandom) * mult;
        _modelKickRotTarget.z += Random.Range(-data.modelKickRollRandom, data.modelKickRollRandom) * mult;
        _modelKickBackTarget  += data.modelKickBack * mult;

        _modelKickRotTarget.x = Mathf.Clamp(_modelKickRotTarget.x, -data.modelKickMaxPitch, 0f);
        _modelKickRotTarget.y = Mathf.Clamp(_modelKickRotTarget.y, -data.modelKickMaxYaw,   data.modelKickMaxYaw);
        _modelKickRotTarget.z = Mathf.Clamp(_modelKickRotTarget.z, -data.modelKickMaxRoll,  data.modelKickMaxRoll);
        _modelKickBackTarget  = Mathf.Min(_modelKickBackTarget, data.modelKickBackMax);
    }

    // ── Injection ──────────────────────────────────────────────────────────

    /// <summary>Called by GunController.Initialize() while the object is disabled.</summary>
    public void Initialize(
        Transform gunPivot, PlayerMotor motor,
        Transform playerCam, CameraController cc, GunController gc)
    {
        _gunPivot = gunPivot;
        _playerMotor = motor;
        _playerCam = playerCam;
        _cameraController = cc;
        _gunController = gc;
        _feel = gc.weaponData != null ? gc.weaponData.feel : new WeaponFeelData();
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Start()
    {
        if (!_gunPivot)
        {
            Debug.LogError("GunSway: gunPivot not injected — call Initialize() before enabling.", this);
            enabled = false;
            return;
        }

        _restPos = _gunPivot.localPosition;
        _restRot = _gunPivot.localRotation;
        _currentPos = _restPos;
        _currentRot = _restRot;
        _wasGrounded = _playerMotor ? _playerMotor.IsGrounded : true;
    }

    private void Update()
    {
        if (!_gunPivot || !_playerMotor || _feel == null) return;

        float dt = Time.deltaTime;
        bool grounded   = _playerMotor.IsGrounded;
        bool sprinting  = _playerMotor.IsSprinting;
        float vertVel   = _playerMotor.VerticalVelocity;
        float horizSpeed = _playerMotor.HorizontalVelocity.magnitude;

        float adsWeight = _gunController ? _gunController.AdsWeight : 0f;
        float hipWeight = 1f - adsWeight;
        float bobWeight = Mathf.Lerp(1f, 0.5f, adsWeight);

        // ── Mouse sway + tilt ───────────────────────────────────────────
        float mouseX = Input.GetAxisRaw("Mouse X");
        float mouseY = Input.GetAxisRaw("Mouse Y");

        Vector3 swayOffset = Vector3.ClampMagnitude(
            new Vector3(-mouseX * _feel.swayAmount, -mouseY * _feel.swayAmount, 0f),
            _feel.swayMaxDelta) * hipWeight;

        float tiltMult   = Mathf.Lerp(1f, _feel.adsTiltMultiplier, adsWeight);
        float tiltTarget = -mouseX * _feel.tiltAmount * tiltMult;
        _mouseTiltCurrent = Mathf.SmoothDamp(
            _mouseTiltCurrent, tiltTarget, ref _mouseTiltVelocity, 1f / _feel.tiltSmooth);

        // ── Breathing ───────────────────────────────────────────────────
        float breathT    = Time.time * _feel.breatheFrequency * Mathf.PI * 2f;
        bool  isIdle     = horizSpeed < _feel.walkBobSpeedThreshold && grounded;
        float breathMult = Mathf.Lerp(_feel.adsBreathScale, 1f, hipWeight);
        Vector3 breatheOffset = isIdle
            ? new Vector3(
                Mathf.Sin(breathT * 0.7f) * _feel.breatheAmplitudeX,
                Mathf.Sin(breathT)        * _feel.breatheAmplitudeY,
                0f) * breathMult
            : Vector3.zero;

        // ── Walk / sprint bob ───────────────────────────────────────────
        bool isWalking  = horizSpeed >= _feel.walkBobSpeedThreshold && grounded && !sprinting;
        bool isSprinting = sprinting && grounded;
        bool shouldBob  = isWalking || isSprinting;

        float weightBobMult = _playerMotor
            ? _playerMotor.WeaponWeightMultiplier * _playerMotor.EncumbranceWeightMultiplier
            : 1f;
        float bobFreq = (isSprinting ? _feel.sprintBobFrequency : _feel.walkBobFrequency) * weightBobMult;
        float bobAmpY = isSprinting ? _feel.sprintBobAmplitudeY : _feel.walkBobAmplitudeY;
        float bobAmpX = isSprinting ? _feel.sprintBobAmplitudeX : _feel.walkBobAmplitudeX;

        if (shouldBob)
            _bobTimer += dt * bobFreq * Mathf.PI * 2f;
        else
            _bobTimer = Mathf.MoveTowards(_bobTimer,
                Mathf.Round(_bobTimer / Mathf.PI) * Mathf.PI, dt * 8f);

        int currentStep = Mathf.FloorToInt(_bobTimer / Mathf.PI);
        if (shouldBob && currentStep != _lastBobStep)
        {
            _lastBobStep = currentStep;
            _stepNudgeOffset = (currentStep % 2 == 0 ? 1f : -1f) * _feel.stepNudgeAmount;
        }
        _stepNudgeOffset = Mathf.SmoothDamp(
            _stepNudgeOffset, 0f, ref _stepNudgeVelocity, 1f / _feel.stepNudgeSmooth);

        Vector3 bobOffset = shouldBob
            ? new Vector3(
                Mathf.Sin(_bobTimer) * bobAmpX + _stepNudgeOffset,
                Mathf.Abs(Mathf.Sin(_bobTimer)) * bobAmpY,
                0f) * bobWeight
            : Vector3.zero;

        // ── Sprint tilt ─────────────────────────────────────────────────
        float sprintTiltTarget = isSprinting ? _feel.sprintTiltZ * hipWeight : 0f;
        _sprintTiltCurrent = Mathf.SmoothDamp(
            _sprintTiltCurrent, sprintTiltTarget, ref _sprintTiltVelocity, 1f / _feel.sprintTiltSmooth);

        // ── Lean gun tilt ───────────────────────────────────────────────
        float leanTilt = _cameraController != null
            ? _cameraController.LeanWeight * _feel.leanGunTiltAmount
            : 0f;

        // ── Airborne rise ───────────────────────────────────────────────
        float airTarget = grounded ? 0f : _feel.airborneRiseAmount;
        float airSmooth = grounded ? _feel.airborneReturnSmooth : _feel.airborneRiseSmooth;
        _airborneOffset = Mathf.SmoothDamp(_airborneOffset, airTarget, ref _airborneVelocity, airSmooth);

        // ── Landing slam ────────────────────────────────────────────────
        if (grounded && !_wasGrounded && _lastVerticalVelocity < _feel.landVelocityThresh)
        {
            float impact = Mathf.Clamp01(_lastVerticalVelocity / _feel.landVelocityThresh);
            _landOffset   = -_feel.landSlamAmount * impact;
            _landVelocity = 0f;
        }
        _landOffset = Mathf.SmoothDamp(_landOffset, 0f, ref _landVelocity, _feel.landRecoverSmooth);
        _wasGrounded          = grounded;
        _lastVerticalVelocity = vertVel;

        // ── ADS walking jolt — acceleration-based: steady speed = no offset ─
        Vector3 localVel = _playerCam
            ? _playerCam.InverseTransformDirection(_playerMotor.HorizontalVelocity)
            : Vector3.zero;
        Vector3 accel = dt > 0.0001f ? (localVel - _prevLocalVel) / dt : Vector3.zero;
        _prevLocalVel = localVel;
        Vector3 inertiaTarget = Vector3.ClampMagnitude(
            new Vector3(-accel.x, -accel.z * 0.4f, 0f) * _feel.adsInertiaAmount,
            _feel.adsInertiaMaxDelta) * adsWeight;
        _adsInertiaOffset = Vector3.SmoothDamp(
            _adsInertiaOffset, inertiaTarget, ref _adsInertiaVelocity, _feel.adsInertiaSmooth);

        // ── ADS aim lag — gun holds previous world orientation, springs to camera ─
        // Positive mouseY = camera looks up → gun should stay down → positive local X lag.
        // Positive mouseX = camera looks right → gun should stay left → negative local Y lag.
        _adsAimLag.x += mouseY * _feel.adsAimLagAmount * adsWeight;
        _adsAimLag.y -= mouseX * _feel.adsAimLagAmount * adsWeight;
        _adsAimLag = Vector3.ClampMagnitude(_adsAimLag, _feel.adsAimLagMax);
        float catchup = _feel.adsAimLagCatchup * (adsWeight < 0.5f ? 2f : 1f);
        _adsAimLag = Vector3.Lerp(_adsAimLag, Vector3.zero, catchup * dt);

        // ── Compose target ──────────────────────────────────────────────
        Vector3 targetPos = _restPos
            + (swayOffset + breatheOffset + bobOffset) * _feel.masterIntensity
            + new Vector3(0f, (_airborneOffset + _landOffset) * _feel.masterIntensity, 0f)
            + _adsInertiaOffset;

        float totalZ = (_mouseTiltCurrent + _sprintTiltCurrent) * _feel.masterIntensity + leanTilt;
        Quaternion restOffsetRot = Quaternion.Euler(Vector3.Lerp(
            _feel.hipRestRotationOffset, _feel.adsRestRotationOffset, adsWeight));
        Quaternion lagRot  = Quaternion.Euler(_adsAimLag.x * adsWeight, _adsAimLag.y * adsWeight, 0f);
        Quaternion rollRot = Quaternion.Euler(0f, 0f, totalZ);
        Quaternion targetRot = _restRot * restOffsetRot * lagRot * rollRot;

        // ── Smooth to target ────────────────────────────────────────────
        _currentPos = Vector3.SmoothDamp(_currentPos, targetPos, ref _posVelocity, 1f / _feel.returnSmooth);

        Vector3 ce = _currentRot.eulerAngles;
        Vector3 te = targetRot.eulerAngles;
        float ex = Mathf.SmoothDampAngle(ce.x, te.x, ref _rotXVel, 1f / _feel.returnSmooth);
        float ey = Mathf.SmoothDampAngle(ce.y, te.y, ref _rotYVel, 1f / _feel.returnSmooth);
        float ez = Mathf.SmoothDampAngle(ce.z, te.z, ref _rotZVel, 1f / _feel.returnSmooth);
        _currentRot = Quaternion.Euler(ex, ey, ez);
    }

    private void LateUpdate()
    {
        if (!_gunPivot) return;

        // Model kick ticked here so it captures AddModelKick calls from GunController.Update (order 10000).
        TickModelKick();

        _gunPivot.localPosition = _currentPos + new Vector3(0f, 0f, -_modelKickBackCurrent);
        _gunPivot.localRotation = _currentRot * Quaternion.Euler(_modelKickRotCurrent);
    }

    private void TickModelKick()
    {
        var data = _gunController?.weaponData?.recoil;
        if (data == null) return;

        float dt = Time.deltaTime;

        // Target decays toward zero (return speed — same Lerp-rate convention as RecoilController).
        _modelKickRotTarget  = Vector3.Lerp(_modelKickRotTarget,  Vector3.zero, data.modelKickReturnSpeed * dt);
        _modelKickBackTarget = Mathf.Lerp(_modelKickBackTarget, 0f,            data.modelKickReturnSpeed * dt);

        // Current chases target (follow speed — snappy kick snap).
        _modelKickRotCurrent  = Vector3.Lerp(_modelKickRotCurrent,  _modelKickRotTarget,  data.modelKickFollowSpeed * dt);
        _modelKickBackCurrent = Mathf.Lerp(_modelKickBackCurrent, _modelKickBackTarget, data.modelKickFollowSpeed * dt);
    }
}
