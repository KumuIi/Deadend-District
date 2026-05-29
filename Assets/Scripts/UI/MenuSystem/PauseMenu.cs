using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Orchestrates the 3D pause menu button sequence entirely in code — no Animator needed.
/// Open:  buttons fly in one by one with stagger.
/// Click: clicked button fires first, remaining buttons cascade out after it lands.
/// Close: all buttons fly out, then cursor locks, then timeScale restores, then deactivates.
///
/// Key rule: always lock cursor BEFORE restoring timeScale so gameplay never runs
/// with an unlocked cursor (which causes a camera spike on the first frame).
///
/// Implementors: one instance on PauseRoot (child of player camera).
/// </summary>
public class PauseMenu : MonoBehaviour
{
    [Header("Buttons (assign in order they should fly in)")]
    [SerializeField] private MenuButton3D[] _buttons;

    [Header("Timing")]
    [SerializeField] private float _flyInStagger  = 0.08f;
    [SerializeField] private float _flyOutStagger = 0.06f;

    [Header("Flashdrive menu")]
    [SerializeField] private FlashdriveMenuController _flashdriveMenu;

    [Header("Save gating (hub-only)")]
    [Tooltip("The Save button. Clicking it outside the hub shakes instead of opening the menu.")]
    [SerializeField] private MenuButton3D _saveButton;

    [Header("Scene")]
    [SerializeField] private string _mainMenuScene = "MainMenu";

    private bool _isOpen;
    private bool _isBlocking;
    private float _savedTimeScale = 1f;

    private void Awake()
    {
        gameObject.SetActive(false);

        foreach (var btn in _buttons)
            if (btn != null) btn.OnClicked += OnButtonClicked;

        // Saving is hub-only (manual-save model): the guard makes the Save button shake
        // instead of opening the flashdrive when clicked during a run. Loading is allowed
        // anywhere — a mid-run load routes through RunManager.LoadSlot back to the hub.
        if (_saveButton != null) _saveButton.ClickGuard = IsInHub;

        // A gated button must also be one of _buttons (the array drives fly-in/cascade). If it
        // isn't, the inspector ref is wrong and the guard silently governs the wrong object.
        WarnIfNotAButton(_saveButton, nameof(_saveButton));

        if (_flashdriveMenu != null)
        {
            _flashdriveMenu.OnReturnRequested += ShowBullets;
            _flashdriveMenu.OnActionExecuted  += OnFlashdriveActionExecuted;
        }
    }

    /// <summary>True only in the hub — the one safe place to save/load (see manual-save model).</summary>
    private bool IsInHub() =>
        RunManager.Instance == null || RunManager.Instance.State == RunManager.RunState.InHub;

    private void WarnIfNotAButton(MenuButton3D btn, string fieldName)
    {
        if (btn == null) return;
        if (System.Array.IndexOf(_buttons, btn) < 0)
            Debug.LogWarning($"[PauseMenu] {fieldName} is not in the _buttons array — " +
                             "its gate won't be applied to the real menu button.", this);
    }

    private void OnDestroy()
    {
        foreach (var btn in _buttons)
            if (btn != null) btn.OnClicked -= OnButtonClicked;

        if (_flashdriveMenu != null)
        {
            _flashdriveMenu.OnReturnRequested -= ShowBullets;
            _flashdriveMenu.OnActionExecuted  -= OnFlashdriveActionExecuted;
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public bool IsOpen => _isOpen;

    public void Open()
    {
        if (_isOpen) return;
        _isOpen = true;

        // Cancel any pending deactivation from a previous close
        StopAllCoroutines();

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        _savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        if (!_isBlocking)
        {
            GameInputState.Block();
            _isBlocking = true;
        }

        // ResetAndFlyIn resets _hasFledOut so clicks work even after rapid open/close
        for (int i = 0; i < _buttons.Length; i++)
            _buttons[i]?.ResetAndFlyIn(i * _flyInStagger);
    }

    // ── Escape close (via MenuController) ─────────────────────────────────

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;
        StartCoroutine(CloseSequence());
    }

    private IEnumerator CloseSequence()
    {
        // Fly everything out at timeScale=0 (SetUpdate(true) handles this)
        for (int i = 0; i < _buttons.Length; i++)
            _buttons[i]?.FlyOut(i * _flyOutStagger);

        float totalDelay = _buttons.Length * _flyOutStagger + 0.25f;
        yield return new WaitForSecondsRealtime(totalDelay);

        // Lock cursor FIRST, then restore timeScale — prevents the one-frame
        // camera spike that happens when gameplay resumes with cursor still unlocked
        if (_isBlocking)
        {
            GameInputState.Unblock();
            _isBlocking = false;
        }

        Time.timeScale = _savedTimeScale;
        gameObject.SetActive(false);
    }

    // ── Cascade after a button click ───────────────────────────────────────

    private void OnButtonClicked(MenuButton3D clicked)
    {
        int cascade = 0;
        foreach (var btn in _buttons)
        {
            if (btn == clicked) continue;
            btn?.FlyOut(cascade * _flyOutStagger);
            cascade++;
        }

        float totalDelay = (_buttons.Length - 1) * _flyOutStagger + 0.25f;
        StartCoroutine(DeactivateAfter(totalDelay));
    }

    private IEnumerator DeactivateAfter(float realSeconds)
    {
        yield return new WaitForSecondsRealtime(realSeconds);
        if (!_isOpen)
            gameObject.SetActive(false);
    }

    // ── Button callbacks — wire MenuButton3D.OnClick to these ─────────────

    public void OnResume()
    {
        if (!_isBlocking) return;
        _isOpen = false;

        // Lock cursor FIRST, then restore timeScale (same rule as CloseSequence)
        GameInputState.Unblock();
        _isBlocking = false;
        Time.timeScale = _savedTimeScale;
        // Deactivation handled by OnButtonClicked cascade
    }

    public void OnSave()
    {
        // Defensive: the Save button's ClickGuard normally blocks this outside the hub,
        // but guard here too in case OnSave is invoked from another path.
        if (!IsInHub()) return;
        _flashdriveMenu?.Open(SaveSlotButton3D.SlotMode.Save);
    }

    // Loading is allowed anywhere; a mid-run load returns to the hub via RunManager.LoadSlot.
    public void OnLoad() => _flashdriveMenu?.Open(SaveSlotButton3D.SlotMode.Load);

    /// <summary>Re-fly bullets in — called when flashdrive menu returns without action.</summary>
    public void ShowBullets()
    {
        for (int i = 0; i < _buttons.Length; i++)
            _buttons[i]?.ResetAndFlyIn(i * _flyInStagger);
    }

    private void OnFlashdriveActionExecuted()
    {
        // Save/load completed — close pause menu fully
        if (_isBlocking)
        {
            GameInputState.Unblock();
            _isBlocking = false;
        }
        Time.timeScale = _savedTimeScale;
        _isOpen = false;
        gameObject.SetActive(false);
    }

    public void OnReturnToMainMenu()
    {
        if (_isBlocking)
        {
            GameInputState.Unblock();
            _isBlocking = false;
        }
        Time.timeScale = 1f;
        _isOpen = false;
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

}
