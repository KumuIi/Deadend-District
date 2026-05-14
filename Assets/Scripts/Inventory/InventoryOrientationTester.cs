#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

/// <summary>
/// DEV ONLY — places a test item directly into the live inventory so you can
/// dial in ItemSO.modelOrientationOffset and gridRotationAxis while seeing
/// the real result in-game.
///
/// Workflow:
///   1. Add this component to any GameObject in the scene.
///   2. Assign InventoryUI and the ItemSO you want to fix.
///   3. Enter Play Mode and open the inventory (Tab).
///   4. Press F9 — item spawns in the grid.
///   5. Adjust orientationOffset until the model looks correct (not rotated).
///   6. Press R (or tick previewGridRotated) — see how it looks when grid-rotated.
///      If the rotation axis is wrong, adjust gridRotationAxis (default 0,0,1 is correct
///      for most items — the panel normal).
///   7. Press F11 — saves BOTH values into the .asset file on disk.
///   8. Press F10 to remove, then repeat for the next item.
/// </summary>
public sealed class InventoryOrientationTester : MonoBehaviour
{
    [Header("=== DEV ONLY — DELETE BEFORE SHIP ===")]
    public InventoryUI inventory;

    [Tooltip("The ItemSO whose orientation you want to fix.")]
    public ItemSO targetItem;

    [Header("Controls")]
    public KeyCode spawnKey    = KeyCode.F9;
    public KeyCode removeKey   = KeyCode.F10;
    public KeyCode copyKey     = KeyCode.F11;
    [Tooltip("Toggle grid rotation while the item is live in the inventory.")]
    public KeyCode rotateKey   = KeyCode.R;

    [Header("Live Values — tweak until both states look correct")]
    [Tooltip("Corrects the model's export orientation. Applied before grid rotation.")]
    public Vector3 orientationOffset = Vector3.zero;

    [Tooltip(
        "Axis the model spins around when grid-rotated 90°.\n" +
        "Z (0,0,1) = panel normal = flat spin on the panel surface. ← correct for most items.\n" +
        "Change only if the default spin looks wrong.")]
    public Vector3 gridRotationAxis = Vector3.forward;

    [Header("State (read-only)")]
    [Tooltip("Current grid rotation state of the test item.")]
    public bool previewGridRotated = false;

    // ── Private ───────────────────────────────────────────────────────────
    private ItemInstance      _testInstance;
    private InventoryItemView _testView;

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void Update()
    {
        if (Input.GetKeyDown(spawnKey))  SpawnInInventory();
        if (Input.GetKeyDown(removeKey)) RemoveFromInventory();
        if (Input.GetKeyDown(copyKey))   CopyToSO();
        if (Input.GetKeyDown(rotateKey) && _testInstance != null)
        {
            previewGridRotated = !previewGridRotated;
            Debug.Log($"[OrientationTester] Grid rotation: {previewGridRotated}");
        }

        // Push live values every frame so Inspector tweaks update immediately
        if (_testInstance != null && _testView != null)
        {
            targetItem.modelOrientationOffset = orientationOffset;
            targetItem.gridRotationAxis       = gridRotationAxis;
            _testInstance.isRotated           = previewGridRotated;
            _testView.RefreshLayout(inventory.cellSize);
        }
    }

    private void OnDestroy() => RemoveFromInventory();

    // ── Actions ───────────────────────────────────────────────────────────

    private void SpawnInInventory()
    {
        if (inventory == null || targetItem == null)
        {
            Debug.LogWarning("[OrientationTester] Assign InventoryUI and targetItem first.");
            return;
        }
        if (_testInstance != null)
        {
            Debug.LogWarning("[OrientationTester] Already spawned. Press F10 to remove first.");
            return;
        }

        // Sync starting values from SO so we continue from where we left off
        orientationOffset  = targetItem.modelOrientationOffset;
        gridRotationAxis   = targetItem.gridRotationAxis;
        previewGridRotated = false;

        _testInstance = new ItemInstance(targetItem);
        var result    = inventory.TryPickup(_testInstance);

        if (result != PickupResult.Placed)
        {
            Debug.LogWarning("[OrientationTester] No free space in inventory.");
            _testInstance = null;
            return;
        }

        inventory.Views.TryGetValue(_testInstance, out _testView);

        Debug.Log("[OrientationTester] Spawned in inventory.\n" +
                  "  Adjust 'orientationOffset' → fixes base orientation\n" +
                  "  Adjust 'gridRotationAxis'  → fixes how R-rotation looks\n" +
                  "  R = toggle grid rotation | F11 = save to SO | F10 = remove");
    }

    private void RemoveFromInventory()
    {
        if (_testInstance == null) return;
        inventory?.RemoveItem(_testInstance);
        _testInstance = null;
        _testView     = null;
        Debug.Log("[OrientationTester] Removed test item.");
    }

    private void CopyToSO()
    {
        if (targetItem == null) return;

        targetItem.modelOrientationOffset = orientationOffset;
        targetItem.gridRotationAxis       = gridRotationAxis;

#if UNITY_EDITOR
        EditorUtility.SetDirty(targetItem);
        AssetDatabase.SaveAssetIfDirty(targetItem);
        Debug.Log($"[OrientationTester] Saved to '{targetItem.name}':\n" +
                  $"  orientationOffset = {orientationOffset}\n" +
                  $"  gridRotationAxis  = {gridRotationAxis}");
#else
        Debug.Log($"[OrientationTester] Written to SO at runtime (not persisted to disk without Editor).");
#endif
    }

    // ── Scene Gizmo ───────────────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        string status = _testInstance != null
            ? $"LIVE | rotated={previewGridRotated}"
            : "not spawned";

        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 0.5f,
            targetItem != null
                ? $"[OrientationTester] {targetItem.itemName} ({status})\n" +
                  $"offset: {orientationOffset}  axis: {gridRotationAxis}\n" +
                  $"F9=Spawn  R=Rotate  F11=Save  F10=Remove"
                : "[OrientationTester]\n(no item assigned)");
    }
#endif
}
