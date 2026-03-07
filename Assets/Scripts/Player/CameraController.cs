using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private PlayerMovementConfig config;

    private float _pitch;
    private float _fovRatio;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // tan(vFOV/2) / tan(hFOV/2) simplifies to 1/aspect
        // e.g. 16:9 → 9/16 = 0.5625, so Y is always 56.25% of X on a 16:9 screen
        _fovRatio = 1f / GetComponent<Camera>().aspect;
    }

    void LateUpdate()
    {
        float mouseY = Input.GetAxisRaw("Mouse Y") * config.mouseSensitivity * _fovRatio;

        _pitch -= mouseY;
        _pitch = Mathf.Clamp(_pitch, -config.verticalLookLimit, config.verticalLookLimit);

        transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }
}
