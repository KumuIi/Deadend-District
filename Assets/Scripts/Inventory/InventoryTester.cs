using UnityEngine;

/// <summary>
/// Dev-only script for testing inventory pickup without a full loot system.
/// Attach to any GameObject in the scene.
/// </summary>
public sealed class InventoryTester : MonoBehaviour
{
    [Header("=== DEV ONLY — DELETE BEFORE SHIP ===")]
    public InventoryUI inventory;

    [Tooltip("ItemSOs to cycle through when pressing the add key.")]
    public ItemSO[] testItems;

    public KeyCode addKey    = KeyCode.F;
    public KeyCode removeKey = KeyCode.G;

    private int          _index    = 0;
    private ItemInstance _lastAdded;

    private void Update()
    {
        if (Input.GetKeyDown(addKey))    AddNext();
        if (Input.GetKeyDown(removeKey)) RemoveLast();
    }

    private void AddNext()
    {
        if (inventory == null || testItems == null || testItems.Length == 0)
        {
            Debug.LogWarning("[InventoryTester] Assign InventoryUI and at least one ItemSO.");
            return;
        }

        var so     = testItems[_index % testItems.Length];
        _index++;

        var item   = new ItemInstance(so);
        var result = inventory.TryPickup(item);

        if (result == PickupResult.Placed)
        {
            _lastAdded = item;
            Debug.Log($"[InventoryTester] Added '{so.itemName}' at {item.gridPosition} " +
                      $"rotated={item.isRotated} | free cells: {inventory.Grid.GetFreeCellCount()}");
        }
        else
        {
            Debug.LogWarning($"[InventoryTester] No space for '{so.itemName}'.");
        }
    }

    private void RemoveLast()
    {
        if (_lastAdded == null) return;
        inventory.RemoveItem(_lastAdded);
        Debug.Log($"[InventoryTester] Removed '{_lastAdded.data.itemName}'.");
        _lastAdded = null;
    }
}
