using System;
using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mode-aware 3D bullet menu. ONE controller drives ONE pool of MenuButton3D bullets across three
/// modes — there are no separate menu scenes or screens:
///
///   • Pause     — Esc during gameplay. Resume / Save / Load / Return to Menu.
///   • Death     — raised by RunManager on death instead of returning to the hub. A 70%-red wall
///                 fades in, "YOU DIED" shows where the top bullet would be, and only the bottom
///                 bullets fly in: Load / Return to Menu / Quit.
///   • MainMenu  — shown on boot (over the already-loaded hub, behind a background image) and when
///                 "Return to Menu" is pressed. Start New Game / Load / Quit.
///
/// Each mode picks which bullets fly in (BOTTOM-aligned, so the top stays free for the title text),
/// relabels them (MenuButton3D.SetLabel) and repoints their click (MenuButton3D.RuntimeAction).
/// Loading/saving is delegated to the existing FlashdriveMenuController.
///
/// The root GameObject stays ACTIVE the whole time (so this component can subscribe to the death
/// event); "closed" simply means every bullet is hidden and the overlays are transparent.
///
/// Implementors: one instance on the menu root (child of the player camera), alongside the
/// FlashdriveMenuRoot. Provide one more bullet than the busiest mode needs so the top slot is free.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    public enum Mode { Pause, Death, MainMenu }

    // Logical menu entries. The same physical bullet can host different entries in different modes.
    private enum Entry { Resume, Save, Load, ReturnToMenu, Quit, StartNewGame }

    [Header("Bullets (ordered TOP → BOTTOM). Entries fill from the bottom up; provide one extra so the top stays free for the title.")]
    [SerializeField] private MenuButton3D[] _bullets;

    [Header("Title (3D TMP above the bullets — used for YOU DIED / the game name)")]
    [SerializeField] private GameObject _titleObject;
    [SerializeField] private TMP_Text   _titleLabel;
    [SerializeField] private string     _deathTitle = "YOU DIED";
    [SerializeField] private string     _menuTitle  = "DEADEND DISTRICT";

    [Header("Title reveal (plays AFTER the bullets — slow 'aura' moment)")]
    [Tooltip("Seconds to wait after opening before the title starts revealing — let the bullets land first.")]
    [SerializeField] private float _titleRevealDelay    = 0.5f;
    [Tooltip("How long the slow title fade-up takes.")]
    [SerializeField] private float _titleRevealDuration = 3f;
    [Tooltip("Title starts at this multiple of its normal size and settles to 1× as it fades in (the 'aura').")]
    [SerializeField] private float _titleRevealScaleFrom = 1.12f;

    private Vector3 _titleBaseScale = Vector3.one;

    [Header("Overlays — built in code on the linked canvas")]
    [Tooltip("Empty Canvas hosting the red death wall + the main-menu background. Forced to " +
             "Screen Space - Camera at the plane distance below so it renders BEHIND the 3D bullets " +
             "(camera children, closer) but IN FRONT of the world. Screen Space - Overlay would cover the bullets.")]
    [SerializeField] private Canvas _overlayCanvas;
    [Tooltip("Camera the overlay canvas renders through — the player camera this menu hangs under. " +
             "Falls back to Camera.main if left empty.")]
    [SerializeField] private Camera _menuCamera;
    [Tooltip("Canvas plane distance (metres). Must be a bit LARGER than the bullets' distance from " +
             "the camera so the bullets draw in front. If the red wall covers the bullets, increase it; " +
             "keep it small so nearby walls don't poke through.")]
    [SerializeField] private float _overlayPlaneDistance = 0.3f;
    [Tooltip("Main-menu background. Drop your image here as a Sprite — code builds the full-screen Image.")]
    [SerializeField] private Sprite _backgroundSprite;
    [SerializeField] private Color  _redColor        = new Color(0.55f, 0f, 0f, 1f);
    [SerializeField] private float  _redOverlayAlpha = 0.7f;
    [SerializeField] private float  _overlayFade     = 0.4f;

    // Built at runtime on _overlayCanvas (full-screen Image + CanvasGroup each).
    private CanvasGroup _redOverlay;
    private CanvasGroup _backgroundImage;

    [Header("Timing")]
    [SerializeField] private float _flyInStagger  = 0.08f;
    [SerializeField] private float _flyOutStagger = 0.06f;

    [Header("HUD Canvas")]
    [Tooltip("The Overlay canvas named 'HUD' under the player rig. Auto-found at runtime if left empty.")]
    [SerializeField] private Canvas _hudCanvas; // auto-found: the Overlay canvas named "HUD" under the player rig

    [Header("Flashdrive menu (save/load)")]
    [SerializeField] private FlashdriveMenuController _flashdriveMenu;

    [Header("Boot")]
    [Tooltip("Open the main menu automatically when the scene starts (the hub is already loaded behind the background image).")]
    [SerializeField] private bool _openMainMenuOnStart = true;

    // ── Labels per mode (top → bottom of the USED region) ────────────────────
    private static readonly Entry[] PauseEntries    = { Entry.Resume, Entry.Save, Entry.Load, Entry.ReturnToMenu };
    private static readonly Entry[] DeathEntries    = { Entry.Load, Entry.ReturnToMenu, Entry.Quit };
    private static readonly Entry[] MainMenuEntries = { Entry.StartNewGame, Entry.Load, Entry.Quit };

    // ── State ────────────────────────────────────────────────────────────────
    private Mode  _mode;
    private bool  _isOpen;
    private bool  _isBlocking;
    private float _savedTimeScale = 1f;
    private bool  _deathSubscribed;

    public bool IsOpen => _isOpen;
    public Mode CurrentMode => _mode;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        // The root stays active; "closed" = all bullets hidden + overlays transparent.
        BuildOverlays();
        HideAllBullets();

        // Auto-resolve the HUD Overlay canvas if not assigned in the Inspector.
        // PauseMenu lives under the [Player] prefab, so transform.root is the player root.
        // The HUD canvas is at [Player]/[UI]/HUD.
        if (_hudCanvas == null)
        {
            var root = transform.root;
            foreach (var c in root.GetComponentsInChildren<Canvas>(true))
                if (c.gameObject.name == "HUD") { _hudCanvas = c; break; }
        }

        // Cache the title's authored scale so the reveal can animate from a larger size back to it.
        Transform titleT = _titleObject != null ? _titleObject.transform
                         : _titleLabel  != null ? _titleLabel.transform : null;
        if (titleT != null) _titleBaseScale = titleT.localScale;
        HideTitle();

        foreach (var b in _bullets)
            if (b != null) b.OnClicked += OnButtonClicked;

        if (_flashdriveMenu != null)
        {
            _flashdriveMenu.OnReturnRequested += FlyInCurrentMode;
            _flashdriveMenu.OnActionExecuted  += OnFlashdriveActionExecuted;
        }
    }

    private void Start()
    {
        // Subscribe after all Awakes so RunManager.Instance exists.
        if (RunManager.Instance != null)
        {
            RunManager.Instance.DeathScreenRequested += HandleDeathRequested;
            _deathSubscribed = true;
        }

        if (_openMainMenuOnStart)
            OpenMainMenu();
    }

    private void OnDestroy()
    {
        foreach (var b in _bullets)
            if (b != null) b.OnClicked -= OnButtonClicked;

        if (_flashdriveMenu != null)
        {
            _flashdriveMenu.OnReturnRequested -= FlyInCurrentMode;
            _flashdriveMenu.OnActionExecuted  -= OnFlashdriveActionExecuted;
        }

        if (_deathSubscribed && RunManager.Instance != null)
            RunManager.Instance.DeathScreenRequested -= HandleDeathRequested;
    }

    /// <summary>
    /// Builds the red death wall + the main-menu background as full-screen Images on the linked
    /// canvas, and forces that canvas to Screen Space - Camera so it sits BEHIND the 3D bullets
    /// (camera children, closer than the plane) but IN FRONT of the world. Both start transparent.
    /// </summary>
    private void BuildOverlays()
    {
        if (_overlayCanvas == null)
        {
            Debug.LogWarning("[PauseMenu] No _overlayCanvas assigned — the red death wall and the " +
                             "main-menu background won't appear. Link an empty Canvas + the player camera.", this);
            return;
        }

        Camera cam = _menuCamera != null ? _menuCamera : Camera.main;
        if (cam == null)
            Debug.LogWarning("[PauseMenu] No _menuCamera and no Camera.main — Screen Space - Camera " +
                             "canvas has no camera and will behave like an overlay (covering the bullets).", this);

        _overlayCanvas.renderMode    = RenderMode.ScreenSpaceCamera;
        _overlayCanvas.worldCamera   = cam;
        _overlayCanvas.planeDistance = _overlayPlaneDistance;

        _redOverlay      = BuildFullScreen("RedDeathWall",   _redColor,   null);
        _backgroundImage = BuildFullScreen("MenuBackground", Color.white, _backgroundSprite);
    }

    private CanvasGroup BuildFullScreen(string objectName, Color color, Sprite sprite)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer));
        var rt = (RectTransform)go.transform;
        rt.SetParent(_overlayCanvas.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var img = go.AddComponent<Image>();
        img.color         = color;
        img.sprite        = sprite;
        img.raycastTarget = false;   // the menu uses MenuInputHandler's own ray, not Unity UI events

        var cg = go.AddComponent<CanvasGroup>();
        cg.alpha          = 0f;
        cg.blocksRaycasts = false;
        cg.interactable   = false;
        return cg;
    }

    /// <summary>True only in the hub — the one safe place to save (manual-save model).</summary>
    private bool IsInHub() =>
        RunManager.Instance == null || RunManager.Instance.State == RunManager.RunState.InHub;

    // ── Public entry points (one per mode) ───────────────────────────────────

    /// <summary>Pause mode — opened by MenuController on Escape during gameplay.</summary>
    public void Open()
    {
        if (_isOpen && _mode == Mode.Pause) return;
        OpenMode(Mode.Pause, PauseEntries, showTitle: false, title: null, overlay: null);
    }

    public void OpenMainMenu()
    {
        // Hide the Overlay HUD canvas — it renders above the camera-space background overlay
        // and must not be visible while the main menu is shown.
        if (_hudCanvas != null) _hudCanvas.enabled = false;
        OpenMode(Mode.MainMenu, MainMenuEntries, showTitle: true, title: _menuTitle, overlay: _backgroundImage);
    }

    private void OpenDeath()
    {
        // A prior scene transition may have left the black fade canvas (sortingOrder 999) opaque;
        // clear it so the death scene shows through the red wall.
        SceneTransitionManager.Instance?.ClearFadeImmediate();
        OpenMode(Mode.Death, DeathEntries, showTitle: true, title: _deathTitle, overlay: _redOverlay);
    }

    // ── Death hook (RunManager.DeathScreenRequested) ─────────────────────────

    private bool HandleDeathRequested()
    {
        OpenDeath();
        return true;   // consumed — RunManager skips its fade-to-black + hub return
    }

    // ── Core open ────────────────────────────────────────────────────────────

    private void OpenMode(Mode mode, Entry[] entries, bool showTitle, string title, CanvasGroup overlay)
    {
        _mode   = mode;
        _isOpen = true;

        StopAllCoroutines();

        // Capture the timescale to restore to. From running gameplay this is 1; never restore to 0.
        if (mode == Mode.Pause)
            _savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        else
            _savedTimeScale = 1f;

        Time.timeScale = 0f;

        if (!_isBlocking)
        {
            GameInputState.Block();
            _isBlocking = true;
        }

        if (showTitle && _titleLabel != null)
        {
            if (_titleObject != null) _titleObject.SetActive(true);

            // Keep the title on a single line regardless of the text box width (otherwise a narrow
            // RectTransform wraps "YOU DIED" onto stacked lines).
            _titleLabel.enableWordWrapping = false;
            _titleLabel.overflowMode       = TMPro.TextOverflowModes.Overflow;
            _titleLabel.text               = title;

            RevealTitle();   // slow "aura" fade-up, AFTER the bullets have flown in
        }
        else
        {
            HideTitle();
        }

        FadeOverlay(_redOverlay,      overlay == _redOverlay      ? _redOverlayAlpha : 0f);
        FadeOverlay(_backgroundImage, overlay == _backgroundImage ? 1f               : 0f);

        ApplyEntriesAndFlyIn(entries);
    }

    /// <summary>
    /// Bottom-aligns the mode's entries onto the bullet pool: the last N bullets host the N entries
    /// (so the top slot(s) stay free for the title), the rest are hidden.
    /// </summary>
    private void ApplyEntriesAndFlyIn(Entry[] entries)
    {
        int n         = _bullets.Length;
        int k         = entries.Length;
        int firstUsed = Mathf.Max(0, n - k);

        for (int i = 0; i < n; i++)
        {
            var b = _bullets[i];
            if (b == null) continue;

            if (i < firstUsed)
            {
                b.gameObject.SetActive(false);   // OnDisable unregisters it from the hit registry
                continue;
            }

            int entryIdx = i - firstUsed;
            Entry e      = entries[entryIdx];

            b.gameObject.SetActive(true);
            b.SetLabel(LabelFor(e));
            b.RuntimeAction = ActionFor(e);
            b.ClickGuard    = (e == Entry.Save) ? (Func<bool>)IsInHub : null;   // saving is hub-only
            b.ResetAndFlyIn(entryIdx * _flyInStagger);
        }
    }

    /// <summary>Re-fly the current mode's bullets without disturbing timescale/title/overlay —
    /// called when the flashdrive menu is dismissed without acting.</summary>
    private void FlyInCurrentMode() => ApplyEntriesAndFlyIn(EntriesFor(_mode));

    private static Entry[] EntriesFor(Mode m) => m switch
    {
        Mode.Death    => DeathEntries,
        Mode.MainMenu => MainMenuEntries,
        _             => PauseEntries,
    };

    private static string LabelFor(Entry e) => e switch
    {
        Entry.Resume       => "Resume",
        Entry.Save         => "Save",
        Entry.Load         => "Load",
        Entry.ReturnToMenu => "Return to Menu",
        Entry.Quit         => "Quit",
        Entry.StartNewGame => "Start New Game",
        _                  => "",
    };

    private Action ActionFor(Entry e) => e switch
    {
        Entry.Resume       => OnResume,
        Entry.Save         => OnSave,
        Entry.Load         => OnLoad,
        Entry.ReturnToMenu => OnReturnToMainMenu,
        Entry.Quit         => OnExitGame,
        Entry.StartNewGame => OnStartNewGame,
        _                  => null,
    };

    // ── Escape close (MenuController) ─────────────────────────────────────────

    /// <summary>Escape only closes Pause mode — Death and MainMenu are not Escape-dismissable.</summary>
    public void Close()
    {
        if (!_isOpen || _mode != Mode.Pause) return;

        // If the save/load submenu is up, Escape dismisses the drives AND closes the whole pause
        // menu (back to gameplay) — it does not bounce back to the bullets. The pause menu's
        // _isOpen flag doesn't distinguish the submenu layer, so close it explicitly here.
        if (_flashdriveMenu != null && _flashdriveMenu.IsOpen)
        {
            _flashdriveMenu.CloseForExit();
            OnResume();             // unblock input, restore timescale, hide overlays/title
            return;                 // bullets are already hidden (they flew out on Save/Load click)
        }

        OnResume();
        CascadeAndHide(null);   // no clicked button — fly all out
    }

    public void ForceClose() => Close();

    // ── Click cascade ─────────────────────────────────────────────────────────

    private void OnButtonClicked(MenuButton3D clicked)
    {
        // Fly the OTHER active bullets out. Mode-switching / flashdrive actions re-arrange the
        // bullets themselves afterwards; terminal actions (Resume/Quit/StartNewGame) hide them.
        int cascade = 0;
        foreach (var b in _bullets)
        {
            if (b == null || b == clicked || !b.gameObject.activeSelf) continue;
            b.FlyOut(cascade * _flyOutStagger);
            cascade++;
        }
    }

    private void CascadeAndHide(MenuButton3D clicked)
    {
        OnButtonClicked(clicked);
        StopAllCoroutines();
        StartCoroutine(HideAfterFlyOut());
    }

    private IEnumerator HideAfterFlyOut()
    {
        float wait = _bullets.Length * _flyOutStagger + 0.3f;
        yield return new WaitForSecondsRealtime(wait);
        if (!_isOpen) HideAllBullets();
    }

    private void HideAllBullets()
    {
        foreach (var b in _bullets)
            if (b != null) b.gameObject.SetActive(false);
    }

    private void FadeOverlay(CanvasGroup cg, float target)
    {
        if (cg == null) return;
        cg.DOKill();
        cg.DOFade(target, _overlayFade).SetUpdate(true);
    }

    /// <summary>
    /// Slow dramatic title reveal: after the bullets have flown in, the title fades from invisible
    /// to full and eases down from a slightly larger size to its authored size over a few seconds —
    /// the "aura" moment. Uses unscaled time so it plays while the game is paused (timeScale 0).
    /// </summary>
    private void RevealTitle()
    {
        Transform t = _titleObject != null ? _titleObject.transform : _titleLabel.transform;
        t.DOKill();

        _titleLabel.alpha = 0f;
        t.localScale      = _titleBaseScale * _titleRevealScaleFrom;

        // Fade alpha 0 → 1 (SetTarget(t) so t.DOKill() also cancels this on the next open).
        DOTween.To(() => _titleLabel.alpha, a => _titleLabel.alpha = a, 1f, _titleRevealDuration)
               .SetDelay(_titleRevealDelay).SetEase(Ease.InOutSine).SetUpdate(true).SetTarget(t);

        // Settle scale back to authored size.
        t.DOScale(_titleBaseScale, _titleRevealDuration)
         .SetDelay(_titleRevealDelay).SetEase(Ease.OutQuad).SetUpdate(true);
    }

    private void HideTitle()
    {
        Transform t = _titleObject != null ? _titleObject.transform
                    : _titleLabel  != null ? _titleLabel.transform : null;
        if (t != null) t.DOKill();
        if (_titleLabel  != null) _titleLabel.alpha = 0f;
        if (_titleObject != null) _titleObject.SetActive(false);
        if (t != null) t.localScale = _titleBaseScale;
    }

    // ── Entry actions (assigned to bullets via RuntimeAction) ─────────────────

    /// <summary>Resume gameplay. Lock cursor BEFORE restoring timescale (camera-spike rule).</summary>
    public void OnResume()
    {
        if (!_isOpen) return;
        _isOpen = false;

        if (_isBlocking)
        {
            GameInputState.Unblock();
            _isBlocking = false;
        }
        Time.timeScale = _savedTimeScale;

        // Re-enable the HUD canvas when gameplay resumes.
        if (_hudCanvas != null) _hudCanvas.enabled = true;

        HideTitle();
        FadeOverlay(_redOverlay, 0f);
        FadeOverlay(_backgroundImage, 0f);

        StartCoroutine(HideAfterFlyOut());   // bullets already cascading out from the click
    }

    public void OnSave()
    {
        if (!IsInHub()) return;   // defensive; the Save bullet's ClickGuard already shakes outside the hub
        _flashdriveMenu?.Open(SaveSlotButton3D.SlotMode.Save);
    }

    public void OnLoad() => _flashdriveMenu?.Open(SaveSlotButton3D.SlotMode.Load);

    /// <summary>Switch to the main menu IN PLACE (no scene load) — the background image covers the
    /// live scene. Resets RunManager state so the next run/load behaves correctly.</summary>
    public void OnReturnToMainMenu()
    {
        RunManager.Instance?.AbandonRunForMainMenu();
        OpenMainMenu();   // StopAllCoroutines + re-fly handles the transition; stays blocked/paused
    }

    public void OnStartNewGame()
    {
        // Resume (unblock, restore timescale, fade background out, hide bullets) only AFTER the
        // blank state has actually been applied. When New Game is chosen from a sector this is
        // deferred until the async hub-return + baseline restore finish, so the player never
        // regains control mid-transition.
        if (RunManager.Instance != null)
            RunManager.Instance.NewGame(OnResume);
        else
            OnResume();
    }

    public void OnExitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnFlashdriveActionExecuted()
    {
        // Save/Load finished — close the menu fully and resume.
        _isOpen = false;
        if (_isBlocking)
        {
            GameInputState.Unblock();
            _isBlocking = false;
        }
        Time.timeScale = 1f;

        // Re-enable the HUD canvas when gameplay resumes after a save/load action.
        if (_hudCanvas != null) _hudCanvas.enabled = true;

        HideTitle();
        FadeOverlay(_redOverlay, 0f);
        FadeOverlay(_backgroundImage, 0f);
        HideAllBullets();
    }
}
