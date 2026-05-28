using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles all scene loading with a full-screen fade transition.
/// Owns a DontDestroyOnLoad Canvas (sort order 999) for the black fade overlay.
///
/// LoadHub  — Single load, replaces all scenes (hub contains the player rig).
/// LoadSector — Additive load alongside Hub. Restores Run-scoped save after load.
/// UnloadSector — Despawns poolable entities then unloads the scene.
///
/// Implementors: one instance on the GameSystems GameObject.
/// </summary>
public class SceneTransitionManager : MonoBehaviour
{
    public static SceneTransitionManager Instance { get; private set; }

    [Header("Fade")]
    [SerializeField] private float _fadeDuration = 0.4f;

    [Header("Scenes")]
    [SerializeField] private string _hubSceneName = "Hub";
    [SerializeField] private string _defaultSaveSlot = "slot0";

    public event Action OnSceneTransitionStarted;
    public event Action OnSceneTransitionFinished;

    private CanvasGroup _fadeGroup;
    private bool _isTransitioning;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildFadeCanvas();
    }

    // ── Public API ─────────────────────────────────────────────────────────

    /// <summary>Returns true if the transition started, false if one was already in progress.</summary>
    public bool LoadHub() { if (_isTransitioning) return false; StartCoroutine(LoadHubRoutine()); return true; }

    /// <summary>Returns true if the transition started, false if one was already in progress.</summary>
    public bool LoadSector(string sectorName) { if (_isTransitioning) return false; StartCoroutine(LoadSectorRoutine(sectorName)); return true; }

    public void UnloadSector(string sectorName) => StartCoroutine(UnloadSectorRoutine(sectorName));

    // ── Routines ───────────────────────────────────────────────────────────

    private IEnumerator LoadHubRoutine()
    {
        if (_isTransitioning) yield break;
        _isTransitioning = true;
        OnSceneTransitionStarted?.Invoke();

        yield return FadeOut();
        // Queue restores BEFORE loading — SaveSystem flushes them when sceneLoaded fires
        SaveSystem.Instance?.RestoreAfterSceneLoad(RunScopeTag.Profile, _defaultSaveSlot);
        SaveSystem.Instance?.RestoreAfterSceneLoad(RunScopeTag.World, _defaultSaveSlot);
        yield return SceneManager.LoadSceneAsync(_hubSceneName, LoadSceneMode.Single);
        yield return FadeIn();

        _isTransitioning = false;
        OnSceneTransitionFinished?.Invoke();
    }

    private IEnumerator LoadSectorRoutine(string sectorName)
    {
        if (_isTransitioning) yield break;
        _isTransitioning = true;
        OnSceneTransitionStarted?.Invoke();

        yield return FadeOut();

        // Queue restore BEFORE loading so sceneLoaded fires with pending scopes ready
        SaveSystem.Instance?.RestoreAfterSceneLoad(RunScopeTag.Run, _defaultSaveSlot);
        var op = SceneManager.LoadSceneAsync(sectorName, LoadSceneMode.Additive);
        yield return op;

        SceneManager.SetActiveScene(SceneManager.GetSceneByName(sectorName));

        yield return FadeIn();

        _isTransitioning = false;
        OnSceneTransitionFinished?.Invoke();
    }

    private IEnumerator UnloadSectorRoutine(string sectorName)
    {
        if (_isTransitioning) yield break;
        _isTransitioning = true;
        OnSceneTransitionStarted?.Invoke();

        yield return FadeOut();

        Scene scene = SceneManager.GetSceneByName(sectorName);
        if (scene.IsValid())
        {
            // Notify poolable entities before unload
            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
            {
                foreach (var entity in root.GetComponentsInChildren<IPoolableSpawnedEntity>(includeInactive: true))
                    entity.OnDespawned();
            }
            yield return SceneManager.UnloadSceneAsync(sectorName);
        }

        // Restore hub scene as active
        Scene hub = SceneManager.GetSceneByName(_hubSceneName);
        if (hub.IsValid()) SceneManager.SetActiveScene(hub);

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

    // ── Fade canvas setup ──────────────────────────────────────────────────

    private void BuildFadeCanvas()
    {
        var canvasGO = new GameObject("FadeCanvas");
        canvasGO.transform.SetParent(transform);
        DontDestroyOnLoad(canvasGO);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999;

        canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

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
