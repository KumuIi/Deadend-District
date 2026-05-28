using UnityEngine;

/// <summary>
/// Sits on the player camera. Raycasts on left-click and dispatches to
/// MenuButton3D, SaveSlotButton3D, or FlashdriveButton depending on what's active.
///
/// Implementors: one instance on the player camera (used in both main menu and gameplay).
/// </summary>
[RequireComponent(typeof(Camera))]
public class MenuInputHandler : MonoBehaviour
{
    [SerializeField] private LayerMask _clickMask = ~0;
    [SerializeField] private float     _rayDistance = 100f;

    private Camera _cam;

    private MenuButton3D       _currentHoveredBtn;
    private SaveSlotButton3D   _currentHoveredSlot;
    private FlashdriveButton   _currentHoveredDrive;

    private void Awake() => _cam = GetComponent<Camera>();

    private void Update()
    {
        UpdateHover();
        if (Input.GetMouseButtonDown(0)) TryClick();
    }

    private void UpdateHover()
    {
        // Priority: MenuButton3D > FlashdriveButton > SaveSlotButton3D
        var btn   = Raycast<MenuButton3D>();
        var drive = btn == null ? Raycast<FlashdriveButton>() : null;
        var slot  = btn == null && drive == null ? Raycast<SaveSlotButton3D>() : null;

        if (btn != _currentHoveredBtn)
        {
            _currentHoveredBtn?.OnHoverExit();
            _currentHoveredBtn = btn;
            _currentHoveredBtn?.OnHoverEnter();
        }

        if (drive != _currentHoveredDrive)
        {
            _currentHoveredDrive?.OnHoverExit();
            _currentHoveredDrive = drive;
            _currentHoveredDrive?.OnHoverEnter();
        }

        if (slot != _currentHoveredSlot)
        {
            _currentHoveredSlot?.OnHoverExit();
            _currentHoveredSlot = slot;
            _currentHoveredSlot?.OnHoverEnter();
        }
    }

    private void TryClick()
    {
        var btn = Raycast<MenuButton3D>();
        if (btn != null) { btn.Click(); return; }

        var drive = Raycast<FlashdriveButton>();
        if (drive != null) { drive.Click(); return; }

        var slot = Raycast<SaveSlotButton3D>();
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
