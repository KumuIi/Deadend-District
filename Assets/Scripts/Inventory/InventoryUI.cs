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

    [Header("=== Flashlight Aim ===")]
    [Tooltip("Empty Transform positioned where inventory items sit in world space. The flashlight beam aims here when the inventory is open.")]
    [SerializeField] private Transform _inventoryLightTarget;

    [Header("=== Drop Settings ===")]
    [Tooltip("Origin transform for item drops (assign player camera). Falls back to Camera.main.")]
    [SerializeField] private Transform _dropOrigin;
    [Tooltip("Forward throw force in m/s.")]
    [SerializeField] private float _dropThrowForce = 5f;
    [Tooltip("Layer the dropped item's collider is placed on — must match PlayerInteractor's interaction mask. Default 6 = InteractI.")]
    [SerializeField] private int _droppedItemLayer = 6;

    [Header("=== Equipment ===")]
    [Tooltip("If false, the right-click menu omits Equip/Unequip. Uncheck on the stash so items " +
             "can't be equipped while stored — move them to the player inventory to equip.")]
    [SerializeField] private bool _allowEquip = true;

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

    /// <summary>
    /// All live InventoryUI instances. Used to route a drag that leaves one grid into another
    /// open grid (player inventory ↔ stash). Registered in Awake, removed in OnDestroy.
    /// All participating panels must share the same modelLayer so transferred 3D models render.
    /// </summary>
    private static readonly List<InventoryUI> _activePanels = new List<InventoryUI>();

    /// <summary>
    /// The player's own inventory panel — definitionally the one wired to a WeaponManager
    /// (secondary container panels like the stash have none). World pickups route items here
    /// instead of letting FindObjectOfType pick an arbitrary panel once a second grid exists.
    /// Returns null (not an arbitrary panel) if no panel declares a WeaponManager — routing a
    /// pickup into the stash would be worse than failing loudly; callers log the null.
    /// </summary>
    public static InventoryUI Player
    {
        get
        {
            foreach (var p in _activePanels)
                if (p != null && p.weaponManager != null) return p;
            return null;
        }
    }

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
        if (_inventoryLightTarget == null)
            Debug.LogWarning("[InventoryUI] InventoryLightTarget is not assigned — flashlight will not aim at inventory.", this);

        Grid = new InventoryGrid(gridWidth, gridHeight);

        SetupRootTransform();
        BuildPanel(GetComponent<RectTransform>());

        _panel.localEulerAngles = new Vector3(tiltX, tiltY, 0f);
        _panel.gameObject.SetActive(false);

        _highlighter = new InventoryHighlighter(
            _cellImages, gridWidth, Grid, cellNormal, cellHighlight, cellBlocked);

        _drag = new InventoryDragController(
            Grid, _highlighter, _canvas, _itemsLayer, _dragLayer, cellSize);

        _drag.OnDroppedOnItem      = HandleDragInteraction;
        _drag.TryCrossGridDrop     = TryHandoffToOtherPanel;
        _drag.TryCrossGridHighlight = TryHandoffHighlight;

        _activePanels.Add(this);

        if (_canvas != null)
        {
            _tooltip     = new InventoryTooltip(_canvas);
            _contextMenu = new InventoryContextMenu(_canvas);

            _contextMenu.AllowEquip       = _allowEquip;
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
        flashlightSlot?.EndInventoryAim();
        _activePanels.Remove(this);
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
        // Idempotent: each open Blocks GameInputState and each close Unblocks it. Re-issuing the
        // current state would corrupt the shared (reference-counted) block count, so no-op here.
        if (open == IsOpen) return;

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

        if (open)
        {
            GameInputState.Block();
            flashlightSlot?.BeginInventoryAim(_inventoryLightTarget);
        }
        else
        {
            GameInputState.Unblock();
            flashlightSlot?.EndInventoryAim();
        }
    }

    /// <summary>
    /// Rotates whatever needs rotating across ALL open panels.
    /// Called by InventoryInputHandler — works regardless of which panel has focus so the stash
    /// (which has no InventoryInputHandler) responds to R without any forwarding chain.
    /// </summary>
    public static void BroadcastRotate()
    {
        // Drag in progress takes priority — check all panels first.
        foreach (var panel in _activePanels)
        {
            if (!panel.IsOpen) continue;
            if (panel._drag.IsDragging) { panel._drag.RotateDragged(); return; }
        }
        // Rotate the hovered item on whichever panel the cursor is over.
        foreach (var panel in _activePanels)
        {
            if (!panel.IsOpen || panel._hoveredView == null) continue;
            panel.OnItemRotate(panel._hoveredView);
            return;
        }
    }

    /// <summary>Instance-level rotate — kept for any external callers.</summary>
    public void RequestRotate() => BroadcastRotate();

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
            // TryEquip calls Unequip first which ends any active inventory aim; restart it.
            if (IsOpen)
                flashlightSlot.BeginInventoryAim(_inventoryLightTarget);
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

        // Detach any live equipment state tied to this item before it leaves the inventory.
        DetachEquipmentFor(item);

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

    // ── Cross-grid drag (player inventory ↔ stash) ────────────────────────

    /// <summary>
    /// Called each drag frame when the cursor is outside this panel's grid.
    /// Passes null item to clear highlights; passes a real item to show where it would land.
    /// </summary>
    private bool TryHandoffHighlight(ItemInstance item, Vector2 screenPos)
    {
        bool handled = false;
        foreach (var panel in _activePanels)
        {
            if (panel == this || !panel.IsOpen) continue;
            handled |= panel.TryHighlight(item, screenPos);
        }
        return handled;
    }

    /// <summary>
    /// Shows (or clears) a cell highlight on THIS panel for an item being dragged from another.
    /// Pass null item to clear. Returns true if the cursor is over this panel's grid.
    /// </summary>
    private bool TryHighlight(ItemInstance item, Vector2 screenPos)
    {
        if (item == null)
        {
            _highlighter.ClearHighlight();
            return false;
        }

        // Reuse the drag controller's coordinate mapping to find which cell the cursor is over.
        if (!_drag.TryGetCellUnderCursor(screenPos, out Vector2Int cell))
        {
            _highlighter.ClearHighlight();
            return false;
        }

        Vector2Int snapPos = new Vector2Int(
            Mathf.Clamp(cell.x - item.CurrentSize.x / 2, 0, Grid.Width  - item.CurrentSize.x),
            Mathf.Clamp(cell.y - item.CurrentSize.y / 2, 0, Grid.Height - item.CurrentSize.y));
        _highlighter.HighlightCells(item, snapPos);
        return true;
    }

    /// <summary>
    /// Called by this panel's drag controller when an item is released outside this grid.
    /// Offers the dragged view to every other open panel until one accepts it.
    /// </summary>
    private bool TryHandoffToOtherPanel(InventoryItemView view, Vector2 screenPos)
    {
        foreach (var panel in _activePanels)
        {
            if (panel == this || !panel.IsOpen) continue;
            if (panel.AcceptCrossGridDrop(view, screenPos)) return true;
        }
        return false;
    }

    /// <summary>
    /// Receives a view dragged out of another panel. Places the item in this grid under the
    /// cursor, then reparents the view and transfers ownership. Returns false if it doesn't fit.
    /// </summary>
    private bool AcceptCrossGridDrop(InventoryItemView view, Vector2 screenPos)
    {
        if (!_drag.TryPlaceExternal(view.Item, screenPos)) return false;

        InventoryUI source = view.Owner;
        if (source != null && source != this)
        {
            // The item is leaving the source panel — tear down any live equipment state it
            // held there (equipped weapon/flashlight, weapon-switcher entry) so the gun/light
            // doesn't keep running after the item is moved to another grid (e.g. the stash).
            source.DetachEquipmentFor(view.Item);
            source._views.Remove(view.Item); // item already left the source grid at begin-drag
        }

        // worldPositionStays: false — let RefreshLayout set anchoredPosition from grid coords.
        // true would convert the player-panel world position into this panel's local space, which
        // is garbage when the two panels have different rotations (tilt angles).
        view.transform.SetParent(_itemsLayer, false);
        view.Owner = this;
        view.SetDragging(false);
        // Force the canvas layout to recalculate BEFORE RefreshLayout reads world corners,
        // so PlaceModel gets correct geometry in the same frame (no one-frame wrong-position flash).
        Canvas.ForceUpdateCanvases();
        view.RefreshLayout(cellSize);
        _views[view.Item] = view;
        return true;
    }

    /// <summary>
    /// Tears down live equipment state for an item that is about to leave this inventory
    /// (dropped to the world, or transferred to another grid). Unequips the active weapon or
    /// flashlight and removes the weapon from the switcher. Safe to call on a non-equipped item.
    /// </summary>
    /// <summary>
    /// Removes an item from the grid, first detaching any live equipment state tied to it
    /// (active weapon → EquipNothing + RemoveWeapon, equipped flashlight → Unequip). Use this
    /// instead of RemoveItem when the item may currently be equipped — e.g. selling to a trader.
    /// </summary>
    public void RemoveItemAndDetach(ItemInstance item)
    {
        DetachEquipmentFor(item);
        RemoveItem(item);
    }

    private void DetachEquipmentFor(ItemInstance item)
    {
        if (item is FlashlightItemInstance)
        {
            if (flashlightSlot != null && flashlightSlot.EquippedItem == item)
                flashlightSlot.Unequip();
        }
        else if (item is WeaponItemInstance weapon)
        {
            // Unequip only if this is the currently active weapon.
            if (weapon == _equippedItem)
            {
                _equippedItem = null;
                weaponManager?.EquipNothing();
            }

            // Always remove from the switchable list — applies even to holstered weapons.
            if (weapon.LinkedGun != null)
            {
                weaponManager?.RemoveWeapon(weapon.LinkedGun);
                weapon.LinkedGun = null;
            }
        }
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
