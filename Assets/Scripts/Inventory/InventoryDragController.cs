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
    }

    /// <summary>Called by InventoryItemView.OnDrag.</summary>
    public void OnDrag(InventoryItemView view, PointerEventData e)
    {
        Camera uiCam = GetUICamera();

        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                _dragLayer, e.position, uiCam, out Vector3 worldPos))
            view.transform.position = worldPos;

        Vector2Int? cell = ScreenToCell(e.position);
        if (cell.HasValue)
        {
            Vector2Int snapPos = ClampToGrid(
                cell.Value - new Vector2Int(view.Item.CurrentSize.x / 2,
                                           view.Item.CurrentSize.y / 2),
                view.Item.CurrentSize);
            _highlighter.HighlightCells(view.Item, snapPos);
        }
        else
        {
            _highlighter.ClearHighlight();
        }
    }

    /// <summary>Called by InventoryItemView.OnEndDrag.</summary>
    public void OnEndDrag(InventoryItemView view, PointerEventData e)
    {
        _highlighter.ClearHighlight();

        bool placed = false;
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

            placed = _grid.TryPlace(view.Item, snapPos);
        }

        if (!placed)
        {
            // Restore to origin
            view.Item.isRotated = _dragOriginRotated;
            _grid.TryPlace(view.Item, _dragOriginPos);
        }

        view.transform.SetParent(_itemsLayer, true);
        view.SetDragging(false);
        view.RefreshLayout(_cellSize);
        _draggedView = null;
    }

    /// <summary>
    /// Rotates the currently dragged item in place (called from InventoryInputHandler
    /// while dragging). Does NOT go through the grid because the item was already removed.
    /// </summary>
    public void RotateDragged()
    {
        if (_draggedView == null) return;
        _draggedView.Item.isRotated = !_draggedView.Item.isRotated;
        _draggedView.RefreshLayout(_cellSize);
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
