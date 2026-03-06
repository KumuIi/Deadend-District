using UnityEngine;

[CreateAssetMenu(fileName = "MovementConfig", menuName = "Deadend District/Movement Config")]
public class PlayerMovementConfig : ScriptableObject
{
    [Header("Walking")]
    public float walkSpeed = 4.0f;
    public float sprintSpeed = 7.0f;
    public float crouchSpeed = 2.0f;
    public float acceleration = 50.0f;
    public float deceleration = 40.0f;
    public float airAcceleration = 8.0f;

    [Header("Jumping")]
    public float jumpForce = 7.0f;
    public float coyoteTime = 0.12f;
    public float jumpBufferTime = 0.1f;

    [Header("Gravity")]
    public float gravity = 20.0f;
    public float maxFallSpeed = 30.0f;
    public float fallMultiplier = 1.5f;

    [Header("Slopes")]
    public float maxWalkableAngle = 46.0f;
    public float slopeSlideSpeed = 8.0f;

    [Header("Steps")]
    public float maxStepHeight = 0.35f;
    public float stepCheckDepth = 0.4f;

    [Header("Capsule")]
    public float standHeight = 1.8f;
    public float crouchHeight = 1.0f;
    public float capsuleRadius = 0.3f;

    [Header("Ground Detection")]
    public float groundCheckDistance = 0.08f;
    public float groundSnapDistance = 0.3f;
    public LayerMask groundMask = ~0;
}
