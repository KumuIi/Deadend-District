using DG.Tweening;
using UnityEngine;

/// <summary>
/// Physical 3D button representing one save slot.
/// Place up to 5 in your Load/Save area. Assign slotName in the inspector.
/// Dims itself (low emission/alpha) when the slot has no save data.
/// PauseMenu sets Mode before the area is shown so the button knows
/// whether to save or load on click.
///
/// Implementors: one per save slot object in the MainMenu and PauseMenu load areas.
/// </summary>
public class SaveSlotButton3D : MonoBehaviour
{
    public enum SlotMode { Load, Save }

    [SerializeField] private string _slotName = "slot0";
    [SerializeField] private SlotMode _mode = SlotMode.Load;

    [Header("Scene (main menu load only)")]
    [Tooltip("Scene to load after restoring this slot. Leave empty to restore in-place (pause menu).")]
    [SerializeField] private string _sceneToLoad;

    [Header("Visuals")]
    [SerializeField] private Renderer _renderer;
    [SerializeField] private Color _emptyColor = new Color(0.3f, 0.3f, 0.3f);
    [SerializeField] private Color _filledColor = Color.white;
    [SerializeField] private float _hoverScale = 1.08f;
    [SerializeField] private float _hoverDuration = 0.15f;

    private Vector3 _baseScale;
    private bool _isHovered;
    private bool _slotExists;

    private void Awake()
    {
        _baseScale = transform.localScale;
    }

    private void OnEnable()
    {
        Refresh();
    }

    /// <summary>Re-check whether the slot has data. Call after any save/load operation.</summary>
    public void Refresh()
    {
        _slotExists = SaveSystem.Instance != null && SaveSystem.Instance.SlotExists(_slotName);

        if (_renderer != null)
        {
            _renderer.material.color = _slotExists ? _filledColor : _emptyColor;
        }
    }

    public void SetMode(SlotMode mode)
    {
        _mode = mode;
        Refresh();
    }

    public void OnHoverEnter()
    {
        if (_isHovered) return;
        _isHovered = true;
        transform.DOScale(_baseScale * _hoverScale, _hoverDuration).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    public void OnHoverExit()
    {
        if (!_isHovered) return;
        _isHovered = false;
        transform.DOScale(_baseScale, _hoverDuration).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    public void Click()
    {
        if (SaveSystem.Instance == null) return;
        if (_mode == SlotMode.Load && !_slotExists) return;

        if (_mode == SlotMode.Load)
        {
            if (!string.IsNullOrEmpty(_sceneToLoad))
            {
                // Queue restores so hub-scene ISaveables register before data is applied.
                SaveSystem.Instance.RestoreAfterSceneLoad(RunScopeTag.Profile, _slotName);
                SaveSystem.Instance.RestoreAfterSceneLoad(RunScopeTag.World, _slotName);
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
}
