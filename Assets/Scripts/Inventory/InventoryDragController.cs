using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Result returned by the drag-interaction callback when an item is dropped on another item.</summary>
public enum DragInteractionResult
{
    /// <summary>No interaction matched — fall through to normal placement logic.</summary>
    NotHandled,
    /// <summary>Interaction consumed the dragged item (e.g. empty ammo box, magazine loaded into weapon).</summary>
    HandledConsumeDragged,
    /// <summary>Interaction handled but the dragged item should be returned to its origin (e.g. partial ammo load).</summary>
    HandledReturnDragged,
}

/// <summary>
/// Handles all drag state for the inventory grid.
/// Created by InventoryUI and receives callbacks from InventoryItemView.
/// Has no MonoBehaviour dependency — pure logic, driven by InventoryUI.
/// </summary>
public sealed class InventoryDragController
{
    // ── Dependencies ──────────────────────────────────────────────────────
    private readonly InventoryGrid        _grid;
    private readonly InventoryHighlighter _highlighter;
    private readonly Canvas               _canvas;
    private readonly RectTransform        _itemsLayer;
    private readonly RectTransform        _dragLayer;
    private readonly float                _cellSize;

    /// <summary>
    /// Optional callback fired when the dragged item is released over a cell already occupied
    /// by a different item. Return value controls what happens to the dragged view.
    /// </summary>
    public Func<ItemInstance, ItemInstance, DragInteractionResult> OnDroppedOnItem;

    /// <summary>
    /// Optional callback fired when the item cannot be placed in THIS grid (cursor released
    /// outside it, or over an occupied cell). Gives another open InventoryUI (e.g. the stash)
    /// the chance to receive the item and take ownership of its view.
    /// Returns true if another grid accepted the item — in that case this controller must not
    /// restore the item to its origin.
    /// </summary>
    public Func<InventoryItemView, Vector2, bool> TryCrossGridDrop;

    /// <summary>
    /// Optional callback fired each drag frame when the cursor is outside this grid.
    /// Lets another open panel show its own cell highlight so the player sees where the item
    /// will land. The callback should clear its highlight and return false if the cursor is not
    /// over that panel. InventoryUI wires this to TryHandoffHighlight.
    /// </summary>
    public Func<ItemInstance, Vector2, bool> TryCrossGridHighlight;

    // ── Drag state ────────────────────────────────────────────────────────
    private InventoryItemView _draggedView;
    private Vector2Int        _dragOriginPos;
    private bool              _dragOriginRotated;

    /// <summary>True while an item is being dragged.</summary>
    public bool IsDragging => _draggedView != null;

    /// <summary>The view currently being dragged, or null.</summary>
    public InventoryItemView DraggedView => _draggedView;

    public InventoryDragController(
        InventoryGrid        grid,
        InventoryHighlighter highlighter,
        Canvas               canvas,
        RectTransform        itemsLayer,
        RectTransform        dragLayer,
        float                cellSize)
    {
        _grid        = grid;
        _highlighter = highlighter;
        _canvas      = canvas;
        _itemsLayer  = itemsLayer;
        _dragLayer   = dragLayer;
        _cellSize    = cellSize;
    }

    // ── Drag events ───────────────────────────────────────────────────────

    /// <summary>Called by InventoryItemView.OnBeginDrag.</summary>
    public void OnBeginDrag(InventoryItemView view, PointerEventData e)
    {
        _draggedView       = view;
        _dragOriginPos     = view.Item.gridPosition;
        _dragOriginRotated = view.Item.isRotated;

        _grid.Remove(view.Item);
        view.SetDragging(true);
        view.transform.SetParent(_dragLayer, true);
        // Shift pivot to center so the item follows the cursor centered on the item body.
        // Must happen after SetParent so world corners are in the correct canvas space.
        view.CenterPivotForDrag();
    }

    /// <summary>Called by InventoryItemView.OnDrag.</summary>
    public void OnDrag(InventoryItemView view, PointerEventData e)
    {
        Camera uiCam = GetUICamera();

        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                _dragLayer, e.position, uiCam, out Vector3 worldPos))
            view.transform.position = worldPos;

        // Keep the 3D model positioned at the dragged view's current location every frame.
        // LateUpdate only refreshes views in _views, but the dragged view has been removed from
        // the source grid so its model would otherwise freeze at the last placed position.
        view.PlaceModel(forceCanvasUpdate: true);

        Vector2Int? cell = ScreenToCell(e.position);
        if (cell.HasValue)
        {
            Vector2Int snapPos = ClampToGrid(
                cell.Value - new Vector2Int(view.Item.CurrentSize.x / 2,
                                           view.Item.CurrentSize.y / 2),
                view.Item.CurrentSize);
            _highlighter.HighlightCells(view.Item, snapPos);
            // Cursor is over this grid — clear any highlight on other panels.
            TryCrossGridHighlight?.Invoke(null, e.position);
        }
        else
        {
            _highlighter.ClearHighlight();
            // Cursor is outside this grid — let another open panel show its highlight.
            TryCrossGridHighlight?.Invoke(view.Item, e.position);
        }
    }

    /// <summary>Called by InventoryItemView.OnEndDrag.</summary>
    public void OnEndDrag(InventoryItemView view, PointerEventData e)
    {
        _highlighter.ClearHighlight();
        // Clear any highlight that was showing on a cross-grid panel during the drag.
        TryCrossGridHighlight?.Invoke(null, e.position);

        Vector2Int? cell = ScreenToCell(e.position);
        if (cell.HasValue)
        {
            Vector2Int snapPos = ClampToGrid(
                cell.Value - new Vector2Int(view.Item.CurrentSize.x / 2,
                                           view.Item.CurrentSize.y / 2),
                view.Item.CurrentSize);

            // Check if dropping on top of another item
            ItemInstance target = _grid.GetAt(cell.Value);
            if (target != null && OnDroppedOnItem != null)
            {
                var result = OnDroppedOnItem(view.Item, target);
                if (result != DragInteractionResult.NotHandled)
                {
                    if (result == DragInteractionResult.HandledConsumeDragged)
                    {
                        // InventoryUI already removed the item from its view dictionary;
                        // destroy the dragged GameObject here.
                        view.transform.SetParent(_itemsLayer, true);
                        view.SetDragging(false);
                        UnityEngine.Object.Destroy(view.gameObject);
                    }
                    else // HandledReturnDragged
                    {
                        view.Item.isRotated = _dragOriginRotated;
                        _grid.TryPlace(view.Item, _dragOriginPos);
                        view.transform.SetParent(_itemsLayer, true);
                        view.SetDragging(false);
                        view.RefreshLayout(_cellSize);
                    }
                    _draggedView = null;
                    return;
                }
            }

            if (_grid.TryPlace(view.Item, snapPos))
            {
                view.transform.SetParent(_itemsLayer, true);
                view.SetDragging(false);
                view.RefreshLayout(_cellSize);
                _draggedView = null;
                return;
            }
        }

        // Couldn't place in this grid — offer the item to another open grid (e.g. the stash).
        // On success the target panel reparents the view and takes ownership, so we must not
        // restore it to our origin or touch it further.
        if (TryCrossGridDrop != null && TryCrossGridDrop(view, e.position))
        {
            _draggedView = null;
            return;
        }

        // Nothing accepted it — restore to origin in this grid.
        view.Item.isRotated = _dragOriginRotated;
        _grid.TryPlace(view.Item, _dragOriginPos);
        view.transform.SetParent(_itemsLayer, true);
        view.SetDragging(false);
        view.RefreshLayout(_cellSize);
        _draggedView = null;
    }

    /// <summary>
    /// Returns the grid cell under <paramref name="screenPos"/> in this panel's coordinate space.
    /// Used by other panels to perform highlight mapping without duplicating coordinate logic.
    /// </summary>
    public bool TryGetCellUnderCursor(Vector2 screenPos, out Vector2Int cell)
    {
        var result = ScreenToCell(screenPos);
        cell = result ?? default;
        return result.HasValue;
    }

    /// <summary>
    /// Places <paramref name="item"/> into THIS grid at the cell under <paramref name="screenPos"/>,
    /// if it fits. Used when an item is dragged in from another InventoryUI. The item must already
    /// be removed from its source grid (it is, once a drag begins). Returns false if the cursor is
    /// outside this grid or the target cells are occupied.
    /// </summary>
    public bool TryPlaceExternal(ItemInstance item, Vector2 screenPos)
    {
        Vector2Int? cell = ScreenToCell(screenPos);
        if (!cell.HasValue) return false;

        Vector2Int snapPos = ClampToGrid(
            cell.Value - new Vector2Int(item.CurrentSize.x / 2, item.CurrentSize.y / 2),
            item.CurrentSize);

        return _grid.TryPlace(item, snapPos);
    }

    /// <summary>
    /// Rotates the currently dragged item in place (called from InventoryInputHandler
    /// while dragging). Does NOT go through the grid because the item was already removed.
    /// </summary>
    public void RotateDragged()
    {
        if (_draggedView == null) return;
        _draggedView.Item.isRotated = !_draggedView.Item.isRotated;
        // RefreshDraggedRotation updates size + model only — does not reset anchoredPosition.
        // RefreshLayout would snap the view back to its grid origin coords.
        _draggedView.RefreshDraggedRotation(_cellSize);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private Camera GetUICamera() =>
        _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? _canvas.worldCamera
            : null;

    private Vector2Int? ScreenToCell(Vector2 screenPos)
    {
        Camera cam = GetUICamera();
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _itemsLayer, screenPos, cam, out Vector2 local))
            return null;

        // _itemsLayer anchored top-left; Y increases downward in grid but upward in Unity local space.
        int x = Mathf.FloorToInt(local.x / _cellSize);
        int y = Mathf.FloorToInt(-local.y / _cellSize);

        return _grid.IsInBounds(x, y) ? new Vector2Int(x, y) : (Vector2Int?)null;
    }

    private Vector2Int ClampToGrid(Vector2Int pos, Vector2Int itemSize) =>
        new Vector2Int(
            Mathf.Clamp(pos.x, 0, _grid.Width  - itemSize.x),
            Mathf.Clamp(pos.y, 0, _grid.Height - itemSize.y));
}
