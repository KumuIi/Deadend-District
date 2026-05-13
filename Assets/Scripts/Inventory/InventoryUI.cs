using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Builds and manages the visual inventory grid.
///
/// Scene setup:
///   1. Add this component to a child RectTransform inside your UI Canvas.
///   2. Anchor it to the right edge in the Inspector (anchorMin/Max = right).
///   3. Assign cellPrefab (a simple Image prefab) and itemViewPrefab, or leave null
///      to auto-generate plain Images/RawImages at runtime.
///   4. Toggle visibility via the openKey (default: Tab).
///
/// Runtime hierarchy created under this RectTransform:
///   CellsLayer  — GridLayoutGroup of cell Images
///   ItemsLayer  — free-positioned InventoryItemViews
///   DragLayer   — top-most layer; item lives here while being dragged
/// </summary>
public class InventoryUI : MonoBehaviour
{
    [Header("=== Grid ===")]
    public int   gridWidth  = 5;
    public int   gridHeight = 10;
    public float cellSize   = 64f;

    [Header("=== Colours ===")]
    public Color cellNormal    = new Color(0.18f, 0.18f, 0.24f, 1f);
    public Color cellHighlight = new Color(0.15f, 0.65f, 0.15f, 1f);
    public Color cellBlocked   = new Color(0.65f, 0.1f,  0.1f,  1f);

    [Header("=== Position ===")]
    [Tooltip("Pixel gap between the panel's top-right corner and the screen's top-right corner")]
    public float paddingRight = 20f;
    [Tooltip("Pixel gap from the top of the screen")]
    public float paddingTop   = 20f;

    [Header("=== 3D Tilt ===")]
    [Tooltip("X rotation in degrees — positive swings the bottom toward the viewer (hanging/lying effect). " +
             "Pivot is the top-right corner, so the panel hangs from there. " +
             "For true perspective warp, switch your Canvas to Screen Space – Camera mode.")]
    public float tiltX = 35f;
    [Tooltip("Y rotation in degrees — negative swings the left edge away (slight side angle)")]
    public float tiltY = -8f;

    [Header("=== 3D Models ===")]
    [Tooltip("Layer index for inventory item models. Create a layer named 'InventoryItems' in " +
             "Project Settings → Tags and Layers, then enter its index here.")]
    public int modelLayer = 31;

    [Header("=== Prefabs (optional) ===")]
    [Tooltip("Simple Image prefab for grid cells. Leave null to auto-create.")]
    public GameObject cellPrefab;
    [Tooltip("RawImage+CanvasGroup+InventoryItemView prefab. Leave null to auto-create.")]
    public GameObject itemViewPrefab;

    // ── Public data ───────────────────────────────────────────────────────
    public InventoryGrid Grid { get; private set; }

    // ── Private runtime ───────────────────────────────────────────────────
    private Image[]       _cellImages;
    private RectTransform _panel;       // toggled on/off — NOT this.gameObject
    private RectTransform _itemsLayer;
    private RectTransform _dragLayer;
    private Camera        _overlayCamera; // depth-only camera that draws 3D models on top
    private Canvas        _canvas;        // cached root canvas

    private InventoryItemView _draggedView;
    private InventoryItemView _hoveredView;
    private Vector2Int        _dragOriginPos;
    private bool              _dragOriginRotated;
    private Vector2Int[]      _highlighted = System.Array.Empty<Vector2Int>();

    private readonly Dictionary<ItemInstance, InventoryItemView> _views
        = new Dictionary<ItemInstance, InventoryItemView>();

    // ── Lifecycle ─────────────────────────────────────────────────────────

    void Awake()
    {
        // Unity Panels come with a default semi-transparent Image — remove it so only
        // _panel's background is visible (and only when the inventory is open).
        var defaultImage = GetComponent<Image>();
        if (defaultImage) Destroy(defaultImage);

        _canvas = GetComponentInParent<Canvas>();

        // Scene-setup guards: drag/click requires both of these in the scene.
        if (_canvas != null && !_canvas.GetComponent<GraphicRaycaster>())
            Debug.LogWarning("InventoryUI: Canvas is missing a GraphicRaycaster — drag/click won't work.");
        if (!FindObjectOfType<EventSystem>())
            Debug.LogWarning("InventoryUI: No EventSystem in scene — drag/click won't work. Add one via GameObject > UI > Event System.");

        // 3D model overlay requires the canvas to be in Screen Space – Camera mode.
        // In Screen Space Overlay the canvas renders AFTER every camera, so world-space
        // models are always hidden behind it. Switch Canvas > Render Mode to Screen Space – Camera.
        if (_canvas != null && _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            Debug.LogError("InventoryUI: Canvas is Screen Space Overlay — 3D item models will be invisible. " +
                           "Change the Canvas Render Mode to Screen Space – Camera and assign your player camera.");

        Grid = new InventoryGrid(gridWidth, gridHeight);

        SetupRootTransform();
        BuildPanel(GetComponent<RectTransform>());
        CreateOverlayCamera();

        _panel.localEulerAngles = new Vector3(tiltX, tiltY, 0f);
        _panel.gameObject.SetActive(false); // hide panel, NOT this GO — Update() must keep running
    }

    void CreateOverlayCamera()
    {
        if (modelLayer < 0 || modelLayer > 31)
        {
            Debug.LogWarning("InventoryUI: modelLayer must be 0-31. 3D item models won't render.");
            return;
        }

        // Not parented to the canvas — must live in scene world space so its transform
        // can be synced to Camera.main each frame. Parenting to a RectTransform puts it
        // at a corner of the panel, looking the wrong direction.
        var go = new GameObject("InventoryModelOverlay");
        _overlayCamera = go.AddComponent<Camera>();
        _overlayCamera.cullingMask = 1 << modelLayer;
        _overlayCamera.enabled     = false;

        if (!TryConfigureAsURPOverlay())
        {
            // Built-In render pipeline: depth-only clear composites on top of everything.
            _overlayCamera.clearFlags = CameraClearFlags.Depth;
            _overlayCamera.depth      = 50;
        }
    }

    // ── URP overlay camera wiring ─────────────────────────────────────────
    // Stored so SetOpen can add/remove the overlay from the URP camera stack.
    private System.Type   _urpDataType;    // UniversalAdditionalCameraData type, null = Built-In
    private System.Object _urpMainData;   // main camera's URP data component

    /// <summary>
    /// Scans all loaded assemblies for URP types (no hard package dependency).
    /// Configures the overlay camera as CameraRenderType.Overlay and suppresses
    /// post-processing so it never re-blurs the scene.
    /// Returns true if URP was found and configured; false = Built-In pipeline.
    /// </summary>
    bool TryConfigureAsURPOverlay()
    {
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
            return false;

        // Scan all assemblies — avoids fragile hardcoded assembly name
        System.Type dataType = null;
        foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            dataType = asm.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
            if (dataType != null) break;
        }
        if (dataType == null) return false;

        var overlayData = _overlayCamera.GetComponent(dataType)
                       ?? _overlayCamera.gameObject.AddComponent(dataType);

        // renderType must be set as the actual enum value, not a bare int
        var renderTypeProp = dataType.GetProperty("renderType");
        if (renderTypeProp != null)
            renderTypeProp.SetValue(overlayData,
                System.Enum.ToObject(renderTypeProp.PropertyType, 1)); // 1 = Overlay

        dataType.GetProperty("renderPostProcessing")?.SetValue(overlayData, false);

        _urpDataType = dataType;
        return true;
    }

    void Start()
    {
        var mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogWarning("InventoryUI: No Camera.main — tag your player camera MainCamera.");
            return;
        }

        // Hide modelLayer from the main camera so only the overlay camera sees the models
        mainCam.cullingMask &= ~(1 << modelLayer);

        if (_urpDataType != null)
            _urpMainData = mainCam.GetComponent(_urpDataType);
    }

    void OnDestroy()
    {
        if (_panel != null && _panel.gameObject.activeSelf)
            GameInputState.Unblock();

        if (_overlayCamera != null)
        {
            RemoveFromURPStack();
            Destroy(_overlayCamera.gameObject);
        }
    }

    // Adds/removes the overlay camera from the URP stack and enables/disables it.
    // In URP, add+enable on open and remove+disable on close is the safest cross-version approach.
    void SetURPStackActive(bool active)
    {
        if (_urpMainData == null) return;
        var stack = _urpDataType.GetProperty("cameraStack")?.GetValue(_urpMainData)
                    as System.Collections.IList;
        if (stack == null) return;

        if (active && !stack.Contains(_overlayCamera))
            stack.Add(_overlayCamera);
        else if (!active)
            stack.Remove(_overlayCamera);

        _overlayCamera.enabled = active;
    }

    void RemoveFromURPStack()
    {
        if (_urpDataType == null || _urpMainData == null) return;
        var stack = _urpDataType.GetProperty("cameraStack")?.GetValue(_urpMainData)
                    as System.Collections.IList;
        stack?.Remove(_overlayCamera);
    }

    /// <summary>
    /// Anchors this rect to the top-right corner of the Canvas.
    /// The invisible root stays flat; only _panel gets the 3D tilt.
    /// </summary>
    void SetupRootTransform()
    {
        var rt       = GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 1f);   // top-right of Canvas
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot     = new Vector2(1f, 1f);   // position/size from top-right corner
        rt.sizeDelta = new Vector2(gridWidth * cellSize, gridHeight * cellSize);
        // Move inward and downward from the top-right screen corner
        rt.anchoredPosition = new Vector2(-paddingRight, -paddingTop);
    }

    [Header("=== Controls ===")]
    public KeyCode openKey   = KeyCode.Tab;
    public KeyCode rotateKey = KeyCode.R;

    void Update()
    {
        // This only works because we toggle _panel, not this.gameObject.
        if (Input.GetKeyDown(openKey))
            SetOpen(!_panel.gameObject.activeSelf);

        // R rotates the dragged item (if dragging) or the hovered item
        if (Input.GetKeyDown(rotateKey))
        {
            if (_draggedView != null)
                RotateDuringDrag();
            else if (_hoveredView != null)
                OnItemRotate(_hoveredView);
        }
    }

    void RotateDuringDrag()
    {
        _draggedView.Item.isRotated = !_draggedView.Item.isRotated;
        _draggedView.RefreshLayout(cellSize);
    }

    public void SetHovered(InventoryItemView view) => _hoveredView = view;

    // Overlay camera must match the scene camera every frame so models project onto
    // their correct screen positions and the "facing north" rotation issue is avoided.
    void LateUpdate()
    {
        if (_overlayCamera == null || !_overlayCamera.enabled) return;
        Camera src = GetRenderCamera();
        if (src == null) return;
        _overlayCamera.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
        _overlayCamera.fieldOfView    = src.fieldOfView;
        _overlayCamera.nearClipPlane  = src.nearClipPlane;
        _overlayCamera.farClipPlane   = src.farClipPlane;
        _overlayCamera.aspect         = src.aspect;
    }

    // Use the canvas's assigned render camera; fall back to Camera.main.
    Camera GetRenderCamera()
    {
        if (_canvas != null && _canvas.worldCamera != null) return _canvas.worldCamera;
        return Camera.main;
    }

    void SetOpen(bool open)
    {
        _panel.gameObject.SetActive(open);

        if (_overlayCamera)
        {
            if (_urpDataType != null) SetURPStackActive(open);
            else                      _overlayCamera.enabled = open;
        }

        // Show/hide 3D models — they live in world space so the camera alone isn't enough
        foreach (var view in _views.Values)
            view.SetModelVisible(open);

        if (open) GameInputState.Block();
        else      GameInputState.Unblock();
    }

    // ── UI construction ───────────────────────────────────────────────────

    void BuildPanel(RectTransform root)
    {
        float gridPixelW = gridWidth  * cellSize;
        float gridPixelH = gridHeight * cellSize;

        // ── Outer panel (toggled + tilted) ────────────────────────────────
        // Pivot at top-right (1,1) = the rotation hangs the panel from that corner.
        // Anchor also at top-right of root so it sits flush with the root's corner.
        _panel              = NewChild(root, "InventoryPanel");
        _panel.anchorMin    = new Vector2(1f, 1f);
        _panel.anchorMax    = new Vector2(1f, 1f);
        _panel.pivot        = new Vector2(1f, 1f);
        _panel.anchoredPosition = Vector2.zero;
        _panel.sizeDelta    = new Vector2(gridPixelW, gridPixelH);

        // Dark background
        var bg   = _panel.gameObject.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.08f, 0.95f);

        BuildLayers(_panel, gridPixelW, gridPixelH);
    }

    void BuildLayers(RectTransform root, float gridPixelW, float gridPixelH)
    {
        // ── Cells ─────────────────────────────────────────────────────────
        var cellsGO = NewChild<GridLayoutGroup>(root, "CellsLayer");
        StretchFill(cellsGO);
        var glg = cellsGO.GetComponent<GridLayoutGroup>();
        glg.cellSize        = new Vector2(cellSize - 1f, cellSize - 1f);
        glg.spacing         = Vector2.one;
        glg.startCorner     = GridLayoutGroup.Corner.UpperLeft;
        glg.startAxis       = GridLayoutGroup.Axis.Horizontal;
        glg.childAlignment  = TextAnchor.UpperLeft;
        glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = gridWidth;

        int total = gridWidth * gridHeight;
        _cellImages = new Image[total];
        for (int i = 0; i < total; i++)
        {
            GameObject cellGO = cellPrefab
                ? Instantiate(cellPrefab, cellsGO)
                : new GameObject($"Cell_{i}", typeof(Image));
            cellGO.transform.SetParent(cellsGO, false);
            var img = cellGO.GetComponent<Image>() ?? cellGO.AddComponent<Image>();
            img.color = cellNormal;
            _cellImages[i] = img;
        }

        // ── Items (positioned absolutely on top of cells) ──────────────────
        var itemsGO = NewChild(root, "ItemsLayer");
        _itemsLayer = itemsGO;
        AnchorTopLeft(_itemsLayer);
        // Explicit pixel size — never rely on root.sizeDelta which is 0 under stretch anchors
        _itemsLayer.sizeDelta = new Vector2(gridPixelW, gridPixelH);

        // ── Drag layer (topmost) ───────────────────────────────────────────
        var dragGO = NewChild(root, "DragLayer");
        _dragLayer = dragGO;
        StretchFill(_dragLayer);
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Primary pickup entry point — call this from your interaction / loot system.
    /// Tries the item's current orientation first, then rotated, to maximise the
    /// chance of finding a slot. Returns NoSpace if neither fits.
    /// </summary>
    public bool IsDragging => _draggedView != null;

    public PickupResult TryPickup(ItemInstance item)
    {
        if (item == null || IsDragging) return PickupResult.NoSpace;

        // First try: natural orientation
        Vector2Int? pos = Grid.FindFreeSpace(item);
        if (pos.HasValue)
        {
            PlaceItemAt(item, pos.Value);
            return PickupResult.Placed;
        }

        // Second try: rotate 90° — a tall item might fit as a wide one
        item.isRotated = !item.isRotated;
        pos = Grid.FindFreeSpace(item);
        if (pos.HasValue)
        {
            PlaceItemAt(item, pos.Value);
            return PickupResult.Placed;
        }

        // Neither orientation fits — restore and report
        item.isRotated = !item.isRotated;
        return PickupResult.NoSpace;
    }

    /// <summary>Places an item at a specific position and creates its view.</summary>
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

    // ── View management ───────────────────────────────────────────────────

    void SpawnView(ItemInstance item)
    {
        GameObject go = itemViewPrefab
            ? Instantiate(itemViewPrefab, _itemsLayer)
            : new GameObject(item.data.itemName,
                typeof(RectTransform), typeof(RawImage),
                typeof(CanvasGroup), typeof(InventoryItemView));
        go.transform.SetParent(_itemsLayer, false);

        // Ensure required components
        if (!go.GetComponent<RawImage>())       go.AddComponent<RawImage>();
        if (!go.GetComponent<CanvasGroup>())    go.AddComponent<CanvasGroup>();
        if (!go.GetComponent<InventoryItemView>()) go.AddComponent<InventoryItemView>();

        var rt = go.GetComponent<RectTransform>();
        AnchorTopLeft(rt);
        go.name = item.data.itemName;

        var view = go.GetComponent<InventoryItemView>();
        view.Initialize(item, this, modelLayer, cellSize);
        // If inventory is open when an item is picked up, show model immediately
        view.SetModelVisible(_panel.gameObject.activeSelf);
        _views[item] = view;
    }

    // ── Drag callbacks (called by InventoryItemView) ───────────────────────

    public void OnItemBeginDrag(InventoryItemView view, PointerEventData e)
    {
        _draggedView       = view;
        _dragOriginPos     = view.Item.gridPosition;
        _dragOriginRotated = view.Item.isRotated;

        Grid.Remove(view.Item);
        view.SetDragging(true);
        view.transform.SetParent(_dragLayer, true); // lift to drag layer
    }

    public void OnItemDrag(InventoryItemView view, PointerEventData e)
    {
        // In Screen Space – Camera mode, e.position is screen-space but transform.position
        // is world-space; ScreenPointToWorldPointInRectangle does the correct projection.
        Camera uiCam = _canvas && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? _canvas.worldCamera : null;

        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                _dragLayer, e.position, uiCam, out Vector3 worldPos))
            view.transform.position = worldPos;

        // Highlight the cells the item would land on
        Vector2Int? cell = ScreenToCell(e.position);
        if (cell.HasValue)
        {
            Vector2Int size    = view.Item.CurrentSize;
            Vector2Int snapPos = ClampToGrid(cell.Value - new Vector2Int(size.x / 2, size.y / 2), size);
            HighlightCells(view.Item, snapPos);
        }
        else
        {
            ClearHighlight();
        }
    }

    public void OnItemEndDrag(InventoryItemView view, PointerEventData e)
    {
        ClearHighlight();

        bool placed = false;
        Vector2Int? cell = ScreenToCell(e.position);
        if (cell.HasValue)
        {
            Vector2Int size    = view.Item.CurrentSize;
            Vector2Int snapPos = ClampToGrid(cell.Value - new Vector2Int(size.x / 2, size.y / 2), size);
            placed = Grid.TryPlace(view.Item, snapPos);
        }

        if (!placed)
        {
            // Restore origin
            view.Item.isRotated = _dragOriginRotated;
            Grid.TryPlace(view.Item, _dragOriginPos);
        }

        view.transform.SetParent(_itemsLayer, true);
        view.SetDragging(false);
        view.RefreshLayout(cellSize);
        _draggedView = null;
    }

    public void OnItemRotate(InventoryItemView view)
    {
        Vector2Int savedPos = view.Item.gridPosition;
        bool       savedRot = view.Item.isRotated;

        Grid.Remove(view.Item);
        view.Item.isRotated = !savedRot;

        if (!Grid.TryPlace(view.Item, savedPos))
        {
            // Rotation doesn't fit — roll back
            view.Item.isRotated = savedRot;
            Grid.TryPlace(view.Item, savedPos);
        }

        view.RefreshLayout(cellSize);
    }

    // ── Cell highlighting ─────────────────────────────────────────────────

    void HighlightCells(ItemInstance item, Vector2Int topLeft)
    {
        ClearHighlight();
        Vector2Int size = item.CurrentSize;
        var cells       = new List<Vector2Int>(size.x * size.y);

        for (int y = topLeft.y; y < topLeft.y + size.y; y++)
        for (int x = topLeft.x; x < topLeft.x + size.x; x++)
        {
            if (!Grid.IsInBounds(x, y)) continue;
            cells.Add(new Vector2Int(x, y));

            var occupant = Grid.GetAt(x, y);
            bool blocked = occupant != null && occupant != item;
            _cellImages[y * gridWidth + x].color = blocked ? cellBlocked : cellHighlight;
        }

        _highlighted = cells.ToArray();
    }

    void ClearHighlight()
    {
        foreach (var c in _highlighted)
            if (Grid.IsInBounds(c.x, c.y))
                _cellImages[c.y * gridWidth + c.x].color = cellNormal;
        _highlighted = System.Array.Empty<Vector2Int>();
    }

    // ── Coordinate conversion ─────────────────────────────────────────────

    Vector2Int? ScreenToCell(Vector2 screenPos)
    {
        Camera cam = _canvas && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? _canvas.worldCamera : null;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _itemsLayer, screenPos, cam, out Vector2 local))
            return null;

        // _itemsLayer is anchored top-left; localPos (0,0) = top-left of grid.
        // Y increases downward in grid space but upward in Unity local space, hence -local.y.
        int x = Mathf.FloorToInt(local.x / cellSize);
        int y = Mathf.FloorToInt(-local.y / cellSize);

        return Grid.IsInBounds(x, y) ? new Vector2Int(x, y) : (Vector2Int?)null;
    }

    Vector2Int ClampToGrid(Vector2Int pos, Vector2Int itemSize) =>
        new Vector2Int(
            Mathf.Clamp(pos.x, 0, Grid.Width  - itemSize.x),
            Mathf.Clamp(pos.y, 0, Grid.Height - itemSize.y));

    // ── Layout helpers ────────────────────────────────────────────────────

    static RectTransform NewChild(RectTransform parent, string n)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go.GetComponent<RectTransform>();
    }

    static RectTransform NewChild<T>(RectTransform parent, string n) where T : Component
    {
        var rt = NewChild(parent, n);
        rt.gameObject.AddComponent<T>();
        return rt;
    }

    static void StretchFill(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void AnchorTopLeft(RectTransform rt)
    {
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(0, 1);
        rt.pivot     = new Vector2(0, 1); rt.anchoredPosition = Vector2.zero;
    }
}

public enum PickupResult { Placed, NoSpace }
