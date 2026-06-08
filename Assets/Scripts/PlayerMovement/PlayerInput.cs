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

        // Read WASD (+ arrows) explicitly instead of GetAxisRaw("Horizontal"/"Vertical").
        // The legacy axes also bind a gamepad left-stick; a drifting/idle controller feeds a
        // constant value and pushes the player in one direction forever. Explicit keys can't drift.
        float h = 0f, v = 0f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow))  h -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow))  v -= 1f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))    v += 1f;
        MoveInput  = new Vector2(h, v);
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
