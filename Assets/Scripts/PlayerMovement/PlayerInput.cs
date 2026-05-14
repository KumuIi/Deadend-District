using UnityEngine;

/// <summary>
/// Thin input reader. Collects raw input every Update and exposes clean
/// properties to other systems. JumpPressed uses a latch (|=) so no press
/// is lost between Update and FixedUpdate.
/// </summary>
public class PlayerInput : MonoBehaviour
{
    public Vector2 MoveInput  { get; private set; }
    public bool    SprintHeld { get; private set; }
    public bool    CrouchHeld { get; private set; }
    public bool    JumpPressed { get; private set; }

    /// -1 = lean left (Q), 0 = none, +1 = lean right (E)
    public float LeanInput { get; private set; }

    void Update()
    {
        if (GameInputState.GameplayBlocked)
        {
            MoveInput  = Vector2.zero;
            SprintHeld = false;
            CrouchHeld = false;
            LeanInput  = 0f;
            // JumpPressed intentionally NOT cleared here — buffered jumps expire naturally
            return;
        }

        MoveInput  = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        SprintHeld = Input.GetKey(KeyCode.LeftShift);
        CrouchHeld = Input.GetKey(KeyCode.LeftControl);
        JumpPressed |= Input.GetKeyDown(KeyCode.Space);

        float lean = 0f;
        if (Input.GetKey(KeyCode.Q)) lean -= 1f;
        if (Input.GetKey(KeyCode.E)) lean += 1f;
        LeanInput = lean;
    }

    public void ConsumeJump() => JumpPressed = false;
}
