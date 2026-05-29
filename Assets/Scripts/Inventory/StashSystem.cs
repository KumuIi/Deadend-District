using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// The player's persistent storage at the hub. Wraps a second InventoryUI (its own grid)
/// and exposes it as an ILootContainer so generic container UIs (trader, chests, …) can use it.
///
/// Access is hub-only: gated on the WSM key "zone.hub.active" so the stash can never be opened
/// mid-run. The stash chest world object (see StashChest) drives Open()/Close() through this
/// singleton — nothing references the stash UI directly across scenes.
///
/// Persistence lives in StashSaveAdapter (RunScopeTag.Profile): contents survive death,
/// extraction, and sector reloads. This component only saves on close as a convenience.
///
/// Implementors: one instance on the persistent player/UI rig (DontDestroyOnLoad).
/// </summary>
[DefaultExecutionOrder(6)] // after InventoryUI (order 5) so both grids exist before any Open call
public class StashSystem : MonoBehaviour, ILootContainer
{
    public static StashSystem Instance { get; private set; }

    [Header("=== Panels ===")]
    // The stash InventoryUI must be a SEPARATE GameObject from the player inventory, with the same
    // modelLayer, positioned so it does not overlap the player panel (e.g. larger paddingRight),
    // and WITHOUT an InventoryInputHandler — otherwise Tab would toggle the stash directly and
    // bypass the hub gate / paired-open / save-on-close flow handled here.
    [Tooltip("InventoryUI dedicated to the stash grid. Separate GameObject, same modelLayer, " +
             "no InventoryInputHandler, positioned clear of the player panel (e.g. larger paddingRight).")]
    [SerializeField] private InventoryUI _stashUI;
    [Tooltip("The player's inventory UI — opened alongside the stash so items can be dragged between the two grids.")]
    [SerializeField] private InventoryUI _playerInventoryUI;

    [Header("=== Access ===")]
    [Tooltip("WSM key that must be true for the stash to be openable. Written by the hub zone trigger.")]
    [SerializeField] private string _hubActiveKey = "zone.hub.active";
    [Tooltip("Bypass the hub gate. Useful for testing before the hub zone trigger exists.")]
    [SerializeField] private bool _ignoreHubGate = false;

    /// <summary>True while the stash panel is open.</summary>
    public bool IsOpen { get; private set; }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (_stashUI == null)
            Debug.LogError("[StashSystem] Stash InventoryUI is not assigned.", this);
        if (_playerInventoryUI == null)
            Debug.LogError("[StashSystem] Player InventoryUI is not assigned.", this);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        // If the player closed their inventory (Tab/Escape) while the stash was open, close the
        // stash too so the panels stay in sync and GameInputState block counts stay balanced.
        if (IsOpen && _playerInventoryUI != null && !_playerInventoryUI.IsOpen)
            Close();
    }

    // ── Access gate ────────────────────────────────────────────────────────

    /// <summary>True if the stash is currently usable (in the hub, or gate bypassed).</summary>
    public bool CanAccess()
    {
        if (_ignoreHubGate) return true;
        var wsm = WorldStateManager.Instance;
        return wsm != null && wsm.GetBool(_hubActiveKey);
    }

    // ── Open / Close ───────────────────────────────────────────────────────

    public void Open(GameObject interactor)
    {
        if (IsOpen) return;
        if (!CanAccess())
        {
            Debug.Log("[StashSystem] Stash can only be accessed in the hub.");
            return;
        }
        if (_stashUI == null || _playerInventoryUI == null) return;

        // Guard against double-blocking GameInputState if a panel is already open via Tab.
        if (!_playerInventoryUI.IsOpen) _playerInventoryUI.SetOpen(true);
        if (!_stashUI.IsOpen)           _stashUI.SetOpen(true);
        IsOpen = true;
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;

        if (_stashUI != null && _stashUI.IsOpen)                     _stashUI.SetOpen(false);
        if (_playerInventoryUI != null && _playerInventoryUI.IsOpen) _playerInventoryUI.SetOpen(false);

        // MANUAL-SAVE MODEL: closing the stash no longer autosaves.
        // Auto-committing Profile scope here (while Run/inventory only saves manually) let a
        // reload stitch a fresh stash onto an old inventory — an item dupe. Saving is now fully
        // the player's responsibility via the flashdrive menu, so all scopes commit together.
        // Re-enable for Tarkov-style auto-commit (and route inventory/stash through one save point).
        // string slot = RunManager.Instance?.ActiveSaveSlot;
        // if (string.IsNullOrEmpty(slot)) slot = "slot0";
        // SaveSystem.Instance?.SaveProfile(slot);
    }

    // ── ILootContainer ─────────────────────────────────────────────────────

    public string ContainerName => "Stash";

    public IReadOnlyList<ItemInstance> Items =>
        _stashUI != null ? new List<ItemInstance>(_stashUI.Grid.PlacedItems)
                         : new List<ItemInstance>();

    public bool CanAddItem(ItemInstance item)
    {
        if (item == null || _stashUI == null) return false;

        var grid = _stashUI.Grid;
        if (grid.FindFreeSpace(item) != null) return true;

        // Mirror InventoryUI.TryPickup: also test the rotated orientation, then restore.
        item.isRotated = !item.isRotated;
        bool fits = grid.FindFreeSpace(item) != null;
        item.isRotated = !item.isRotated;
        return fits;
    }

    public bool TryAddItem(ItemInstance item)
    {
        if (item == null || _stashUI == null) return false;
        return _stashUI.TryPickup(item) == PickupResult.Placed;
    }

    public bool TryRemoveItem(ItemInstance item)
    {
        if (item == null || _stashUI == null) return false;
        if (!_stashUI.Grid.PlacedItems.Contains(item)) return false;
        _stashUI.RemoveItem(item);
        return true;
    }
}
