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

    [Header("Save Slots")]
    [SerializeField] private SaveSlotButton3D[] _slotButtons;

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
    }

    private void OnDestroy()
    {
        foreach (var btn in _buttons)
            if (btn != null) btn.OnClicked -= OnButtonClicked;
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

    public void SaveSlot(string slot) => SaveSystem.Instance?.SaveAll(slot);
    public void LoadSlot(string slot) => SaveSystem.Instance?.LoadAll(slot);

    // ── Helpers ────────────────────────────────────────────────────────────

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
