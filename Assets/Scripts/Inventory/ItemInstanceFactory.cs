using UnityEngine;

/// <summary>
/// Creates the correct typed ItemInstance subclass for a given ItemSO.
/// Use this instead of `new ItemInstance(so)` anywhere items enter the inventory from the world.
/// </summary>
public static class ItemInstanceFactory
{
    public static ItemInstance Create(ItemSO so)
    {
        if (so == null) { Debug.LogWarning("[ItemInstanceFactory] Null ItemSO passed to Create()."); return null; }
        if (so is WeaponSO weapon)   return new WeaponItemInstance(weapon);
        if (so is MagazineSO mag)    return new MagazineItemInstance(mag);
        if (so is AmmunitionSO ammo) return new AmmoItemInstance(ammo);
        return new ItemInstance(so);
    }
}
