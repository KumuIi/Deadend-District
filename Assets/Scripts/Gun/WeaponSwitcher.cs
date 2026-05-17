using UnityEngine;

/// <summary>
/// Reads keyboard (1–9) and scroll-wheel input and tells WeaponManager which
/// slot to equip. Always derives the current index from WeaponManager so it
/// stays in sync even when other systems call Equip() directly.
/// </summary>
public class WeaponSwitcher : MonoBehaviour
{
    public WeaponManager weaponManager;

    private void Update()
    {
        // Input switching intentionally disabled — will be replaced by hotbar hotkey system.
    }

    private int GetCurrentIndex()
    {
        GunController cur = weaponManager.CurrentWeapon;
        if (cur == null) return 0;
        for (int i = 0; i < weaponManager.Weapons.Count; i++)
            if (weaponManager.Weapons[i] == cur) return i;
        return 0;
    }

    private void EquipAt(int index)
    {
        if (index == GetCurrentIndex()) return;
        weaponManager.Equip(index);
    }
}
