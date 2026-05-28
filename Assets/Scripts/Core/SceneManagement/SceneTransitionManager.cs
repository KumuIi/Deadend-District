using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles all scene loading with a full-screen fade transition.
///
/// Architecture: Hub is the PERMANENT base scene — it never unloads.
/// Sectors load additively on top of Hub, then unload when the run ends.
/// "Returning to hub" = unload the active sector + re-show HubRoot.
/// This means the player rig and all GameSystems can simply live in Hub
/// as normal scene objects — no DontDestroyOnLoad required for them.
///
/// SceneTransitionManager itself IS DontDestroyOnLoad because it owns
/// the fade canvas and must survive sector loads/unloads.
///
/// Implementors: one instance on the GameSystems GameObject in Hub scene.
/// </summary>
public class SceneTransitionManager : MonoBehaviour
{
    public static SceneTransitionManager Instance { get; private set; }

    [Header("Fade")]
    [SerializeField] private float _fadeDuration = 0.4f;

    [Header("Hub")]
    [SerializeField] private string     _hubSceneName = "Hub";
    [Tooltip("Root empty that parents ALL Hub scene geometry/NPCs. Hidden while in a sector.")]
    [SerializeField] private GameObject _hubRoot;

    private string ActiveSlot => RunManager.Instance != null ? RunManager.Instance.ActiveSaveSlot : "slot0";

    public event Action OnSceneTransitionStarted;
    public event Action OnSceneTransitionFinished;

    private CanvasGroup _fadeGroup;
    private bool        _isTransitioning;
    private string      _activeSectorName;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildFadeCanvas();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>
    /// "Return to hub" — unloads the active sector and re-shows Hub geometry.
    /// Hub itself never reloads. Returns false if a transition is already running.
    /// </summary>
    public bool LoadHub()
    {
        if (_isTransitioning) return false;
        StartCoroutine(ReturnToHubRoutine());
        return true;
    }

    /// <summary>
    /// Load a sector additively on top of Hub and hide Hub geometry.
    /// Returns false if a transition is already running.
    /// </summary>
    public bool LoadSector(string sectorName)
    {
        if (_isTransitioning) return false;
        StartCoroutine(LoadSectorRoutine(sectorName));
        return true;
    }

    // ── Routines ───────────────────────────────────────────────────────────

    private IEnumerator ReturnToHubRoutine()
    {
        _isTransitioning = true;
        OnSceneTransitionStarted?.Invoke();

        yield return FadeOut();

        // Unload active sector if one exists
        if (!string.IsNullOrEmpty(_activeSectorName))
        {
            Scene sector = SceneManager.GetSceneByName(_activeSectorName);
            if (sector.IsValid())
            {
                foreach (var root in sector.GetRootGameObjects())
                    foreach (var entity in root.GetComponentsInChildren<IPoolableSpawnedEntity>(true))
                        entity.OnDespawned();

                yield return SceneManager.UnloadSceneAsync(_activeSectorName);
            }
            _activeSectorName = null;
        }

        // Re-show hub geometry and restore hub as active scene
        if (_hubRoot != null) _hubRoot.SetActive(true);
        Scene hub = SceneManager.GetSceneByName(_hubSceneName);
        if (hub.IsValid()) SceneManager.SetActiveScene(hub);

        // Queue save restores — flush one frame after Start() on hub objects
        SaveSystem.Instance?.RestoreAfterSceneLoad(RunScopeTag.Profile, ActiveSlot);
        SaveSystem.Instance?.RestoreAfterSceneLoad(RunScopeTag.World, ActiveSlot);

        yield return FadeIn();

        _isTransitioning = false;
        OnSceneTransitionFinished?.Invoke();
    }

    private IEnumerator LoadSectorRoutine(string sectorName)
    {
        _isTransitioning = true;
        OnSceneTransitionStarted?.Invoke();

        yield return FadeOut();

        // Hide hub so only the sector is visible
        if (_hubRoot != null) _hubRoot.SetActive(false);

        // Queue Run restore before load so sceneLoaded fires with pending scopes
        SaveSystem.Instance?.RestoreAfterSceneLoad(RunScopeTag.Run, ActiveSlot);

        yield return SceneManager.LoadSceneAsync(sectorName, LoadSceneMode.Additive);

        _activeSectorName = sectorName;
        Scene sector = SceneManager.GetSceneByName(sectorName);
        if (sector.IsValid()) SceneManager.SetActiveScene(sector);

        yield return FadeIn();

        _isTransitioning = false;
        OnSceneTransitionFinished?.Invoke();
    }

    // ── Fade helpers ───────────────────────────────────────────────────────

    public IEnumerator FadeOut()
    {
        bool done = false;
        _fadeGroup.DOFade(1f, _fadeDuration).SetUpdate(true).OnComplete(() => done = true);
        yield return new WaitUntil(() => done);
    }

    public IEnumerator FadeIn()
    {
        bool done = false;
        _fadeGroup.DOFade(0f, _fadeDuration).SetUpdate(true).OnComplete(() => done = true);
        yield return new WaitUntil(() => done);
    }

    // ── Fade canvas ────────────────────────────────────────────────────────

    private void BuildFadeCanvas()
    {
        var canvasGO = new GameObject("FadeCanvas");
        canvasGO.transform.SetParent(transform);
        DontDestroyOnLoad(canvasGO);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;
        canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();

        var imageGO = new GameObject("FadeImage");
        imageGO.transform.SetParent(canvasGO.transform, false);

        var rt = imageGO.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var image = imageGO.AddComponent<UnityEngine.UI.Image>();
        image.color = Color.black;

        _fadeGroup = imageGO.AddComponent<CanvasGroup>();
        _fadeGroup.alpha          = 0f;
        _fadeGroup.blocksRaycasts = false;
        _fadeGroup.interactable   = false;
    }
}
