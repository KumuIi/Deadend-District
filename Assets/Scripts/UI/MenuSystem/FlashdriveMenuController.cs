using System.Collections;
using UnityEngine;

/// <summary>
/// Orchestrates the flashdrive save/load menu.
///
/// Open sequence:  Return drive flies in → slot drives fly in staggered.
/// Select:         Slot drive moves to SelectedAnchor, Z rotation -90.
/// Return pressed (nothing selected): Return flies left, slots shrink, bullets re-appear.
/// Return pressed (drive selected):   Deselects the drive.
/// Slot clicked (unselected):         Selects it.
/// Slot clicked (selected):           Executes save/load, closes menu, resumes.
///
/// Implementors: one instance on FlashdriveMenuRoot (child of camera, alongside PauseRoot).
/// </summary>
public class FlashdriveMenuController : MonoBehaviour
{
    [Header("Drives")]
    [SerializeField] private FlashdriveButton   _returnDrive;
    [SerializeField] private FlashdriveButton[] _slotDrives;

    [Header("Selected anchor (place empty in front of camera)")]
    [SerializeField] private Transform _selectedAnchor;

    [Header("Timing")]
    [SerializeField] private float _flyInStagger  = 0.08f;
    [SerializeField] private float _closeWait     = 0.35f;

    // ── State ──────────────────────────────────────────────────────────────

    private SaveSlotButton3D.SlotMode _mode;
    private FlashdriveButton          _selectedDrive;

    // PauseMenu subscribes to these
    public event System.Action OnReturnRequested;
    public event System.Action OnActionExecuted;

    // ── Unity lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        gameObject.SetActive(false);

        if (_returnDrive != null) _returnDrive.OnClicked += HandleClick;
        foreach (var d in _slotDrives)
            if (d != null) d.OnClicked += HandleClick;
    }

    private void OnDestroy()
    {
        if (_returnDrive != null) _returnDrive.OnClicked -= HandleClick;
        foreach (var d in _slotDrives)
            if (d != null) d.OnClicked -= HandleClick;
    }

    // ── Public API (called by PauseMenu) ───────────────────────────────────

    public void Open(SaveSlotButton3D.SlotMode mode)
    {
        _mode = mode;
        _selectedDrive = null;

        StopAllCoroutines();
        gameObject.SetActive(true);

        // Reset all drives
        _returnDrive?.ResetToBase();
        foreach (var d in _slotDrives) d?.ResetToBase();

        // Refresh slot labels
        foreach (var d in _slotDrives) d?.RefreshLabel(mode);

        // Return drive flies in first, then slot drives staggered
        _returnDrive?.FlyIn(0f);
        for (int i = 0; i < _slotDrives.Length; i++)
            _slotDrives[i]?.FlyIn((i + 1) * _flyInStagger);
    }

    // ── Input dispatch (called by MenuInputHandler) ────────────────────────

    public void OnHoverEnter(FlashdriveButton drive) => drive?.OnHoverEnter();
    public void OnHoverExit(FlashdriveButton drive)  => drive?.OnHoverExit();

    // ── Click handling ─────────────────────────────────────────────────────

    private void HandleClick(FlashdriveButton drive)
    {
        if (drive.IsReturn)
        {
            if (_selectedDrive != null)
            {
                // Deselect current drive and stay in menu
                _selectedDrive.Deselect();
                _selectedDrive.RefreshLabel(_mode);
                _selectedDrive = null;
            }
            else
            {
                // Close flashdrive menu and return to bullets
                StartCoroutine(CloseSequence());
            }
            return;
        }

        // Slot drive
        if (!drive.IsSelected)
        {
            // Deselect previous
            if (_selectedDrive != null)
            {
                _selectedDrive.Deselect();
                _selectedDrive.RefreshLabel(_mode);
            }
            _selectedDrive = drive;
            drive.Select(_selectedAnchor);
        }
        else
        {
            // Already selected — execute
            ExecuteSlot(drive);
        }
    }

    private void ExecuteSlot(FlashdriveButton drive)
    {
        if (SaveSystem.Instance == null) return;

        if (_mode == SaveSlotButton3D.SlotMode.Save)
        {
            SaveSystem.Instance.SaveAll(drive.SlotName);
            RunManager.Instance?.SetActiveSlot(drive.SlotName);
        }
        else
        {
            RunManager.Instance?.SetActiveSlot(drive.SlotName);
            SaveSystem.Instance.LoadAll(drive.SlotName);
        }

        StartCoroutine(ExecuteCloseSequence());
    }

    // ── Close sequences ────────────────────────────────────────────────────

    private IEnumerator CloseSequence()
    {
        // Return drive continues left off screen
        _returnDrive?.FlyOutLeft();

        // Slot drives shrink with slight stagger
        for (int i = 0; i < _slotDrives.Length; i++)
            _slotDrives[i]?.ShrinkOut(i * 0.05f);

        yield return new WaitForSecondsRealtime(_closeWait);
        gameObject.SetActive(false);
        OnReturnRequested?.Invoke();
    }

    private IEnumerator ExecuteCloseSequence()
    {
        // All drives shrink out
        _returnDrive?.ShrinkOut();
        for (int i = 0; i < _slotDrives.Length; i++)
            _slotDrives[i]?.ShrinkOut(i * 0.05f);

        yield return new WaitForSecondsRealtime(_closeWait);
        gameObject.SetActive(false);
        OnActionExecuted?.Invoke();
    }
}
