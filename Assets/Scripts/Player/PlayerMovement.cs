using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement Settings")]
    public float speed = 5f;
    public float jumpHeight = 2f;
    public float gravity = -9.81f;

    [Header("Ground Detection")]
    public Transform groundCheck; // Assign your GroundCheck empty GameObject here
    public float groundCheckDistance = 0.2f;
    
    private CharacterController controller;
    private Vector3 velocity;
    private bool isGrounded;

    void Start()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        // Raycast ground check
        CheckGround();
        
        // Get input
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        // Calculate movement direction
        Vector3 move = transform.right * horizontal + transform.forward * vertical;
        controller.Move(move * speed * Time.deltaTime);

        // Ground check - reset velocity when grounded
        if (isGrounded)
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

        // Apply gravity
        velocity.y += gravity * Time.deltaTime;
    
        // Apply vertical movement
        controller.Move(velocity * Time.deltaTime);
    }

    void CheckGround()
    {
        // Cast raycast from the GroundCheck position
        RaycastHit hit;
        isGrounded = Physics.Raycast(groundCheck.position, Vector3.down, out hit, groundCheckDistance, ~0, QueryTriggerInteraction.Ignore);
        
        // Debug visualization (green when grounded, red when not)
        Debug.DrawRay(groundCheck.position, Vector3.down * groundCheckDistance, isGrounded ? Color.green : Color.red);
    }
}