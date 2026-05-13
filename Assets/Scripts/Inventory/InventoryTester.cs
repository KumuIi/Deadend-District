using UnityEngine;

/// <summary>
/// Dev-only script for testing inventory pickup without a full loot system.
/// Attach to any GameObject in the scene. Delete before shipping.
/// </summary>
public class InventoryTester : MonoBehaviour
{
    public InventoryUI inventory;

    [Tooltip("ItemSOs to cycle through when pressing the add key")]
    public ItemSO[] testItems;

    public KeyCode addKey    = KeyCode.F;
    public KeyCode removeKey = KeyCode.G;

    private int _index = 0;
    private ItemInstance _lastAdded;

    void Update()
    {
        if (Input.GetKeyDown(addKey))
            AddNext();

        if (Input.GetKeyDown(removeKey) && _lastAdded != null)
            Remove();
    }

    void AddNext()
    {
        if (inventory == null || testItems == null || testItems.Length == 0)
        {
            Debug.LogWarning("InventoryTester: assign InventoryUI and at least one ItemSO.");
            return;
        }

        ItemSO so = testItems[_index % testItems.Length];
        _index++;

        var item   = new ItemInstance(so);
        var result = inventory.TryPickup(item);

        if (result == PickupResult.Placed)
        {
            _lastAdded = item;
            Debug.Log($"[InventoryTester] Added '{so.itemName}' at {item.gridPosition}  " +
                      $"rotated={item.isRotated}  " +
                      $"remaining cells: {FreeCount()}");
        }
        else
        {
            Debug.LogWarning($"[InventoryTester] No space for '{so.itemName}'.");
        }
    }

    void Remove()
    {
        inventory.RemoveItem(_lastAdded);
        Debug.Log($"[InventoryTester] Removed '{_lastAdded.data.itemName}'.");
        _lastAdded = null;
    }

    int FreeCount()
    {
        int free = 0;
        for (int y = 0; y < inventory.Grid.Height; y++)
        for (int x = 0; x < inventory.Grid.Width;  x++)
            if (inventory.Grid.GetAt(x, y) == null) free++;
        return free;
    }
}
