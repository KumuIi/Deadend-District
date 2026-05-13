using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure data model for a 2D inventory grid — no MonoBehaviour, no UI.
/// Width = columns (X axis), Height = rows (Y axis). Origin = top-left cell (0,0).
///
/// Each cell holds a reference to the ItemInstance covering it, or null.
/// Items record their own top-left gridPosition so moves can be validated
/// without scanning the full array.
/// </summary>
public class InventoryGrid
{
    public readonly int Width;
    public readonly int Height;

    private readonly ItemInstance[,]    _cells;
    private readonly HashSet<ItemInstance> _placed = new HashSet<ItemInstance>();

    public IReadOnlyCollection<ItemInstance> PlacedItems => _placed;

    public InventoryGrid(int width, int height)
    {
        Width  = width;
        Height = height;
        _cells = new ItemInstance[width, height];
    }

    // ── Queries ───────────────────────────────────────────────────────────

    public bool IsInBounds(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height;

    public ItemInstance GetAt(int x, int y) =>
        IsInBounds(x, y) ? _cells[x, y] : null;

    public ItemInstance GetAt(Vector2Int pos) => GetAt(pos.x, pos.y);

    /// <summary>
    /// Returns true if the item's footprint fits at pos with no occupied-by-another-item conflicts.
    /// An item may overlap its own current cells (needed for in-place rotation checks).
    /// </summary>
    public bool CanPlace(ItemInstance item, Vector2Int pos)
    {
        Vector2Int size = item.CurrentSize;
        for (int y = pos.y; y < pos.y + size.y; y++)
        for (int x = pos.x; x < pos.x + size.x; x++)
        {
            if (!IsInBounds(x, y)) return false;
            ItemInstance occupant = _cells[x, y];
            if (occupant != null && occupant != item) return false;
        }
        return true;
    }

    // ── Placement ─────────────────────────────────────────────────────────

    /// <summary>
    /// Places the item at pos. If the item is already in this grid it is moved.
    /// Returns false without modifying state if placement is invalid.
    /// </summary>
    public bool TryPlace(ItemInstance item, Vector2Int pos)
    {
        if (!CanPlace(item, pos)) return false;

        if (_placed.Contains(item)) RemoveInternal(item);

        Vector2Int size   = item.CurrentSize;
        item.gridPosition = pos;

        for (int y = pos.y; y < pos.y + size.y; y++)
        for (int x = pos.x; x < pos.x + size.x; x++)
            _cells[x, y] = item;

        _placed.Add(item);
        return true;
    }

    /// <summary>Removes the item from the grid. Returns false if it wasn't placed.</summary>
    public bool Remove(ItemInstance item)
    {
        if (!_placed.Contains(item)) return false;
        RemoveInternal(item);
        return true;
    }

    private void RemoveInternal(ItemInstance item)
    {
        Vector2Int size = item.CurrentSize;
        Vector2Int pos  = item.gridPosition;
        for (int y = pos.y; y < pos.y + size.y; y++)
        for (int x = pos.x; x < pos.x + size.x; x++)
            if (_cells[x, y] == item) _cells[x, y] = null;
        _placed.Remove(item);
    }

    /// <summary>
    /// Scans top-left to bottom-right for the first position where the item fits.
    /// Returns null if no free space exists.
    /// </summary>
    public Vector2Int? FindFreeSpace(ItemInstance item)
    {
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width;  x++)
        {
            var pos = new Vector2Int(x, y);
            if (CanPlace(item, pos)) return pos;
        }
        return null;
    }

    // ── Save data ─────────────────────────────────────────────────────────

    /// <summary>Returns the minimal data needed to reconstruct this grid's contents.</summary>
    public List<GridSaveEntry> GetSaveData()
    {
        var entries = new List<GridSaveEntry>(_placed.Count);
        foreach (ItemInstance item in _placed)
        {
            entries.Add(new GridSaveEntry
            {
                soName    = item.data.name,   // ScriptableObject asset name — use for lookup
                gridX     = item.gridPosition.x,
                gridY     = item.gridPosition.y,
                isRotated = item.isRotated,
            });
        }
        return entries;
    }
}

/// <summary>Serialisable record for one item's grid position. Store this; rebuild everything else.</summary>
[System.Serializable]
public class GridSaveEntry
{
    public string soName;
    public int    gridX;
    public int    gridY;
    public bool   isRotated;
}
