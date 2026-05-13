using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Invisible hit-test surface in the UI grid that owns a real world-space 3D model.
/// The model is spawned in world space on a dedicated layer and positioned every
/// LateUpdate to match this rect's world corners, so it visually sits on the grid.
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

    // Real 3D model displayed in world space above the grid cell(s)
    private GameObject _model;
    private Vector3    _modelScale = Vector3.one;
    private bool       _scaleDirty = true;
    private bool       _isDragging;

    // Nice isometric-ish display angle
    private static readonly Quaternion DisplayRotation = Quaternion.Euler(25f, 35f, 0f);

    // ── Initialisation ─────────────────────────────────────────────────────

    public void Initialize(ItemInstance item, InventoryUI owner, int modelLayer, float cellSize)
    {
        Item  = item;
        Owner = owner;

        _rect  = GetComponent<RectTransform>();
        _image = GetComponent<RawImage>();
        _group = GetComponent<CanvasGroup>();

        // Invisible hit surface — 3D model provides all visuals.
        // We need a tiny non-zero alpha so UGUI still registers this as a valid raycast target.
        _image.color         = new Color(1f, 1f, 1f, 0.004f);
        _image.raycastTarget = true;

        RefreshLayout(cellSize);

        if (item.data.modelPrefab != null)
        {
            _model = Instantiate(item.data.modelPrefab);
            SetLayerRecursive(_model, modelLayer);
            _model.SetActive(false); // hidden until inventory opens
        }
    }

    public void SetModelVisible(bool visible)
    {
        if (_model != null) _model.SetActive(visible);
    }

    // ── Layout ─────────────────────────────────────────────────────────────

    /// <summary>Snaps the rect to the item's current grid position and marks scale dirty.</summary>
    public void RefreshLayout(float cellSize)
    {
        Vector2Int sz = Item.CurrentSize;
        _rect.sizeDelta        = new Vector2(sz.x * cellSize, sz.y * cellSize);
        _rect.anchoredPosition = new Vector2(
             Item.gridPosition.x * cellSize,
            -Item.gridPosition.y * cellSize);
        _scaleDirty = true; // world size may have changed (e.g. after rotation)
    }

    // ── Drag state ─────────────────────────────────────────────────────────

    public void SetDragging(bool dragging)
    {
        _isDragging           = dragging;
        _group.blocksRaycasts = !dragging; // must be false so drag layer doesn't eat events
    }

    // ── 3D model sync ──────────────────────────────────────────────────────

    void LateUpdate()
    {
        if (_model == null) return;

        var corners = new Vector3[4];
        _rect.GetWorldCorners(corners);
        // corners: 0=BL, 1=TL, 2=TR, 3=BR
        Vector3 center = (corners[0] + corners[2]) * 0.5f;

        if (_scaleDirty)
        {
            float worldW = (corners[3] - corners[0]).magnitude;
            float worldH = (corners[1] - corners[0]).magnitude;
            if (worldW > 0.0001f)
            {
                _modelScale = ComputeFitScale(worldW, worldH);
                _scaleDirty = false;
            }
        }

        // Place model slightly in front of the canvas plane so the overlay camera sees it.
        // _rect.forward points INTO the screen; negating it points toward the viewer.
        _model.transform.position = center - _rect.forward * 0.1f;

        // Rotation is camera-relative so the isometric display angle is constant regardless
        // of which direction the player faces (the overlay camera always matches Camera.main).
        Camera cam = Camera.main;
        _model.transform.rotation   = cam != null ? cam.transform.rotation * DisplayRotation : DisplayRotation;
        _model.transform.localScale = _modelScale;
        _model.SetActive(true);
    }

    void OnDestroy()
    {
        if (_model != null) Destroy(_model);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    Vector3 ComputeFitScale(float worldW, float worldH)
    {
        _model.transform.localScale = Vector3.one;
        _model.transform.rotation   = DisplayRotation;

        var bounds = new Bounds(_model.transform.position, Vector3.zero);
        foreach (var r in _model.GetComponentsInChildren<Renderer>())
            bounds.Encapsulate(r.bounds);

        if (bounds.extents == Vector3.zero)
            return Vector3.one * (worldH * 0.5f);

        // Fit within worldW × worldH with 15% padding, constrained by both axes
        float sx = worldW * 0.85f / bounds.size.x;
        float sy = worldH * 0.85f / bounds.size.y;
        return Vector3.one * Mathf.Min(sx, sy);
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        if (layer < 0) return;
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            t.gameObject.layer = layer;
    }

    // ── Event forwarding ───────────────────────────────────────────────────

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
