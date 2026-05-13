using UnityEngine;

/// <summary>
/// Runtime placement state of one inventory item.
/// The ItemSO is the immutable template; this class holds the mutable position + rotation.
///
/// Subclass for item-specific mutable state (e.g. MagazineInstance extends this concept
/// as a companion, though it is not a subclass here because it carries additional domain
/// logic beyond grid placement).
///
/// Save contract: store (data.name, gridPosition.x, gridPosition.y, isRotated).
/// Reconstruct by loading the SO by name and calling new ItemInstance(so) + restoring fields.
/// </summary>
public class ItemInstance
{
    public readonly ItemSO data;

    /// <summary>Top-left cell this item occupies in the grid (grid space, origin = top-left).</summary>
    public Vector2Int gridPosition;

    /// <summary>True when rotated 90°, which swaps width and height.</summary>
    public bool isRotated;

    public ItemInstance(ItemSO definition)
    {
        data = definition;
    }

    /// <summary>Effective cell footprint — accounts for rotation.</summary>
    public Vector2Int CurrentSize => isRotated
        ? new Vector2Int(data.gridSize.y, data.gridSize.x)
        : data.gridSize;
}
