/// <summary>
/// A single named equipment slot that holds one item at a time.
/// Implementors: WeaponSlot (wraps WeaponManager), FlashlightSlot, HeadlampSlot,
///               ArmourSlot (Wave 5).
/// All slots register with EquipmentController on Awake.
/// SlotId constants: "weapon_primary", "weapon_secondary", "flashlight", "headlamp", "armour".
/// </summary>
public interface IEquipmentSlot
{
    string       SlotId        { get; }
    ItemInstance EquippedItem  { get; }
    bool         TryEquip(ItemInstance item);
    void         Unequip();
}
