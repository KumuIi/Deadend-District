using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private PlayerMovementConfig config;
    [SerializeField] private PlayerInput playerInput;

    [Header("=== Leaning ===")]
    [Tooltip("Max camera roll angle in degrees at full lean")]
    public float leanAngle      = 15f;
    [Tooltip("Max lateral camera offset in units at full lean")]
    public float leanSideOffset = 0.15f;
    [Tooltip("Lean smoothing speed")]
    public float leanSmooth     = 10f;

    // Smoothed lean value (-1..1). Read by GunSway for extra gun tilt.
    public float LeanWeight { get; private set; }

    private float   _pitch;
    private float   _fovRatio;
    private float   _leanCurrent;
    private float   _leanVelocity;
    private Vector3 _baseLocalPos;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        _fovRatio     = 1f / GetComponent<Camera>().aspect;
        _baseLocalPos = transform.localPosition;
    }

    void LateUpdate()
    {
        float mouseY = Input.GetAxisRaw("Mouse Y") * config.mouseSensitivity * _fovRatio;
        _pitch -= mouseY;
        _pitch = Mathf.Clamp(_pitch, -config.verticalLookLimit, config.verticalLookLimit);

        // ── Lean ─────────────────────────────────────────────────────────────
        float leanTarget = playerInput ? playerInput.LeanInput : 0f;
        _leanCurrent = Mathf.SmoothDamp(_leanCurrent, leanTarget, ref _leanVelocity, 1f / leanSmooth);
        LeanWeight   = _leanCurrent;

        // Positive lean = lean right: camera rolls clockwise (negative Z euler) and shifts right (+X local)
        transform.localRotation = Quaternion.Euler(_pitch, 0f, -_leanCurrent * leanAngle);
        transform.localPosition = _baseLocalPos + new Vector3(_leanCurrent * leanSideOffset, 0f, 0f);
    }
}
