using UnityEngine;

/// <summary>
/// A <see cref="LockedDoor"/> opened by a <see cref="KeySO"/> carried in the player inventory.
/// Matching is by id: a key unlocks this door when its <see cref="KeySO.targetDoorId"/> equals
/// this door's <c>doorId</c>. A <see cref="KeySO.singleUse"/> key is consumed on success.
/// </summary>
public class KeyLockedDoor : LockedDoor
{
    protected override UnlockAttempt BeginUnlock(GameObject interactor)
    {
        if (!TryFindMatchingKey(out var item, out var key))
            return UnlockAttempt.Failed;

        // Consume single-use keys only once the unlock is actually authorised.
        if (key.singleUse)
            InventoryUI.Player?.RemoveItemAndDetach(item);

        return UnlockAttempt.Succeeded;
    }

    protected override string GetLockedPrompt(GameObject interactor)
    {
        return TryFindMatchingKey(out _, out var key)
            ? $"Unlock Door ({key.itemName})"
            : "Locked";
    }

    /// <summary>
    /// Scans the player grid for a KeySO whose targetDoorId matches this door. Shared by the
    /// prompt and the unlock so the two never disagree. Plain foreach — no LINQ allocations.
    /// </summary>
    private bool TryFindMatchingKey(out ItemInstance item, out KeySO key)
    {
        item = null;
        key  = null;

        var grid = InventoryUI.Player?.Grid;
        if (grid == null) return false;

        foreach (var instance in grid.PlacedItems)
        {
            if (instance?.data is KeySO k && k.targetDoorId == DoorId)
            {
                item = instance;
                key  = k;
                return true;
            }
        }
        return false;
    }
}
