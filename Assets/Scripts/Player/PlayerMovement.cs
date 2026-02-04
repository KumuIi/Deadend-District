using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float speed = 5f;
    public float jumpHeight = 2f;
    public float gravity = -9.81f;
    
    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundDistance = 0.4f;
    public LayerMask groundMask;

    private CharacterController controller;
    private Vector3 velocity;
    private bool isGrounded;
    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        // Get input
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        // Calculate movement direction
        Vector3 move = transform.right * horizontal + transform.forward * vertical;
        controller.Move(move * speed * Time.deltaTime);

        // Ground check - reset velocity when grounded
        if (controller.isGrounded)
        {
            // Jump
            if (Input.GetButtonDown("Jump"))
            {
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            if (velocity.y < 0)
            {
                velocity.y = -2f;
            }

        }

        // Apply gravity ONCE
        velocity.y += gravity * Time.deltaTime;
    
        // Apply vertical movement
        controller.Move(velocity * Time.deltaTime);
    }
}