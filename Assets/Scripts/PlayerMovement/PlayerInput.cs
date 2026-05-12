using UnityEngine;

public class PlayerInput : MonoBehaviour
{
    public Vector2 MoveInput   { get; private set; }
    public bool    SprintHeld  { get; private set; }
    public bool    JumpPressed { get; private set; }
    /// <summary>-1 = lean left (Q), 0 = none, +1 = lean right (E)</summary>
    public float   LeanInput   { get; private set; }

    void Update()
    {
        MoveInput   = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        SprintHeld  = Input.GetKey(KeyCode.LeftShift);
        JumpPressed |= Input.GetKeyDown(KeyCode.Space);

        float lean = 0f;
        if (Input.GetKey(KeyCode.Q)) lean -= 1f;
        if (Input.GetKey(KeyCode.E)) lean += 1f;
        LeanInput = lean;
    }

    public void ConsumeJump() => JumpPressed = false;
    
    
    
}
