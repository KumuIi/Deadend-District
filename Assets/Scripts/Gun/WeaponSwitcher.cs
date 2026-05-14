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
        if (weaponManager == null) return;

        int count   = weaponManager.Weapons.Count;
        int current = GetCurrentIndex();

        // Number keys 1–9
        for (int i = 0; i < count && i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                EquipAt(i);
                return;
            }
        }

        // Scroll wheel
        float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
        if (scroll > 0f) EquipAt((current - 1 + count) % count);
        if (scroll < 0f) EquipAt((current + 1) % count);
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
