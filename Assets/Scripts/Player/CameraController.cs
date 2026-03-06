using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("Camera Settings")]
    public float mouseSensitivity = 2f;

    [Header("Height Smoothing")]
    [SerializeField] private float cameraSmoothing = 15f;

    private Transform _playerBody;
    private PlayerMotor _motor;
    private float _xRotation;
    private float _yRotation;
    private float _currentCameraY;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        _playerBody = transform.parent;
        _motor = GetComponentInParent<PlayerMotor>();
        _yRotation = _playerBody.eulerAngles.y;
        _currentCameraY = _motor != null ? _motor.CurrentHeight - 0.2f : transform.localPosition.y;
    }

    void LateUpdate()
    {
        // Get mouse input (raw delta from Input System comes through Pointer/delta)
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // Apply mouse input to rotation
        _xRotation -= mouseY;
        _xRotation = Mathf.Clamp(_xRotation, -90f, 90f);
        _yRotation += mouseX;

        // Apply rotations
        transform.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
        _playerBody.rotation = Quaternion.Euler(0f, _yRotation, 0f);

        // Smooth camera height for crouch/step transitions
        if (_motor != null)
        {
            float targetY = _motor.CurrentHeight - 0.2f;
            _currentCameraY = Mathf.Lerp(_currentCameraY, targetY, cameraSmoothing * Time.deltaTime);
            transform.localPosition = new Vector3(0f, _currentCameraY, 0f);
        }
    }
}
