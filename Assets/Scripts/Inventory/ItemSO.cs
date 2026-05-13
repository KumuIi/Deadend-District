using UnityEngine;

/// <summary>
/// Base ScriptableObject for every item that can live in the inventory grid.
/// Extend this for weapons, magazines, gear, consumables, etc.
///
/// Save contract: only the SO's asset name + grid position + rotation are stored.
/// Rebuild the full item state at load time by finding the SO by name.
/// </summary>
public abstract class ItemSO : ScriptableObject
{
    [Header("=== Item Identity ===")]
    public string itemName = "Item";

    [Header("=== Inventory Grid ===")]
    [Tooltip("Footprint in grid cells (width x height). Right-click in inventory to rotate.")]
    public Vector2Int gridSize = Vector2Int.one;

    [Header("=== Visuals ===")]
    [Tooltip("3D model instantiated in the preview renderer — rendered into the inventory slot")]
    public GameObject modelPrefab;
    [Tooltip("Fallback tint when no model is assigned")]
    public Color      itemColor = new Color(0.35f, 0.45f, 0.6f, 1f);
}
