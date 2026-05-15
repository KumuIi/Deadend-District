using UnityEngine;
using UnityEngine.EventSystems;
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
    public InventoryUI  Owner { get; private set; }

    private RectTransform _rect;
    private RawImage      _image;
    private CanvasGroup   _group;
    private GameObject    _model;

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

        _model = Instantiate(item.data.modelPrefab);
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
    public void PlaceModel()
    {
        if (_model == null) return;

        Canvas.ForceUpdateCanvases();

        var corners = new Vector3[4];
        _rect.GetWorldCorners(corners);

        Vector3 center = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
        float worldW   = Vector3.Distance(corners[0], corners[3]);
        float worldH   = Vector3.Distance(corners[0], corners[1]);

        // ── Build rotation in explicit layers ────────────────────────────
        Quaternion panelTilt    = _rect.rotation;
        Quaternion flatOnPanel  = Quaternion.Euler(-90f, 0f, 0f);
        Quaternion perItemOff   = Quaternion.Euler(Item.data.modelOrientationOffset);

        // Grid rotation: 90° around the per-item axis (default Z = panel normal).
        // AngleAxis works in LOCAL space here because we compose it AFTER the offset.
        Quaternion gridRotation = Item.isRotated
            ? Quaternion.AngleAxis(90f, Item.data.gridRotationAxis.normalized)
            : Quaternion.identity;

        _model.transform.rotation = panelTilt * flatOnPanel * perItemOff * gridRotation;
        _model.transform.position = center - _rect.forward * 0.004f;

        // Scale: fit bounding sphere to 80% of longest world-space dimension
        _model.transform.localScale = Vector3.one;
        var b = new Bounds(_model.transform.position, Vector3.zero);
        foreach (var r in _model.GetComponentsInChildren<Renderer>(true))
            b.Encapsulate(r.bounds);

        if (b.extents != Vector3.zero)
        {
            float fitSize     = Mathf.Max(worldW, worldH) * 0.8f;
            float modelRadius = b.extents.magnitude;
            _model.transform.localScale = Vector3.one * (fitSize * 0.5f / modelRadius);
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

    public void SetModelVisible(bool visible)
    {
        if (_model != null) _model.SetActive(visible);
    }

    public void SetDragging(bool dragging)
    {
        _group.blocksRaycasts = !dragging;
        if (_model != null) _model.SetActive(!dragging);
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
