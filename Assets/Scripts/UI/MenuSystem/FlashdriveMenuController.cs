using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

    public event System.Action OnReturnRequested;
    public event System.Action OnActionExecuted;

    // ── Tooltip ────────────────────────────────────────────────────────────

    private Canvas        _tooltipCanvas;
    private RectTransform _tooltipRT;
    private TextMeshProUGUI _tooltipText;

    // ── Unity lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        BuildTooltip(); // build BEFORE deactivating so canvas parents to scene root cleanly
        gameObject.SetActive(false);

        if (_returnDrive != null) _returnDrive.OnClicked += HandleClick;
        foreach (var d in _slotDrives)
            if (d != null) d.OnClicked += HandleClick;
    }

    private void BuildTooltip()
    {
        // Root-level canvas — never parented to FlashdriveMenuRoot so it survives
        // that object being deactivated/activated without interference.
        var canvasGO = new GameObject("FlashdriveTooltipCanvas");
        DontDestroyOnLoad(canvasGO);

        _tooltipCanvas = canvasGO.AddComponent<Canvas>();
        _tooltipCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _tooltipCanvas.sortingOrder = 998;
        canvasGO.AddComponent<CanvasScaler>();

        var tooltipGO = new GameObject("TooltipText");
        tooltipGO.transform.SetParent(canvasGO.transform, false);

        _tooltipRT           = tooltipGO.AddComponent<RectTransform>();
        _tooltipRT.sizeDelta = new Vector2(220f, 100f);
        _tooltipRT.pivot     = new Vector2(0f, 1f); // top-left anchor follows cursor

        var cg = tooltipGO.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable   = false;

        _tooltipText                    = tooltipGO.AddComponent<TextMeshProUGUI>();
        _tooltipText.fontSize           = 13;
        _tooltipText.color              = Color.white;
        _tooltipText.alignment          = TextAlignmentOptions.TopLeft;
        _tooltipText.richText           = true;
        _tooltipText.enableWordWrapping = false;
        _tooltipText.raycastTarget      = false;

        // Use the same font as InventoryTooltip
        var font = Resources.Load<TMPro.TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
        if (font != null) _tooltipText.font = font;

        canvasGO.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_returnDrive != null) _returnDrive.OnClicked -= HandleClick;
        foreach (var d in _slotDrives)
            if (d != null) d.OnClicked -= HandleClick;

        if (_tooltipCanvas != null)
            Destroy(_tooltipCanvas.gameObject);
    }

    // ── Tooltip API (called by MenuInputHandler) ───────────────────────────

    public void OnDriveHovered(FlashdriveButton drive, Vector2 screenPos)
    {
        if (drive == null || drive.IsReturn || _tooltipCanvas == null) return;

        var meta = SaveMetadataIO.Read(drive.SlotName);
        _tooltipText.text = meta != null
            ? BuildTooltipText(meta, drive.SlotName)
            : $"<b>{SlotDisplayName(drive.SlotName)}</b>\nEmpty";

        _tooltipCanvas.gameObject.SetActive(true);
        CanvasUtils.MoveToScreenPoint(_tooltipRT, _tooltipCanvas, screenPos);
    }

    public void OnDriveUnhovered()
    {
        _tooltipCanvas?.gameObject.SetActive(false);
    }

    public void UpdateTooltipPos(Vector2 screenPos)
    {
        if (_tooltipCanvas != null && _tooltipCanvas.gameObject.activeSelf)
            CanvasUtils.MoveToScreenPoint(_tooltipRT, _tooltipCanvas, screenPos);
    }

    private static string BuildTooltipText(SaveSlotMetadata meta, string slotName)
    {
        string loc   = string.IsNullOrEmpty(meta.SceneId) ? "?" : meta.SceneId;
        string time  = FormatTime(meta.PlaySeconds);
        string creds = $"{meta.Credits:N0} cr";
        string date  = FormatDate(meta.SaveTime);
        return $"<b>{SlotDisplayName(slotName)}</b>\n{loc}  ·  {time}\n{creds}  ·  {date}";
    }

    private static string SlotDisplayName(string slotName)
    {
        if (int.TryParse(slotName.Replace("slot", ""), out int idx))
            return $"Slot {idx + 1}";
        return slotName;
    }

    private static string FormatTime(float s)
    {
        int h = (int)(s / 3600), m = (int)((s % 3600) / 60);
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }

    private static string FormatDate(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return "";
        if (System.DateTime.TryParse(iso, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("MMM d · HH:mm");
        return "";
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
        OnDriveUnhovered();
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
        OnDriveUnhovered();
        gameObject.SetActive(false);
        OnActionExecuted?.Invoke();
    }
}
