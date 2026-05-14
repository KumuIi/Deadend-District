using UnityEngine;

[CreateAssetMenu(fileName = "MovementConfig", menuName = "Deadend District/Movement Config")]
public class PlayerMovementConfig : ScriptableObject
{
    [Header("General")]
    public bool inputEnabled = true;

    [Header("Walking")]
    public float walkSpeed   = 4.0f;
    public float sprintSpeed = 7.0f;
    public float crouchSpeed = 2.0f;

    [Header("Acceleration")]
    [Tooltip("How fast the player accelerates to target speed (units/s^2)")]
    public float acceleration    = 60f;
    [Tooltip("How fast the player decelerates when no input (units/s^2)")]
    public float deceleration    = 80f;
    [Tooltip("Deceleration rate while airborne (units/s^2)")]
    public float airDeceleration = 15f;

    [Header("Crouch")]
    [Tooltip("Capsule height while crouching")]
    public float crouchHeight    = 1.0f;
    [Tooltip("Capsule height while standing")]
    public float standHeight     = 2.0f;
    [Tooltip("Speed at which capsule height lerps")]
    public float crouchLerpSpeed = 12f;

    [Header("Jump")]
    public float     jumpForce      = 6f;
    public float     gravity        = 25f;
    public float     coyoteTime     = 0.12f;
    public float     jumpBufferTime = 0.15f;
    public LayerMask groundMask;
    public LayerMask collisionMask;

    [Header("Slopes")]
    public float maxSlopeAngle    = 45f;
    public float slideStopAngle   = 30f;
    public float slideWiggleSpeed = 1.5f;
    public float slideSpeed       = 8f;

    [Header("Ground Detection")]
    [Tooltip("Extra distance the ground probe extends below the bottom of the capsule. " +
             "Increase this if the player loses ground contact on small bumps. " +
             "Decrease if the player snaps to the floor too early when jumping. " +
             "Default: 0.05 (5cm)")]
    public float groundCheckExtra = 0.05f;

    [Tooltip("Extra distance below ground contact that triggers step-up detection. " +
             "Raise to climb higher steps, lower to be more selective. " +
             "Default: 0.25 (25cm — roughly a standard stair riser)")]
    public float maxStepHeight = 0.25f;

    [Header("Camera")]
    public float mouseSensitivity  = 2f;
    public float verticalLookLimit = 85f;
}
