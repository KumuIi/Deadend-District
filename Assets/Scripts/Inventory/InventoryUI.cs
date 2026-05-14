using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Builds and manages the visual inventory grid.
///
/// Responsibilities (only):
///   - Grid data ownership
///   - UI layer construction (cells, items layer, drag layer)
///   - View spawning and destruction
///   - Public API surface (TryPickup, RemoveItem, PlaceItemAt, SetOpen, GetSaveData)
///   - Wiring InventoryDragController and InventoryHighlighter together
///
/// Input is handled by InventoryInputHandler (sibling component).
///
/// Scene setup:
///   1. Add this component to a child RectTransform inside a Canvas.
///   2. Assign Canvas to Screen Space – Camera mode for correct 3D tilt.
///   3. Optionally assign cellPrefab / itemViewPrefab, or leave null to auto-generate.
///   4. Add InventoryInputHandler to the same GameObject for keyboard control.
/// </summary>
public sealed class InventoryUI : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────

    [Header("=== Grid ===")]
    public int   gridWidth  = 5;
    public int   gridHeight = 10;
    public float cellSize   = 64f;

    [Header("=== Colours ===")]
    [Tooltip("Keep alpha low (0.15–0.3) so 3D models are visible behind the cells.")]
    public Color cellNormal    = new Color(0.18f, 0.18f, 0.24f, 0.20f);
    public Color cellHighlight = new Color(0.15f, 0.65f, 0.15f, 0.70f);
    public Color cellBlocked   = new Color(0.65f, 0.10f, 0.10f, 0.70f);

    [Header("=== Position ===")]
    [Tooltip("Pixel gap between the panel's top-right corner and the screen's top-right corner.")]
    public float paddingRight = 20f;
    [Tooltip("Pixel gap from the top of the screen.")]
    public float paddingTop   = 20f;

    [Header("=== 3D Tilt ===")]
    [Tooltip("X rotation in degrees. Positive swings the bottom toward the viewer.")]
    public float tiltX = 35f;
    [Tooltip("Y rotation in degrees. Negative swings the left edge away.")]
    public float tiltY = -8f;

    [Header("=== 3D Models ===")]
    [Tooltip("Layer used exclusively for inventory models. Create a layer named 'InventoryItems'.")]
    public int modelLayer = 31;

    [Header("=== Prefabs (optional) ===")]
    [Tooltip("Simple Image prefab for grid cells. Leave null to auto-create.")]
    public GameObject cellPrefab;
    [Tooltip("RawImage + CanvasGroup + InventoryItemView prefab. Leave null to auto-create.")]
    public GameObject itemViewPrefab;

    // ── Public data ───────────────────────────────────────────────────────

    /// <summary>The underlying data model for the grid.</summary>
    public InventoryGrid Grid { get; private set; }

    /// <summary>True when the inventory panel is currently visible.</summary>
    public bool IsOpen => _panel != null && _panel.gameObject.activeSelf;

    // ── Private runtime ───────────────────────────────────────────────────

    private Image[]       _cellImages;
    private RectTransform _panel;
    private RectTransform _itemsLayer;
    private RectTransform _dragLayer;
    private Canvas        _canvas;

    private InventoryDragController _drag;
    private InventoryHighlighter    _highlighter;
    private InventoryItemView       _hoveredView;

    /// <summary>Read-only access to spawned views — used by InventoryOrientationTester.</summary>
    public IReadOnlyDictionary<ItemInstance, InventoryItemView> Views => _views;

    private readonly Dictionary<ItemInstance, InventoryItemView> _views
        = new Dictionary<ItemInstance, InventoryItemView>();

    // ── Lifecycle ─────────────────────────────────────────────────────────

    private void Awake()
    {
        // Remove Unity's default Panel Image so the background is clean
        var defaultImage = GetComponent<Image>();
        if (defaultImage) Destroy(defaultImage);

        _canvas = GetComponentInParent<Canvas>();

        if (_canvas == null)
            Debug.LogError($"[InventoryUI] No Canvas found in parents of '{gameObject.name}'. " +
                           "This component must be a child of a Canvas.");

        if (_canvas != null && !_canvas.GetComponent<GraphicRaycaster>())
            Debug.LogWarning("[InventoryUI] Canvas is missing a GraphicRaycaster — drag/click won't work.");

        if (FindObjectOfType<EventSystem>() == null)
            Debug.LogWarning("[InventoryUI] No EventSystem in scene — drag/click won't work.");

        if (_canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            Debug.LogWarning("[InventoryUI] Canvas is Screen Space Overlay — " +
                             "the 3D tilt effect won't look correct. Use Screen Space – Camera.");

        Grid = new InventoryGrid(gridWidth, gridHeight);

        SetupRootTransform();
        BuildPanel(GetComponent<RectTransform>());

        _panel.localEulerAngles = new Vector3(tiltX, tiltY, 0f);
        _panel.gameObject.SetActive(false);

        _highlighter = new InventoryHighlighter(
            _cellImages, gridWidth, Grid, cellNormal, cellHighlight, cellBlocked);

        _drag = new InventoryDragController(
            Grid, _highlighter, _canvas, _itemsLayer, _dragLayer, cellSize);
    }

    private void Start()
    {
        var mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogWarning("[InventoryUI] No Camera.main — tag your player camera 'MainCamera'.");
            return;
        }
        mainCam.cullingMask |= 1 << modelLayer;

        if (_canvas != null &&
            _canvas.renderMode == RenderMode.ScreenSpaceCamera &&
            _canvas.worldCamera == null)
            Debug.LogWarning("[InventoryUI] Canvas Render Camera is not assigned — " +
                             "set it to your player camera in the Canvas Inspector.");
    }

    private void OnDestroy()
    {
        if (IsOpen) GameInputState.Unblock();
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Primary pickup entry point. Tries the item's natural orientation first,
    /// then rotated 90°, to maximise the chance of finding a slot.
    /// </summary>
    public PickupResult TryPickup(ItemInstance item)
    {
        if (item == null || _drag.IsDragging) return PickupResult.NoSpace;

        Vector2Int? pos = Grid.FindFreeSpace(item);
        if (pos.HasValue) { PlaceItemAt(item, pos.Value); return PickupResult.Placed; }

        item.isRotated = !item.isRotated;
        pos = Grid.FindFreeSpace(item);
        if (pos.HasValue) { PlaceItemAt(item, pos.Value); return PickupResult.Placed; }

        item.isRotated = !item.isRotated; // restore
        return PickupResult.NoSpace;
    }

    /// <summary>Places an item at a specific grid position and spawns its view.</summary>
    public bool PlaceItemAt(ItemInstance item, Vector2Int pos)
    {
        if (!Grid.TryPlace(item, pos)) return false;
        SpawnView(item);
        return true;
    }

    /// <summary>Removes an item from the grid and destroys its view.</summary>
    public void RemoveItem(ItemInstance item)
    {
        Grid.Remove(item);
        if (_views.TryGetValue(item, out var view))
        {
            _views.Remove(item);
            Destroy(view.gameObject);
        }
    }

    /// <summary>Opens or closes the inventory panel.</summary>
    public void SetOpen(bool open)
    {
        _panel.gameObject.SetActive(open);

        foreach (var view in _views.Values)
        {
            if (open) view.PlaceModel();
            view.SetModelVisible(open);
        }

        if (open) GameInputState.Block();
        else      GameInputState.Unblock();
    }

    /// <summary>
    /// Rotates the dragged item (if dragging) or the hovered item (if hovering).
    /// Called by InventoryInputHandler on the rotate key.
    /// </summary>
    public void RequestRotate()
    {
        if (_drag.IsDragging)
            _drag.RotateDragged();
        else if (_hoveredView != null)
            OnItemRotate(_hoveredView);
    }

    /// <summary>Convenience wrapper for InventoryGrid.GetSaveData().</summary>
    public System.Collections.Generic.List<InventoryGrid.GridSaveEntry> GetSaveData() =>
        Grid.GetSaveData();

    // ── Internal callbacks (called by InventoryItemView) ──────────────────

    public void OnItemBeginDrag(InventoryItemView view, PointerEventData e) =>
        _drag.OnBeginDrag(view, e);

    public void OnItemDrag(InventoryItemView view, PointerEventData e) =>
        _drag.OnDrag(view, e);

    public void OnItemEndDrag(InventoryItemView view, PointerEventData e) =>
        _drag.OnEndDrag(view, e);

    public void OnItemRotate(InventoryItemView view)
    {
        Vector2Int savedPos = view.Item.gridPosition;
        bool       savedRot = view.Item.isRotated;

        Grid.Remove(view.Item);
        view.Item.isRotated = !savedRot;

        if (!Grid.TryPlace(view.Item, savedPos))
        {
            view.Item.isRotated = savedRot;
            Grid.TryPlace(view.Item, savedPos);
        }

        view.RefreshLayout(cellSize);
    }

    public void SetHovered(InventoryItemView view) => _hoveredView = view;

    // ── View management ───────────────────────────────────────────────────

    private void SpawnView(ItemInstance item)
    {
        GameObject go = itemViewPrefab
            ? Instantiate(itemViewPrefab, _itemsLayer)
            : new GameObject(item.data.itemName,
                             typeof(RectTransform),
                             typeof(RawImage),
                             typeof(CanvasGroup),
                             typeof(InventoryItemView));

        go.transform.SetParent(_itemsLayer, false);

        // Ensure required components in case a custom prefab is missing any
        if (!go.GetComponent<RawImage>())      go.AddComponent<RawImage>();
        if (!go.GetComponent<CanvasGroup>())   go.AddComponent<CanvasGroup>();
        if (!go.GetComponent<InventoryItemView>()) go.AddComponent<InventoryItemView>();

        var rt = go.GetComponent<RectTransform>();
        AnchorTopLeft(rt);
        go.name = item.data.itemName;

        var view = go.GetComponent<InventoryItemView>();
        view.Initialize(item, this, modelLayer, cellSize);
        view.SetModelVisible(IsOpen);

        _views[item] = view;
    }

    // ── UI construction ───────────────────────────────────────────────────

    private void SetupRootTransform()
    {
        var rt = GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(1f, 1f);
        rt.anchorMax        = new Vector2(1f, 1f);
        rt.pivot            = new Vector2(1f, 1f);
        rt.sizeDelta        = new Vector2(gridWidth * cellSize, gridHeight * cellSize);
        rt.anchoredPosition = new Vector2(-paddingRight, -paddingTop);
    }

    private void BuildPanel(RectTransform root)
    {
        float gridPixelW = gridWidth  * cellSize;
        float gridPixelH = gridHeight * cellSize;

        // Outer panel — toggled and tilted; pivot top-right so rotation hangs from that corner
        _panel                    = NewChild(root, "InventoryPanel");
        _panel.anchorMin          = new Vector2(1f, 1f);
        _panel.anchorMax          = new Vector2(1f, 1f);
        _panel.pivot              = new Vector2(1f, 1f);
        _panel.anchoredPosition   = Vector2.zero;
        _panel.sizeDelta          = new Vector2(gridPixelW, gridPixelH);
        _panel.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

        BuildLayers(_panel, gridPixelW, gridPixelH);
    }

    private void BuildLayers(RectTransform root, float gridPixelW, float gridPixelH)
    {
        // ── Cells ────────────────────────────────────────────────────────
        var cellsRT = NewChild(root, "CellsLayer");
        StretchFill(cellsRT);

        var glg = cellsRT.gameObject.AddComponent<GridLayoutGroup>();
        glg.cellSize       = new Vector2(cellSize - 1f, cellSize - 1f);
        glg.spacing        = Vector2.one;
        glg.startCorner    = GridLayoutGroup.Corner.UpperLeft;
        glg.startAxis      = GridLayoutGroup.Axis.Horizontal;
        glg.childAlignment = TextAnchor.UpperLeft;
        glg.constraint     = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = gridWidth;

        int total    = gridWidth * gridHeight;
        _cellImages  = new Image[total];
        for (int i = 0; i < total; i++)
        {
            GameObject cellGO = cellPrefab
                ? Instantiate(cellPrefab, cellsRT)
                : new GameObject($"Cell_{i}", typeof(Image));
            cellGO.transform.SetParent(cellsRT, false);

            var img = cellGO.GetComponent<Image>() ?? cellGO.AddComponent<Image>();
            img.color    = cellNormal;
            _cellImages[i] = img;
        }

        // ── Items layer (absolutely positioned over cells) ────────────────
        _itemsLayer          = NewChild(root, "ItemsLayer");
        AnchorTopLeft(_itemsLayer);
        _itemsLayer.sizeDelta = new Vector2(gridPixelW, gridPixelH);

        // ── Drag layer (topmost) ──────────────────────────────────────────
        _dragLayer = NewChild(root, "DragLayer");
        StretchFill(_dragLayer);
    }

    // ── Layout helpers ────────────────────────────────────────────────────

    private static RectTransform NewChild(RectTransform parent, string n)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    private static void StretchFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    private static void AnchorTopLeft(RectTransform rt)
    {
        rt.anchorMin          = new Vector2(0f, 1f);
        rt.anchorMax          = new Vector2(0f, 1f);
        rt.pivot              = new Vector2(0f, 1f);
        rt.anchoredPosition   = Vector2.zero;
    }
}

/// <summary>Result of a TryPickup call.</summary>
public enum PickupResult { Placed, NoSpace }
