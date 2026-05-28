using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Orchestrates the 3D pause menu button sequence entirely in code — no Animator needed.
/// Open:  buttons fly in one by one with stagger.
/// Click: clicked button fires first, then remaining buttons cascade out after it lands.
/// Close: all buttons fly out with stagger, then PauseRoot deactivates.
/// Owns exactly one GameInputState.Block token and one Time.timeScale snapshot.
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

        gameObject.SetActive(true);

        _savedTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        if (!_isBlocking)
        {
            GameInputState.Block();
            _isBlocking = true;
        }

        // Fly buttons in one by one
        for (int i = 0; i < _buttons.Length; i++)
            _buttons[i]?.FlyIn(i * _flyInStagger);
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

        CascadeAllOut(clickedButton: null);
    }

    // ── Called when any button finishes its click fly-out ──────────────────

    private void OnButtonClicked(MenuButton3D clicked)
    {
        // Fly out every other button after the clicked one lands
        int cascade = 0;
        foreach (var btn in _buttons)
        {
            if (btn == clicked) continue;
            btn?.FlyOut(cascade * _flyOutStagger);
            cascade++;
        }

        // Deactivate after all cascades finish
        float totalDelay = (_buttons.Length - 1) * _flyOutStagger + 0.25f;
        Invoke(nameof(Deactivate), totalDelay);
    }

    private void CascadeAllOut(MenuButton3D clickedButton)
    {
        int cascade = 0;
        foreach (var btn in _buttons)
        {
            btn?.FlyOut(cascade * _flyOutStagger);
            cascade++;
        }

        float totalDelay = _buttons.Length * _flyOutStagger + 0.25f;
        Invoke(nameof(Deactivate), totalDelay);
    }

    private void Deactivate()
    {
        if (!_isOpen)
            gameObject.SetActive(false);
    }

    // ── Button callbacks — wire MenuButton3D.OnClick to these ─────────────

    public void OnResume()
    {
        if (!_isBlocking) return;
        Time.timeScale = _savedTimeScale;
        GameInputState.Unblock();
        _isBlocking = false;
        _isOpen = false;
        // cascade handled by OnButtonClicked
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
