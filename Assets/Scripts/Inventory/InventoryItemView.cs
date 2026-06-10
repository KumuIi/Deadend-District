using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Hit-test surface in the inventory grid.
/// The item's 3D model prefab is instantiated as a normal scene object and physically
/// placed at the cells' world-space centre so it appears to lie flat on the inventory panel.
///
/// Rotation pipeline (applied in order):
///   1. panelTilt       — inherits the canvas panel's world rotation
///   2. flatOnPanel     — Euler(-90,0,0) lays the model flat on the panel surface
///   3. perItemOffset   — ItemSO.modelOrientationOffset corrects per-model export differences
///   4. gridRotation    — 90° around ItemSO.gridRotationAxis (default: panel normal = Z)
///                        when the item is rotated in the grid
///
/// The grid rotation spins the model around the panel's surface normal (Z after steps 1-3),
/// which matches the grid's cell-swap behaviour (width ↔ height) correctly.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(RawImage))]
[RequireComponent(typeof(CanvasGroup))]
public sealed class InventoryItemView : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public ItemInstance Item  { get; private set; }

    /// <summary>
    /// The InventoryUI that currently owns this view. Set on Initialize and reassigned
    /// by InventoryUI when an item is dragged from one grid into another (player ↔ stash).
    /// </summary>
    public InventoryUI Owner { get; internal set; }

    private RectTransform _rect;
    private RawImage      _image;
    private CanvasGroup   _group;
    private GameObject    _model;

    // Cached to avoid per-frame GetComponentsInChildren traversal
    private readonly Vector3[] _corners = new Vector3[4];
    private float _cachedModelRadius;
    private bool  _cachedIsRotated;

    // ── Initialisation ────────────────────────────────────────────────────

    public void Initialize(ItemInstance item, InventoryUI owner, int modelLayer, float cellSize)
    {
        Item  = item;
        Owner = owner;

        _rect  = GetComponent<RectTransform>();
        _image = GetComponent<RawImage>();
        _group = GetComponent<CanvasGroup>();

        _image.color         = new Color(0f, 0f, 0f, 0f);
        _image.raycastTarget = true;

        RefreshLayout(cellSize);

        if (item.data.modelPrefab == null) return;

        // InstantiateCentered wraps the model so its render-bounds center sits on the wrapper
        // origin — cancelling pivot offsets some Blender/FBX exports bake into the mesh. Without
        // this an off-pivot model renders off-frame and scaled to nothing (invisible icon), since
        // PlaceModel positions/scales by the transform origin and the radius below inflates by the
        // offset distance.
        _model = ModelPrefabUtil.InstantiateCentered(item.data.modelPrefab);

        // Bind the model's lifetime to the persistent inventory rig, NOT the active scene.
        // Instantiate with no parent adopts SceneManager.GetActiveScene() — which is the SECTOR
        // during a run-entry inventory restore (the restore flushes one frame after
        // SetActiveScene(sector)). Those sector-owned models are destroyed by UnloadSceneAsync on
        // extraction, leaving the item interactive but invisible. Move it to the owner's scene
        // (Hub/DontDestroyOnLoad) so it survives sector unloads. Kept unparented so the canvas
        // RectTransform scale never distorts the mesh; PlaceModel drives its world transform.
        Scene targetScene = owner != null ? owner.gameObject.scene : gameObject.scene;
        if (targetScene.IsValid() && targetScene.isLoaded)
            SceneManager.MoveGameObjectToScene(_model, targetScene);

        _model.SetActive(false);
        _model.SetLayerRecursive(modelLayer);

        PlaceModel();
    }

    // ── 3D model placement ────────────────────────────────────────────────

    /// <summary>
    /// Positions and orients the model so it lies flat on its grid cells.
    ///
    /// Rotation order:
    ///   panelTilt × flatOnPanel × perItemOffset × gridRotation
    ///
    /// gridRotation spins around ItemSO.gridRotationAxis in the model's LOCAL space
    /// (i.e. AFTER the first three steps are applied). Default axis (0,0,1) = panel normal,
    /// so the model spins flat on the panel surface — matching the grid's width/height swap.
    /// </summary>
    public void PlaceModel(bool forceCanvasUpdate = true)
    {
        if (_model == null) return;

        if (forceCanvasUpdate) Canvas.ForceUpdateCanvases();

        _rect.GetWorldCorners(_corners);

        Vector3 center = (_corners[0] + _corners[1] + _corners[2] + _corners[3]) * 0.25f;
        float worldW   = Vector3.Distance(_corners[0], _corners[3]);
        float worldH   = Vector3.Distance(_corners[0], _corners[1]);

        // ── Build rotation in explicit layers ────────────────────────────
        Quaternion panelTilt    = _rect.rotation;
        Quaternion flatOnPanel  = Quaternion.Euler(-90f, 0f, 0f);
        Quaternion perItemOff   = Quaternion.Euler(Item.data.modelOrientationOffset);

        // Grid rotation: 90° around the per-item axis (default Z = panel normal).
        Quaternion gridRotation = Item.isRotated
            ? Quaternion.AngleAxis(90f, Item.data.gridRotationAxis.normalized)
            : Quaternion.identity;

        _model.transform.rotation = panelTilt * flatOnPanel * perItemOff * gridRotation;
        _model.transform.position = center - _rect.forward * 0.004f;

        // Recompute radius only when grid rotation changes — the mesh bounds at scale=1
        // are constant for a given rotation, so no traversal needed every frame.
        if (_cachedModelRadius == 0f || Item.isRotated != _cachedIsRotated)
        {
            _model.transform.localScale = Vector3.one;
            var b = new Bounds(_model.transform.position, Vector3.zero);
            foreach (var r in _model.GetComponentsInChildren<Renderer>(true))
                b.Encapsulate(r.bounds);

            _cachedModelRadius = b.extents == Vector3.zero ? 0f : b.extents.magnitude;
            _cachedIsRotated   = Item.isRotated;
        }

        if (_cachedModelRadius > 0f)
        {
            float fitSize = Mathf.Max(worldW, worldH) * 0.8f;
            _model.transform.localScale = Vector3.one * (fitSize * 0.5f / _cachedModelRadius);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────

    public void RefreshLayout(float cellSize)
    {
        Vector2Int sz = Item.CurrentSize;
        _rect.sizeDelta        = new Vector2(sz.x * cellSize, sz.y * cellSize);
        _rect.anchoredPosition = new Vector2(
             Item.gridPosition.x *  cellSize,
            -Item.gridPosition.y *  cellSize);

        PlaceModel();
    }

    /// <summary>
    /// Updates only the rect size and 3D model after a rotation during drag.
    /// Does NOT reset anchoredPosition — during drag, position is controlled by OnDrag via
    /// world coords, so resetting it here would snap the item back to its grid origin.
    /// </summary>
    public void RefreshDraggedRotation(float cellSize)
    {
        Vector2Int sz = Item.CurrentSize;
        _rect.sizeDelta = new Vector2(sz.x * cellSize, sz.y * cellSize);
        PlaceModel();
    }

    public void SetModelVisible(bool visible)
    {
        if (_model != null) _model.SetActive(visible);
    }

    public void SetDragging(bool dragging)
    {
        _group.blocksRaycasts = !dragging;
        // Keep the 3D model visible while dragging so the item is identifiable across both grids.
        // PlaceModel is called each frame (LateUpdate or OnDrag) so it follows the cursor.

        if (!dragging)
        {
            // Restore top-left pivot so RefreshLayout's anchoredPosition math is correct.
            _rect.pivot = new Vector2(0f, 1f);
        }
    }

    /// <summary>
    /// Shifts the rect pivot to center (0.5, 0.5) while preserving the current visual position.
    /// Call after reparenting to the drag layer so OnDrag world-position sets place the cursor
    /// at the item's center rather than its top-left corner.
    /// </summary>
    public void CenterPivotForDrag()
    {
        _rect.GetWorldCorners(_corners);
        Vector3 center = (_corners[0] + _corners[1] + _corners[2] + _corners[3]) * 0.25f;
        _rect.pivot = new Vector2(0.5f, 0.5f);
        // Re-apply center position — Unity doesn't auto-compensate pivot changes in code.
        transform.position = center;
    }

    private void OnDestroy()
    {
        if (_model != null) Destroy(_model);
    }

    // ── Event forwarding ──────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData e) => Owner.OnItemBeginDrag(this, e);
    public void OnDrag(PointerEventData e)      => Owner.OnItemDrag(this, e);
    public void OnEndDrag(PointerEventData e)   => Owner.OnItemEndDrag(this, e);

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Right)
            Owner.OnItemRightClick(this, e);
    }

    public void OnPointerEnter(PointerEventData e) => Owner.SetHovered(this);
    public void OnPointerExit(PointerEventData e)  => Owner.SetHovered(null);
}
