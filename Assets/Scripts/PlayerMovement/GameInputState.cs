using UnityEngine;

/// <summary>
/// Global input gate. Set GameplayBlocked = true to freeze all player controls
/// (movement, look, shooting) while keeping UI systems running.
///
/// This is a static class — no scene setup or singleton GO needed.
/// Any system (inventory, dialogue, menus) can block/unblock independently.
/// The cursor is managed here so every blocker doesn't have to remember to do it.
/// </summary>
public static class GameInputState
{
    private static int _blockCount = 0;

    /// <summary>True while any system has requested a gameplay block.</summary>
    public static bool GameplayBlocked => _blockCount > 0;

    /// <summary>
    /// Increment the block count. Multiple systems can block simultaneously —
    /// gameplay only resumes when every blocker calls Unblock().
    /// </summary>
    public static void Block()
    {
        _blockCount++;
        if (_blockCount == 1)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
    }

    /// <summary>Release one block. Gameplay resumes when count reaches zero.</summary>
    public static void Unblock()
    {
        _blockCount = Mathf.Max(0, _blockCount - 1);
        if (_blockCount == 0)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
    }
}
