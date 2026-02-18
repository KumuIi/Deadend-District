using UnityEngine;

public class CameraController : MonoBehaviour
{
    [Header("Camera Settings")]
    public float mouseSensitivity = 2f;
    public Transform playerBody;

    private float xRotation = 0f;
    private float yRotation = 0f;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        yRotation = playerBody.eulerAngles.y;
    }

    void Update()
    {
        // Get mouse input
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

        // Apply mouse input to base rotation
        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);
        yRotation += mouseX;

        // Apply base rotation + recoil to camera
        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // Rotate player body for horizontal look
        playerBody.rotation = Quaternion.Euler(0f, yRotation, 0f);
    }
}
