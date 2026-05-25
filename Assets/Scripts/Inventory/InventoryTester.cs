using UnityEngine;

/// <summary>
/// Dev-only script for testing inventory pickup without a full loot system.
/// Attach to any GameObject in the scene.
/// </summary>
public sealed class InventoryTester : MonoBehaviour
{
    [Header("=== DEV ONLY — DELETE BEFORE SHIP ===")]
    public InventoryUI    inventory;
    public WeaponManager  weaponManager;

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

        var so   = testItems[_index % testItems.Length];
        _index++;

        // Spawn the correctly-typed instance so tooltip / interactions work
        ItemInstance item;
        switch (so)
        {
            case WeaponSO ws:
            {
                var wi = new WeaponItemInstance(ws);
                // Link to the matching GunController in the scene (if any)
                if (weaponManager != null)
                    foreach (var gun in weaponManager.Weapons)
                        if (gun.weaponData == ws) { wi.LinkedGun = gun; break; }
                item = wi;
                break;
            }
            case MagazineSO ms:
                item = new MagazineItemInstance(ms);
                break;
            case AmmunitionSO a:
                item = new AmmoItemInstance(a);
                break;
            default:
                item = ItemInstanceFactory.Create(so);
                break;
        }

        var result = inventory.TryPickup(item);

        if (result == PickupResult.Placed)
        {
            _lastAdded = item;
            Debug.Log($"[InventoryTester] Added '{so.itemName}' ({item.GetType().Name}) " +
                      $"at {item.gridPosition} rotated={item.isRotated} | " +
                      $"free cells: {inventory.Grid.GetFreeCellCount()}");
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
