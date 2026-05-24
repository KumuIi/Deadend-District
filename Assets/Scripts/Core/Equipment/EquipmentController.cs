using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central registry of all IEquipmentSlot instances on the player.
/// Weapon slots delegate to WeaponManager; non-weapon slots (flashlight, headlamp)
/// register independently. This controller never owns equip logic — slots own that.
/// </summary>
public class EquipmentController : MonoBehaviour
{
    public static EquipmentController Instance { get; private set; }

    private readonly Dictionary<string, IEquipmentSlot> _slots = new Dictionary<string, IEquipmentSlot>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void RegisterSlot(IEquipmentSlot slot)
    {
        if (slot == null) return;
        _slots[slot.SlotId] = slot;
    }

    public void UnregisterSlot(string slotId) =>
        _slots.Remove(slotId);

    public IEquipmentSlot GetSlot(string slotId) =>
        _slots.TryGetValue(slotId, out var slot) ? slot : null;

    public bool EquipToSlot(string slotId, ItemInstance item)
    {
        var slot = GetSlot(slotId);
        if (slot == null)
        {
            Debug.LogWarning($"[EquipmentController] Slot '{slotId}' not registered.");
            return false;
        }
        return slot.TryEquip(item);
    }

    public ItemInstance GetEquipped(string slotId) =>
        GetSlot(slotId)?.EquippedItem;
}
