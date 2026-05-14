using UnityEngine;

/// <summary>
/// Global input gate. Set GameplayBlocked = true to freeze all player controls
/// (movement, look, shooting) while keeping UI systems running.
///
/// Static class — no scene setup or singleton GO needed.
/// Any system (inventory, dialogue, menus) can block/unblock independently.
/// Cursor ownership lives here so no blocker needs to manage it manually.
/// </summary>
public static class GameInputState
{
    // ── Weapon input ───────────────────────────────────────────────────────

    /// <summary>Fire button held (full-auto).</summary>
    public static bool FireHeld     => Input.GetButton("Fire1");
    /// <summary>Fire button pressed this frame (semi/burst).</summary>
    public static bool FirePressed  => Input.GetButtonDown("Fire1");
    /// <summary>Aim / ADS button held.</summary>
    public static bool AimHeld      => Input.GetButton("Fire2");
    /// <summary>Reload key pressed this frame.</summary>
    public static bool ReloadPressed => Input.GetKeyDown(KeyCode.R);
    /// <summary>Hold-open / debug bolt key held.</summary>
    public static bool HoldOpenHeld  => Input.GetKey(KeyCode.H);

    // --

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

    /// <summary>Force all blocks cleared — use in scene transitions / reloads.</summary>
    public static void ForceUnblockAll()
    {
        _blockCount      = 0;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }
}
