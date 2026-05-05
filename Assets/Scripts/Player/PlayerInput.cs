using UnityEngine;

public class PlayerInput : MonoBehaviour
{
    public Vector2 MoveInput  { get; private set; }
    public bool    SprintHeld { get; private set; }
    public bool    JumpPressed { get; private set; }

    void Update()
    {
        MoveInput   = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        SprintHeld  = Input.GetKey(KeyCode.LeftShift);
        JumpPressed |= Input.GetKeyDown(KeyCode.Space);
    }

    public void ConsumeJump() => JumpPressed = false;
    
    
    
}
