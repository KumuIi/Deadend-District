using UnityEngine;

/// <summary>
/// Sits on the main menu camera. Raycasts on left-click and dispatches
/// to MenuButton3D or SaveSlotButton3D. Gates clicks when GameInputState
/// is blocked (e.g. a sub-panel is open).
///
/// Implementors: one instance on the MainMenu camera GameObject.
/// </summary>
[RequireComponent(typeof(Camera))]
public class MenuInputHandler : MonoBehaviour
{
    [SerializeField] private LayerMask _clickMask = ~0;
    [SerializeField] private float _rayDistance = 100f;

    private Camera _cam;
    private MenuButton3D _currentHoveredBtn;
    private SaveSlotButton3D _currentHoveredSlot;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
    }

    private void Update()
    {
        UpdateHover();

        if (Input.GetMouseButtonDown(0))
            TryClick();
    }

    private void UpdateHover()
    {
        MenuButton3D btn = Raycast<MenuButton3D>();
        if (btn != _currentHoveredBtn)
        {
            _currentHoveredBtn?.OnHoverExit();
            _currentHoveredBtn = btn;
            _currentHoveredBtn?.OnHoverEnter();
        }

        SaveSlotButton3D slot = btn == null ? Raycast<SaveSlotButton3D>() : null;
        if (slot != _currentHoveredSlot)
        {
            _currentHoveredSlot?.OnHoverExit();
            _currentHoveredSlot = slot;
            _currentHoveredSlot?.OnHoverEnter();
        }
    }

    private void TryClick()
    {
        MenuButton3D btn = Raycast<MenuButton3D>();
        if (btn != null) { btn.Click(); return; }

        SaveSlotButton3D slot = Raycast<SaveSlotButton3D>();
        slot?.Click();
    }

    private T Raycast<T>() where T : Component
    {
        Ray ray = _cam.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, _rayDistance, _clickMask))
            return hit.collider.GetComponentInParent<T>();
        return null;
    }
}
