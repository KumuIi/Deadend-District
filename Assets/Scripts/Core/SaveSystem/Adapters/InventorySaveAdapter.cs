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
public class InventorySaveAdapter : MonoBehaviour, ISaveable
{
    [SerializeField] private InventoryUI _inventoryUI;

    public string SaveId   => "player.inventory";
    public string SaveType => "Inventory";

    private void Start()
    {
        // Register in Start, not OnEnable — guarantees SaveSystem.Instance
        // exists (initialized in Awake) before adapters attempt to register.
        SaveSystem.Instance?.Register(this);
    }

    private void OnDisable() => SaveSystem.Instance?.Unregister(this);

    public object CaptureSaveData()
    {
        if (_inventoryUI == null) throw new InvalidOperationException("InventoryUI not assigned.");
        return new InventorySaveData { entries = _inventoryUI.Grid.GetSaveData() };
    }

    public void RestoreSaveData(object data)
    {
        if (_inventoryUI == null) return;
        var dto = JsonUtility.FromJson<InventorySaveData>((string)data);
        if (dto?.entries == null) return;

        // ClearAll() + LoadFromSaveData() atomically replaces grid contents and rebuilds views.
        var resolver = new ResourcesItemSOResolver();
        _inventoryUI.LoadFromSaveData(dto.entries, resolver);
    }
}

[Serializable]
public class InventorySaveData
{
    public List<InventoryGrid.GridSaveEntry> entries;
}
