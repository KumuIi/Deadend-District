using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tracks the currently-active 3D menu buttons of each type so <see cref="MenuInputHandler"/> can
/// hit-test them without a per-frame Object.FindObjectsByType scan — that scan allocates and walks
/// every object of the type every frame the cursor is unlocked (e.g. the entire time the main menu
/// is up). Buttons register in OnEnable and unregister in OnDisable, so a closed menu's buttons are
/// simply absent from the list.
///
/// One static list per concrete T (MenuButton3D, FlashdriveButton, SaveSlotButton3D).
/// </summary>
public static class MenuHitRegistry<T> where T : Component
{
    private static readonly List<T> _active = new List<T>();

    public static IReadOnlyList<T> Active => _active;

    public static void Register(T item)
    {
        if (item != null && !_active.Contains(item)) _active.Add(item);
    }

    public static void Unregister(T item) => _active.Remove(item);
}
