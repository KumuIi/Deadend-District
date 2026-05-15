using UnityEngine;

/// <summary>
/// Bootstraps cursor locking for gameplay.
///
///   • Locks + hides the cursor on Awake (frame 0, before any Start).
///   • Re-locks when the game window regains focus (alt-tab back in).
///   • Escape key unlocks for debugging in-editor; clicking the window
///     re-locks. This matches standard FPS cursor behaviour.
///
/// </summary>
public class CursorLocker : MonoBehaviour
{
    void Awake()
    {
        GameInputState.LockCursor();
    }

    void OnApplicationFocus(bool hasFocus)
    {
        // Re-lock when the player clicks back into the window after alt-tabbing.
        // Only re-lock if gameplay is not currently blocked by a menu/UI.
        if (hasFocus && !GameInputState.GameplayBlocked)
            GameInputState.LockCursor();
    }

#if UNITY_EDITOR
    void Update()
    {
        // In-editor convenience: Escape releases the cursor so you can use
        // the Unity editor UI. Clicking the Game view re-locks via OnApplicationFocus.
        if (Input.GetKeyDown(KeyCode.Escape) && !GameInputState.GameplayBlocked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
    }
#endif
}