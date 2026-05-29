using UnityEngine;

/// <summary>
/// CameraController — attach this to the Camera child of the player, NOT the root.

/// ══ PROPERTIES USED BY OTHER SCRIPTS ════════════════════════════════════
///
///   LeanWeight  — smoothed lean value (-1..+1). Read by GunSway and others.
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerMovementConfig config;
    [SerializeField] private PlayerInput playerInput;

    [Header("Crouch Camera Heights")]
    [Tooltip("Local Y of this transform when standing. " +
             "Set to match your prefab eye height (~1.65 for a 2m capsule).")]
    [SerializeField] private float standCamY       = 1.65f;
    [Tooltip("Local Y of this transform when crouching.")]
    [SerializeField] private float crouchCamY      = 0.70f;
    [SerializeField] private float crouchLerpSpeed = 8f;

    [Header("Leaning")]
    [Tooltip("Max camera roll angle in degrees at full lean.")]
    public float leanAngle      = 15f;
    [Tooltip("Max lateral camera offset in units at full lean.")]
    public float leanSideOffset = 0.15f;
    [Tooltip("Lean smoothing speed.")]
    public float leanSmooth     = 10f;

    /// <summary>
    /// Smoothed lean value in range [-1, +1].
    /// Positive = leaning right. Read by GunSway and any other system.
    /// </summary>
    public float LeanWeight { get; private set; }

    /// <summary>Current vertical look angle (pitch) in degrees. Read by the save system.</summary>
    public float Pitch => _pitch;

    /// <summary>
    /// Sets the vertical look angle directly. Used by the save system to restore where the
    /// player was looking. Clamped to the configured vertical look limit.
    /// </summary>
    public void SetPitch(float pitch)
    {
        _pitch = config != null
            ? Mathf.Clamp(pitch, -config.verticalLookLimit, config.verticalLookLimit)
            : pitch;
    }

    private PlayerMotor _motor;
    private float   _pitch;
    private float   _fovRatio;
    private float   _leanCurrent;
    private float   _leanVelocity;
    private Vector3 _baseLocalPos;

    void Start()
    {
        // Cursor locking is owned by GameInputState — do NOT lock here.

        _motor = GetComponentInParent<PlayerMotor>();

        Camera cam = GetComponent<Camera>();
        _fovRatio  = (cam != null && cam.aspect > 0f) ? 1f / cam.aspect : 1f;

        _baseLocalPos = transform.localPosition;
    }

    void LateUpdate()
    {
        // ── Pitch (vertical look) ─────────────────────────────────────────
        if (!GameInputState.GameplayBlocked)
        {
            float mouseY = Input.GetAxisRaw("Mouse Y") * config.mouseSensitivity * _fovRatio;
            _pitch -= mouseY;
            _pitch  = Mathf.Clamp(_pitch, -config.verticalLookLimit, config.verticalLookLimit);
        }

        // ── Lean ──────────────────────────────────────────────────────────
        // LeanInput: -1 = lean left (Q), 0 = none, +1 = lean right (E)
        float leanTarget = playerInput != null ? playerInput.LeanInput : 0f;
        _leanCurrent = Mathf.SmoothDamp(_leanCurrent, leanTarget, ref _leanVelocity, 1f / leanSmooth);
        LeanWeight   = _leanCurrent;

        // ── Crouch height lerp ────────────────────────────────────────────
        float targetY = (_motor != null && _motor.IsCrouching) ? crouchCamY : standCamY;
        Vector3 lp    = transform.localPosition;
        lp.y          = Mathf.Lerp(lp.y, targetY, crouchLerpSpeed * Time.deltaTime);
        transform.localPosition = lp;

        // ── Apply pitch rotation + lateral lean offset ────────────────────
        // Positive lean = lean right: roll clockwise (-Z euler), shift right (+X local)
        transform.localRotation = Quaternion.Euler(_pitch, 0f, -_leanCurrent * leanAngle);
        Vector3 pos = transform.localPosition;
        pos.x = _baseLocalPos.x + _leanCurrent * leanSideOffset;
        transform.localPosition = pos;
    }
}
