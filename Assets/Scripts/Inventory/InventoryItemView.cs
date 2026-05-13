using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Hit-test surface in the inventory grid. The item's 3D model prefab is instantiated as a
/// normal scene object and physically placed at the cell's world-space position so it appears
/// to lie flat on the inventory panel. No RenderTexture or per-item camera involved.
/// </summary>
[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(RawImage))]
[RequireComponent(typeof(CanvasGroup))]
public class InventoryItemView : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public ItemInstance Item  { get; private set; }
    public InventoryUI  Owner { get; private set; }

    private RectTransform _rect;
    private RawImage      _image;
    private CanvasGroup   _group;
    private bool          _isDragging;
    private GameObject    _model;

    // ── Initialisation ─────────────────────────────────────────────────────────

    public void Initialize(ItemInstance item, InventoryUI owner, int modelLayer, float cellSize)
    {
        Item  = item;
        Owner = owner;

        _rect  = GetComponent<RectTransform>();
        _image = GetComponent<RawImage>();
        _group = GetComponent<CanvasGroup>();

        // RawImage is fully transparent — it exists only to receive pointer/drag events.
        _image.color         = new Color(0f, 0f, 0f, 0f);
        _image.raycastTarget = true;

        RefreshLayout(cellSize);

        if (item.data.modelPrefab == null) return;

        _model = Instantiate(item.data.modelPrefab);
        _model.SetActive(false); // hidden until inventory is opened
        SetLayerRecursive(_model, modelLayer);

        PlaceModel();
    }

    // ── 3D placement ───────────────────────────────────────────────────────────

    // Positions and scales the model so it physically occupies its grid cells.
    // Forcing canvas layout update ensures GetWorldCorners returns valid positions
    // even when called in the same frame the item is spawned.
    public void PlaceModel()
    {
        if (_model == null) return;

        Canvas.ForceUpdateCanvases();

        var corners = new Vector3[4];
        _rect.GetWorldCorners(corners);
        // corners: [0]=BL  [1]=TL  [2]=TR  [3]=BR

        Vector3 center = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;

        // World-space size of one grid unit so multi-cell items scale correctly.
        float worldW   = Vector3.Distance(corners[0], corners[3]);
        float worldH   = Vector3.Distance(corners[0], corners[1]);
        float cellUnit = Mathf.Min(worldW / Item.CurrentSize.x, worldH / Item.CurrentSize.y);

        // Lie flat on the panel surface:
        //   _rect.rotation encodes the panel tilt (e.g. tiltX=35, tiltY=-8).
        //   Euler(-90, …, 0) in that local space rotates the model so its top
        //   faces the viewer instead of its front — "lying on its back" on the panel.
        float yawOffset = Item.isRotated ? 90f : 0f;
        _model.transform.rotation = _rect.rotation * Quaternion.Euler(-90f, yawOffset, 0f);

        // Offset slightly toward the camera so the model is in front of the panel quad,
        // preventing z-fighting with the canvas background image.
        _model.transform.position = center - _rect.forward * 0.004f;

        // Scale to fill ~75 % of one cell unit (leaves a visible margin).
        _model.transform.localScale = Vector3.one;
        var b = new Bounds(_model.transform.position, Vector3.zero);
        foreach (var r in _model.GetComponentsInChildren<Renderer>(true))
            b.Encapsulate(r.bounds);

        if (b.extents != Vector3.zero)
        {
            float diameter = b.extents.magnitude * 2f;
            _model.transform.localScale = Vector3.one * (cellUnit * 0.75f / diameter);
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public void RefreshLayout(float cellSize)
    {
        Vector2Int sz = Item.CurrentSize;
        _rect.sizeDelta        = new Vector2(sz.x * cellSize, sz.y * cellSize);
        _rect.anchoredPosition = new Vector2(
             Item.gridPosition.x * cellSize,
            -Item.gridPosition.y * cellSize);

        PlaceModel();
    }

    public void SetModelVisible(bool visible)
    {
        if (_model != null) _model.SetActive(visible);
    }

    public void SetDragging(bool dragging)
    {
        _isDragging           = dragging;
        _group.blocksRaycasts = !dragging;

        // Hide the physical model while the ghost is being dragged; show on drop.
        if (_model != null) _model.SetActive(!dragging);
    }

    // ── Cleanup ────────────────────────────────────────────────────────────────

    void OnDestroy()
    {
        if (_model != null) Destroy(_model);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    static void SetLayerRecursive(GameObject go, int layer)
    {
        if (layer < 0) return;
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }

    // ── Event forwarding ───────────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData e)    => Owner.OnItemBeginDrag(this, e);
    public void OnDrag(PointerEventData e)          => Owner.OnItemDrag(this, e);
    public void OnEndDrag(PointerEventData e)       => Owner.OnItemEndDrag(this, e);

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Right)
            Owner.OnItemRotate(this);
    }

    public void OnPointerEnter(PointerEventData e) => Owner.SetHovered(this);
    public void OnPointerExit(PointerEventData e)  => Owner.SetHovered(null);
}
