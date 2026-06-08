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
        {
            if (_debugLogs)
            {
                Debug.Log($"[KeyLockedDoor:{name}] No key matching doorId='{DoorId}'. " +
                          $"Player-grid keys: {DescribeInventoryKeys()}", this);
                DumpAllInventories(); // decisive: shows EVERY panel + contents so we can see where the key actually is
            }
            return UnlockAttempt.Failed;
        }

        if (_debugLogs)
            Debug.Log($"[KeyLockedDoor:{name}] Matched key '{key.itemName}' " +
                      $"(targetDoorId='{key.targetDoorId}', singleUse={key.singleUse}). Unlocking.", this);

        // Consume single-use keys only once the unlock is actually authorised.
        if (key.singleUse)
            InventoryUI.Player?.RemoveItemAndDetach(item);

        return UnlockAttempt.Succeeded;
    }

    /// <summary>Debug helper: lists every KeySO in the player grid and its targetDoorId, to spot id mismatches.</summary>
    private string DescribeInventoryKeys()
    {
        var grid = InventoryUI.Player?.Grid;
        if (grid == null) return "(no player inventory)";

        var sb = new System.Text.StringBuilder();
        foreach (var instance in grid.PlacedItems)
            if (instance?.data is KeySO k)
                sb.Append($"['{k.itemName}'->'{k.targetDoorId}'] ");

        return sb.Length == 0 ? "(none)" : sb.ToString();
    }

    /// <summary>
    /// Debug helper: enumerates EVERY InventoryUI in the scene (active or not), flags which one
    /// is <see cref="InventoryUI.Player"/>, and dumps each grid's full contents with item types.
    /// This reveals the two cases that produce "(none)": key sitting in a non-Player panel, or a
    /// grid item whose data isn't actually a KeySO.
    /// </summary>
    private void DumpAllInventories()
    {
        var player = InventoryUI.Player;
        var panels = FindObjectsOfType<InventoryUI>(true);

        Debug.Log($"[KeyLockedDoor:{name}] === INVENTORY DUMP === panels={panels.Length}, " +
                  $"InventoryUI.Player={(player == null ? "NULL (no panel has a weaponManager!)" : player.name)}");

        foreach (var p in panels)
        {
            var grid = p.Grid;
            if (grid == null) { Debug.Log($"   panel '{p.name}' (isPlayer={p == player}) grid=NULL", p); continue; }

            var sb    = new System.Text.StringBuilder();
            int count = 0;
            foreach (var inst in grid.PlacedItems)
            {
                count++;
                var so = inst?.data;
                sb.Append($"{(so == null ? "?" : so.name)}<{(so == null ? "?" : so.GetType().Name)}> ");
            }
            Debug.Log($"   panel '{p.name}' isPlayer={p == player} items({count}): [{sb}]", p);
        }
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

        // Fail closed: an unconfigured door (empty id) must never be opened by an equally
        // unconfigured key — that would consume the key and write a colliding "door..unlocked" flag.
        if (string.IsNullOrWhiteSpace(DoorId)) return false;

        var grid = InventoryUI.Player?.Grid;
        if (grid == null) return false;

        foreach (var instance in grid.PlacedItems)
        {
            if (instance?.data is KeySO k
                && !string.IsNullOrWhiteSpace(k.targetDoorId)
                && string.Equals(k.targetDoorId, DoorId, System.StringComparison.Ordinal))
            {
                item = instance;
                key  = k;
                return true;
            }
        }
        return false;
    }
}
