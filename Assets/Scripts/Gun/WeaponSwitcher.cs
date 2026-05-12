using UnityEngine;

/// <summary>
/// Reads switch input (number keys + scroll wheel) and tells WeaponManager
/// which weapon to equip. Knows nothing about refs or lifecycle — that's
/// WeaponManager's job.
/// </summary>
public class WeaponSwitcher : MonoBehaviour
{
    public WeaponManager weaponManager;
    public Weapon[] weapons;

    private int _current = -1;

    void Start()
    {
        if (weapons.Length > 0)
            EquipAt(0);
    }

    void Update()
    {
        // Number keys 1–9
        for (int i = 0; i < weapons.Length && i < 9; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                EquipAt(i);
                return;
            }
        }

        // Scroll wheel
        float scroll = Input.GetAxisRaw("Mouse ScrollWheel");
        if (scroll > 0f)  EquipAt((_current - 1 + weapons.Length) % weapons.Length);
        if (scroll < 0f)  EquipAt((_current + 1)                   % weapons.Length);
    }

    void EquipAt(int index)
    {
        if (index < 0 || index >= weapons.Length || index == _current) return;
        _current = index;
        weaponManager.Equip(weapons[index]);
    }
}
