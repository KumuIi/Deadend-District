using UnityEngine;

/// <summary>
/// Spring-physics sway for the hand-held flashlight.
/// Attach to the flashlight root GO. Reads mouse delta and player velocity,
/// applies an underdamped spring rotation so the light lags and overshoots —
/// giving the "slowly aiming in darkness" feel.
///
/// Scene setup:
///   1. Attach to the pre-placed flashlight GO.
///   2. Assign FlashlightSlot, PlayerMotor, and CameraTransform in the Inspector.
/// </summary>
public class FlashlightSway : MonoBehaviour
{
    [SerializeField] private FlashlightSlot _flashlightSlot;
    [SerializeField] private PlayerMotor    _playerMotor;
    [Tooltip("Stable orientation reference for world→local velocity conversion (assign player camera).")]
    [SerializeField] private Transform      _cameraTransform;

    [Header("=== Rotation Spring ===")]
    [Tooltip("Spring pull strength — lower = laggier catch-up.")]
    [SerializeField] private float _stiffness      = 8f;
    [Tooltip("Damping — lower = more overshoot and oscillation.")]
    [SerializeField] private float _damping        = 4f;
    [Tooltip("Max rotation offset in degrees.")]
    [SerializeField] private float _maxAngle       = 12f;
    [Tooltip("How much mouse look contributes to sway.")]
    [SerializeField] private float _mouseInfluence = 2f;
    [Tooltip("How much horizontal velocity contributes to sway.")]
    [SerializeField] private float _velocityInfluence = 1.5f;

    [Header("=== Axis Orientation (fix a flipped model) ===")]
    [Tooltip("Swap pitch/yaw onto the model's local axes. Enable if the flashlight is authored " +
             "rolled ~90° so vertical look drives horizontal sway and vice-versa.")]
    [SerializeField] private bool _swapAxes   = true;
    [Tooltip("Flip the up/down sway direction if it feels inverted.")]
    [SerializeField] private bool _invertPitch = false;
    [Tooltip("Flip the left/right sway direction if it feels inverted.")]
    [SerializeField] private bool _invertYaw   = false;

    [Header("=== Walk Bob ===")]
    [SerializeField] private float _bobAmplitude  = 0.004f;
    [SerializeField] private float _bobFrequency  = 1.8f;

    // ── Cached base pose ───────────────────────────────────────────────────
    private Vector3    _baseLocalPos;
    private Quaternion _baseLocalRot;

    // ── Spring state ───────────────────────────────────────────────────────
    private Vector3 _currentRot;
    private Vector3 _rotVelocity;

    // ── Position lag state ─────────────────────────────────────────────────
    private Vector3 _currentPosOffset;
    private Vector3 _posVelocity;

    // ── Bob state ──────────────────────────────────────────────────────────
    private float _bobTimer;

    /// <summary>Written each frame by FlashlightSlot from ReloadDip.FlashlightPositionOffset.</summary>
    public Vector3 DipPositionOffset { get; set; }
    /// <summary>Written each frame by FlashlightSlot from ReloadDip.FlashlightRotationOffset.</summary>
    public Vector3 DipRotationOffset { get; set; }

    private void Start()
    {
        _baseLocalPos = transform.localPosition;
        _baseLocalRot = transform.localRotation;
    }

    private void LateUpdate()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.033f);

        bool equipped = _flashlightSlot != null && _flashlightSlot.EquippedFlashlight != null;

        if (!equipped)
        {
            // Smoothly return to base pose; also drain velocities so re-equip doesn't kick
            _currentRot       = Vector3.Lerp(_currentRot,       Vector3.zero, dt * 6f);
            _currentPosOffset = Vector3.Lerp(_currentPosOffset, Vector3.zero, dt * 6f);
            _rotVelocity      = Vector3.Lerp(_rotVelocity,      Vector3.zero, dt * 6f);
            _posVelocity      = Vector3.Lerp(_posVelocity,      Vector3.zero, dt * 6f);
            ApplyPose(Vector3.zero);
            return;
        }

        // ── Rotation target from mouse + velocity ──────────────────────────
        // Think in PITCH (vertical look) and YAW (horizontal look); Compose() then lays them onto
        // the model's local Euler axes (swapped/inverted as configured) so a rolled model isn't flipped.
        // Zero the look delta while a menu/inventory is open so the beam settles to rest
        // instead of swaying with the now-free cursor. Velocity terms self-zero (movement is blocked).
        bool inputBlocked = GameInputState.GameplayBlocked;
        float mouseX = inputBlocked ? 0f : Input.GetAxisRaw("Mouse X");
        float mouseY = inputBlocked ? 0f : Input.GetAxisRaw("Mouse Y");

        float pitch = -mouseY * _mouseInfluence;
        float yaw   =  mouseX * _mouseInfluence;

        // Velocity contribution — strafing swings the beam sideways (yaw). Camera-local space
        // avoids feeding the sway back into itself.
        if (_playerMotor != null && _cameraTransform != null)
        {
            Vector3 localVel = _cameraTransform.InverseTransformDirection(
                _playerMotor.HorizontalVelocity);
            yaw += -localVel.x * _velocityInfluence;
        }

        Vector3 rotTarget = Vector3.ClampMagnitude(Compose(pitch, yaw), _maxAngle);

        // ── Underdamped spring (can overshoot, unlike SmoothDamp) ─────────
        _rotVelocity += (rotTarget - _currentRot) * _stiffness * dt;
        _rotVelocity *= Mathf.Clamp01(1f - _damping * dt); // safe: never inverts
        _currentRot  += _rotVelocity * dt;

        // ── Position lag from velocity ────────────────────────────────────
        Vector3 posTarget = Vector3.zero;
        if (_playerMotor != null && _cameraTransform != null)
        {
            Vector3 localVel = _cameraTransform.InverseTransformDirection(
                _playerMotor.HorizontalVelocity);
            posTarget = new Vector3(-localVel.x, 0f, 0f) * (_velocityInfluence * 0.002f);
            posTarget = Vector3.ClampMagnitude(posTarget, 0.02f);
        }

        _posVelocity    += (posTarget - _currentPosOffset) * _stiffness * dt;
        _posVelocity    *= Mathf.Clamp01(1f - _damping * dt);
        _currentPosOffset += _posVelocity * dt;

        // ── Walk bob ──────────────────────────────────────────────────────
        bool moving = _playerMotor != null &&
                      _playerMotor.HorizontalVelocity.magnitude > 0.1f &&
                      _playerMotor.IsGrounded;

        if (moving)
            _bobTimer += dt * _bobFrequency * Mathf.PI * 2f;
        else
            _bobTimer = Mathf.MoveTowards(_bobTimer,
                Mathf.Round(_bobTimer / Mathf.PI) * Mathf.PI, dt * 6f);

        Vector3 bob = moving
            ? new Vector3(0f, Mathf.Abs(Mathf.Sin(_bobTimer)) * _bobAmplitude, 0f)
            : Vector3.zero;

        ApplyPose(bob);
    }

    /// <summary>
    /// Lays conceptual pitch/yaw offsets onto the flashlight's local Euler axes. Handles models
    /// authored rolled ~90° (<see cref="_swapAxes"/>) and per-axis sign inversion.
    /// </summary>
    private Vector3 Compose(float pitch, float yaw)
    {
        if (_invertPitch) pitch = -pitch;
        if (_invertYaw)   yaw   = -yaw;
        return _swapAxes ? new Vector3(yaw, pitch, 0f) : new Vector3(pitch, yaw, 0f);
    }

    private void ApplyPose(Vector3 bob)
    {
        transform.localPosition = _baseLocalPos + _currentPosOffset + bob + DipPositionOffset;
        transform.localRotation = _baseLocalRot * Quaternion.Euler(_currentRot) * Quaternion.Euler(DipRotationOffset);
    }
}
