using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the highlight colour of grid cell Images during drag operations.
/// Owns no MonoBehaviour state — created and driven by InventoryUI.
/// </summary>
public sealed class InventoryHighlighter
{
    private readonly Image[]       _cellImages;
    private readonly int           _gridWidth;
    private readonly InventoryGrid _grid;
    private readonly Color         _normal;
    private readonly Color         _highlight;
    private readonly Color         _blocked;

    private Vector2Int[] _highlighted = System.Array.Empty<Vector2Int>();

    public InventoryHighlighter(
        Image[]        cellImages,
        int            gridWidth,
        InventoryGrid  grid,
        Color          normal,
        Color          highlight,
        Color          blocked)
    {
        _cellImages = cellImages;
        _gridWidth  = gridWidth;
        _grid       = grid;
        _normal     = normal;
        _highlight  = highlight;
        _blocked    = blocked;
    }

    /// <summary>
    /// Highlights all cells the item would occupy if placed at <paramref name="topLeft"/>.
    /// Cells already occupied by another item are shown in the blocked colour.
    /// </summary>
    public void HighlightCells(ItemInstance item, Vector2Int topLeft)
    {
        ClearHighlight();

        var offsets = item.GetCurrentOffsets();
        var cells   = new System.Collections.Generic.List<Vector2Int>(offsets.Length);

        foreach (var offset in offsets)
        {
            int x = topLeft.x + offset.x;
            int y = topLeft.y + offset.y;
            if (!_grid.IsInBounds(x, y)) continue;

            cells.Add(new Vector2Int(x, y));

            var occupant = _grid.GetAt(x, y);
            bool blocked = occupant != null && occupant != item;
            _cellImages[y * _gridWidth + x].color = blocked ? _blocked : _highlight;
        }

        _highlighted = cells.ToArray();
    }

    /// <summary>Restores all currently highlighted cells to the normal colour.</summary>
    public void ClearHighlight()
    {
        foreach (var c in _highlighted)
            if (_grid.IsInBounds(c.x, c.y))
                _cellImages[c.y * _gridWidth + c.x].color = _normal;

        _highlighted = System.Array.Empty<Vector2Int>();
    }
}
