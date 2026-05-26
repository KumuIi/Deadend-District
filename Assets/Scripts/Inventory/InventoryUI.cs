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
[DefaultExecutionOrder(5)] // must run after CameraController (default order 0)
public sealed class InventoryUI : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────

    [Header("=== Grid ===")]
    public int   gridWidth  = 5;
    public int   gridHeight = 10;
    public float cellSize   = 64f;

    [Header("=== Weapon Integration ===")]
    [Tooltip("Assign WeaponManager so 'Equip' and 'Remove Magazine' context menu actions work.")]
    public WeaponManager weaponManager;
    [Tooltip("Assign FlashlightSlot on the Player so flashlight equip/unequip works.")]
    public FlashlightSlot flashlightSlot;

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

    [Header("=== Drop Settings ===")]
    [Tooltip("Origin transform for item drops (assign player camera). Falls back to Camera.main.")]
    [SerializeField] private Transform _dropOrigin;
    [Tooltip("Forward throw force in m/s.")]
    [SerializeField] private float _dropThrowForce = 5f;
    [Tooltip("Layer the dropped item's collider is placed on — must match PlayerInteractor's interaction mask. Default 6 = InteractI.")]
    [SerializeField] private int _droppedItemLayer = 6;

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
    private InventoryTooltip        _tooltip;
    private InventoryContextMenu    _contextMenu;

    /// <summary>
    /// The specific WeaponItemInstance whose state is currently loaded into the active GunController.
    /// C# reference equality (wi == _equippedItem) is all we need — each WeaponItemInstance
    /// is a unique heap object even when two items share the same WeaponSO.
    /// </summary>
    private WeaponItemInstance _equippedItem;

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

        if (weaponManager == null)
            Debug.LogError("[InventoryUI] WeaponManager is not assigned — weapon equip will not work.", this);
        if (flashlightSlot == null)
            Debug.LogError("[InventoryUI] FlashlightSlot is not assigned — flashlight equip will not work.", this);

        Grid = new InventoryGrid(gridWidth, gridHeight);

        SetupRootTransform();
        BuildPanel(GetComponent<RectTransform>());

        _panel.localEulerAngles = new Vector3(tiltX, tiltY, 0f);
        _panel.gameObject.SetActive(false);

        _highlighter = new InventoryHighlighter(
            _cellImages, gridWidth, Grid, cellNormal, cellHighlight, cellBlocked);

        _drag = new InventoryDragController(
            Grid, _highlighter, _canvas, _itemsLayer, _dragLayer, cellSize);

        _drag.OnDroppedOnItem = HandleDragInteraction;

        if (_canvas != null)
        {
            _tooltip     = new InventoryTooltip(_canvas);
            _contextMenu = new InventoryContextMenu(_canvas);

            _contextMenu.OnEquip          = ContextMenu_Equip;
            _contextMenu.OnUnequip        = ContextMenu_Unequip;
            _contextMenu.OnRemoveMagazine = ContextMenu_RemoveMagazine;
            _contextMenu.OnRemoveBattery  = ContextMenu_RemoveBattery;
            _contextMenu.OnDrop           = ContextMenu_Drop;

            // C# reference equality: each WeaponItemInstance is a unique object even
            // if two items share the same WeaponSO. No GUID needed for runtime checks.
            _contextMenu.IsItemEquipped = item =>
                (item is WeaponItemInstance wi && wi == _equippedItem) ||
                (item is FlashlightItemInstance && flashlightSlot != null && flashlightSlot.EquippedItem == item);

        }
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

        if (_dropOrigin == null)
            _dropOrigin = mainCam.transform;

        if (_canvas != null &&
            _canvas.renderMode == RenderMode.ScreenSpaceCamera &&
            _canvas.worldCamera == null)
            Debug.LogWarning("[InventoryUI] Canvas Render Camera is not assigned — " +
                             "set it to your player camera in the Canvas Inspector.");
    }

    private void Update()
    {
        // Keep tooltip position glued to the cursor while hovering
        if (_hoveredView != null && IsOpen)
            _tooltip?.UpdatePosition(Input.mousePosition);
    }

    private void LateUpdate()
    {
        // Re-place models every frame while open so they track the camera-driven canvas.
        // ForceUpdateCanvases called once here; PlaceModel skips it to avoid N canvas flushes.
        if (!IsOpen || _views.Count == 0) return;
        Canvas.ForceUpdateCanvases();
        foreach (var view in _views.Values)
            view.PlaceModel(forceCanvasUpdate: false);
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

        if (!open)
        {
            _tooltip?.Hide();
            _contextMenu?.Hide();
            _hoveredView = null;
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

    /// <summary>
    /// Removes all items and destroys all views. Call before loading from a save file.
    /// Does not close the panel.
    /// </summary>
    public void ClearAll()
    {
        foreach (var view in _views.Values)
            if (view != null) Destroy(view.gameObject);
        _views.Clear();
        foreach (var item in new List<ItemInstance>(Grid.PlacedItems))
            Grid.Remove(item);
    }

    /// <summary>
    /// Clears the grid, loads entries from save data, and spawns views for each placed item.
    /// Call on scene load after all SOs are available via <paramref name="resolver"/>.
    /// </summary>
    public void LoadFromSaveData(
        System.Collections.Generic.List<InventoryGrid.GridSaveEntry> entries,
        IItemSOResolver resolver)
    {
        ClearAll();
        Grid.LoadFromSaveData(entries, resolver);
        foreach (var item in Grid.PlacedItems)
            SpawnView(item);
    }

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

    public void SetHovered(InventoryItemView view)
    {
        _hoveredView = view;
        if (view != null)
            _tooltip?.Show(view.Item, Input.mousePosition);
        else
            _tooltip?.Hide();
    }

    /// <summary>Shows the context menu for the right-clicked item.</summary>
    public void OnItemRightClick(InventoryItemView view, UnityEngine.EventSystems.PointerEventData e)
    {
        _contextMenu?.Show(view.Item, e.position);
    }

    // ── Context menu actions ──────────────────────────────────────────────

    private void ContextMenu_Equip(ItemInstance item)
    {
        if (item is WeaponItemInstance wi)
        {
            if (weaponManager == null) return;
            if (weaponManager.EquipFromInventory(wi, HandleInventoryReload))
                _equippedItem = wi;
        }
        else if (item is FlashlightItemInstance fi)
        {
            if (flashlightSlot == null)
            {
                Debug.LogError("[InventoryUI] FlashlightSlot is not assigned — cannot equip flashlight.", this);
                return;
            }
            flashlightSlot.TryEquip(fi);
        }
    }

    private void ContextMenu_Unequip(ItemInstance item)
    {
        if (item is WeaponItemInstance)
        {
            if (weaponManager == null) return;
            _equippedItem = null;
            weaponManager.EquipNothing();
        }
        else if (item is FlashlightItemInstance)
        {
            if (flashlightSlot == null)
            {
                Debug.LogError("[InventoryUI] FlashlightSlot is not assigned — cannot unequip flashlight.", this);
                return;
            }
            flashlightSlot.Unequip();
        }
    }

    private void ContextMenu_RemoveBattery(ItemInstance item)
    {
        if (item is not FlashlightItemInstance fi) return;
        if (fi.InsertedBattery == null) return;

        // Preflight: check space before committing
        if (Grid.FindFreeSpace(fi.InsertedBattery) == null)
        {
            Debug.LogWarning("[InventoryUI] Remove Battery: no free space in inventory.");
            return;
        }

        BatteryItemInstance battery = fi.EjectBattery();
        TryPickup(battery);
        flashlightSlot?.OnBatteryLoaded(fi); // sync HUD after eject
    }

    private void ContextMenu_RemoveMagazine(ItemInstance item)
    {
        if (!(item is WeaponItemInstance wi)) return;
        MagazineItemInstance mag = wi.EjectMagazine();
        if (mag == null) return;

        // Only sync the live gun if this specific WeaponItemInstance is the active one.
        bool isEquipped = wi == _equippedItem;
        if (isEquipped) wi.LinkedGun.EjectMagazine();

        if (TryPickup(mag) == PickupResult.NoSpace)
        {
            // Inventory full — put the magazine back
            wi.LoadMagazine(mag);
            if (isEquipped) wi.LinkedGun.InsertMagazine(mag.RuntimeMag);
            Debug.LogWarning("[InventoryUI] Remove Magazine: no free space in inventory.");
        }
    }

    private void HandleInventoryReload(GunController gun)
    {
        // _equippedItem is the C# object whose state is currently in the GunController.
        WeaponItemInstance wi = _equippedItem;
        if (wi == null || wi.LinkedGun != gun) return;

        // Find the fullest compatible non-empty magazine in inventory
        MagazineItemInstance bestMag = null;
        foreach (var item in Grid.PlacedItems)
        {
            if (!(item is MagazineItemInstance mag)) continue;
            if (mag.MagDef.caliber != wi.WeaponDef.caliber) continue;
            if (mag.RuntimeMag.IsEmpty) continue;
            if (bestMag == null || mag.RuntimeMag.BulletCount > bestMag.RuntimeMag.BulletCount)
                bestMag = mag;
        }

        if (bestMag == null)
        {
            Debug.Log("[InventoryUI] Reload: no compatible magazine found in inventory.");
            return;
        }

        // Eject the current magazine from both inventory record and live gun
        MagazineItemInstance oldMag = wi.EjectMagazine();
        gun.EjectMagazine();

        // Always return the ejected magazine — even empty ones belong in inventory.
        if (oldMag != null)
            TryPickup(oldMag);

        // Remove the new magazine from inventory, record it in the weapon, and start reload
        RemoveItem(bestMag);
        wi.BeginReloadWith(bestMag);
        gun.StartReload(bestMag.RuntimeMag, playerInitiated: true);
    }

    private void ContextMenu_Drop(ItemInstance item)
    {
        // Use Camera.main.forward so pitch (looking up/down) is always respected,
        // regardless of what transform is assigned to _dropOrigin.
        var throwDir = Camera.main != null ? Camera.main.transform.forward : _dropOrigin.forward;

        // Spawn first — only commit state changes if it succeeds
        if (!ItemDropSpawner.TryDrop(item, _dropOrigin, throwDir, _dropThrowForce,
                                     interactableLayer: _droppedItemLayer,
                                     obstacleMask: Physics.DefaultRaycastLayers))
        {
            Debug.LogWarning($"[InventoryUI] Drop failed for '{item.data?.itemName}' — item kept in inventory.");
            return;
        }

        // Unequip flashlight before dropping
        if (item is FlashlightItemInstance)
        {
            if (flashlightSlot == null)
                Debug.LogError("[InventoryUI] FlashlightSlot is not assigned — flashlight dropped without unequipping.", this);
            else if (flashlightSlot.EquippedItem == item)
                flashlightSlot.Unequip();
        }

        // Clean up weapon state after successful spawn
        if (item is WeaponItemInstance droppedWeapon)
        {
            // Unequip only if this is the currently active weapon
            if (droppedWeapon == _equippedItem)
            {
                _equippedItem = null;
                weaponManager?.EquipNothing();
            }

            // Always remove from switchable list — applies even to holstered weapons
            if (droppedWeapon.LinkedGun != null)
            {
                weaponManager?.RemoveWeapon(droppedWeapon.LinkedGun);
                droppedWeapon.LinkedGun = null;
            }
        }

        RemoveItem(item);
    }

    // ── Drag interaction handler ──────────────────────────────────────────

    /// <summary>
    /// Called by InventoryDragController when an item is dropped on top of another item.
    /// Handles ammo → magazine loading and magazine → weapon loading.
    /// </summary>
    private DragInteractionResult HandleDragInteraction(ItemInstance dragged, ItemInstance target)
    {
        // ── Ammo box → Magazine ───────────────────────────────────────────
        if (dragged is AmmoItemInstance ammo && target is MagazineItemInstance mag)
        {
            if (ammo.AmmoDef.caliber != mag.MagDef.caliber) return DragInteractionResult.NotHandled;

            int space = mag.MagDef.capacity - mag.RuntimeMag.BulletCount;
            if (space <= 0) return DragInteractionResult.NotHandled;

            int taken = ammo.TakeRounds(space);
            for (int i = 0; i < taken; i++)
                mag.RuntimeMag.LoadRound(ammo.AmmoDef);

            if (ammo.IsEmpty)
            {
                // Remove the empty box from the views dict; drag controller destroys its GameObject
                _views.Remove(ammo);
                return DragInteractionResult.HandledConsumeDragged;
            }
            return DragInteractionResult.HandledReturnDragged;
        }

        // ── Battery → Flashlight ──────────────────────────────────────────
        if (dragged is BatteryItemInstance battery && target is FlashlightItemInstance flashlight)
        {
            if (flashlightSlot == null) return DragInteractionResult.NotHandled;

            // Must eject the old battery first via right-click Remove Battery
            if (flashlight.InsertedBattery != null) return DragInteractionResult.NotHandled;

            // Remove battery from grid first — flashlight takes ownership
            BatteryItemInstance ejected = flashlight.LoadBattery(battery);
            _views.Remove(battery);

            // Safety: LoadBattery ejects any prior battery, but we blocked that above
            if (ejected != null)
            {
                if (TryPickup(ejected) == PickupResult.NoSpace)
                    Debug.LogWarning("[InventoryUI] Battery swap: no space to return old battery — it is lost.");
            }

            // Notify FlashlightSlot so HUD and events sync
            flashlightSlot.OnBatteryLoaded(flashlight);
            return DragInteractionResult.HandledConsumeDragged;
        }

        // ── Magazine → Weapon ─────────────────────────────────────────────
        if (dragged is MagazineItemInstance magazine && target is WeaponItemInstance weapon)
        {
            if (!weapon.LoadMagazine(magazine)) return DragInteractionResult.NotHandled;
            // If this weapon is currently equipped, push the magazine into the live GunController.
            if (weapon == _equippedItem && weapon.LinkedGun != null)
                weapon.LinkedGun.InsertMagazine(magazine.RuntimeMag);
            _views.Remove(magazine);
            return DragInteractionResult.HandledConsumeDragged;
        }

        return DragInteractionResult.NotHandled;
    }

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
