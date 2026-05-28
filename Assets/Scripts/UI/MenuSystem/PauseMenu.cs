using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Drives the 3D pause menu. PauseRoot is a child of the player camera
/// and has an Animator that slides it into/out of view.
/// DOTween is used only for button hover/click feel (via MenuButton3D).
/// Owns exactly one GameInputState.Block token and one Time.timeScale snapshot.
///
/// Animator requires two triggers: "Open" and "Close".
///
/// Implementors: one instance on PauseRoot in each gameplay scene.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Header("Animator")]
    [SerializeField] private Animator _animator;
    [SerializeField] private string _openTrigger  = "Open";
    [SerializeField] private string _closeTrigger = "Close";

    [Header("Save Slots")]
    [SerializeField] private SaveSlotButton3D[] _slotButtons;

    [Header("Scene")]
    [SerializeField] private string _mainMenuScene = "MainMenu";

    private bool _isOpen;
    private bool _isBlocking;
    private float _savedTimeScale = 1f;

    // ── Public API (called by MenuController) ──────────────────────────────

    public bool IsOpen => _isOpen;

    public void Open()
    {
        if (_isOpen) return;
        _isOpen = true;

        _savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        if (!_isBlocking)
        {
            GameInputState.Block();
            _isBlocking = true;
        }

        _animator?.SetTrigger(_openTrigger);
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        Time.timeScale = _savedTimeScale;

        if (_isBlocking)
        {
            GameInputState.Unblock();
            _isBlocking = false;
        }

        _animator?.SetTrigger(_closeTrigger);
    }

    // ── Button callbacks — wire MenuButton3D.OnClick to these ─────────────

    public void OnResume() => Close();

    public void OnSave()
    {
        SetSlotMode(SaveSlotButton3D.SlotMode.Save);
        RefreshSlots();
    }

    public void OnLoad()
    {
        SetSlotMode(SaveSlotButton3D.SlotMode.Load);
        RefreshSlots();
    }

    public void OnReturnToMainMenu()
    {
        Close();
        Time.timeScale = 1f;
        SceneManager.LoadScene(_mainMenuScene);
    }

    public void OnExitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ── Save slot helpers ──────────────────────────────────────────────────

    public void SaveSlot(string slot) => SaveSystem.Instance?.SaveAll(slot);
    public void LoadSlot(string slot) => SaveSystem.Instance?.LoadAll(slot);

    private void SetSlotMode(SaveSlotButton3D.SlotMode mode)
    {
        if (_slotButtons == null) return;
        foreach (var btn in _slotButtons)
            btn?.SetMode(mode);
    }

    private void RefreshSlots()
    {
        if (_slotButtons == null) return;
        foreach (var btn in _slotButtons)
            btn?.Refresh();
    }
}
