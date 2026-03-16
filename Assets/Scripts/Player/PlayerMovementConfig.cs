using UnityEngine;

[CreateAssetMenu(fileName = "MovementConfig", menuName = "Deadend District/Movement Config")]
public class PlayerMovementConfig : ScriptableObject
{
    [Header("General")]
    public bool inputEnabled = true;
    
    [Header("Walking")]
    public float walkSpeed = 4.0f;
    public float sprintSpeed = 7.0f;
    public float crouchSpeed = 2.0f;

    [Header("Jump")]
    public float jumpForce  = 6f;
    public float gravity    = 25f;
    public float coyoteTime = 0.12f;
    public LayerMask groundMask;
    public LayerMask collisionMask;

    [Header("Slopes")]
    public float maxSlopeAngle        = 45f;
    public float slideStopAngle       = 30f;
    public float slideWiggleSpeed     = 1.5f;
    public float slideSpeed           = 8f;

    [Header("Camera")]
    public float mouseSensitivity = 2f;
    public float verticalLookLimit = 85f;
}
