using UnityEngine;

/// <summary>
/// Reads switch input and tells WeaponManager which slot to equip.
/// Knows nothing about refs, lifecycle, or gun internals.
/// </summary>
public class WeaponSwitcher : MonoBehaviour
{
    public WeaponManager weaponManager;

    private int _current = -1;

    void Update()
    {
        int count = weaponManager.weapons.Length;

        for (int i = 0; i < count && i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                EquipAt(i);
                return;
            }
        }

        float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
        if (scroll > 0f) EquipAt((_current - 1 + count) % count);
        if (scroll < 0f) EquipAt((_current + 1)          % count);
    }

    void EquipAt(int index)
    {
        if (index == _current) return;
        _current = index;
        weaponManager.Equip(index);
    }
}
