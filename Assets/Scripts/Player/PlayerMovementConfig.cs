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
    public LayerMask groundMask;
    public LayerMask collisionMask;

    [Header("Camera")]
    public float mouseSensitivity = 2f;
    public float verticalLookLimit = 85f;
}
