using System;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// Physical 3D button representing one save slot.
/// Place up to 5 in your Load/Save area. Assign slotName in the inspector.
/// Dims itself when the slot has no save data.
/// On hover: reads sidecar metadata and fades in an info text panel.
///
/// Implementors: one per save slot object in the MainMenu and PauseMenu load areas.
/// </summary>
public class SaveSlotButton3D : MonoBehaviour
{
    public enum SlotMode { Load, Save }

    [SerializeField] private string   _slotName = "slot0";
    [SerializeField] private SlotMode _mode     = SlotMode.Load;

    [Header("Scene (main menu load only)")]
    [Tooltip("Scene to load after restoring. Leave empty to restore in-place (pause menu).")]
    [SerializeField] private string _sceneToLoad;

    [Header("Visuals")]
    [SerializeField] private Renderer _renderer;
    [SerializeField] private Color    _emptyColor  = new Color(0.3f, 0.3f, 0.3f);
    [SerializeField] private Color    _filledColor = Color.white;
    [SerializeField] private float    _hoverScale    = 1.08f;
    [SerializeField] private float    _hoverDuration = 0.15f;

    [Header("Hover info text (assign a child TMP)")]
    [SerializeField] private TextMeshPro _infoText;
    [SerializeField] private float       _infoFadeDuration = 0.2f;

    private Vector3 _baseScale;
    private bool    _isHovered;
    private bool    _slotExists;

    private void Awake()  => _baseScale = transform.localScale;

    private void OnEnable()
    {
        MenuHitRegistry<SaveSlotButton3D>.Register(this);
        Refresh();
    }

    private void OnDisable() => MenuHitRegistry<SaveSlotButton3D>.Unregister(this);

    public void Refresh()
    {
        _slotExists = SaveMetadataIO.Exists(_slotName);

        if (_renderer != null)
            _renderer.material.color = _slotExists ? _filledColor : _emptyColor;

        if (_infoText != null)
        {
            _infoText.alpha = 0f;
            _infoText.text  = "";
        }
    }

    public void SetMode(SlotMode mode) { _mode = mode; Refresh(); }

    // ── Hover ──────────────────────────────────────────────────────────────

    public void OnHoverEnter()
    {
        if (_isHovered) return;
        _isHovered = true;

        transform.DOScale(_baseScale * _hoverScale, _hoverDuration)
                 .SetEase(Ease.OutQuad).SetUpdate(true);

        if (_infoText == null) return;

        var meta = SaveMetadataIO.Read(_slotName);
        _infoText.text = meta != null ? BuildInfoText(meta) : "Empty slot";

        _infoText.DOFade(1f, _infoFadeDuration).SetUpdate(true);
    }

    public void OnHoverExit()
    {
        if (!_isHovered) return;
        _isHovered = false;

        transform.DOScale(_baseScale, _hoverDuration)
                 .SetEase(Ease.OutQuad).SetUpdate(true);

        _infoText?.DOFade(0f, _infoFadeDuration).SetUpdate(true);
    }

    // ── Click ──────────────────────────────────────────────────────────────

    public void Click()
    {
        if (SaveSystem.Instance == null) return;
        if (_mode == SlotMode.Load && !_slotExists) return;

        if (_mode == SlotMode.Load)
        {
            if (!string.IsNullOrEmpty(_sceneToLoad))
            {
                // Restore all scopes so a loaded slot is a complete snapshot: Profile
                // (stash/quests), World (timers), and Run (inventory, health, position + look).
                SaveSystem.Instance.RestoreAfterSceneLoad(RunScopeTag.Profile, _slotName);
                SaveSystem.Instance.RestoreAfterSceneLoad(RunScopeTag.World, _slotName);
                SaveSystem.Instance.RestoreAfterSceneLoad(RunScopeTag.Run, _slotName);
                UnityEngine.SceneManagement.SceneManager.LoadScene(_sceneToLoad);
            }
            else
            {
                SaveSystem.Instance.LoadAll(_slotName);
            }
        }
        else
        {
            SaveSystem.Instance.SaveAll(_slotName);
            Refresh();
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string BuildInfoText(SaveSlotMetadata meta)
    {
        string location = string.IsNullOrEmpty(meta.SceneId) ? "Unknown" : meta.SceneId;
        string playtime = FormatPlaytime(meta.PlaySeconds);
        string date     = FormatDate(meta.SaveTime);
        string credits  = $"{meta.Credits:N0} cr";

        return $"{location}\n{playtime} played\n{credits}\n{date}";
    }

    private static string FormatPlaytime(float seconds)
    {
        int h = (int)(seconds / 3600);
        int m = (int)((seconds % 3600) / 60);
        return h > 0 ? $"{h}h {m}m" : $"{m}m";
    }

    private static string FormatDate(string iso)
    {
        if (string.IsNullOrEmpty(iso)) return "";
        if (DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
            return dt.ToLocalTime().ToString("MMM d · HH:mm");
        return iso;
    }
}
