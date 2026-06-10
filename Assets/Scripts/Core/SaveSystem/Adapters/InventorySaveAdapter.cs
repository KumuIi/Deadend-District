using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bridges InventoryUI (and its InventoryGrid) into the ISaveable system.
/// Attach to the same GameObject as InventoryUI, or to a dedicated SaveBridge object.
///
/// Future: if the player has multiple containers (vest, backpack, stash) register one
/// adapter per grid with a unique SaveId per container.
/// </summary>
public class InventorySaveAdapter : MonoBehaviour, ISaveable, IRunLifecycleListener
{
    [SerializeField] private InventoryUI _inventoryUI;

    public string      SaveId    => "player.inventory";
    public string      SaveType  => "Inventory";
    public RunScopeTag SaveScope => RunScopeTag.Run;

    private void Start()
    {
        SaveSystem.Instance?.Register(this);
        RunManager.Instance?.RegisterListener(this);
    }

    private void OnDisable()
    {
        SaveSystem.Instance?.Unregister(this);
        RunManager.Instance?.UnregisterListener(this);
    }

    // ── IRunLifecycleListener ──────────────────────────────────────────────

    public void OnRunStarted() { }
    public void OnRunExtracted() { }
    public void OnReturnedToHub() { }

    public void OnRunDied()
    {
        // ClearAll() removes data AND destroys item views — using Grid.ClearAll() alone leaves stale views
        if (_inventoryUI != null)
            _inventoryUI.ClearAll();
    }

    public object CaptureSaveData()
    {
        if (_inventoryUI == null) throw new InvalidOperationException("InventoryUI not assigned.");

        var data = new InventorySaveData { entries = _inventoryUI.Grid.GetSaveData() };
        WriteEquipPos(_inventoryUI.EquippedWeapon,
                      ref data.equippedWeaponX, ref data.equippedWeaponY);
        WriteEquipPos(_inventoryUI.EquippedFlashlight,
                      ref data.equippedFlashlightX, ref data.equippedFlashlightY);
        return data;
    }

    public void RestoreSaveData(object data)
    {
        if (_inventoryUI == null) return;
        var dto = JsonUtility.FromJson<InventorySaveData>((string)data);
        if (dto?.entries == null) return;

        // ClearAll() + LoadFromSaveData() atomically replaces grid contents and rebuilds views.
        var resolver = new ResourcesItemSOResolver();
        _inventoryUI.LoadFromSaveData(dto.entries, resolver);

        // Re-equip exactly what was in hand at save time (or clear both slots for the -1 sentinel).
        _inventoryUI.RestoreEquipped(
            new Vector2Int(dto.equippedWeaponX,     dto.equippedWeaponY),
            new Vector2Int(dto.equippedFlashlightX, dto.equippedFlashlightY));
    }

    /// <summary>Records an equipped item's grid anchor, or leaves the (-1,-1) "none" default.</summary>
    private static void WriteEquipPos(ItemInstance item, ref int x, ref int y)
    {
        if (item == null) return;
        x = item.gridPosition.x;
        y = item.gridPosition.y;
    }
}

[Serializable]
public class InventorySaveData
{
    public List<InventoryGrid.GridSaveEntry> entries;

    // Equipped-loadout anchors. -1 = nothing equipped in that slot (also the default for pre-this-change
    // saves, which load as fully unequipped). A grid position uniquely identifies a placed item, so this
    // re-equips the exact restored instance — preserving its magazine / battery state.
    public int equippedWeaponX     = -1;
    public int equippedWeaponY     = -1;
    public int equippedFlashlightX = -1;
    public int equippedFlashlightY = -1;
}
