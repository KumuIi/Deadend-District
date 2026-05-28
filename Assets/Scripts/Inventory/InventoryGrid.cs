using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure C# data model for a 2D inventory grid — no MonoBehaviour, no UnityEngine.UI.
/// Width = columns (X axis), Height = rows (Y axis). Origin = top-left cell (0,0).
///
/// Each cell holds a reference to the ItemInstance covering it, or null.
/// Items use their cellOffsets (via ItemInstance.GetCurrentOffsets) for shape-aware placement,
/// supporting rectangles, L-shapes, and any custom footprint defined in the ItemSO.
/// </summary>
public class InventoryGrid
{
    public readonly int Width;
    public readonly int Height;

    private readonly ItemInstance[,] _cells;
    private readonly HashSet<ItemInstance> _placed = new HashSet<ItemInstance>();

    /// <summary>All items currently placed in this grid.</summary>
    public IReadOnlyCollection<ItemInstance> PlacedItems => _placed;

    /// <summary>Fired after any placement, removal, or load that changes grid contents.</summary>
    public event Action OnChanged;

    public InventoryGrid(int width, int height)
    {
        Width  = width;
        Height = height;
        _cells = new ItemInstance[width, height];
    }

    // ── Queries ───────────────────────────────────────────────────────────

    /// <summary>Returns true if (x,y) is inside the grid boundaries.</summary>
    public bool IsInBounds(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height;

    /// <summary>Returns the item occupying cell (x,y), or null.</summary>
    public ItemInstance GetAt(int x, int y) =>
        IsInBounds(x, y) ? _cells[x, y] : null;

    /// <inheritdoc cref="GetAt(int,int)"/>
    public ItemInstance GetAt(Vector2Int pos) => GetAt(pos.x, pos.y);

    /// <summary>
    /// Returns the total number of unoccupied cells in the grid.
    /// </summary>
    public int GetFreeCellCount()
    {
        int free = 0;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                if (_cells[x, y] == null) free++;
        return free;
    }

    /// <summary>
    /// Returns true if the item's shape (in its current rotation) fits at
    /// <paramref name="pos"/> with no conflicts.
    /// An item may overlap its own current cells (needed for in-place rotation).
    /// </summary>
    public bool CanPlace(ItemInstance item, Vector2Int pos)
    {
        foreach (var offset in item.GetCurrentOffsets())
        {
            int x = pos.x + offset.x;
            int y = pos.y + offset.y;
            if (!IsInBounds(x, y)) return false;
            var occupant = _cells[x, y];
            if (occupant != null && occupant != item) return false;
        }
        return true;
    }

    // ── Placement ─────────────────────────────────────────────────────────

    /// <summary>
    /// Places the item at <paramref name="pos"/>. If the item is already in
    /// this grid it is moved. Returns false without modifying state if invalid.
    /// </summary>
    public bool TryPlace(ItemInstance item, Vector2Int pos)
    {
        if (!CanPlace(item, pos)) return false;

        if (_placed.Contains(item)) RemoveInternal(item);

        item.gridPosition = pos;

        foreach (var offset in item.GetCurrentOffsets())
            _cells[pos.x + offset.x, pos.y + offset.y] = item;

        _placed.Add(item);
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>Removes every item from the grid. Used by death handling.</summary>
    public void ClearAll()
    {
        var all = new List<ItemInstance>(_placed);
        foreach (var item in all) RemoveInternal(item);
        OnChanged?.Invoke();
    }

    /// <summary>Removes the item from the grid. Returns false if it wasn't placed.</summary>
    public bool Remove(ItemInstance item)
    {
        if (!_placed.Contains(item)) return false;
        RemoveInternal(item);
        OnChanged?.Invoke();
        return true;
    }

    private void RemoveInternal(ItemInstance item)
    {
        // Use the item's stored position + current offsets to clear cells
        foreach (var offset in item.GetCurrentOffsets())
        {
            int x = item.gridPosition.x + offset.x;
            int y = item.gridPosition.y + offset.y;
            if (IsInBounds(x, y) && _cells[x, y] == item)
                _cells[x, y] = null;
        }
        _placed.Remove(item);
    }

    /// <summary>
    /// Scans top-left to bottom-right for the first position where the item fits.
    /// Returns null if no free space exists.
    /// </summary>
    public Vector2Int? FindFreeSpace(ItemInstance item)
    {
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                var pos = new Vector2Int(x, y);
                if (CanPlace(item, pos)) return pos;
            }
        return null;
    }

    // ── Save / Load ───────────────────────────────────────────────────────

    /// <summary>Returns the minimal data needed to reconstruct this grid's contents.</summary>
    public List<GridSaveEntry> GetSaveData()
    {
        var entries = new List<GridSaveEntry>(_placed.Count);
        foreach (var item in _placed)
            entries.Add(new GridSaveEntry
            {
                soName    = item.data.name,
                gridX     = item.gridPosition.x,
                gridY     = item.gridPosition.y,
                isRotated = item.isRotated,
            });
        return entries;
    }

    /// <summary>
    /// Rebuilds the grid from previously saved data.
    /// Uses <paramref name="resolver"/> to look up ItemSO assets by name — the grid
    /// itself has no dependency on Resources or any Unity loading API.
    /// Entries whose SO cannot be resolved are skipped with a logged warning.
    /// </summary>
    public void LoadFromSaveData(List<GridSaveEntry> entries, IItemSOResolver resolver)
    {
        if (entries == null || resolver == null) return;

        foreach (var entry in entries)
        {
            var so = resolver.Resolve(entry.soName);
            if (so == null)
            {
                Debug.LogWarning($"[InventoryGrid] Could not resolve ItemSO '{entry.soName}' — skipping.");
                continue;
            }

            var item = ItemInstanceFactory.Create(so);
            item.gridPosition = new Vector2Int(entry.gridX, entry.gridY);
            item.isRotated    = entry.isRotated;

            if (!TryPlace(item, item.gridPosition))
                Debug.LogWarning($"[InventoryGrid] Could not place '{entry.soName}' at " +
                                 $"({entry.gridX},{entry.gridY}) during load — position occupied or out of bounds.");
        }
        OnChanged?.Invoke();
    }

    /// <summary>Serialisable record for one item's grid placement. Store this; rebuild everything else from the SO.</summary>
    [System.Serializable]
    public class GridSaveEntry
    {
        /// <summary>ScriptableObject asset name — pass to IItemSOResolver.Resolve().</summary>
        public string soName;
        public int    gridX;
        public int    gridY;
        public bool   isRotated;
    }
}
