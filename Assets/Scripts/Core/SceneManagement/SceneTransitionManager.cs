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

    public bool IsTransitioning => _isTransitioning;

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

        // NOTE: No save restore here. The hub is permanent — it never reloads — so its objects
        // keep their in-memory state across a run, and RunManager already saves Profile before
        // returning to hub. A RestoreAfterSceneLoad(...) call here would never flush (no
        // SceneManager.sceneLoaded fires when only a sector is unloaded) and would instead leak
        // into the next sector load's flush. Explicit loads (main menu / flashdrive) handle their
        // own restore separately.

        yield return FadeIn();

        // Teleport after fade — hub geometry is visible and old sector is fully gone
        if (hub.IsValid()) TeleportToSpawnInScene(hub, hubOnly: true);

        _isTransitioning = false;
        OnSceneTransitionFinished?.Invoke();
    }

    private IEnumerator LoadSectorRoutine(string sectorName)
    {
        _isTransitioning = true;
        OnSceneTransitionStarted?.Invoke();

        yield return FadeOut();

        // Unload the current sector if one is already loaded (sector-to-sector transition)
        if (!string.IsNullOrEmpty(_activeSectorName))
        {
            Scene current = SceneManager.GetSceneByName(_activeSectorName);
            if (current.IsValid())
            {
                foreach (var root in current.GetRootGameObjects())
                    foreach (var entity in root.GetComponentsInChildren<IPoolableSpawnedEntity>(true))
                        entity.OnDespawned();

                yield return SceneManager.UnloadSceneAsync(_activeSectorName);
            }
            _activeSectorName = null;
        }

        // Hide hub so only the sector is visible
        if (_hubRoot != null) _hubRoot.SetActive(false);

        // Queue Run restore before load so sceneLoaded fires with pending scopes
        SaveSystem.Instance?.RestoreAfterSceneLoad(RunScopeTag.Run, ActiveSlot);

        yield return SceneManager.LoadSceneAsync(sectorName, LoadSceneMode.Additive);

        _activeSectorName = sectorName;
        Scene sector = SceneManager.GetSceneByName(sectorName);
        if (sector.IsValid()) SceneManager.SetActiveScene(sector);

        yield return FadeIn();

        // Teleport after fade — sector geometry is loaded and hub is fully hidden
        if (sector.IsValid()) TeleportToSpawnInScene(sector, hubOnly: false);

        _isTransitioning = false;
        OnSceneTransitionFinished?.Invoke();
    }

    // ── Spawn helpers ──────────────────────────────────────────────────────

    private static void TeleportToSpawnInScene(Scene scene, bool hubOnly = false)
    {
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var spawn in root.GetComponentsInChildren<PlayerSpawnPoint>(true))
            {
                if (hubOnly && !spawn.IsHubSpawn) continue;
                spawn.Teleport();
                return;
            }
        }
        Debug.LogWarning($"[SceneTransitionManager] No PlayerSpawnPoint found in '{scene.name}' (hubOnly={hubOnly}).");
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
