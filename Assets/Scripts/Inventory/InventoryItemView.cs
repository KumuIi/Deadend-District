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

        // Total world-space footprint of the item (all cells combined).
        float worldW = Vector3.Distance(corners[0], corners[3]);
        float worldH = Vector3.Distance(corners[0], corners[1]);

        // Lie flat on the panel surface.
        // _rect.rotation encodes the panel tilt; Euler(-90, …, 0) rotates the model
        // so its top faces the viewer rather than its front.
        float yawOffset = Item.isRotated ? 90f : 0f;
        _model.transform.rotation = _rect.rotation * Quaternion.Euler(-90f, yawOffset, 0f);

        // Offset slightly toward the camera to sit in front of the panel quad.
        _model.transform.position = center - _rect.forward * 0.004f;

        // Scale: fit the model's bounding sphere to 80 % of the item's longest world-space
        // dimension. Using Max(W, H) rather than a per-cell value keeps the size identical
        // in both rotations — a 2×1 and a 1×2 item both resolve to the same longer edge.
        _model.transform.localScale = Vector3.one;
        var b = new Bounds(_model.transform.position, Vector3.zero);
        foreach (var r in _model.GetComponentsInChildren<Renderer>(true))
            b.Encapsulate(r.bounds);

        if (b.extents != Vector3.zero)
        {
            float fitSize      = Mathf.Max(worldW, worldH) * 0.8f;
            float modelRadius  = b.extents.magnitude;
            _model.transform.localScale = Vector3.one * (fitSize * 0.5f / modelRadius);
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
