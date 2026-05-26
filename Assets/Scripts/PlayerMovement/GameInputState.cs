using UnityEngine;

/// <summary>
/// Global input gate. Set GameplayBlocked = true to freeze all player controls
/// (movement, look, shooting) while keeping UI systems running.
///
/// This is a static class — no scene setup or singleton GO needed.
/// Any system (inventory, dialogue, menus) can block/unblock independently.
/// The cursor is managed here so every blocker doesn't have to remember to do it.
///
/// ══ CURSOR BOOTSTRAP ════════════════════════════════════════════════════
///
///   Because this is a static class it has no Awake/Start. Cursor locking on
///   game start is handled by CursorLocker — a tiny auto-loaded MonoBehaviour
///   that calls GameInputState.LockCursor() once on Awake. It also re-locks
///   when the application regains focus (e.g. alt-tabbing back in).
///
/// </summary>
public static class GameInputState
{
    // ── Weapon input ───────────────────────────────────────────────────────

    /// <summary>Fire button held (full-auto) — left mouse button only.</summary>
    public static bool FireHeld      => Input.GetMouseButton(0);
    /// <summary>Fire button pressed this frame (semi/burst) — left mouse button only.</summary>
    public static bool FirePressed   => Input.GetMouseButtonDown(0);
    /// <summary>Aim / ADS button held — right mouse button.</summary>
    public static bool AimHeld       => Input.GetMouseButton(1);
    /// <summary>Reload key pressed this frame.</summary>
    public static bool ReloadPressed => Input.GetKeyDown(KeyCode.R);
    /// <summary>Hold-open / debug bolt key held.</summary>
    public static bool HoldOpenHeld  => Input.GetKey(KeyCode.H);
    /// <summary>Interact key pressed this frame (F).</summary>
    public static bool InteractPressed => Input.GetKeyDown(KeyCode.F);

    // ──────────────────────────────────────────────────────────────────────

    private static int _blockCount = 0;

    /// <summary>True while any system has requested a gameplay block.</summary>
    public static bool GameplayBlocked => _blockCount > 0;

    /// <summary>
    /// Lock cursor and hide it — call once at game start via CursorLocker.
    /// Safe to call when already locked (no-op).
    /// </summary>
    public static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;
    }

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
