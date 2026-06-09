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

    /// <summary>
    /// Re-pins the camera to its authored resting pose on the body. Restores the horizontal eye
    /// offset (local x/z) and clears any in-progress lean, so the camera sits centered on the
    /// capsule axis. Does NOT touch pitch — that is restored separately by the save system.
    ///
    /// Call this after anything that may have world-moved this transform off the body axis (e.g.
    /// the in-place menu rig). LateUpdate self-heals x/z anyway, but calling this on restore
    /// guarantees a centered camera on the very first frame, with no transient off-center frame.
    /// </summary>
    public void ResetRig()
    {
        _leanCurrent  = 0f;
        _leanVelocity = 0f;
        LeanWeight    = 0f;

        Vector3 lp = transform.localPosition;
        lp.x = _baseLocalPos.x;
        lp.z = _baseLocalPos.z;
        transform.localPosition = lp;
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

        // ── Position: crouch height + lateral lean, re-pinned to the body axis ──
        // Author the FULL local position every frame (x AND z from _baseLocalPos) so any external
        // displacement of this transform — e.g. the in-place menu rig world-moving the camera —
        // self-heals the moment gameplay resumes, instead of leaving the eye point off-centre and
        // making yaw arc the camera around the capsule. Only y carries state (crouch lerp).
        float targetY = (_motor != null && _motor.IsCrouching) ? crouchCamY : standCamY;
        float y       = Mathf.Lerp(transform.localPosition.y, targetY, crouchLerpSpeed * Time.deltaTime);
        transform.localPosition = new Vector3(
            _baseLocalPos.x + _leanCurrent * leanSideOffset,
            y,
            _baseLocalPos.z);

        // ── Apply pitch rotation + lean roll ──────────────────────────────
        // Positive lean = lean right: roll clockwise (-Z euler), shift right (+X local, above)
        transform.localRotation = Quaternion.Euler(_pitch, 0f, -_leanCurrent * leanAngle);
    }
}
