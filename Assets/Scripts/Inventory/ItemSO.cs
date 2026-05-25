using UnityEngine;

/// <summary>
/// Base ScriptableObject for every item that can live in the inventory grid.
/// Extend this for weapons, gear, consumables, etc.
///
/// Shape contract:
///   <see cref="cellOffsets"/> defines every cell the item occupies relative to its
///   top-left anchor (0,0). For a simple 2×3 rectangle you can leave the array empty
///   and set <see cref="gridSize"/> instead — the rectangle offsets are generated
///   automatically at runtime via <see cref="GetOffsets"/>.
///
/// Save contract: only the SO's asset name + grid position + rotation are stored.
/// Rebuild the full item state at load time by resolving the SO by name via IItemSOResolver.
/// </summary>
public abstract class ItemSO : ScriptableObject
{
    [Header("=== Item Identity ===")]
    public string itemName = "Item";

    [Header("=== Inventory Shape ===")]
    [Tooltip("Simple rectangular footprint. Ignored when cellOffsets is non-empty.")]
    public Vector2Int gridSize = Vector2Int.one;

    [Tooltip(
        "Custom cell offsets relative to the top-left anchor (0,0). " +
        "Leave EMPTY to use gridSize as a plain rectangle. " +
        "Example L-shape (3 wide, extra cell below right): (0,0) (1,0) (2,0) (2,1)")]
    public Vector2Int[] cellOffsets = System.Array.Empty<Vector2Int>();

    [Header("=== Weight ===")]
    [Tooltip("Item weight in kilograms. Contributes to player encumbrance.")]
    [Range(0f, 50f)]
    public float weightKg = 0.5f;

    [Header("=== Visuals ===")]
    [Tooltip("3D model instantiated in the scene and physically placed over the inventory panel.")]
    public GameObject modelPrefab;

    [Tooltip("Fallback tint when no model is assigned.")]
    public Color itemColor = new Color(0.35f, 0.45f, 0.6f, 1f);

    [Header("=== Model Orientation ===")]
    [Tooltip(
        "Per-item rotation applied after the base flat-on-panel rotation. " +
        "Use InventoryOrientationTester in Play Mode to dial this in visually.")]
    public Vector3 modelOrientationOffset = Vector3.zero;

    [Tooltip(
        "Axis (in LOCAL model space, AFTER orientationOffset is applied) around which " +
        "the model spins when the item is grid-rotated 90°.\n\n" +
        "The panel surface normal is Z, so (0,0,1) = spin flat on the panel ← correct default.\n" +
        "Use InventoryOrientationTester's 'Preview Grid Rotated' toggle to verify this looks right.")]
    public Vector3 gridRotationAxis = Vector3.forward; // (0,0,1) = panel normal

    // ── Shape API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the canonical (un-rotated) cell offsets for this item.
    /// If <see cref="cellOffsets"/> is non-empty those are used directly;
    /// otherwise a rectangle defined by <see cref="gridSize"/> is generated.
    /// </summary>
    public Vector2Int[] GetOffsets()
    {
        if (cellOffsets != null && cellOffsets.Length > 0)
            return cellOffsets;

        int w    = Mathf.Max(1, gridSize.x);
        int h    = Mathf.Max(1, gridSize.y);
        var rect = new Vector2Int[w * h];
        int i    = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                rect[i++] = new Vector2Int(x, y);
        return rect;
    }
}
