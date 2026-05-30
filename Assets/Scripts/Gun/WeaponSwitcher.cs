using UnityEngine;

/// <summary>
/// Reads keyboard (1–9) and scroll-wheel input and tells WeaponManager which
/// slot to equip. Always derives the current index from WeaponManager so it
/// stays in sync even when other systems call Equip() directly.
///
/// All input reads are gated behind GameInputState.GameplayBlocked, so opening
/// the inventory, a trader, or the pause menu suppresses weapon switching.
/// </summary>
public class WeaponSwitcher : MonoBehaviour
{
    public WeaponManager weaponManager;

    [Tooltip("Highest number key bound to a slot (1..N). Keys past the weapon count are ignored.")]
    [SerializeField] private int _maxNumberKeys = 9;

    private void Update()
    {
        if (weaponManager == null || GameInputState.GameplayBlocked) return;

        int count = weaponManager.Weapons.Count;
        if (count == 0) return;

        // Number keys 1..N → slot index 0..N-1
        int keys = Mathf.Min(_maxNumberKeys, count);
        for (int i = 0; i < keys; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                EquipAt(i);
                return; // a key press this frame wins over scroll
            }
        }

        // Scroll wheel cycles through slots, wrapping at both ends.
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            int dir = scroll > 0f ? 1 : -1;
            int cur = GetCurrentIndex();
            int next = cur < 0
                ? (dir > 0 ? 0 : count - 1)        // from unarmed: scroll up → first, down → last
                : (cur + dir + count) % count;      // +count keeps modulo positive
            EquipAt(next);
        }
    }

    private int GetCurrentIndex()
    {
        GunController cur = weaponManager.CurrentWeapon;
        if (cur == null) return -1; // unarmed — so EquipAt(0) isn't wrongly treated as a no-op
        for (int i = 0; i < weaponManager.Weapons.Count; i++)
            if (weaponManager.Weapons[i] == cur) return i;
        return -1;
    }

    private void EquipAt(int index)
    {
        if (index == GetCurrentIndex()) return;
        weaponManager.Equip(index);
    }
}
